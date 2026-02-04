using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedHeatingParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeficitMediumP1",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DeficitMediumP2",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DeficitMediumP3",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ForecastExtremeColdThreshold",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ForecastExtremeWarmThreshold",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "TopRoomsCount",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ValveClosedTemp",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ValveTempOffset",
                table: "HeatingParameters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeficitMediumP1",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeficitMediumP2",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeficitMediumP3",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ForecastExtremeColdThreshold",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ForecastExtremeWarmThreshold",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TopRoomsCount",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ValveClosedTemp",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ValveTempOffset",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
