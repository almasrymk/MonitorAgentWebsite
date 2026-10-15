using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitorAgent.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.CreateTable(
                name: "Pages",
                schema: "content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedVersion = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageVersions",
                schema: "content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    BodyEn = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    EditedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageVersions_Pages_PageId",
                        column: x => x.PageId,
                        principalSchema: "content",
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Slug",
                schema: "content",
                table: "Pages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_PageId_Number",
                schema: "content",
                table: "PageVersions",
                columns: new[] { "PageId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageVersions",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Pages",
                schema: "content");
        }
    }
}
