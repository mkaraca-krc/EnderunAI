using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReturnsAndReversal : Migration
    {
        /// <summary>
        /// Alış/satış iadesi ve kesinleşmiş fatura iptali.
        ///
        /// İade faturaları ayrı tabloda değil, aynı fatura tablosunda
        /// IsReturn + OriginalInvoiceId ile tutuluyor: cari bakiyesi,
        /// liste, onay akışı ve raporlar tek kaynaktan okusun.
        /// Kalemdeki OriginalItemId kısmi iadenin dayanağı — hangi
        /// kalemden ne kadar iade edildiği oradan çıkar.
        ///
        /// ReversalVoucherId: kesinleşmiş fatura iptalinde üretilen ters
        /// fiş. Orijinal fiş silinmez; ikisi de defterde durur.
        ///
        /// Mevcut faturalarda IsReturn=false ve diğer alanlar boş kalır;
        /// davranış değişmez.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "supplier_invoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "supplier_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturn",
                table: "supplier_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceId",
                table: "supplier_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalVoucherId",
                table: "supplier_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalItemId",
                table: "supplier_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturn",
                table: "sales_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceId",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalVoucherId",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalItemId",
                table: "sales_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesReturnAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_OriginalInvoiceId",
                table: "supplier_invoices",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_ReversalVoucherId",
                table: "supplier_invoices",
                column: "ReversalVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_OriginalItemId",
                table: "supplier_invoice_items",
                column: "OriginalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_invoices_OriginalInvoiceId",
                table: "sales_invoices",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_invoices_ReversalVoucherId",
                table: "sales_invoices",
                column: "ReversalVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_invoice_items_OriginalItemId",
                table: "sales_invoice_items",
                column: "OriginalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_SalesReturnAccountId",
                table: "company_finance_settings",
                column: "SalesReturnAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_SalesReturnAcc~",
                table: "company_finance_settings",
                column: "SalesReturnAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_invoice_items_sales_invoice_items_OriginalItemId",
                table: "sales_invoice_items",
                column: "OriginalItemId",
                principalTable: "sales_invoice_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_invoices_accounting_vouchers_ReversalVoucherId",
                table: "sales_invoices",
                column: "ReversalVoucherId",
                principalTable: "accounting_vouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_invoices_sales_invoices_OriginalInvoiceId",
                table: "sales_invoices",
                column: "OriginalInvoiceId",
                principalTable: "sales_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_items_supplier_invoice_items_OriginalItemId",
                table: "supplier_invoice_items",
                column: "OriginalItemId",
                principalTable: "supplier_invoice_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoices_accounting_vouchers_ReversalVoucherId",
                table: "supplier_invoices",
                column: "ReversalVoucherId",
                principalTable: "accounting_vouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoices_supplier_invoices_OriginalInvoiceId",
                table: "supplier_invoices",
                column: "OriginalInvoiceId",
                principalTable: "supplier_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_SalesReturnAcc~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_invoice_items_sales_invoice_items_OriginalItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_invoices_accounting_vouchers_ReversalVoucherId",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_invoices_sales_invoices_OriginalInvoiceId",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_items_supplier_invoice_items_OriginalItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoices_accounting_vouchers_ReversalVoucherId",
                table: "supplier_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoices_supplier_invoices_OriginalInvoiceId",
                table: "supplier_invoices");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoices_OriginalInvoiceId",
                table: "supplier_invoices");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoices_ReversalVoucherId",
                table: "supplier_invoices");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_items_OriginalItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_sales_invoices_OriginalInvoiceId",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "IX_sales_invoices_ReversalVoucherId",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "IX_sales_invoice_items_OriginalItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_SalesReturnAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "IsReturn",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceId",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "ReversalVoucherId",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "IsReturn",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceId",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "ReversalVoucherId",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropColumn(
                name: "SalesReturnAccountId",
                table: "company_finance_settings");
        }
    }
}
