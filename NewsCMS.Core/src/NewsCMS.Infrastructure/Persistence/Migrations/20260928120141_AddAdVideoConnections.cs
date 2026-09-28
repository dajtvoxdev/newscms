using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdVideoConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdVideoConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OperatorKeyEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OperatorKeyPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdVideoConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteAdVideoTenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdVideoTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteAdVideoTenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteAdVideoTenants_SiteId",
                table: "SiteAdVideoTenants",
                column: "SiteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdVideoConnections");

            migrationBuilder.DropTable(
                name: "SiteAdVideoTenants");
        }
    }
}
