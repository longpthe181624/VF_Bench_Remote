using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentCategory",
                table: "TepDuLieuChungs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentFunction",
                table: "TepDuLieuChungs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentProgram",
                table: "TepDuLieuChungs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "TepDuLieuChungs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentCategory",
                table: "TepDuLieuChungs");

            migrationBuilder.DropColumn(
                name: "DocumentFunction",
                table: "TepDuLieuChungs");

            migrationBuilder.DropColumn(
                name: "DocumentProgram",
                table: "TepDuLieuChungs");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "TepDuLieuChungs");
        }
    }
}
