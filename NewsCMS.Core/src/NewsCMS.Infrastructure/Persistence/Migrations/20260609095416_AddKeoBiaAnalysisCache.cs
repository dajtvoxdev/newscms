using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaAnalysisCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiAnalysisContent",
                table: "KeoBiaMatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiAnalysisExpiresAt",
                table: "KeoBiaMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiAnalysisGeneratedAt",
                table: "KeoBiaMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiAnalysisProbabilityJson",
                table: "KeoBiaMatches",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiAnalysisSource",
                table: "KeoBiaMatches",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAnalysisContent",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "AiAnalysisExpiresAt",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "AiAnalysisGeneratedAt",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "AiAnalysisProbabilityJson",
                table: "KeoBiaMatches");

            migrationBuilder.DropColumn(
                name: "AiAnalysisSource",
                table: "KeoBiaMatches");
        }
    }
}
