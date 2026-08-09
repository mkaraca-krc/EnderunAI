using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyExpenseAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AccommodationCost",
                table: "personnel_duties",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceiptAmount",
                table: "personnel_duties",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SettlementAdvanceId",
                table: "personnel_duties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementAtUtc",
                table: "personnel_duties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SettlementByUserId",
                table: "personnel_duties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SettlementDecision",
                table: "personnel_duties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementNote",
                table: "personnel_duties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TravelCost",
                table: "personnel_duties",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccommodationCost",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "ReceiptAmount",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "SettlementAdvanceId",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "SettlementAtUtc",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "SettlementByUserId",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "SettlementDecision",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "SettlementNote",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "TravelCost",
                table: "personnel_duties");
        }
    }
}
