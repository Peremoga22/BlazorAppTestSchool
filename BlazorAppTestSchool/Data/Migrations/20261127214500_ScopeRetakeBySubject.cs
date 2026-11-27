using BlazorAppTestSchool.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorAppTestSchool.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261127214500_ScopeRetakeBySubject")]
    public class ScopeRetakeBySubject : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "BiologyRetakeApprovals",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Subject",
                table: "BiologyRetakeApprovals");
        }
    }
}
