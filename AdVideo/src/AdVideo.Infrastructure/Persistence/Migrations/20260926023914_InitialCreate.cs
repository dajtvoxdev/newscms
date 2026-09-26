using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FormatCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Example = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndpointUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapabilityJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreditExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DailyCostLimitUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderDescriptors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDescriptors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false),
                    MinValue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MaxValue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ApiKeyRotatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdVideoProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultBrandKitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdVideoProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdVideoProjects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdVideoJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    AspectRatio = table.Column<int>(type: "int", nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "int", nullable: false),
                    BriefJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FormatCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProviderModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VoiceProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    HasPerson = table.Column<bool>(type: "bit", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    RegenerateCount = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RawProviderError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalVideoAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ThumbnailAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubtitleAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoiceAudioAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TermsAcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TermsAcceptedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdVideoJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdVideoJobs_AdVideoProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "AdVideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Bucket = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(127)", maxLength: 127, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    HasAudio = table.Column<bool>(type: "bit", nullable: false),
                    IsQcPassed = table.Column<bool>(type: "bit", nullable: false),
                    DerivedFromAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_AdVideoJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "AdVideoJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaAssets_MediaAssets_DerivedFromAssetId",
                        column: x => x.DerivedFromAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShotIndex = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false),
                    FailureKind = table.Column<int>(type: "int", nullable: false),
                    RawError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostIsReported = table.Column<bool>(type: "bit", nullable: false),
                    BilledCharacterCount = table.Column<int>(type: "int", nullable: true),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    DescriptorSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderCalls_AdVideoJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "AdVideoJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VisualPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SpokenText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NarrationStartSeconds = table.Column<double>(type: "float", nullable: false),
                    NarrationEndSeconds = table.Column<double>(type: "float", nullable: false),
                    VideoDurationSeconds = table.Column<int>(type: "int", nullable: false),
                    VideoStartSeconds = table.Column<double>(type: "float", nullable: false),
                    AudioStartInTimelineSeconds = table.Column<double>(type: "float", nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: true),
                    ProviderModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TtsRequestId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ClipAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NativeAudioAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsLipSynced = table.Column<bool>(type: "bit", nullable: false),
                    RenderStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenderFinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shots_AdVideoJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "AdVideoJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Shots_MediaAssets_ClipAssetId",
                        column: x => x.ClipAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shots_MediaAssets_NativeAudioAssetId",
                        column: x => x.NativeAudioAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdVideoJobs_ProjectId",
                table: "AdVideoJobs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AdVideoJobs_Status_CreatedAt",
                table: "AdVideoJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdVideoJobs_Tenant_CreatedAt",
                table: "AdVideoJobs",
                columns: new[] { "TenantId", "CreatedAt" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AdVideoJobs_Tenant_IdempotencyKey",
                table: "AdVideoJobs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AdVideoProjects_Tenant_Name",
                table: "AdVideoProjects",
                columns: new[] { "TenantId", "Name" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Checksum",
                table: "MediaAssets",
                column: "ChecksumSha256");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_DerivedFromAssetId",
                table: "MediaAssets",
                column: "DerivedFromAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Job_Kind",
                table: "MediaAssets",
                columns: new[] { "JobId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Tenant_Kind",
                table: "MediaAssets",
                columns: new[] { "TenantId", "Kind" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PromptTemplates_Code_Active",
                table: "PromptTemplates",
                column: "Code",
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PromptTemplates_Code_Version",
                table: "PromptTemplates",
                columns: new[] { "Code", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_Job_CreatedAt",
                table: "ProviderCalls",
                columns: new[] { "JobId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_Provider_CreatedAt",
                table: "ProviderCalls",
                columns: new[] { "Provider", "CreatedAt" })
                .Annotation("SqlServer:Include", new[] { "CostUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_Tenant_CreatedAt",
                table: "ProviderCalls",
                columns: new[] { "TenantId", "CreatedAt" })
                .Annotation("SqlServer:Include", new[] { "CostUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentials_Category_Active_Priority",
                table: "ProviderCredentials",
                columns: new[] { "Category", "IsActive", "Priority" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProviderCredentials_Provider_Model_Category_Tenant",
                table: "ProviderCredentials",
                columns: new[] { "Provider", "ModelId", "Category", "TenantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDescriptors_Sha256",
                table: "ProviderDescriptors",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "UX_ProviderDescriptors_Code_Active",
                table: "ProviderDescriptors",
                column: "Code",
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProviderDescriptors_Code_Version",
                table: "ProviderDescriptors",
                columns: new[] { "Code", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_ClipAssetId",
                table: "Shots",
                column: "ClipAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_NativeAudioAssetId",
                table: "Shots",
                column: "NativeAudioAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_Status_RenderStartedAt",
                table: "Shots",
                columns: new[] { "Status", "RenderStartedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Shots_Job_Index",
                table: "Shots",
                columns: new[] { "JobId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_ApiKeyPrefix",
                table: "Tenants",
                column: "ApiKeyPrefix",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptTemplates");

            migrationBuilder.DropTable(
                name: "ProviderCalls");

            migrationBuilder.DropTable(
                name: "ProviderCredentials");

            migrationBuilder.DropTable(
                name: "ProviderDescriptors");

            migrationBuilder.DropTable(
                name: "Shots");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "AdVideoJobs");

            migrationBuilder.DropTable(
                name: "AdVideoProjects");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
