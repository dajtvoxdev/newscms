using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImagePromptLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImagePromptLibrarySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrendAutoUpdateEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TrendIntervalHours = table.Column<int>(type: "int", nullable: false),
                    TemplatesPerRun = table.Column<int>(type: "int", nullable: false),
                    TrendLifetimeDays = table.Column<int>(type: "int", nullable: false),
                    RequireReview = table.Column<bool>(type: "bit", nullable: false),
                    Focus = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BlockedTerms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AutoDemoForTrend = table.Column<bool>(type: "bit", nullable: false),
                    MaxAutoDemosPerRun = table.Column<int>(type: "int", nullable: false),
                    DemoModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagePromptLibrarySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImagePromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AspectRatio = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequiresSourceImage = table.Column<bool>(type: "bit", nullable: false),
                    RegionHint = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TrendName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceUrls = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrendRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DemoStorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DemoImageUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DemoUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DemoSource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagePromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImagePromptTrendRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Rejected = table.Column<int>(type: "int", nullable: false),
                    Expired = table.Column<int>(type: "int", nullable: false),
                    DemosCreated = table.Column<int>(type: "int", nullable: false),
                    UsedWebSearch = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagePromptTrendRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagePromptTemplates_Status_Purpose",
                table: "ImagePromptTemplates",
                columns: new[] { "Status", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagePromptTrendRuns_StartedAt",
                table: "ImagePromptTrendRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImagePromptLibrarySettings");

            migrationBuilder.DropTable(
                name: "ImagePromptTemplates");

            migrationBuilder.DropTable(
                name: "ImagePromptTrendRuns");
        }
    }
}
