using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Bordro hesap motorunun ürettiği ara ve nihai değerler için yeni
    /// kolonlar, ücret kartlarındaki bozuk verinin düzeltilmesi ve resmi
    /// maaşın tek kaynağa (ücret kartı) taşınması.
    ///
    /// hr_* tabloları HrDbContext'e ait olduğu ve AppDbContext modeline
    /// girmediği için kolonlar elle yazıldı — modülün mevcut deseni
    /// (bkz. 20260726091113_AddHrPayrollCompensationAmounts) budur.
    /// </summary>
    public partial class AddPayrollCalculationColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE hr_payroll_records
                    ADD COLUMN IF NOT EXISTS ""UnemploymentEmployeeDeduction"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""SgkBase"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""IncomeTaxBase"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""CumulativeIncomeTaxBase"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""IncomeTaxExemption"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""StampTaxExemption"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""SgkEmployerAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""UnemploymentEmployerAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""TotalEmployerCost"" numeric(18,2) NOT NULL DEFAULT 0;
            ");

            // Bozuk çarpanlar: arayüzde yüzde sanılıp 1,5 yerine 150 ve
            // 2 yerine 200 girilmiş. Motor bunları çarpan olarak kullansa
            // fazla mesai 150 kat ödenirdi. 10'dan büyük her değer yüzde
            // kabul edilip 100'e bölünür.
            migrationBuilder.Sql(@"
                UPDATE hr_salary_definitions
                   SET ""OvertimeMultiplier"" = ""OvertimeMultiplier"" / 100
                 WHERE ""OvertimeMultiplier"" > 10;

                UPDATE hr_salary_definitions
                   SET ""SundayMultiplier"" = ""SundayMultiplier"" / 100
                 WHERE ""SundayMultiplier"" > 10;

                UPDATE hr_salary_definitions
                   SET ""PublicHolidayMultiplier"" = ""PublicHolidayMultiplier"" / 100
                 WHERE ""PublicHolidayMultiplier"" > 10;
            ");

            // Brütü boş bırakılmış ücret kartları: net üzerinden geri
            // hesaplanır. Net/brüt oranı istisnalar nedeniyle kişiye göre
            // değişebildiği için yaklaşık %85 kuralı kullanılır ve kayda
            // not düşülür — kesin değeri İK doğrulamalı, sessizce doğru
            // varsaymıyoruz.
            migrationBuilder.Sql(@"
                UPDATE hr_salary_definitions
                   SET ""GrossSalary"" = ROUND(""NetSalary"" / 0.85, 2),
                       ""Description"" = COALESCE(""Description"" || ' | ', '')
                           || 'Brüt, netten yaklaşık hesaplandı - doğrulanmalı'
                 WHERE ""GrossSalary"" <= 0
                   AND ""NetSalary"" > 0;
            ");

            // Resmi maaşın tek doğru kaynağı ücret kartı olacak. Ücret
            // kartı olmayan personelin personel kartındaki maaşı, tek
            // seferlik bir kart olarak taşınır.
            migrationBuilder.Sql(@"
                INSERT INTO hr_salary_definitions (
                    ""Id"", ""CompanyId"", ""PersonnelId"", ""EffectiveStartDate"",
                    ""EffectiveEndDate"", ""GrossSalary"", ""NetSalary"", ""DailyRate"",
                    ""HourlyRate"", ""OvertimeMultiplier"", ""SundayMultiplier"",
                    ""PublicHolidayMultiplier"", ""CurrencyCode"", ""Description"",
                    ""IsActive"", ""IsDeleted"", ""CreatedAtUtc"")
                SELECT
                    gen_random_uuid(),
                    p.""CompanyId"",
                    p.""Id"",
                    COALESCE(p.""EmploymentStartDate"", DATE '2026-01-01'),
                    NULL,
                    p.""MonthlySalary"",
                    ROUND(p.""MonthlySalary"" * 0.85, 2),
                    ROUND(p.""MonthlySalary"" / 30, 2),
                    ROUND(p.""MonthlySalary"" / 225, 2),
                    1.5, 2.0, 2.0,
                    'TRY',
                    'Personel kartındaki maaştan taşındı - brüt/net doğrulanmalı',
                    true, false, now()
                FROM personnel p
                WHERE p.""MonthlySalary"" IS NOT NULL
                  AND p.""MonthlySalary"" > 0
                  AND COALESCE(p.""IsDeleted"", false) = false
                  AND NOT EXISTS (
                        SELECT 1 FROM hr_salary_definitions s
                         WHERE s.""PersonnelId"" = p.""Id""
                           AND COALESCE(s.""IsDeleted"", false) = false);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Taşınan ücret kartları geri alınır; çarpan ve brüt
            // düzeltmeleri veri onarımı olduğu için geri alınmaz.
            migrationBuilder.Sql(@"
                DELETE FROM hr_salary_definitions
                 WHERE ""Description"" = 'Personel kartındaki maaştan taşındı - brüt/net doğrulanmalı';

                ALTER TABLE hr_payroll_records
                    DROP COLUMN IF EXISTS ""UnemploymentEmployeeDeduction"",
                    DROP COLUMN IF EXISTS ""SgkBase"",
                    DROP COLUMN IF EXISTS ""IncomeTaxBase"",
                    DROP COLUMN IF EXISTS ""CumulativeIncomeTaxBase"",
                    DROP COLUMN IF EXISTS ""IncomeTaxExemption"",
                    DROP COLUMN IF EXISTS ""StampTaxExemption"",
                    DROP COLUMN IF EXISTS ""SgkEmployerAmount"",
                    DROP COLUMN IF EXISTS ""UnemploymentEmployerAmount"",
                    DROP COLUMN IF EXISTS ""TotalEmployerCost"";
            ");
        }
    }
}
