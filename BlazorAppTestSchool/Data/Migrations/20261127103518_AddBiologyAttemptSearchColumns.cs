using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorAppTestSchool.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBiologyAttemptSearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Grade",
                table: "BiologyAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StudentName",
                table: "BiologyAttempts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Backfill the new columns for existing rows from the JSON report they already store.
            migrationBuilder.Sql("""
                UPDATE BiologyAttempts
                SET Grade = COALESCE(TRY_CAST(JSON_VALUE(ReportJson, '$.Grade') AS int), 0),
                    StudentName = COALESCE(JSON_VALUE(ReportJson, '$.StudentName'), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Grade",
                table: "BiologyAttempts");

            migrationBuilder.DropColumn(
                name: "StudentName",
                table: "BiologyAttempts");
        }
    }
}
