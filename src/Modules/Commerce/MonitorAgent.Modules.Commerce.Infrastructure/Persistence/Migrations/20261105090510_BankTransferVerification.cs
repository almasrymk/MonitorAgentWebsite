using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BankTransferVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerNote",
                schema: "commerce",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous version has no PendingVerification: those orders wait for payment again, and staff use the
            // manual payment screen with the reference the buyer reported.
            migrationBuilder.Sql("UPDATE [commerce].[Orders] SET [Status] = 'PendingPayment' WHERE [Status] = 'PendingVerification';");
            migrationBuilder.DropColumn(
                name: "CustomerNote",
                schema: "commerce",
                table: "Payments");
        }
    }
}
