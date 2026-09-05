using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(NewsCMS.Infrastructure.Persistence.AppDbContext))]
    [Migration("20260622013114_AddUniqueKeoBiaBetPerPlayerMatch")]
    public partial class AddUniqueKeoBiaBetPerPlayerMatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ;WITH RankedBets AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [SiteId], [PlayerId], [MatchId]
                            ORDER BY [CreatedAt], [Id]
                        ) AS [rn]
                    FROM [KeoBiaBets]
                )
                DELETE FROM [KeoBiaBets]
                WHERE [Id] IN (SELECT [Id] FROM RankedBets WHERE [rn] > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBets_SiteId_PlayerId_MatchId",
                table: "KeoBiaBets",
                columns: new[] { "SiteId", "PlayerId", "MatchId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KeoBiaBets_SiteId_PlayerId_MatchId",
                table: "KeoBiaBets");
        }
    }
}
