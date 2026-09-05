using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeoBiaChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeoBiaChatMessages_KeoBiaPlayers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "KeoBiaPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaChatMessages_PlayerId",
                table: "KeoBiaChatMessages",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaChatMessages_SiteId",
                table: "KeoBiaChatMessages",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaChatMessages_SiteId_CreatedAt",
                table: "KeoBiaChatMessages",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaChatMessages_SiteId_PlayerId_CreatedAt",
                table: "KeoBiaChatMessages",
                columns: new[] { "SiteId", "PlayerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeoBiaChatMessages");
        }
    }
}
