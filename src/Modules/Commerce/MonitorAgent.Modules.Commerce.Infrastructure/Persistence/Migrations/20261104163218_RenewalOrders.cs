using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "commerce",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Purchase");

            migrationBuilder.AddColumn<Guid>(
                name: "RenewsGrantId",
                schema: "commerce",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "commerce",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewsGrantId",
                schema: "commerce",
                table: "Orders");
        }
    }
}
