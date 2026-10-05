using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistTestRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TestRequestId",
                table: "Commands",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Requester = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Device = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Flash = table.Column<bool>(type: "bit", nullable: false),
                    Timing = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestRequestFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TestRequestId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRequestFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestRequestFiles_TestRequests_TestRequestId",
                        column: x => x.TestRequestId,
                        principalTable: "TestRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commands_TestRequestId",
                table: "Commands",
                column: "TestRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRequestFiles_Kind_Sha256",
                table: "TestRequestFiles",
                columns: new[] { "Kind", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_TestRequestFiles_TestRequestId_Kind_SourceId",
                table: "TestRequestFiles",
                columns: new[] { "TestRequestId", "Kind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRequests_Code",
                table: "TestRequests",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRequests_CreatedAt",
                table: "TestRequests",
                column: "CreatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Commands_TestRequests_TestRequestId",
                table: "Commands",
                column: "TestRequestId",
                principalTable: "TestRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commands_TestRequests_TestRequestId",
                table: "Commands");

            migrationBuilder.DropTable(
                name: "TestRequestFiles");

            migrationBuilder.DropTable(
                name: "TestRequests");

            migrationBuilder.DropIndex(
                name: "IX_Commands_TestRequestId",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "TestRequestId",
                table: "Commands");
        }
    }
}
