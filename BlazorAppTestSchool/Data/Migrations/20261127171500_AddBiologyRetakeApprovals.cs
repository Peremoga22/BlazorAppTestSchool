using BlazorAppTestSchool.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorAppTestSchool.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261127171500_AddBiologyRetakeApprovals")]
    public class AddBiologyRetakeApprovals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BiologyRetakeApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastNameKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiologyRetakeApprovals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BiologyRetakeApprovals_LastNameKey_Grade",
                table: "BiologyRetakeApprovals",
                columns: new[] { "LastNameKey", "Grade" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BiologyRetakeApprovals");
        }
    }
}
