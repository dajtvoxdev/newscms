using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaTelegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelegramFirstName",
                table: "KeoBiaPlayers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramPhotoUrl",
                table: "KeoBiaPlayers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TelegramUserId",
                table: "KeoBiaPlayers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUsername",
                table: "KeoBiaPlayers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramVerifiedAt",
                table: "KeoBiaPlayers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaPlayers_SiteId_TelegramUserId",
                table: "KeoBiaPlayers",
                columns: new[] { "SiteId", "TelegramUserId" },
                unique: true,
                filter: "[TelegramUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KeoBiaPlayers_SiteId_TelegramUserId",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "TelegramFirstName",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "TelegramPhotoUrl",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "TelegramUserId",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "TelegramUsername",
                table: "KeoBiaPlayers");

            migrationBuilder.DropColumn(
                name: "TelegramVerifiedAt",
                table: "KeoBiaPlayers");
        }
    }
}
