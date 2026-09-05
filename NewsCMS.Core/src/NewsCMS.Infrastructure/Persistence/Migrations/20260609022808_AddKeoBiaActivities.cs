using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeoBiaActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivityType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PlayerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Badge = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ChoiceLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Cups = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaActivities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaActivities_SiteId",
                table: "KeoBiaActivities",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaActivities_SiteId_ActivityType_CreatedAt",
                table: "KeoBiaActivities",
                columns: new[] { "SiteId", "ActivityType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaActivities_SiteId_CreatedAt",
                table: "KeoBiaActivities",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaActivities_SiteId_MatchId_CreatedAt",
                table: "KeoBiaActivities",
                columns: new[] { "SiteId", "MatchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaActivities_SiteId_PlayerId_CreatedAt",
                table: "KeoBiaActivities",
                columns: new[] { "SiteId", "PlayerId", "CreatedAt" });

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [KeoBiaActivities])
                BEGIN
                    INSERT INTO [KeoBiaActivities] ([Id], [SiteId], [PlayerId], [MatchId], [ActivityType], [PlayerName], [AvatarUrl], [Text], [Badge], [Choice], [ChoiceLabel], [Cups], [CreatedAt], [UpdatedAt])
                    SELECT
                        NEWID(),
                        [p].[SiteId],
                        [p].[Id],
                        NULL,
                        N'join',
                        [p].[DisplayName],
                        [p].[AvatarUrl],
                        CONCAT([p].[DisplayName], N' vừa vào bàn vui.'),
                        N'waving_hand',
                        N'draw',
                        N'Chào mừng',
                        NULL,
                        [p].[CreatedAt],
                        [p].[UpdatedAt]
                    FROM [KeoBiaPlayers] AS [p];

                    INSERT INTO [KeoBiaActivities] ([Id], [SiteId], [PlayerId], [MatchId], [ActivityType], [PlayerName], [AvatarUrl], [Text], [Badge], [Choice], [ChoiceLabel], [Cups], [CreatedAt], [UpdatedAt])
                    SELECT
                        NEWID(),
                        [b].[SiteId],
                        [b].[PlayerId],
                        [b].[MatchId],
                        N'prediction',
                        [p].[DisplayName],
                        [p].[AvatarUrl],
                        CONCAT(
                            [p].[DisplayName],
                            N' vừa gửi ',
                            CONVERT(nvarchar(12), [b].[Cups]),
                            N' cốc cho ',
                            CASE
                                WHEN [b].[Choice] = N'home' THEN [m].[HomeCode]
                                WHEN [b].[Choice] = N'away' THEN [m].[AwayCode]
                                ELSE N'Hòa'
                            END,
                            N' trận ',
                            [m].[HomeName],
                            N' vs ',
                            [m].[AwayName],
                            N'.'),
                        N'sports_bar',
                        [b].[Choice],
                        CASE
                            WHEN [b].[Choice] = N'home' THEN [m].[HomeCode]
                            WHEN [b].[Choice] = N'away' THEN [m].[AwayCode]
                            ELSE N'Hòa'
                        END,
                        [b].[Cups],
                        [b].[CreatedAt],
                        [b].[UpdatedAt]
                    FROM [KeoBiaBets] AS [b]
                    INNER JOIN [KeoBiaPlayers] AS [p] ON [p].[Id] = [b].[PlayerId]
                    INNER JOIN [KeoBiaMatches] AS [m] ON [m].[Id] = [b].[MatchId];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeoBiaActivities");
        }
    }
}
