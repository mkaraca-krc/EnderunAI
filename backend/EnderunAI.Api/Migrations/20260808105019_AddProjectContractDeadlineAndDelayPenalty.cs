using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectContractDeadlineAndDelayPenalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContractDeadlineDate",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DelayPenaltyCapRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DelayPenaltyKind",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DelayPenaltyValue",
                table: "projects",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractDeadlineDate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DelayPenaltyCapRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DelayPenaltyKind",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DelayPenaltyValue",
                table: "projects");
        }
    }
}
