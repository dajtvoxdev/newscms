using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaSiteScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Medias",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "MediaFolders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
DECLARE @defaultSiteId uniqueidentifier =
    (SELECT TOP 1 Id FROM Sites WHERE Slug = 'hailuunguoc' ORDER BY CreatedAt);
IF @defaultSiteId IS NULL
    SET @defaultSiteId = (SELECT TOP 1 Id FROM Sites ORDER BY CreatedAt);
IF @defaultSiteId IS NOT NULL
BEGIN
    DECLARE @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
    UPDATE Medias       SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE MediaFolders SET SiteId = @defaultSiteId WHERE SiteId = @empty;
END
");

            migrationBuilder.CreateIndex(
                name: "IX_Medias_SiteId",
                table: "Medias",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_SiteId",
                table: "MediaFolders",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Medias_SiteId",
                table: "Medias");

            migrationBuilder.DropIndex(
                name: "IX_MediaFolders_SiteId",
                table: "MediaFolders");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "MediaFolders");
        }
    }
}
