using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// İki iş bir arada:
    ///
    /// 1. MERKEZ BİRİMİ. Şubeye masraf merkezi kodu eklenir ve her
    ///    şirkete kalıcı bir "Merkez Ofis" birimi garanti edilir. Merkez
    ///    zaten <c>Branch.IsHeadOffice</c> olarak vardı ama hiçbir yerde
    ///    seçilebilir bir birim değildi ve muhasebedeki masraf merkezi
    ///    kavramına bağlı değildi.
    ///
    /// 2. ÜCRET KARTLARI NET ESASINA. Kartlardaki brüt değerler
    ///    20260803130230_FixMigratedSalaryCardsGrossNet göçünde
    ///    "Brüt = Net / 0,85" kestirmesiyle üretilmişti; o göç kendi
    ///    yorumunda bunun asgari ücret üstünde yaklaşık olduğunu
    ///    söylüyor. Kartlardaki NET değer anlaşılan ücret kabul edilip
    ///    brüt gerçek brütleştirmeyle (SGK + gelir vergisi + damga +
    ///    asgari ücret istisnası) yeniden hesaplanıyor.
    ///
    ///    Yeni brüt değerleri PayrollNetToGrossCalculator ile üretildi ve
    ///    SalaryCardNetBasisConversionTests içinde sabitlendi; parametre
    ///    değişirse o testler kırılır. Ocak esaslı referans brüttür —
    ///    HrMasterDataController.ApplyNetBasisAsync ile aynı çağrı; her
    ///    ayın gerçek brütü bordroda kümülatif matrahla yeniden bulunur.
    ///
    ///    KESİNLEŞMİŞ BORDROLARA DOKUNULMUYOR: yalnızca
    ///    hr_salary_definitions güncelleniyor, hr_payroll_records'a hiç
    ///    yazılmıyor.
    /// </summary>
    public partial class AddBranchCostCenterAndNetBasisSalaryCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "branches",
                type: "text",
                nullable: true);

            // --- 1. Merkez birimi ---

            // Merkez ofisi olmayan şirkete oluştur. Adres şirket
            // ayarlarından gelir; ayrıca yazılmaz ki iki yerde
            // birbirinden sapmasın.
            migrationBuilder.Sql("""
                INSERT INTO branches (
                    "Id", "CompanyId", "Code", "Name", "Address",
                    "IsHeadOffice", "CostCenterCode",
                    "IsActive", "IsDeleted", "CreatedAtUtc")
                SELECT
                    gen_random_uuid(), c."Id", 'MERKEZ', 'Merkez Ofis', c."Address",
                    true, 'MERKEZ',
                    true, false, now() AT TIME ZONE 'utc'
                FROM companies c
                WHERE c."IsDeleted" = false
                  AND NOT EXISTS (
                      SELECT 1 FROM branches b
                      WHERE b."CompanyId" = c."Id"
                        AND b."IsHeadOffice" = true
                        AND b."IsDeleted" = false);
                """);

            // Var olan merkez ofisi standart ada, şirket adresine ve
            // masraf merkezi koduna getir.
            migrationBuilder.Sql("""
                UPDATE branches b
                SET "Name" = 'Merkez Ofis',
                    "Address" = c."Address",
                    "CostCenterCode" = COALESCE(b."CostCenterCode", b."Code"),
                    "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
                FROM companies c
                WHERE c."Id" = b."CompanyId"
                  AND b."IsHeadOffice" = true
                  AND b."IsDeleted" = false;
                """);

            // --- 2. Ücret kartları net esasına ---

            // Yalnızca brüt esaslı (SalaryBasis = 0) ve neti dolu kartlar.
            // Şart, göçün iki kez uygulanmasını da engeller.
            migrationBuilder.Sql("""
                UPDATE hr_salary_definitions
                SET "SalaryBasis" = 1,
                    "TargetNetSalary" = "NetSalary",
                    "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
                WHERE "IsDeleted" = false
                  AND "SalaryBasis" = 0
                  AND "NetSalary" > 0;
                """);

            // Brütleştirmeden çıkan değerler. Asgari ücretli kartlarda
            // (net 28.075,50) eski brüt zaten doğruydu: ÷0,85 tam olarak
            // asgari ücretin brüt/net oranıdır, çünkü istisna gelir ve
            // damga vergisini sıfırlar. O yüzden 33.030,00 değişmiyor.
            //
            // Asgari ücret üstünde kestirme brütü OLDUĞUNDAN DÜŞÜK
            // gösteriyordu: istisna sabit kalıp aşan kısım vergilendiği
            // için toplam kesinti oranı %15'i geçer ve aynı nete ulaşmak
            // daha yüksek brüt gerektirir.
            migrationBuilder.Sql("""
                UPDATE hr_salary_definitions
                SET "GrossSalary" = v.new_gross
                FROM (VALUES
                    (28075.50::numeric,  33030.00::numeric),
                    (35000.00::numeric,  42715.82::numeric),
                    (60000.00::numeric,  77685.26::numeric),
                    (75000.00::numeric,  98666.92::numeric),
                    (90000.00::numeric, 119648.58::numeric)
                ) AS v(net, new_gross)
                WHERE hr_salary_definitions."IsDeleted" = false
                  AND hr_salary_definitions."SalaryBasis" = 1
                  AND hr_salary_definitions."TargetNetSalary" = v.net;
                """);

            // Günlük/saatlik ücretler eski (yanlış) brütten türetilmişti:
            // günlük = brüt ÷ 30. İki şart birden aranıyor — değer eski
            // brütten türetilmiş OLACAK ve yeni brütle artık
            // uyuşmayacak. Böylece elle girilmiş ücretlere ve brütü
            // değişmeyen asgari ücretli kartlara dokunulmuyor.
            // Sıfırlananlar bordroda güncel brütten yeniden hesaplanır
            // (HrApprovalService.BuildSalaryRates).
            migrationBuilder.Sql("""
                UPDATE hr_salary_definitions
                SET "DailyRate" = 0,
                    "HourlyRate" = 0
                WHERE "IsDeleted" = false
                  AND "SalaryBasis" = 1
                  AND "DailyRate" > 0
                  AND "TargetNetSalary" > 0
                  AND ROUND("DailyRate", 2)
                      = ROUND((ROUND("TargetNetSalary" / 0.85, 2)) / 30, 2)
                  AND ROUND("DailyRate", 2) <> ROUND("GrossSalary" / 30, 2);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Kartları brüt esasına döndürür ve brütü eski kestirmeye
            // (net ÷ 0,85) geri getirir.
            migrationBuilder.Sql("""
                UPDATE hr_salary_definitions
                SET "GrossSalary" = ROUND("TargetNetSalary" / 0.85, 2),
                    "SalaryBasis" = 0,
                    "TargetNetSalary" = 0,
                    "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
                WHERE "IsDeleted" = false
                  AND "SalaryBasis" = 1
                  AND "TargetNetSalary" > 0;
                """);

            // Merkez ofis kaydı silinmiyor: personel ona bağlanmış
            // olabilir ve silmek BranchId'yi kırardı. Yalnızca kolon
            // kaldırılıyor.
            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "branches");
        }
    }
}
