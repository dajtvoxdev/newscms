using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260702093000_AddKeoBiaBeerPayments")]
    public partial class AddKeoBiaBeerPayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeoBiaBeerPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Cups = table.Column<int>(type: "int", nullable: false),
                    CoinAmount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QrUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransferContent = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ProviderPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeoBiaBeerPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeoBiaBeerPayments_KeoBiaPlayers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "KeoBiaPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBeerPayments_PlayerId",
                table: "KeoBiaBeerPayments",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBeerPayments_SiteId",
                table: "KeoBiaBeerPayments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBeerPayments_SiteId_Code",
                table: "KeoBiaBeerPayments",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBeerPayments_SiteId_Status_CreatedAt",
                table: "KeoBiaBeerPayments",
                columns: new[] { "SiteId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KeoBiaBeerPayments_SiteId_TransferContent",
                table: "KeoBiaBeerPayments",
                columns: new[] { "SiteId", "TransferContent" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "KeoBiaBeerPayments");
        }
    }
}
