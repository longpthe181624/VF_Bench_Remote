using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class ThietBiChung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HoTroRemote",
                table: "Benches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // EF sinh defaultValue theo giá trị mặc định của kiểu bool (false),
            // KHÔNG theo `= true` khai trong entity. Để nguyên thì mọi bench đã
            // đăng ký trên máy A bỗng thành "không có agent": lệnh chạy trả 409
            // và vòng quét bỏ qua chúng — hỏng im lặng, thẻ bench vẫn xanh.
            //
            // Bench đã có từ trước đều là bench chạy qua agent, nên bật hết lên.
            migrationBuilder.Sql("UPDATE Benches SET HoTroRemote = 1");

            migrationBuilder.AddColumn<bool>(
                name: "HoTroRobot",
                table: "Benches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Loai",
                table: "Benches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Tang",
                table: "Benches",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThuocVeId",
                table: "Benches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DuAns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TaoLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuAns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThietBiDuAns",
                columns: table => new
                {
                    BenchId = table.Column<int>(type: "int", nullable: false),
                    DuAnId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThietBiDuAns", x => new { x.BenchId, x.DuAnId });
                    table.ForeignKey(
                        name: "FK_ThietBiDuAns_Benches_BenchId",
                        column: x => x.BenchId,
                        principalTable: "Benches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThietBiDuAns_DuAns_DuAnId",
                        column: x => x.DuAnId,
                        principalTable: "DuAns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Benches_Loai",
                table: "Benches",
                column: "Loai");

            migrationBuilder.CreateIndex(
                name: "IX_Benches_ThuocVeId",
                table: "Benches",
                column: "ThuocVeId");

            migrationBuilder.CreateIndex(
                name: "IX_DuAns_Ma",
                table: "DuAns",
                column: "Ma",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThietBiDuAns_DuAnId",
                table: "ThietBiDuAns",
                column: "DuAnId");

            migrationBuilder.AddForeignKey(
                name: "FK_Benches_Benches_ThuocVeId",
                table: "Benches",
                column: "ThuocVeId",
                principalTable: "Benches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Benches_Benches_ThuocVeId",
                table: "Benches");

            migrationBuilder.DropTable(
                name: "ThietBiDuAns");

            migrationBuilder.DropTable(
                name: "DuAns");

            migrationBuilder.DropIndex(
                name: "IX_Benches_Loai",
                table: "Benches");

            migrationBuilder.DropIndex(
                name: "IX_Benches_ThuocVeId",
                table: "Benches");

            migrationBuilder.DropColumn(
                name: "HoTroRemote",
                table: "Benches");

            migrationBuilder.DropColumn(
                name: "HoTroRobot",
                table: "Benches");

            migrationBuilder.DropColumn(
                name: "Loai",
                table: "Benches");

            migrationBuilder.DropColumn(
                name: "Tang",
                table: "Benches");

            migrationBuilder.DropColumn(
                name: "ThuocVeId",
                table: "Benches");
        }
    }
}
