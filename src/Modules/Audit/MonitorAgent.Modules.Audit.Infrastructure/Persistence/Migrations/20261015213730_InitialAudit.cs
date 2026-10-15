using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_Action_At",
                schema: "audit",
                table: "Entries",
                columns: new[] { "Action", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_ActorUserId_At",
                schema: "audit",
                table: "Entries",
                columns: new[] { "ActorUserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_At",
                schema: "audit",
                table: "Entries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_Target_At",
                schema: "audit",
                table: "Entries",
                columns: new[] { "Target", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entries",
                schema: "audit");
        }
    }
}
