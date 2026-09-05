using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaCorrectScoreBets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectScoreOddsJson",
                table: "KeoBiaMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CorrectScoreOdds",
                table: "KeoBiaBets",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PredictedAwayScore",
                table: "KeoBiaBets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PredictedHomeScore",
                table: "KeoBiaBets",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectScoreOddsJson",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "CorrectScoreOdds",
                table: "KeoBiaBets");

            migrationBuilder.DropColumn(
                name: "PredictedAwayScore",
                table: "KeoBiaBets");

            migrationBuilder.DropColumn(
                name: "PredictedHomeScore",
                table: "KeoBiaBets");
        }
    }
}
