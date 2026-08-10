using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChequeVoidAndReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "cheques",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "cheques",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "cheques",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "cheque_movements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalVoucherId",
                table: "cheque_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAtUtc",
                table: "cheque_movements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                table: "cheque_movements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "cheque_movements");

            migrationBuilder.DropColumn(
                name: "ReversalVoucherId",
                table: "cheque_movements");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                table: "cheque_movements");

            migrationBuilder.DropColumn(
                name: "ReversedByUserId",
                table: "cheque_movements");
        }
    }
}
