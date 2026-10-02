using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class MucDuLieuChung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MucDuLieuChungs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ThuTu = table.Column<int>(type: "int", nullable: false),
                    MacDinh = table.Column<bool>(type: "bit", nullable: false),
                    TaoLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MucDuLieuChungs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MucDuLieuChungs_Ma",
                table: "MucDuLieuChungs",
                column: "Ma",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MucDuLieuChungs");
        }
    }
}
