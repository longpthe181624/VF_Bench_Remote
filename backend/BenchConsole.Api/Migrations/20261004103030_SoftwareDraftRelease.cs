using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class SoftwareDraftRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "TepDuLieuChungs",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "TepDuLieuChungs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Draft");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "TepDuLieuChungs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TepDuLieuChungs");
        }
    }
}
