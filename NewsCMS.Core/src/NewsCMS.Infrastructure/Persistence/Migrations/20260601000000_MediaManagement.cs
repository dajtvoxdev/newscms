using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsCMS.Infrastructure.Persistence;

#nullable disable

namespace NewsCMS.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260601000000_MediaManagement")]
    public partial class MediaManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns as nullable first so existing rows can be backfilled.
            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "Medias",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Medias",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Medias",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Medias",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Medias",
                type: "datetime2",
                nullable: true);

            // Backfill Kind from MimeType.
            migrationBuilder.Sql(@"
                UPDATE Medias SET Kind = CASE
                    WHEN MimeType LIKE 'image/%'  THEN 'image'
                    WHEN MimeType LIKE 'video/%'  THEN 'video'
                    WHEN MimeType LIKE 'audio/%'  THEN 'audio'
                    WHEN MimeType IN (
                        'application/pdf','application/msword',
                        'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
                        'application/vnd.ms-excel',
                        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                        'application/vnd.ms-powerpoint',
                        'application/vnd.openxmlformats-officedocument.presentationml.presentation',
                        'text/plain','application/rtf','application/zip') THEN 'document'
                    ELSE 'other'
                END
            ");

            // Backfill StorageKey: strip leading '/uploads/' prefix (9 chars) from FilePath.
            migrationBuilder.Sql(@"
                UPDATE Medias
                SET StorageKey = CASE
                    WHEN FilePath LIKE '/uploads/%' THEN SUBSTRING(FilePath, 10, LEN(FilePath))
                    ELSE FilePath
                END
            ");

            // Now enforce NOT NULL.
            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "Medias",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "Medias",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "other",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            // Also fix existing nvarchar(max) columns to use max-lengths from MediaConfiguration.
            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Medias",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "Medias",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "MimeType",
                table: "Medias",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AltText",
                table: "Medias",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            // Indexes.
            migrationBuilder.CreateIndex(
                name: "IX_Medias_FileName",
                table: "Medias",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_Medias_Kind",
                table: "Medias",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_Medias_IsDeleted",
                table: "Medias",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Medias_CreatedAt",
                table: "Medias",
                column: "CreatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Medias_FileName", table: "Medias");
            migrationBuilder.DropIndex(name: "IX_Medias_Kind", table: "Medias");
            migrationBuilder.DropIndex(name: "IX_Medias_IsDeleted", table: "Medias");
            migrationBuilder.DropIndex(name: "IX_Medias_CreatedAt", table: "Medias");

            migrationBuilder.AlterColumn<string>(
                name: "AltText", table: "Medias",
                type: "nvarchar(max)", nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500, oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MimeType", table: "Medias",
                type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(150)", oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "FilePath", table: "Medias",
                type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(1024)", oldMaxLength: 1024);

            migrationBuilder.AlterColumn<string>(
                name: "FileName", table: "Medias",
                type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(260)", oldMaxLength: 260);

            migrationBuilder.DropColumn(name: "StorageKey", table: "Medias");
            migrationBuilder.DropColumn(name: "Kind", table: "Medias");
            migrationBuilder.DropColumn(name: "Title", table: "Medias");
            migrationBuilder.DropColumn(name: "IsDeleted", table: "Medias");
            migrationBuilder.DropColumn(name: "DeletedAt", table: "Medias");
        }
    }
}
