using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260708013000_AddKeoBiaPenaltyScore")]
    public partial class AddKeoBiaPenaltyScore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PenaltyAwayScore",
                table: "KeoBiaMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyHomeScore",
                table: "KeoBiaMatches",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PenaltyAwayScore",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "PenaltyHomeScore",
                table: "KeoBiaMatches");
        }
    }
}
