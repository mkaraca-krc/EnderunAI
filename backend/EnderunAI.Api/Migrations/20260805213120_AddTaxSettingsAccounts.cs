using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxSettingsAccounts : Migration
    {
        /// <summary>
        /// Vergi görünümü için gereken hesap eşlemeleri ve kurumlar
        /// vergisi oranı.
        ///
        /// Mevcut şirketlerin ayarları hesap planındaki karşılıklarına
        /// bağlanır (190 devreden, 360.99 ödenecek KDV, 191.05 / 360.002
        /// sorumlu sıfatıyla KDV) ve oran %25'e çekilir. Yapılmasaydı
        /// mevcut şirkette oran 0 kalır, tahmini vergi her dönem sıfır
        /// görünürdü.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CorporateTaxRate",
                table: "company_finance_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 25m);

            migrationBuilder.AddColumn<Guid>(
                name: "ReverseChargeVatInputAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReverseChargeVatPayableAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VatCarryForwardAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VatPayableAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_ReverseChargeVatInputAccountId",
                table: "company_finance_settings",
                column: "ReverseChargeVatInputAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_ReverseChargeVatPayableAccountId",
                table: "company_finance_settings",
                column: "ReverseChargeVatPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_VatCarryForwardAccountId",
                table: "company_finance_settings",
                column: "VatCarryForwardAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_VatPayableAccountId",
                table: "company_finance_settings",
                column: "VatPayableAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_ReverseChargeV~",
                table: "company_finance_settings",
                column: "ReverseChargeVatInputAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_ReverseCharge~1",
                table: "company_finance_settings",
                column: "ReverseChargeVatPayableAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_VatCarryForwar~",
                table: "company_finance_settings",
                column: "VatCarryForwardAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_VatPayableAcco~",
                table: "company_finance_settings",
                column: "VatPayableAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            // Mevcut şirketlerin oranı: sütun varsayılanı yalnızca yeni
            // satırlara uygulanır, var olanlar 0 kalırdı.
            migrationBuilder.Sql(
                "UPDATE company_finance_settings SET \"CorporateTaxRate\" = 25 " +
                "WHERE \"CorporateTaxRate\" = 0;");

            // Hesap eşlemeleri: kod hesap planında varsa bağlanır, yoksa
            // boş kalır ve ilgili işlem açık bir hata mesajıyla durur.
            void Map(string column, string code, string? fallbackCode = null)
            {
                var codes = fallbackCode is null
                    ? $"'{code}'"
                    : $"'{code}', '{fallbackCode}'";

                migrationBuilder.Sql(
                    $"UPDATE company_finance_settings s " +
                    $"SET \"{column}\" = ( " +
                    $"  SELECT a.\"Id\" FROM accounting_accounts a " +
                    $"  WHERE a.\"CompanyId\" = s.\"CompanyId\" " +
                    $"    AND a.\"Code\" IN ({codes}) " +
                    $"    AND a.\"IsPostingAllowed\" = TRUE " +
                    $"    AND a.\"IsDeleted\" = FALSE " +
                    $"  ORDER BY length(a.\"Code\") DESC LIMIT 1) " +
                    $"WHERE s.\"{column}\" IS NULL;");
            }

            Map("VatCarryForwardAccountId", "190.01", "190");
            Map("VatPayableAccountId", "360.99", "360");
            Map("ReverseChargeVatInputAccountId", "191.05");
            Map("ReverseChargeVatPayableAccountId", "360.002");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_ReverseChargeV~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_ReverseCharge~1",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_VatCarryForwar~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_VatPayableAcco~",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_ReverseChargeVatInputAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_ReverseChargeVatPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_VatCarryForwardAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_VatPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "CorporateTaxRate",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "ReverseChargeVatInputAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "ReverseChargeVatPayableAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "VatCarryForwardAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "VatPayableAccountId",
                table: "company_finance_settings");
        }
    }
}
