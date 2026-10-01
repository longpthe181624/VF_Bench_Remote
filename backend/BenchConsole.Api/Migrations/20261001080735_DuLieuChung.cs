using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class DuLieuChung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TepDuLieuChungs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Loai = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KichThuoc = table.Column<long>(type: "bigint", nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    NguoiTaiLen = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TaiLenLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TepDuLieuChungs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TepDuLieuChungs_Loai",
                table: "TepDuLieuChungs",
                column: "Loai");

            migrationBuilder.CreateIndex(
                name: "IX_TepDuLieuChungs_Loai_Ten",
                table: "TepDuLieuChungs",
                columns: new[] { "Loai", "Ten" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TepDuLieuChungs");
        }
    }
}
