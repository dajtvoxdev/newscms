using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailPageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PathSlug",
                table: "ProductCategories",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplatePageId",
                table: "ProductCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultTemplate",
                table: "Pages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentPageId",
                table: "Pages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ParentPageId_Kind",
                table: "Pages",
                columns: new[] { "ParentPageId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_ParentPageId_Kind",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PathSlug",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "TemplatePageId",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "IsDefaultTemplate",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ParentPageId",
                table: "Pages");
        }
    }
}
