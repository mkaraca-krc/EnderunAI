using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptAccountingVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountingVoucherId",
                table: "goods_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_AccountingVoucherId",
                table: "goods_receipts",
                column: "AccountingVoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_goods_receipts_accounting_vouchers_AccountingVoucherId",
                table: "goods_receipts",
                column: "AccountingVoucherId",
                principalTable: "accounting_vouchers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_goods_receipts_accounting_vouchers_AccountingVoucherId",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "IX_goods_receipts_AccountingVoucherId",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "AccountingVoucherId",
                table: "goods_receipts");
        }
    }
}
