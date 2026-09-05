using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArExperiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArExperiences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetImageUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MindFileUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    VideoUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    VideoWidth = table.Column<double>(type: "float", nullable: false),
                    VideoHeight = table.Column<double>(type: "float", nullable: false),
                    FallbackPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArExperiences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArExperiences_Slug",
                table: "ArExperiences",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArExperiences");
        }
    }
}
