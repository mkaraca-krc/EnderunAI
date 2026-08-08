using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequestRejectionAndRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "purchase_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "purchase_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "purchase_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "purchase_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAtUtc",
                table: "purchase_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByUserId",
                table: "purchase_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionCount",
                table: "purchase_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "ReturnedAtUtc",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "purchase_requests");

            migrationBuilder.DropColumn(
                name: "RevisionCount",
                table: "purchase_requests");
        }
    }
}
