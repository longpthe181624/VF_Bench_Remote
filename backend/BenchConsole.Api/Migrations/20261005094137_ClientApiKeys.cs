using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KeyId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    PermissionsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientApiKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientApiKeys_KeyId",
                table: "ClientApiKeys",
                column: "KeyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientApiKeys");
        }
    }
}
