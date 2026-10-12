using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class RevokeUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE [Users] SET [SecurityStamp] = REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '') WHERE [SecurityStamp] = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");
        }
    }
}
