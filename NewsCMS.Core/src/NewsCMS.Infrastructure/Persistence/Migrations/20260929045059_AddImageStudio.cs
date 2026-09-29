using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiJobId",
                table: "Medias",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Medias",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "upload");

            migrationBuilder.CreateTable(
                name: "ImageJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    ImageModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserPrompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FinalPrompt = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AspectRatio = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Size = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VariantCount = table.Column<int>(type: "int", nullable: false),
                    SourceMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceOutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceMediaIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RegionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    Strategy = table.Column<int>(type: "int", nullable: false),
                    PreserveOutside = table.Column<bool>(type: "bit", nullable: false),
                    MaskStorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AnnotatedStorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ParentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RightsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    ContextType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Adapter = table.Column<int>(type: "int", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Capabilities = table.Column<int>(type: "int", nullable: false),
                    MaxReferenceImages = table.Column<int>(type: "int", nullable: false),
                    MaskConvention = table.Column<int>(type: "int", nullable: false),
                    SupportedSizes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MaxVariants = table.Column<int>(type: "int", nullable: false),
                    Quality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OutputFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PricePerImageUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    ExtraParamsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTestOk = table.Column<bool>(type: "bit", nullable: true),
                    LastTestError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageProviderCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    ImagesReturned = table.Column<int>(type: "int", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageProviderCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageStudioSiteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    MonthlyImageQuota = table.Column<int>(type: "int", nullable: false),
                    PerUserDailyQuota = table.Column<int>(type: "int", nullable: false),
                    BrandStyle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CoverAspect = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProductAspect = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShowAiCaption = table.Column<bool>(type: "bit", nullable: false),
                    AiCaptionText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageStudioSiteSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageJobOutputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PromotedMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PromotedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsPurged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageJobOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageJobOutputs_ImageJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ImageJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Medias_Origin",
                table: "Medias",
                column: "Origin");

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobOutputs_JobId",
                table: "ImageJobOutputs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobOutputs_PromotedMediaId_CreatedAt",
                table: "ImageJobOutputs",
                columns: new[] { "PromotedMediaId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobs_SiteId",
                table: "ImageJobs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobs_SiteId_IdempotencyKey",
                table: "ImageJobs",
                columns: new[] { "SiteId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobs_SiteId_Status_QueuedAt",
                table: "ImageJobs",
                columns: new[] { "SiteId", "Status", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageJobs_Status_QueuedAt",
                table: "ImageJobs",
                columns: new[] { "Status", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageModels_IsActive_SortOrder",
                table: "ImageModels",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageProviderCalls_JobId",
                table: "ImageProviderCalls",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageProviderCalls_SiteId_CreatedAt",
                table: "ImageProviderCalls",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageStudioSiteSettings_SiteId",
                table: "ImageStudioSiteSettings",
                column: "SiteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageJobOutputs");

            migrationBuilder.DropTable(
                name: "ImageModels");

            migrationBuilder.DropTable(
                name: "ImageProviderCalls");

            migrationBuilder.DropTable(
                name: "ImageStudioSiteSettings");

            migrationBuilder.DropTable(
                name: "ImageJobs");

            migrationBuilder.DropIndex(
                name: "IX_Medias_Origin",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "AiJobId",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Medias");
        }
    }
}
