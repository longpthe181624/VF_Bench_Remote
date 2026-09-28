using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BaoCaoChays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CmdId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BenchCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TestCase = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KichThuoc = table.Column<long>(type: "bigint", nullable: false),
                    NhanLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaoCaoChays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Benches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Workshop = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Firmware = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TopicPrefix = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenMay = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastPacketAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PrimaryChannel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PrimaryValue = table.Column<double>(type: "float", nullable: true),
                    PrimaryUnit = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CurrentTestCase = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrentPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrentStep = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Benches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoiTestCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Loai = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Ten = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenFileGoc = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KichThuoc = table.Column<long>(type: "bigint", nullable: false),
                    SoTestCase = table.Column<int>(type: "int", nullable: false),
                    NguoiTaiLen = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TaiLenLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoiTestCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TepNguoiDungs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NguoiDung = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KichThuoc = table.Column<long>(type: "bigint", nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TaiLenLuc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TepNguoiDungs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Benches_BenchId",
                        column: x => x.BenchId,
                        principalTable: "Benches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CmdId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BenchId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TestCase = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commands_Benches_BenchId",
                        column: x => x.BenchId,
                        principalTable: "Benches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchId = table.Column<int>(type: "int", nullable: false),
                    CmdId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TestCase = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Verdict = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RunBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_Benches_BenchId",
                        column: x => x.BenchId,
                        principalTable: "Benches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_BenchId_ClosedAt",
                table: "Alerts",
                columns: new[] { "BenchId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BaoCaoChays_CmdId_TenFile_Sha256",
                table: "BaoCaoChays",
                columns: new[] { "CmdId", "TenFile", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Benches_Code",
                table: "Benches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commands_BenchId",
                table: "Commands",
                column: "BenchId");

            migrationBuilder.CreateIndex(
                name: "IX_Commands_CmdId",
                table: "Commands",
                column: "CmdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoiTestCases_Loai_Ten",
                table: "GoiTestCases",
                columns: new[] { "Loai", "Ten" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_BenchId_FinishedAt",
                table: "Runs",
                columns: new[] { "BenchId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySamples_BenchId_Channel_At",
                table: "TelemetrySamples",
                columns: new[] { "BenchId", "Channel", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_TepNguoiDungs_NguoiDung_TenFile_Sha256",
                table: "TepNguoiDungs",
                columns: new[] { "NguoiDung", "TenFile", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "BaoCaoChays");

            migrationBuilder.DropTable(
                name: "Commands");

            migrationBuilder.DropTable(
                name: "GoiTestCases");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "TelemetrySamples");

            migrationBuilder.DropTable(
                name: "TepNguoiDungs");

            migrationBuilder.DropTable(
                name: "Benches");
        }
    }
}
