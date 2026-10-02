using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class KieuTestGoi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KieuTest",
                table: "GoiTestCases",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KieuTest",
                table: "GoiTestCases");
        }
    }
}
