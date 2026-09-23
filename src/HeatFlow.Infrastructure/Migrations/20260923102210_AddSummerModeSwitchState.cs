using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSummerModeSwitchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SummerModeSwitchState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsSummer = table.Column<bool>(type: "bit", nullable: false),
                    SinceUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummerModeSwitchState", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SummerModeSwitchState");
        }
    }
}
