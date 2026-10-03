using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class SoftwareFileTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SoftwareTypeId",
                table: "TepDuLieuChungs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SoftwareTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TepDuLieuChungs_SoftwareTypeId",
                table: "TepDuLieuChungs",
                column: "SoftwareTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareTypes_Ma",
                table: "SoftwareTypes",
                column: "Ma",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareTypes_Ten",
                table: "SoftwareTypes",
                column: "Ten",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TepDuLieuChungs_SoftwareTypes_SoftwareTypeId",
                table: "TepDuLieuChungs",
                column: "SoftwareTypeId",
                principalTable: "SoftwareTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Chỉ seed một lần, không phục hồi Type Admin đã xoá.
            migrationBuilder.InsertData("SoftwareTypes", new[] { "Id", "Ma", "Ten" }, new object[,] { { 1, "ung-dung", "Ứng dụng" }, { 2, "lib", "Lib" }, { 3, "public", "Public" } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TepDuLieuChungs_SoftwareTypes_SoftwareTypeId",
                table: "TepDuLieuChungs");

            migrationBuilder.DropTable(
                name: "SoftwareTypes");

            migrationBuilder.DropIndex(
                name: "IX_TepDuLieuChungs_SoftwareTypeId",
                table: "TepDuLieuChungs");

            migrationBuilder.DropColumn(
                name: "SoftwareTypeId",
                table: "TepDuLieuChungs");
        }
    }
}
