using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCostAccountingLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountingVoucherLineId",
                table: "ProjectCostTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostTransactions_AccountingVoucherLineId",
                table: "ProjectCostTransactions",
                column: "AccountingVoucherLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCostTransactions_accounting_voucher_lines_Accounting~",
                table: "ProjectCostTransactions",
                column: "AccountingVoucherLineId",
                principalTable: "accounting_voucher_lines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCostTransactions_accounting_voucher_lines_Accounting~",
                table: "ProjectCostTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCostTransactions_AccountingVoucherLineId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropColumn(
                name: "AccountingVoucherLineId",
                table: "ProjectCostTransactions");
        }
    }
}
