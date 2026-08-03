using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressPaymentAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ŞEMA SAPMASI DÜZELTMESİ: ProgressPayment.CancelledAtUtc
            // model ve snapshot'ta vardı ama hiçbir migration onu
            // progress_payments'a eklememişti. EF kolonu var sayıp SELECT/
            // UPDATE'e dahil ettiği için hakediş oluşturma ve iptal etme
            // canlıda 500 veriyordu (liste/detay projeksiyon kullandığından
            // etkilenmiyordu, bu yüzden fark edilmemiş). IF NOT EXISTS ile
            // ekleniyor: kolonun sonradan elle açıldığı ortamlarda da
            // güvenle çalışsın.
            migrationBuilder.Sql(
                @"ALTER TABLE progress_payments ADD COLUMN IF NOT EXISTS ""CancelledAtUtc"" timestamp with time zone NULL;");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingVoucherId",
                table: "progress_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingAccountId",
                table: "progress_payment_deductions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeductionAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payments_AccountingVoucherId",
                table: "progress_payments",
                column: "AccountingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deductions_AccountingAccountId",
                table: "progress_payment_deductions",
                column: "AccountingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_DeductionAccountId",
                table: "company_finance_settings",
                column: "DeductionAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_DeductionAccou~",
                table: "company_finance_settings",
                column: "DeductionAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_payment_deductions_accounting_accounts_AccountingA~",
                table: "progress_payment_deductions",
                column: "AccountingAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_payments_accounting_vouchers_AccountingVoucherId",
                table: "progress_payments",
                column: "AccountingVoucherId",
                principalTable: "accounting_vouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_DeductionAccou~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_payment_deductions_accounting_accounts_AccountingA~",
                table: "progress_payment_deductions");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_payments_accounting_vouchers_AccountingVoucherId",
                table: "progress_payments");

            migrationBuilder.DropIndex(
                name: "IX_progress_payments_AccountingVoucherId",
                table: "progress_payments");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_deductions_AccountingAccountId",
                table: "progress_payment_deductions");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_DeductionAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "AccountingVoucherId",
                table: "progress_payments");

            // CancelledAtUtc kasıtlı olarak düşürülmüyor: bu migration onu
            // "eklemedi", eksik kalmış bir şema sapmasını kapattı.

            migrationBuilder.DropColumn(
                name: "AccountingAccountId",
                table: "progress_payment_deductions");

            migrationBuilder.DropColumn(
                name: "DeductionAccountId",
                table: "company_finance_settings");
        }
    }
}
