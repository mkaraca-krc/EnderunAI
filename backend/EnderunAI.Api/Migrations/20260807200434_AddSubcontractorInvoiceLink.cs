using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractorInvoiceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InvoicedAmount",
                table: "subcontractor_progress_payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierInvoiceId",
                table: "subcontractor_progress_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payments_SupplierInvoiceId",
                table: "subcontractor_progress_payments",
                column: "SupplierInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_subcontractor_progress_payments_supplier_invoices_SupplierI~",
                table: "subcontractor_progress_payments",
                column: "SupplierInvoiceId",
                principalTable: "supplier_invoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subcontractor_progress_payments_supplier_invoices_SupplierI~",
                table: "subcontractor_progress_payments");

            migrationBuilder.DropIndex(
                name: "IX_subcontractor_progress_payments_SupplierInvoiceId",
                table: "subcontractor_progress_payments");

            migrationBuilder.DropColumn(
                name: "InvoicedAmount",
                table: "subcontractor_progress_payments");

            migrationBuilder.DropColumn(
                name: "SupplierInvoiceId",
                table: "subcontractor_progress_payments");
        }
    }
}
