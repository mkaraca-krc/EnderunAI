using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAccountingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeAdvanceAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollExpenseAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollPayableAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SocialSecurityPayableAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxPayableAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_EmployeeAdvanceAccountId",
                table: "company_finance_settings",
                column: "EmployeeAdvanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_PayrollExpenseAccountId",
                table: "company_finance_settings",
                column: "PayrollExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_PayrollPayableAccountId",
                table: "company_finance_settings",
                column: "PayrollPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_SocialSecurityPayableAccountId",
                table: "company_finance_settings",
                column: "SocialSecurityPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_TaxPayableAccountId",
                table: "company_finance_settings",
                column: "TaxPayableAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_EmployeeAdvanc~",
                table: "company_finance_settings",
                column: "EmployeeAdvanceAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_PayrollExpense~",
                table: "company_finance_settings",
                column: "PayrollExpenseAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_PayrollPayable~",
                table: "company_finance_settings",
                column: "PayrollPayableAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_SocialSecurity~",
                table: "company_finance_settings",
                column: "SocialSecurityPayableAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_TaxPayableAcco~",
                table: "company_finance_settings",
                column: "TaxPayableAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_EmployeeAdvanc~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_PayrollExpense~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_PayrollPayable~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_SocialSecurity~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_TaxPayableAcco~",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_EmployeeAdvanceAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_PayrollExpenseAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_PayrollPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_SocialSecurityPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_TaxPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "EmployeeAdvanceAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "PayrollExpenseAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "PayrollPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "SocialSecurityPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "TaxPayableAccountId",
                table: "company_finance_settings");
        }
    }
}
