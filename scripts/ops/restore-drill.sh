#!/usr/bin/env bash
# Backup and restore drill against the docker compose stack (see docs/operations/backup-restore.md).
#
# Takes a full backup with checksums, restores it under a new name, compares the row count of every table, then starts
# a second API on the restored copy and waits until it reports ready. Prints how long the restore took, which is the
# number to compare with the RTO.
set -euo pipefail

: "${SQL_SA_PASSWORD:?Set SQL_SA_PASSWORD (the SA password of the stack)}"
SOURCE_DB="${SOURCE_DB:-MonitorAgentWebsite}"
DRILL_DB="${SOURCE_DB}_RestoreDrill"
BACKUP="/var/opt/mssql/backup/${SOURCE_DB}_drill.bak"

sql() {
  docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SQL_SA_PASSWORD" -h -1 -W -Q "SET NOCOUNT ON; $1"
}

counts() {
  sql "SELECT s.name + '.' + t.name + '=' + CAST(SUM(p.rows) AS varchar(20))
       FROM [$1].sys.tables t JOIN [$1].sys.schemas s ON s.schema_id = t.schema_id
       JOIN [$1].sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
       GROUP BY s.name, t.name ORDER BY 1"
}

echo "Backing up $SOURCE_DB"
docker compose exec -T sqlserver mkdir -p /var/opt/mssql/backup
sql "BACKUP DATABASE [$SOURCE_DB] TO DISK = N'$BACKUP' WITH INIT, CHECKSUM, FORMAT"
sql "RESTORE VERIFYONLY FROM DISK = N'$BACKUP' WITH CHECKSUM"

echo "Restoring as $DRILL_DB"
started=$(date +%s)
files=$(sql "SELECT name + '|' + type_desc FROM [$SOURCE_DB].sys.database_files")
moves=""
while IFS='|' read -r name kind; do
  [ -z "$name" ] && continue
  ext=$([ "$kind" = "LOG" ] && echo ldf || echo mdf)
  moves+=", MOVE N'$name' TO N'/var/opt/mssql/data/${DRILL_DB}_${name}.${ext}'"
done <<< "$files"
sql "IF DB_ID(N'$DRILL_DB') IS NOT NULL BEGIN ALTER DATABASE [$DRILL_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DRILL_DB]; END"
sql "RESTORE DATABASE [$DRILL_DB] FROM DISK = N'$BACKUP' WITH CHECKSUM, RECOVERY $moves"
sql "DBCC CHECKDB ([$DRILL_DB]) WITH NO_INFOMSGS"
restored=$(date +%s)

echo "Comparing row counts"
diff <(counts "$SOURCE_DB") <(counts "$DRILL_DB")
echo "Tables match: $(counts "$DRILL_DB" | wc -l)"

echo "Starting an API on the restored copy"
docker compose run -d --rm --name restore-drill-api -p 5299:8080 \
  -e "ConnectionStrings__Default=Server=sqlserver;Database=$DRILL_DB;User Id=sa;Password=$SQL_SA_PASSWORD;TrustServerCertificate=True" api >/dev/null
trap 'docker rm -f restore-drill-api >/dev/null 2>&1 || true' EXIT
for _ in $(seq 1 30); do curl -fsS http://localhost:5299/health/ready >/dev/null 2>&1 && break; sleep 2; done
curl -fsS http://localhost:5299/health/ready
echo
echo "Restore and check took $((restored - started)) s; API ready on the copy after $(($(date +%s) - started)) s."
