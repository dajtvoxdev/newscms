using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorSessionsAndCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(280)", maxLength: 280, nullable: false),
                    SignatureKind = table.Column<int>(type: "int", nullable: false),
                    SignatureUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SignerKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commitments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitorSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitorKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DayBucket = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PageViews = table.Column<int>(type: "int", nullable: false),
                    IsAuthenticated = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_CreatedAt",
                table: "Commitments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_SignerKey",
                table: "Commitments",
                column: "SignerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitorSessions_DayBucket_VisitorKey",
                table: "VisitorSessions",
                columns: new[] { "DayBucket", "VisitorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitorSessions_LastSeenUtc",
                table: "VisitorSessions",
                column: "LastSeenUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Commitments");

            migrationBuilder.DropTable(
                name: "VisitorSessions");
        }
    }
}
