using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSummerModeBoilerSetpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BoilerSetpointBeforeSummer",
                table: "SummerModeSwitchState",
                type: "decimal(4,1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SummerModeBoilerSetpoint",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoilerSetpointBeforeSummer",
                table: "SummerModeSwitchState");

            migrationBuilder.DropColumn(
                name: "SummerModeBoilerSetpoint",
                table: "HeatingParameters");
        }
    }
}
