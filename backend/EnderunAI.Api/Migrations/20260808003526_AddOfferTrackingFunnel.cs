using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferTrackingFunnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CounterpartyCurrentAccountId",
                table: "offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CounterpartyRole",
                table: "offers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "offers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LostReason",
                table: "offers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LostReasonNote",
                table: "offers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "offers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StatusChangedByUserId",
                table: "offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusNote",
                table: "offers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_offers_CompanyId_Status",
                table: "offers",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_offers_CounterpartyCurrentAccountId",
                table: "offers",
                column: "CounterpartyCurrentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_offers_current_accounts_CounterpartyCurrentAccountId",
                table: "offers",
                column: "CounterpartyCurrentAccountId",
                principalTable: "current_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_offers_current_accounts_CounterpartyCurrentAccountId",
                table: "offers");

            migrationBuilder.DropIndex(
                name: "IX_offers_CompanyId_Status",
                table: "offers");

            migrationBuilder.DropIndex(
                name: "IX_offers_CounterpartyCurrentAccountId",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "CounterpartyCurrentAccountId",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "CounterpartyRole",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "LostReason",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "LostReasonNote",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "StatusChangedByUserId",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "StatusNote",
                table: "offers");
        }
    }
}
