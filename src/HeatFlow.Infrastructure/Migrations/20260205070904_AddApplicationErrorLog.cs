using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationErrorLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationErrorLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExceptionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationErrorLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationErrorLog_OccurredAtUtc",
                table: "ApplicationErrorLog",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationErrorLog_Origin",
                table: "ApplicationErrorLog",
                column: "Origin");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationErrorLog_Phase",
                table: "ApplicationErrorLog",
                column: "Phase");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationErrorLog_Source",
                table: "ApplicationErrorLog",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationErrorLog");
        }
    }
}
