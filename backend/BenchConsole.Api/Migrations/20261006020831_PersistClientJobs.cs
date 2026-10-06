using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BenchConsole.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistClientJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientCaseId",
                table: "Runs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultHash",
                table: "Runs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestJobId",
                table: "Runs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestRequestFileId",
                table: "Runs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevicesJson",
                table: "ClientApiKeys",
                type: "nvarchar(max)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "TestJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TestRequestId = table.Column<int>(type: "int", nullable: false),
                    Device = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ActiveDevice = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotBefore = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    FinishHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestJobs_TestRequests_TestRequestId",
                        column: x => x.TestRequestId,
                        principalTable: "TestRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestResultBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TestJobId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestResultBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestResultBatches_TestJobs_TestJobId",
                        column: x => x.TestJobId,
                        principalTable: "TestJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_TestJobId_TestRequestFileId_ClientCaseId",
                table: "Runs",
                columns: new[] { "TestJobId", "TestRequestFileId", "ClientCaseId" },
                unique: true,
                filter: "[TestJobId] IS NOT NULL AND [ClientCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_TestRequestFileId",
                table: "Runs",
                column: "TestRequestFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TestJobs_ActiveDevice",
                table: "TestJobs",
                column: "ActiveDevice",
                unique: true,
                filter: "[ActiveDevice] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TestJobs_Code",
                table: "TestJobs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestJobs_State_NotBefore",
                table: "TestJobs",
                columns: new[] { "State", "NotBefore" });

            migrationBuilder.CreateIndex(
                name: "IX_TestJobs_TestRequestId",
                table: "TestJobs",
                column: "TestRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestResultBatches_TestJobId_BatchId",
                table: "TestResultBatches",
                columns: new[] { "TestJobId", "BatchId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_TestJobs_TestJobId",
                table: "Runs",
                column: "TestJobId",
                principalTable: "TestJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_TestRequestFiles_TestRequestFileId",
                table: "Runs",
                column: "TestRequestFileId",
                principalTable: "TestRequestFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runs_TestJobs_TestJobId",
                table: "Runs");

            migrationBuilder.DropForeignKey(
                name: "FK_Runs_TestRequestFiles_TestRequestFileId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "TestResultBatches");

            migrationBuilder.DropTable(
                name: "TestJobs");

            migrationBuilder.DropIndex(
                name: "IX_Runs_TestJobId_TestRequestFileId_ClientCaseId",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Runs_TestRequestFileId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ClientCaseId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ResultHash",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "TestJobId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "TestRequestFileId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "DevicesJson",
                table: "ClientApiKeys");
        }
    }
}
