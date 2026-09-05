using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixAiFilteredIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSkills_Key",
                table: "AiSkills");

            migrationBuilder.CreateIndex(
                name: "IX_AiSkills_Key",
                table: "AiSkills",
                column: "Key",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_IsDefault",
                table: "AiConnections",
                column: "IsDefault",
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSkills_Key",
                table: "AiSkills");

            migrationBuilder.DropIndex(
                name: "IX_AiConnections_IsDefault",
                table: "AiConnections");

            migrationBuilder.CreateIndex(
                name: "IX_AiSkills_Key",
                table: "AiSkills",
                column: "Key",
                unique: true);
        }
    }
}
