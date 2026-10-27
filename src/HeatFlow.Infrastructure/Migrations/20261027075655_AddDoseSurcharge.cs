using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDoseSurcharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DoseVentilationSurchargePct",
                table: "HeatingParameters",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LastSurchargePct",
                table: "DoseState",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SurchargeActiveDate",
                table: "DoseState",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoseVentilationSurchargePct",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "LastSurchargePct",
                table: "DoseState");

            migrationBuilder.DropColumn(
                name: "SurchargeActiveDate",
                table: "DoseState");
        }
    }
}
