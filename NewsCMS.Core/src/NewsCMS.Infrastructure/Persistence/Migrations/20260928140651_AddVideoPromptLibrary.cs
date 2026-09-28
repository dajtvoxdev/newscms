using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoPromptLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoPromptLibrarySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrendAutoUpdateEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TrendIntervalHours = table.Column<int>(type: "int", nullable: false),
                    TemplatesPerRun = table.Column<int>(type: "int", nullable: false),
                    TrendLifetimeDays = table.Column<int>(type: "int", nullable: false),
                    RequireReview = table.Column<bool>(type: "bit", nullable: false),
                    Focus = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoPromptLibrarySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ScenePrompt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ScriptTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    AspectRatio = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    HasPerson = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TrendName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceUrls = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrendRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoPromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoPromptTrendRuns",
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
                    UsedWebSearch = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoPromptTrendRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoPromptTemplates_Status_Category",
                table: "VideoPromptTemplates",
                columns: new[] { "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoPromptTrendRuns_StartedAt",
                table: "VideoPromptTrendRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoPromptLibrarySettings");

            migrationBuilder.DropTable(
                name: "VideoPromptTemplates");

            migrationBuilder.DropTable(
                name: "VideoPromptTrendRuns");
        }
    }
}
