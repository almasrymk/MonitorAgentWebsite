using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Licenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "licenses");

            migrationBuilder.CreateTable(
                name: "CustomerLinks",
                schema: "licenses",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLinks", x => x.CompanyId);
                });

            migrationBuilder.CreateTable(
                name: "Grants",
                schema: "licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlatformSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlatformLicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProtectedProductKey = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLinks_PlatformCustomerId",
                schema: "licenses",
                table: "CustomerLinks",
                column: "PlatformCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grants_CompanyId_PlatformLicenseId",
                schema: "licenses",
                table: "Grants",
                columns: new[] { "CompanyId", "PlatformLicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Grants_OrderReference",
                schema: "licenses",
                table: "Grants",
                column: "OrderReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerLinks",
                schema: "licenses");

            migrationBuilder.DropTable(
                name: "Grants",
                schema: "licenses");
        }
    }
}
