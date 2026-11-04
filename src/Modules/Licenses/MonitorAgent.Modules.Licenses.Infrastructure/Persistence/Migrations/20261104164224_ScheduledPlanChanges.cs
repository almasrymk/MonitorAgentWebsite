using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Licenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledPlanChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledPlanChanges",
                schema: "licenses",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledPlanChanges", x => x.SubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPlanChanges_CompanyId",
                schema: "licenses",
                table: "ScheduledPlanChanges",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledPlanChanges",
                schema: "licenses");
        }
    }
}
