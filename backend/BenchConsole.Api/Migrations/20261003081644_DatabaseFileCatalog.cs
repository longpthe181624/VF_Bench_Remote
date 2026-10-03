using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseFileCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    NguoiThayDoi = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThayDoiLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ma = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    TenFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    PhienBan = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KichThuoc = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    NguoiTaiLen = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TaiLenLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NguoiThayDoi = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThayDoiLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatabaseFiles_DatabaseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "DatabaseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseFiles_DatabaseModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "DatabaseModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseFiles_DatabaseTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "DatabaseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseCategories_Ma",
                table: "DatabaseCategories",
                column: "Ma",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseCategories_Ten",
                table: "DatabaseCategories",
                column: "Ten",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseChanges_FileId_Id",
                table: "DatabaseChanges",
                columns: new[] { "FileId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseChanges_PublishedAt",
                table: "DatabaseChanges",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseFiles_CategoryId",
                table: "DatabaseFiles",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseFiles_ModelId_CategoryId_TypeId_TenFile_PhienBan",
                table: "DatabaseFiles",
                columns: new[] { "ModelId", "CategoryId", "TypeId", "TenFile", "PhienBan" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseFiles_Status",
                table: "DatabaseFiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseFiles_TypeId",
                table: "DatabaseFiles",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseModels_Ma",
                table: "DatabaseModels",
                column: "Ma",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseModels_Ten",
                table: "DatabaseModels",
                column: "Ten",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseTypes_Ma",
                table: "DatabaseTypes",
                column: "Ma",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseTypes_Ten",
                table: "DatabaseTypes",
                column: "Ten",
                unique: true);
            // Chỉ tạo một lần; tôn trọng các mục Admin đã xoá.
            migrationBuilder.InsertData("DatabaseModels", new[] { "Id", "Ma", "Ten" }, new object[,] { { 1, "VF6", "VF6" }, { 2, "XMD", "XMD" } });
            migrationBuilder.InsertData("DatabaseCategories", new[] { "Id", "Ma", "Ten" }, new object[,] { { 1, "ALL", "Áp dụng chung" }, { 2, "MHU", "MHU" }, { 3, "IPC", "IPC" } });
            migrationBuilder.InsertData("DatabaseTypes", new[] { "Id", "Ma", "Ten" }, new object[,] { { 1, "DIAGNOSTIC", "Diagnostic" }, { 2, "DBC", "DBC" } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseChanges");

            migrationBuilder.DropTable(
                name: "DatabaseFiles");

            migrationBuilder.DropTable(
                name: "DatabaseCategories");

            migrationBuilder.DropTable(
                name: "DatabaseModels");

            migrationBuilder.DropTable(
                name: "DatabaseTypes");
        }
    }
}
