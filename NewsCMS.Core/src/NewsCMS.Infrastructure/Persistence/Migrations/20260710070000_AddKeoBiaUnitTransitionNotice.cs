using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260710070000_AddKeoBiaUnitTransitionNotice")]
    public partial class AddKeoBiaUnitTransitionNotice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StoppedPlayingAt",
                table: "KeoBiaPlayers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnitTransitionNoticeAcknowledgedAt",
                table: "KeoBiaPlayers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnitTransitionTelegramSentAt",
                table: "KeoBiaPlayers",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoppedPlayingAt",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "UnitTransitionNoticeAcknowledgedAt",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "UnitTransitionTelegramSentAt",
                table: "KeoBiaPlayers");
        }
    }
}
