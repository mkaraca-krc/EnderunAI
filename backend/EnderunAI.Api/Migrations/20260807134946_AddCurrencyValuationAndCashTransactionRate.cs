using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyValuationAndCashTransactionRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountTry",
                table: "cash_transactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Kur varsayılanı 1: EF'in ürettiği 0, mevcut satırları
            // "kuru yok" durumuna düşürür ve TL karşılıkları sıfırlanır.
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "cash_transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            // GERİYE DOLDURMA — bilinçli olarak 1:1.
            //
            // Bu satırların muhasebe fişleri kur alanı sabit 1 ile
            // kesilmişti; deftere giren TL tutar Amount'un kendisi. Kur
            // arşivinden geriye dönük "doğru" kuru yazmak, kaydı
            // kesilmiş fişlerle çelişen bir defter değeri üretirdi.
            // Defterle tutarlı kalmak için mevcut satırlar 1 kuruyla
            // sabitleniyor; dövizli eski hareket varsa DÜZELTİLMESİ
            // GEREKEN kayıt olarak elle ele alınmalıdır.
            migrationBuilder.Sql(@"
                UPDATE cash_transactions
                SET ""ExchangeRate"" = 1,
                    ""AmountTry"" = ""Amount"";");

            migrationBuilder.CreateTable(
                name: "currency_valuation_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValuationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountingVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReversalVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_valuation_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_currency_valuation_runs_accounting_vouchers_AccountingVouch~",
                        column: x => x.AccountingVoucherId,
                        principalTable: "accounting_vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_valuation_runs_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_valuation_run_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyValuationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BookValueLocal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ValuationRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ValuedLocal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PostedDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_valuation_run_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_currency_valuation_run_lines_currency_valuation_runs_Curren~",
                        column: x => x.CurrencyValuationRunId,
                        principalTable: "currency_valuation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_currency_valuation_run_lines_current_accounts_CurrentAccoun~",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_currency_valuation_run_lines_CurrencyValuationRunId",
                table: "currency_valuation_run_lines",
                column: "CurrencyValuationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_currency_valuation_run_lines_CurrentAccountId_CurrencyCode",
                table: "currency_valuation_run_lines",
                columns: new[] { "CurrentAccountId", "CurrencyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_valuation_runs_AccountingVoucherId",
                table: "currency_valuation_runs",
                column: "AccountingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_currency_valuation_runs_CompanyId_ValuationDate",
                table: "currency_valuation_runs",
                columns: new[] { "CompanyId", "ValuationDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currency_valuation_run_lines");

            migrationBuilder.DropTable(
                name: "currency_valuation_runs");

            migrationBuilder.DropColumn(
                name: "AmountTry",
                table: "cash_transactions");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "cash_transactions");
        }
    }
}
