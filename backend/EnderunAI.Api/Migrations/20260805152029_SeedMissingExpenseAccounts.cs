using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedMissingExpenseAccounts : Migration
    {
        /// <summary>
        /// Gider faturasında sık karşılaşılan ama hesap planında karşılığı
        /// olmayan kırılımlar. Kaynak yine seed dosyası
        /// (Data/Seeds/enderun-accounting-accounts.json); burası hesap planı
        /// ZATEN KURULMUŞ şirketlere aynı hesapları taşır, çünkü seed
        /// servisi ancak elle tetiklendiğinde çalışır.
        ///
        /// Ekleme yalnızca üst hesabı olan şirkete ve kod yoksa yapılır;
        /// migration tekrar çalışsa bile mükerrer hesap oluşmaz.
        ///
        /// İki isim düzeltmesi de var:
        /// - 770.03.08 "HABERLERLEŞME" yazım hatasıydı.
        /// - 770.03.10 elektrik, su ve doğalgazı tek hesapta topluyordu;
        ///   doğalgaz ayrı hesaba çıktığı için ismi daraltıldı. İsim
        ///   değişikliği yalnızca hesapta hiç fiş satırı yoksa yapılır —
        ///   geçmiş kayıtların etiketi altından değiştirilmemeli.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO accounting_accounts (
                    "Id", "CompanyId", "ParentAccountId", "Code", "Name",
                    "Nature", "Level", "IsPostingAllowed", "RequiresProject",
                    "RequiresCostCenter", "CurrencyCode", "IsActive",
                    "IsDeleted", "CreatedAtUtc")
                SELECT
                    gen_random_uuid(), parent."CompanyId", parent."Id",
                    yeni.kod, yeni.ad, 0, parent."Level" + 1, TRUE,
                    yeni.proje_zorunlu, TRUE, 'TRY', TRUE, FALSE, NOW()
                FROM (VALUES
                    ('740.03', '740.03.15', 'TEMİZLİK GİDERLERİ', TRUE),
                    ('740.03', '740.03.16', 'GÜVENLİK GİDERLERİ', TRUE),
                    ('770.03', '770.03.12', 'DOĞALGAZ GİDERLERİ', FALSE),
                    ('770.03', '770.03.13', 'İNTERNET VE HAT GİDERLERİ', FALSE),
                    ('770.03', '770.03.14', 'TEMİZLİK GİDERLERİ', FALSE),
                    ('770.03', '770.03.15', 'İSG VE OSGB GİDERLERİ', FALSE)
                ) AS yeni(ust_kod, kod, ad, proje_zorunlu)
                JOIN accounting_accounts parent
                    ON parent."Code" = yeni.ust_kod
                   AND parent."IsDeleted" = FALSE
                WHERE NOT EXISTS (
                    SELECT 1 FROM accounting_accounts mevcut
                    WHERE mevcut."CompanyId" = parent."CompanyId"
                      AND mevcut."Code" = yeni.kod);
                """);

            migrationBuilder.Sql("""
                UPDATE accounting_accounts a
                SET "Name" = 'HABERLEŞME GİDERLERİ', "UpdatedAtUtc" = NOW()
                WHERE a."Code" = '770.03.08'
                  AND a."Name" = 'HABERLERLEŞME GİDERLERİ'
                  AND NOT EXISTS (
                    SELECT 1 FROM accounting_voucher_lines l
                    WHERE l."AccountingAccountId" = a."Id");
                """);

            migrationBuilder.Sql("""
                UPDATE accounting_accounts a
                SET "Name" = 'ELEKTRİK VE SU GİDERLERİ', "UpdatedAtUtc" = NOW()
                WHERE a."Code" = '770.03.10'
                  AND a."Name" = 'ELEKTRİK SU DOĞALGAZ GİDERLERİ'
                  AND NOT EXISTS (
                    SELECT 1 FROM accounting_voucher_lines l
                    WHERE l."AccountingAccountId" = a."Id");
                """);
        }

        /// <summary>
        /// Geri alma yalnızca hiç kullanılmamış hesapları siler; fiş satırı
        /// olan hesap silinseydi geçmiş fişlerin hesabı kaybolurdu.
        /// İsim düzeltmeleri geri alınmaz — yazım hatasına dönmek anlamsız.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM accounting_accounts a
                WHERE a."Code" IN (
                        '740.03.15', '740.03.16',
                        '770.03.12', '770.03.13', '770.03.14', '770.03.15')
                  AND NOT EXISTS (
                    SELECT 1 FROM accounting_voucher_lines l
                    WHERE l."AccountingAccountId" = a."Id");
                """);
        }
    }
}
