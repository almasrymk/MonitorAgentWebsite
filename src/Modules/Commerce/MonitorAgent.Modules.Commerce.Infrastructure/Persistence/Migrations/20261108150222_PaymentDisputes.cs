using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChargedBackAmount",
                schema: "commerce",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Dispute",
                schema: "commerce",
                table: "Orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                schema: "commerce",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputedAt",
                schema: "commerce",
                table: "Orders",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous version has no Chargeback ledger kind: keep the money-out line, as a refund.
            migrationBuilder.Sql("UPDATE [commerce].[Ledger] SET [Kind] = 'Refund' WHERE [Kind] = 'Chargeback';");
            migrationBuilder.DropColumn(
                name: "ChargedBackAmount",
                schema: "commerce",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Dispute",
                schema: "commerce",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                schema: "commerce",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DisputedAt",
                schema: "commerce",
                table: "Orders");
        }
    }
}
