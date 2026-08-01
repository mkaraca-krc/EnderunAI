using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBankAccountLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "PaymentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_BankAccountId",
                table: "PaymentRequests",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRequests_BankAccounts_BankAccountId",
                table: "PaymentRequests",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRequests_BankAccounts_BankAccountId",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_BankAccountId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "PaymentRequests");
        }
    }
}
