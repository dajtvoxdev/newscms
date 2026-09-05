using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSitesAndFeatureModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeatureModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PrimaryDomain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DefaultTheme = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteFeatureModules",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EnabledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteFeatureModules", x => new { x.SiteId, x.FeatureModuleId });
                    table.ForeignKey(
                        name: "FK_SiteFeatureModules_FeatureModules_FeatureModuleId",
                        column: x => x.FeatureModuleId,
                        principalTable: "FeatureModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteFeatureModules_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SiteId",
                table: "Users",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureModules_Code",
                table: "FeatureModules",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteFeatureModules_FeatureModuleId",
                table: "SiteFeatureModules",
                column: "FeatureModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_PrimaryDomain",
                table: "Sites",
                column: "PrimaryDomain",
                unique: true,
                filter: "[PrimaryDomain] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Sites_SiteId",
                table: "Users",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Sites_SiteId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "SiteFeatureModules");

            migrationBuilder.DropTable(
                name: "FeatureModules");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_Users_SiteId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Users");
        }
    }
}
