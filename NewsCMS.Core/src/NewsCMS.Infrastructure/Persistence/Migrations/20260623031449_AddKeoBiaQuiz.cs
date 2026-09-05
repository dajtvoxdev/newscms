using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeoBiaQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeoBiaQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChoicesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RewardCups = table.Column<int>(type: "int", nullable: false),
                    CorrectChoiceKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ClosesAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevealedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TelegramChatId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeoBiaQuestionVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChoiceKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsSettled = table.Column<bool>(type: "bit", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RewardedCups = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaQuestionVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeoBiaQuestionVotes_KeoBiaPlayers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "KeoBiaPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KeoBiaQuestionVotes_KeoBiaQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "KeoBiaQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestions_SiteId",
                table: "KeoBiaQuestions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestions_SiteId_Status_CreatedAt",
                table: "KeoBiaQuestions",
                columns: new[] { "SiteId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestionVotes_PlayerId",
                table: "KeoBiaQuestionVotes",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestionVotes_QuestionId",
                table: "KeoBiaQuestionVotes",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestionVotes_SiteId",
                table: "KeoBiaQuestionVotes",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestionVotes_SiteId_QuestionId",
                table: "KeoBiaQuestionVotes",
                columns: new[] { "SiteId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaQuestionVotes_SiteId_QuestionId_PlayerId",
                table: "KeoBiaQuestionVotes",
                columns: new[] { "SiteId", "QuestionId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeoBiaQuestionVotes");

            migrationBuilder.DropTable(
                name: "KeoBiaQuestions");
        }
    }
}
