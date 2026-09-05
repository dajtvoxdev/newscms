using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiSiteTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisitorSessions_DayBucket_VisitorKey",
                table: "VisitorSessions");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Slug",
                table: "Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SiteSettings",
                table: "SiteSettings");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Slug",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_Slug",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "IX_Posts_Slug",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Commitments_SignerKey",
                table: "Commitments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_ArExperiences_Slug",
                table: "ArExperiences");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "VisitorSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "SiteSettings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "SeoMetas",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Redirects",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ProductCategories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Pages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Menus",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "FormSubmissions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContactForms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Commitments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Banners",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ArExperiences",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill: gán dữ liệu cũ (SiteId = Guid.Empty) về site mặc định để query filter không ẩn mất.
            // Tra site theo slug 'hailuunguoc'; fallback site đầu tiên nếu chưa có. No-op trên DB mới (chưa có row).
            migrationBuilder.Sql(@"
DECLARE @defaultSiteId uniqueidentifier =
    (SELECT TOP 1 Id FROM Sites WHERE Slug = 'hailuunguoc' ORDER BY CreatedAt);
IF @defaultSiteId IS NULL
    SET @defaultSiteId = (SELECT TOP 1 Id FROM Sites ORDER BY CreatedAt);
IF @defaultSiteId IS NOT NULL
BEGIN
    DECLARE @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
    UPDATE Posts             SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Categories        SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Tags              SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Products          SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE ProductCategories SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Pages             SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Menus             SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Banners           SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE SiteSettings      SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE SeoMetas          SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Redirects         SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE ContactForms      SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE FormSubmissions   SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE Commitments       SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE VisitorSessions   SET SiteId = @defaultSiteId WHERE SiteId = @empty;
    UPDATE ArExperiences     SET SiteId = @defaultSiteId WHERE SiteId = @empty;
END
");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SiteSettings",
                table: "SiteSettings",
                columns: new[] { "SiteId", "Key" });

            migrationBuilder.CreateTable(
                name: "SiteDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteDomains_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorSessions_SiteId",
                table: "VisitorSessions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorSessions_SiteId_DayBucket_VisitorKey",
                table: "VisitorSessions",
                columns: new[] { "SiteId", "DayBucket", "VisitorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SiteId",
                table: "Tags",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SiteId_Slug",
                table: "Tags",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_SiteId",
                table: "SiteSettings",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SeoMetas_SiteId",
                table: "SeoMetas",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_SiteId",
                table: "Redirects",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SiteId",
                table: "Products",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SiteId_Sku",
                table: "Products",
                columns: new[] { "SiteId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SiteId_Slug",
                table: "Products",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_SiteId",
                table: "ProductCategories",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_SiteId_Slug",
                table: "ProductCategories",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SiteId",
                table: "Posts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SiteId_Slug",
                table: "Posts",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_SiteId",
                table: "Pages",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_SiteId",
                table: "Menus",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_SiteId",
                table: "FormSubmissions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactForms_SiteId",
                table: "ContactForms",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_SiteId",
                table: "Commitments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_SiteId_SignerKey",
                table: "Commitments",
                columns: new[] { "SiteId", "SignerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SiteId",
                table: "Categories",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SiteId_Slug",
                table: "Categories",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banners_SiteId",
                table: "Banners",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ArExperiences_SiteId",
                table: "ArExperiences",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ArExperiences_SiteId_Slug",
                table: "ArExperiences",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteDomains_Host",
                table: "SiteDomains",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteDomains_SiteId",
                table: "SiteDomains",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteDomains");

            migrationBuilder.DropIndex(
                name: "IX_VisitorSessions_SiteId",
                table: "VisitorSessions");

            migrationBuilder.DropIndex(
                name: "IX_VisitorSessions_SiteId_DayBucket_VisitorKey",
                table: "VisitorSessions");

            migrationBuilder.DropIndex(
                name: "IX_Tags_SiteId",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_SiteId_Slug",
                table: "Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SiteSettings",
                table: "SiteSettings");

            migrationBuilder.DropIndex(
                name: "IX_SiteSettings_SiteId",
                table: "SiteSettings");

            migrationBuilder.DropIndex(
                name: "IX_SeoMetas_SiteId",
                table: "SeoMetas");

            migrationBuilder.DropIndex(
                name: "IX_Redirects_SiteId",
                table: "Redirects");

            migrationBuilder.DropIndex(
                name: "IX_Products_SiteId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SiteId_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SiteId_Slug",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_SiteId",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_SiteId_Slug",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "IX_Posts_SiteId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_SiteId_Slug",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Pages_SiteId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Menus_SiteId",
                table: "Menus");

            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_SiteId",
                table: "FormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_ContactForms_SiteId",
                table: "ContactForms");

            migrationBuilder.DropIndex(
                name: "IX_Commitments_SiteId",
                table: "Commitments");

            migrationBuilder.DropIndex(
                name: "IX_Commitments_SiteId_SignerKey",
                table: "Commitments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SiteId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SiteId_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Banners_SiteId",
                table: "Banners");

            migrationBuilder.DropIndex(
                name: "IX_ArExperiences_SiteId",
                table: "ArExperiences");

            migrationBuilder.DropIndex(
                name: "IX_ArExperiences_SiteId_Slug",
                table: "ArExperiences");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "VisitorSessions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Redirects");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContactForms");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Commitments");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ArExperiences");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SiteSettings",
                table: "SiteSettings",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorSessions_DayBucket_VisitorKey",
                table: "VisitorSessions",
                columns: new[] { "DayBucket", "VisitorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Slug",
                table: "Products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_Slug",
                table: "ProductCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Slug",
                table: "Posts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_SignerKey",
                table: "Commitments",
                column: "SignerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArExperiences_Slug",
                table: "ArExperiences",
                column: "Slug",
                unique: true);
        }
    }
}
