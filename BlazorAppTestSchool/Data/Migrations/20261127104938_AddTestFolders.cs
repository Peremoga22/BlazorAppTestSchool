using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorAppTestSchool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "TestDefinitions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestFolders", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestFolders");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "TestDefinitions");
        }
    }
}
