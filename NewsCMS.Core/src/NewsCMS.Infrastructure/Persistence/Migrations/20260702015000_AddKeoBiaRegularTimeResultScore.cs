using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260702015000_AddKeoBiaRegularTimeResultScore")]
    public partial class AddKeoBiaRegularTimeResultScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultAwayScore",
                table: "KeoBiaMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultHomeScore",
                table: "KeoBiaMatches",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultAwayScore",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "ResultHomeScore",
                table: "KeoBiaMatches");
        }
    }
}
