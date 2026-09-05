using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeoBiaImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ImportedRows = table.Column<int>(type: "int", nullable: false),
                    SkippedRows = table.Column<int>(type: "int", nullable: false),
                    ErrorRows = table.Column<int>(type: "int", nullable: false),
                    ErrorLog = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaImportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeoBiaMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    HomeName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    HomeCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    HomePrimary = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    HomeSecondary = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AwayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AwayCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AwayPrimary = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AwaySecondary = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    KickoffAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    IsHot = table.Column<bool>(type: "bit", nullable: false),
                    HotLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    BaseHomeWeight = table.Column<int>(type: "int", nullable: false),
                    BaseDrawWeight = table.Column<int>(type: "int", nullable: false),
                    BaseAwayWeight = table.Column<int>(type: "int", nullable: false),
                    AiHome = table.Column<int>(type: "int", nullable: false),
                    AiDraw = table.Column<int>(type: "int", nullable: false),
                    AiAway = table.Column<int>(type: "int", nullable: false),
                    AiSummary = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    DefaultCups = table.Column<int>(type: "int", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    ResultChoice = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ResultUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaMatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeoBiaPlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalBets = table.Column<int>(type: "int", nullable: false),
                    TotalCups = table.Column<int>(type: "int", nullable: false),
                    CorrectBets = table.Column<int>(type: "int", nullable: false),
                    WrongBets = table.Column<int>(type: "int", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaPlayers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeoBiaBets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Cups = table.Column<int>(type: "int", nullable: false),
                    IsSettled = table.Column<bool>(type: "bit", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaBets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeoBiaBets_KeoBiaMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "KeoBiaMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KeoBiaBets_KeoBiaPlayers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "KeoBiaPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_MatchId",
                table: "KeoBiaBets",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_PlayerId",
                table: "KeoBiaBets",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_SiteId",
                table: "KeoBiaBets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_SiteId_MatchId_CreatedAt",
                table: "KeoBiaBets",
                columns: new[] { "SiteId", "MatchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_SiteId_PlayerId_CreatedAt",
                table: "KeoBiaBets",
                columns: new[] { "SiteId", "PlayerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaImportJobs_SiteId",
                table: "KeoBiaImportJobs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaMatches_SiteId",
                table: "KeoBiaMatches",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaMatches_SiteId_ExternalId",
                table: "KeoBiaMatches",
                columns: new[] { "SiteId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaPlayers_SiteId",
                table: "KeoBiaPlayers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaPlayers_SiteId_PublicKey",
                table: "KeoBiaPlayers",
                columns: new[] { "SiteId", "PublicKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeoBiaBets");

            migrationBuilder.DropTable(
                name: "KeoBiaImportJobs");

            migrationBuilder.DropTable(
                name: "KeoBiaMatches");

            migrationBuilder.DropTable(
                name: "KeoBiaPlayers");
        }
    }
}
