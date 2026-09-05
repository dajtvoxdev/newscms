using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260702105000_AddKeoBiaQuizPenaltyCups")]
    public partial class AddKeoBiaQuizPenaltyCups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PenaltyCups",
                table: "KeoBiaQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PenaltyCups",
                table: "KeoBiaQuestions");
        }
    }
}
