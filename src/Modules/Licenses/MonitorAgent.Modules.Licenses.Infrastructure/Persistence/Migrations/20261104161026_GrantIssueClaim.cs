using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Licenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantIssueClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IssueClaimedUntil",
                schema: "licenses",
                table: "Grants",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssueClaimedUntil",
                schema: "licenses",
                table: "Grants");
        }
    }
}
