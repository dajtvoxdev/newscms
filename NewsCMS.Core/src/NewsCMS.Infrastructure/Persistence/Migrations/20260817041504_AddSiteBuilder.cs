using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCulture",
                table: "Sites",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AddColumn<string>(
                name: "SupportedCultures",
                table: "Sites",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AlterColumn<string>(
                name: "Robots",
                table: "SeoMetas",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OgImage",
                table: "SeoMetas",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaTitle",
                table: "SeoMetas",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaKeywords",
                table: "SeoMetas",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "SeoMetas",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                table: "SeoMetas",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Canonical",
                table: "SeoMetas",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeFreq",
                table: "SeoMetas",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Culture",
                table: "SeoMetas",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OgType",
                table: "SeoMetas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Priority",
                table: "SeoMetas",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchemaJsonLd",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitterCard",
                table: "SeoMetas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Pages",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Pages",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "BuilderJson",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompiledCss",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompiledHtml",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CssDirty",
                table: "Pages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomJs",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Pages",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "LayoutId",
                table: "Pages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Pages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Pages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Pages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Categories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CoverImageId",
                table: "Categories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Categories",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LayoutId",
                table: "Categories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathSlug",
                table: "Categories",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplatePageId",
                table: "Categories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Categories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BlockDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PreviewImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuilderJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DynamicHandler = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PropsSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    BuilderJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledCss = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomJs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    BuilderJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledCss = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomJs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Excerpt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RateLimitPerMinute = table.Column<int>(type: "int", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteDesignTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDesignTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuilderJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompiledCss = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomJs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteLayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RouteType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreviewImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpecJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeoMetas_SiteId_EntityType_EntityId_Culture",
                table: "SeoMetas",
                columns: new[] { "SiteId", "EntityType", "EntityId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_LayoutId",
                table: "Pages",
                column: "LayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_SiteId_Slug",
                table: "Pages",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockDefinitions_SiteId_Key",
                table: "BlockDefinitions",
                columns: new[] { "SiteId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_CategoryId_Culture",
                table: "CategoryTranslations",
                columns: new[] { "CategoryId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_SiteId",
                table: "CategoryTranslations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemTranslations_MenuItemId_Culture",
                table: "MenuItemTranslations",
                columns: new[] { "MenuItemId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemTranslations_SiteId",
                table: "MenuItemTranslations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisions_PageId_Version",
                table: "PageRevisions",
                columns: new[] { "PageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisions_SiteId",
                table: "PageRevisions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PageTranslations_PageId_Culture",
                table: "PageTranslations",
                columns: new[] { "PageId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageTranslations_SiteId",
                table: "PageTranslations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PostTranslations_PostId_Culture",
                table: "PostTranslations",
                columns: new[] { "PostId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostTranslations_SiteId",
                table: "PostTranslations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteApiKeys_KeyHash",
                table: "SiteApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteApiKeys_SiteId",
                table: "SiteApiKeys",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDesignTokens_SiteId",
                table: "SiteDesignTokens",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDesignTokens_SiteId_Group_Key",
                table: "SiteDesignTokens",
                columns: new[] { "SiteId", "Group", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteLayouts_SiteId",
                table: "SiteLayouts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteLayouts_SiteId_Key",
                table: "SiteLayouts",
                columns: new[] { "SiteId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteRoutes_RouteType_TargetId",
                table: "SiteRoutes",
                columns: new[] { "RouteType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteRoutes_SiteId",
                table: "SiteRoutes",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRoutes_SiteId_Culture_Path",
                table: "SiteRoutes",
                columns: new[] { "SiteId", "Culture", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteTemplates_Key",
                table: "SiteTemplates",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockDefinitions");

            migrationBuilder.DropTable(
                name: "CategoryTranslations");

            migrationBuilder.DropTable(
                name: "MenuItemTranslations");

            migrationBuilder.DropTable(
                name: "PageRevisions");

            migrationBuilder.DropTable(
                name: "PageTranslations");

            migrationBuilder.DropTable(
                name: "PostTranslations");

            migrationBuilder.DropTable(
                name: "SiteApiKeys");

            migrationBuilder.DropTable(
                name: "SiteDesignTokens");

            migrationBuilder.DropTable(
                name: "SiteLayouts");

            migrationBuilder.DropTable(
                name: "SiteRoutes");

            migrationBuilder.DropTable(
                name: "SiteTemplates");

            migrationBuilder.DropIndex(
                name: "IX_SeoMetas_SiteId_EntityType_EntityId_Culture",
                table: "SeoMetas");

            migrationBuilder.DropIndex(
                name: "IX_Pages_LayoutId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_SiteId_Slug",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DefaultCulture",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "SupportedCultures",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ChangeFreq",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "Culture",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "OgType",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "SchemaJsonLd",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "TwitterCard",
                table: "SeoMetas");

            migrationBuilder.DropColumn(
                name: "BuilderJson",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "CompiledCss",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "CompiledHtml",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "CssDirty",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "CustomJs",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "LayoutId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "CoverImageId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "LayoutId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "PathSlug",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "TemplatePageId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Categories");

            migrationBuilder.AlterColumn<string>(
                name: "Robots",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OgImage",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaTitle",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaKeywords",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Canonical",
                table: "SeoMetas",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);
        }
    }
}
