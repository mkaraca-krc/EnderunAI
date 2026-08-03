using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// 2026 bordro parametrelerini resmi değerlerine çeker.
    ///
    /// Seed add-only olduğu için (yalnızca eksik şirket satırını ekler)
    /// hâlihazırda oluşmuş kayıtlar seed düzeltilse bile eski değerlerde
    /// kalır — daha önce finans ayarlarında yaşanan tuzağın aynısı.
    /// Bu yüzden düzeltme veriye doğrudan yazılıyor.
    ///
    /// Düzeltilenler:
    ///   - SGK tavanı: 247.725,00 (taban × 7,5) → 297.270,00 (taban × 9)
    ///   - Gelir vergisi dilimleri: 200.000/420.000/1.000.000/5.400.000
    ///     → 190.000/400.000/1.500.000/5.300.000
    ///   - İşveren SGK indirimi: kapalı/5 puan → açık/2 puan
    ///     (imalat dışı sektör; işveren SGK %20,75 − 2 = %18,75)
    ///   - Kıdem tazminatı tavanı: 53.919,68 (01.01-30.06.2026)
    ///
    /// Doğru olduğu için dokunulmayanlar: asgari ücret 33.030,00 /
    /// 28.075,50, işçi %14 + %1, işveren işsizlik %2, damga ‰7,59.
    ///
    /// Parametreler resmi kaynaktan kullanıcı tarafından verildiği için
    /// doğrulama damgası da atılıyor; aksi halde fail-closed kapı
    /// bordronun kesinleştirilmesini engellemeye devam ederdi.
    /// </summary>
    public partial class Fix2026PayrollParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE company_payroll_settings
                   SET ""MinimumWageGross"" = 33030.00,
                       ""MinimumWageNet"" = 28075.50,
                       ""SgkBaseFloor"" = 33030.00,
                       ""SgkBaseCeiling"" = 297270.00,
                       ""SgkEmployerDiscountEnabled"" = true,
                       ""SgkEmployerDiscountPoints"" = 2,
                       ""SeveranceCeiling"" = 53919.68,
                       ""SeveranceCeilingPeriodNote"" = '01.01.2026-30.06.2026',
                       ""VerifiedAtUtc"" = now() at time zone 'utc',
                       ""VerificationNote"" = '2026 resmi bordro parametreleri (asgari ucret, SGK taban/tavan, gelir vergisi dilimleri, kidem tavani) kullanici tarafindan verildi ve sisteme yazildi.',
                       ""UpdatedAtUtc"" = now() at time zone 'utc'
                 WHERE ""Year"" = 2026;

                UPDATE payroll_tax_brackets b
                   SET ""LowerBound"" = v.lower_bound,
                       ""UpperBound"" = v.upper_bound,
                       ""Rate"" = v.rate,
                       ""UpdatedAtUtc"" = now() at time zone 'utc'
                  FROM (VALUES
                            (1, 0.00,        190000.00, 15.0),
                            (2, 190000.00,   400000.00, 20.0),
                            (3, 400000.00,  1500000.00, 27.0),
                            (4, 1500000.00, 5300000.00, 35.0),
                            (5, 5300000.00,       NULL, 40.0)
                        ) AS v(order_no, lower_bound, upper_bound, rate)
                 WHERE b.""Order"" = v.order_no
                   AND b.""CompanyPayrollSettingsId"" IN (
                        SELECT ""Id"" FROM company_payroll_settings WHERE ""Year"" = 2026);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eski (hatalı) değerlere dönülür ve doğrulama damgası
            // kaldırılır — geri alınan parametrelerle bordro
            // kesinleştirilememeli.
            migrationBuilder.Sql(@"
                UPDATE company_payroll_settings
                   SET ""SgkBaseCeiling"" = 247725.00,
                       ""SgkEmployerDiscountEnabled"" = false,
                       ""SgkEmployerDiscountPoints"" = 5,
                       ""SeveranceCeiling"" = 0,
                       ""SeveranceCeilingPeriodNote"" = NULL,
                       ""VerifiedAtUtc"" = NULL,
                       ""VerifiedByUserId"" = NULL,
                       ""VerificationNote"" = NULL
                 WHERE ""Year"" = 2026;

                UPDATE payroll_tax_brackets b
                   SET ""LowerBound"" = v.lower_bound,
                       ""UpperBound"" = v.upper_bound
                  FROM (VALUES
                            (1, 0.00,        200000.00),
                            (2, 200000.00,   420000.00),
                            (3, 420000.00,  1000000.00),
                            (4, 1000000.00, 5400000.00),
                            (5, 5400000.00,       NULL)
                        ) AS v(order_no, lower_bound, upper_bound)
                 WHERE b.""Order"" = v.order_no
                   AND b.""CompanyPayrollSettingsId"" IN (
                        SELECT ""Id"" FROM company_payroll_settings WHERE ""Year"" = 2026);
            ");
        }
    }
}
