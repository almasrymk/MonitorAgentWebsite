using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Licenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonitorTenantOnCustomerLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MonitorTenantId",
                schema: "licenses",
                table: "CustomerLinks",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonitorTenantId",
                schema: "licenses",
                table: "CustomerLinks");
        }
    }
}
