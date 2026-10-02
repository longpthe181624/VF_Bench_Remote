using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class BatBuocTotp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: false ở đây là ĐÚNG Ý, không phải cái bẫy như lần
            // HoTroRemote. Entity khai `= true` nên tài khoản tạo mới luôn bị
            // bắt ghi danh; còn tài khoản đã có trên máy A giữ false, vì bật
            // đồng loạt là khoá luôn người đang trực nếu lúc đó họ không cầm
            // điện thoại. Muốn bật cho tất cả thì chạy UPDATE, xem CLAUDE.md.
            migrationBuilder.AddColumn<bool>(
                name: "TotpBatBuoc",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotpBatBuoc",
                table: "Users");
        }
    }
}
