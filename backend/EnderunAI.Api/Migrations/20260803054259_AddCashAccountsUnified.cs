using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// cash_accounts ve cash_transactions tabloları 20260724120224
    /// migration'ından beri veritabanında var ama karşılığında hiç EF
    /// modeli yazılmamıştı. Bu migration tabloları sıfırdan oluşturmuyor —
    /// mevcut şemayı modele uyacak şekilde tamamlıyor ve artık kullanılmayan
    /// BankAccounts satırlarını birleşik kasa/banka tablosuna taşıyor.
    /// (Eski BankAccounts/BankTransactions tabloları bilinçli olarak
    /// düşürülmüyor; veri taşındı, tablolar tarihsel kayıt olarak kalıyor.)
    /// </summary>
    public partial class AddCashAccountsUnified : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE cash_accounts
                    ADD COLUMN IF NOT EXISTS ""Type"" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""BankName"" character varying(150) NULL,
                    ADD COLUMN IF NOT EXISTS ""Iban"" character varying(40) NULL;

                ALTER TABLE cash_accounts
                    ALTER COLUMN ""Code"" TYPE character varying(50),
                    ALTER COLUMN ""Name"" TYPE character varying(200),
                    ALTER COLUMN ""CurrencyCode"" TYPE character varying(3);

                ALTER TABLE cash_transactions
                    ADD COLUMN IF NOT EXISTS ""AccountingVoucherId"" uuid NULL;

                ALTER TABLE cash_transactions
                    ALTER COLUMN ""CurrencyCode"" TYPE character varying(3),
                    ALTER COLUMN ""Description"" TYPE character varying(1000),
                    ALTER COLUMN ""DocumentNumber"" TYPE character varying(100),
                    ALTER COLUMN ""SourceModule"" TYPE character varying(100);
            ");

            // Kod benzersizliği modelde unique; mevcut indeks unique değildi.
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_cash_accounts_CompanyId_Code"";
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_cash_accounts_CompanyId_Code""
                    ON cash_accounts (""CompanyId"", ""Code"");
                CREATE INDEX IF NOT EXISTS ""IX_cash_accounts_CompanyId_Type""
                    ON cash_accounts (""CompanyId"", ""Type"");
                CREATE INDEX IF NOT EXISTS ""IX_cash_transactions_AccountingVoucherId""
                    ON cash_transactions (""AccountingVoucherId"");
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'FK_cash_transactions_accounting_vouchers_AccountingVoucherId'
                    ) THEN
                        ALTER TABLE cash_transactions
                            ADD CONSTRAINT ""FK_cash_transactions_accounting_vouchers_AccountingVoucherId""
                            FOREIGN KEY (""AccountingVoucherId"")
                            REFERENCES accounting_vouchers (""Id"") ON DELETE RESTRICT;
                    END IF;
                END $$;
            ");

            // Eski BankAccounts satırlarını birleşik tabloya taşı (Type=1).
            // Muhasebe hesabı boş olanlara 102 Bankalar atanır, kod üretilir.
            // 102 hesabı bulunamayan ortamda (hesap planı yüklenmemiş test
            // veritabanı) satır atlanır — FK NOT NULL olduğu için zorunlu.
            migrationBuilder.Sql(@"
                INSERT INTO cash_accounts (
                    ""Id"", ""CompanyId"", ""Type"", ""Code"", ""Name"", ""BankName"", ""Iban"",
                    ""CurrencyCode"", ""OpeningBalance"", ""AccountingAccountId"",
                    ""IsActive"", ""IsDeleted"", ""CreatedAtUtc"")
                SELECT
                    b.""Id"",
                    b.""CompanyId"",
                    1,
                    'BANKA-' || lpad((row_number() OVER (PARTITION BY b.""CompanyId"" ORDER BY b.""CreatedAtUtc"", b.""Id""))::text, 3, '0'),
                    b.""AccountName"",
                    b.""BankName"",
                    b.""Iban"",
                    COALESCE(NULLIF(b.""CurrencyCode"", ''), 'TRY'),
                    COALESCE(b.""OpeningBalance"", 0),
                    COALESCE(
                        b.""AccountingAccountId"",
                        (SELECT a.""Id"" FROM accounting_accounts a
                          WHERE a.""CompanyId"" = b.""CompanyId""
                            AND a.""Code"" = '102'
                            AND a.""IsDeleted"" = false
                          LIMIT 1)),
                    COALESCE(b.""IsActive"", true),
                    false,
                    COALESCE(b.""CreatedAtUtc"", now())
                FROM ""BankAccounts"" b
                WHERE COALESCE(b.""IsDeleted"", false) = false
                  AND NOT EXISTS (SELECT 1 FROM cash_accounts c WHERE c.""Id"" = b.""Id"")
                  AND COALESCE(
                        b.""AccountingAccountId"",
                        (SELECT a.""Id"" FROM accounting_accounts a
                          WHERE a.""CompanyId"" = b.""CompanyId""
                            AND a.""Code"" = '102'
                            AND a.""IsDeleted"" = false
                          LIMIT 1)) IS NOT NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM cash_accounts WHERE ""Type"" = 1;

                ALTER TABLE cash_transactions
                    DROP CONSTRAINT IF EXISTS ""FK_cash_transactions_accounting_vouchers_AccountingVoucherId"";
                DROP INDEX IF EXISTS ""IX_cash_transactions_AccountingVoucherId"";
                ALTER TABLE cash_transactions DROP COLUMN IF EXISTS ""AccountingVoucherId"";

                DROP INDEX IF EXISTS ""IX_cash_accounts_CompanyId_Type"";
                ALTER TABLE cash_accounts
                    DROP COLUMN IF EXISTS ""Type"",
                    DROP COLUMN IF EXISTS ""BankName"",
                    DROP COLUMN IF EXISTS ""Iban"";
            ");
        }
    }
}
