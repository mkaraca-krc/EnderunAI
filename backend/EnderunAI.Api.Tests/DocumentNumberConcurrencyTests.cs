using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// BELGE NUMARASI ÜRETECİ — EŞZAMANLILIK.
///
/// NEDEN ADANMIŞ TEST: bu servis MHS (muhasebe fişi), VCK/ACK (çek),
/// SAT/SFT (fatura), PR (talep), GRV (görev) — hepsinin numarasını
/// üretiyor. Bugüne kadar tek güvencesi çek modülünün kendi
/// eşzamanlılık testiydi; o test bir gün değişse üreteç sessizce
/// korumasız kalırdı.
///
/// İDDİA "HEPSİ FARKLI", "BOŞLUKSUZ" DEĞİL — ÖLÇÜLDÜ:
///
/// Üreteç, çağıranın transaction'ına KATILIYOR (`CurrentTransaction`
/// varsa komuta atanıyor), yoksa kendi başına çalışıyor. Yani
/// boşluksuzluk çağırana bağlı:
///
///   A GRUBU (transaction açan): çek, satış/alış faturası, mal kabul,
///   sayım, perakende. Belge kaydedilemezse numara da geri alınır,
///   boşluk oluşmaz.
///
///   B GRUBU (transaction açmayan): MUHASEBE FİŞİ, teklif/RFQ,
///   malzeme talebi, sekreterya, e-fatura içe aktarım. Numara ham SQL
///   ile kendi implicit transaction'ında commit oluyor; sonraki
///   SaveChanges patlarsa numara YANAR.
///
/// Boşluksuzluk resmi bir beklenti (denetimde "12345 nerede"
/// sorusunun cevabı "sistem yakmış" olamaz) ama beş servisi
/// ilgilendiriyor ve AYRI PAKET — DURUM.md'de açık madde. Bu yüzden
/// buradaki iddia yalnızca TEKİLLİK.
/// </summary>
[Collection("Integration")]
public sealed class DocumentNumberConcurrencyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Aynı belge tipi için eşzamanlı üretim: dönen numaraların
    /// HEPSİ FARKLI olmalı.
    ///
    /// Eski hali "oku, artır, yaz" olsaydı iki istek aynı sayıyı okur
    /// ve aynı numarayı alırdı. Bugün tek SQL ifadesi
    /// (INSERT ... ON CONFLICT DO UPDATE ... RETURNING) kullanılıyor;
    /// artırım veritabanında atomik.
    /// </summary>
    [Fact]
    public async Task EszamanliUretim_AyniNumarayiIkiKezVermez()
    {
        const int istekSayisi = 12;

        using var kurulum = fixture.Factory.Services.CreateScope();
        var db = kurulum.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        var tip = $"TEST_TIP_{Guid.NewGuid():N}"[..24];

        /*
         * HER İSTEK KENDİ SCOPE'UNDA: DbContext iş parçacığı güvenli
         * değil. Tek context paylaşılsaydı test, üretecin değil
         * EF'in eşzamanlılık davranışını ölçerdi.
         */
        var gorevler = Enumerable.Range(0, istekSayisi).Select(async _ =>
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var uretec = scope.ServiceProvider
                .GetRequiredService<IDocumentNumberService>();

            return await uretec.GenerateAsync(
                proje.CompanyId, tip, "TST", CancellationToken.None);
        });

        var numaralar = await Task.WhenAll(gorevler);

        Assert.Equal(istekSayisi, numaralar.Length);
        Assert.Equal(istekSayisi, numaralar.Distinct().Count());

        // Sorgunun gerçekten çalıştığının karşı kontrolü: hepsi boş
        // dönseydi "hepsi farklı" iddiası da düşerdi ama Distinct
        // 1 verirdi — yine de açıkça sınıyoruz.
        Assert.All(numaralar, x => Assert.StartsWith("TST-", x));
    }

    /// <summary>
    /// A GRUBU DAVRANIŞI: çağıran transaction açtıysa üreteç ona
    /// KATILIR ve geri alma numarayı da geri alır.
    ///
    /// Bu testin değeri, yukarıdaki sınırlamayı iddia değil ÖLÇÜM
    /// yapması: "boşluk oluşmaz" sözü yalnız bu grup için verilebilir.
    /// </summary>
    [Fact]
    public async Task TransactionGeriAlinirsa_NumaraDaGeriAlinir()
    {
        using var kurulum = fixture.Factory.Services.CreateScope();
        var db = kurulum.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        var tip = $"TEST_TX_{Guid.NewGuid():N}"[..24];

        string ilk;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var uretec = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            await using var tx = await ctx.Database.BeginTransactionAsync();

            ilk = await uretec.GenerateAsync(
                proje.CompanyId, tip, "TST", CancellationToken.None);

            // GERİ ALINIYOR: belge kaydedilemedi senaryosu.
            await tx.RollbackAsync();
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var uretec = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            var ikinci = await uretec.GenerateAsync(
                proje.CompanyId, tip, "TST", CancellationToken.None);

            // AYNI NUMARA GERİ GELDİ: geri alma sırayı da geri sardı,
            // yani bu grupta boşluk oluşmuyor.
            Assert.Equal(ilk, ikinci);
        }
    }

    /// <summary>
    /// B GRUBU DAVRANIŞI: transaction YOKSA numara yanar.
    ///
    /// Bu test bir KUSURU belgeliyor, bir güvenceyi değil. Muhasebe
    /// fişi (MHS) bu grupta ve boşluksuzluk resmi bir beklenti;
    /// denetimde "12345 nerede" sorusunun cevabı "sistem yakmış"
    /// olamaz.
    ///
    /// TEST NEDEN VAR: DURUM.md'ye "boşluk oluşuyor" yazmadan önce
    /// oluştuğunu GÖRMEK gerekiyordu. Düzeltmesi ayrı paket (beş
    /// servisi ilgilendiriyor); test o gün geldiğinde davranışın
    /// değiştiğini de kanıtlayacak — o zaman bu testin adı ve
    /// beklentisi tersine döner.
    /// </summary>
    [Fact]
    public async Task TransactionYoksa_BasarisizKayittanSonraNumaraYanar()
    {
        using var kurulum = fixture.Factory.Services.CreateScope();
        var db = kurulum.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        var tip = $"TEST_NOTX_{Guid.NewGuid():N}"[..24];

        string ilk;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var uretec = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            // TRANSACTION AÇILMIYOR — muhasebe fişi akışının aynısı.
            ilk = await uretec.GenerateAsync(
                proje.CompanyId, tip, "TST", CancellationToken.None);

            // Belge kaydı burada patlıyor sayılıyor: numara alındı,
            // kayıt yazılamadı. Numarayı geri alacak bir sarmalayıcı
            // yok.
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var uretec = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            var ikinci = await uretec.GenerateAsync(
                proje.CompanyId, tip, "TST", CancellationToken.None);

            /*
             * BOŞLUK OLUŞTU: ikinci numara birinciden FARKLI, yani
             * birinci numara hiçbir belgeye ait olmadan yandı.
             *
             * A grubunda (yukarıdaki test) aynı numara geri gelmişti.
             * Aradaki fark tam olarak çağıranın transaction açıp
             * açmamasından geliyor.
             */
            Assert.NotEqual(ilk, ikinci);
        }
    }
}

/// <summary>
/// BELGE NUMARASI SAYIMLA ÜRETİLMEZ — SÖZLEŞME.
///
/// NEDEN VAR: GRV numarasını merkezî üretece taşıdım ve sonda ile
/// sınadım — sabotaj (eski `CountAsync() + 1` haline geri çevirme)
/// hiçbir testi kırmadı. Yani taşımanın kendisinin güvencesi yoktu:
/// eşzamanlılık testleri ÜRETECİ sınıyor, onu KİMİN KULLANDIĞINI
/// değil.
///
/// Bu bekçi kaynak koda bakıyor: bir tabloyu sayıp sonucu belge
/// numarasına çeviren kod olmamalı. Sayım eşzamanlı iki isteğe aynı
/// sayıyı verir ve silinen kayıtları saymadığı için numara geriye
/// bile gidebilir.
/// </summary>
public sealed class BelgeNumarasiSozlesmeTests
{
    [Fact]
    public void HicbirYerde_SayimlaBelgeNumarasiUretilmemeli()
    {
        var kok = BulKok();
        var api = Path.Combine(kok, "EnderunAI.Api");

        var bulgular = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
        {
            if (dosya.Contains("/obj/") || dosya.Contains("/bin/")) continue;
            if (dosya.Contains("/Migrations/")) continue;

            var satirlar = File.ReadAllLines(dosya);

            for (var i = 0; i < satirlar.Length; i++)
            {
                var satir = satirlar[i];

                // Numara biçimi bir sayaçtan besleniyor mu:
                //   $"XXX-{yil}-{sequence + 1:D5}"
                if (!satir.Contains(":D", StringComparison.Ordinal)) continue;
                if (!satir.Contains("+ 1", StringComparison.Ordinal) &&
                    !satir.Contains("+1", StringComparison.Ordinal)) continue;

                // Yakın satırlarda sayım var mı (aynı metotta).
                var pencereBasi = Math.Max(0, i - 12);
                var pencere = string.Join('\n', satirlar[pencereBasi..(i + 1)]);

                if (!pencere.Contains("CountAsync", StringComparison.Ordinal)) continue;

                bulgular.Add($"{Path.GetRelativePath(kok, dosya)}:{i + 1}");
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "SAYIMLA ÜRETİLEN BELGE NUMARASI BULUNDU:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nBu bir yarış hatasıdır: iki eşzamanlı istek aynı " +
            "sayıyı okur ve aynı numarayı alır. Ayrıca sayım silinmiş " +
            "kayıtları saymadığı için numara geriye gidebilir. " +
            "IDocumentNumberService.GenerateAsync kullanın — artırım " +
            "tek SQL ifadesinde, veritabanında atomik.");
    }

    private static string BulKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName
            ?? throw new InvalidOperationException("Çözüm kökü bulunamadı.");
    }
}

/// <summary>
/// MUHASEBE FİŞİ NUMARASINDA BOŞLUK OLMAZ.
///
/// AYRI TEST, AYRI İDDİA. Genel eşzamanlılık testinin iddiası
/// "hepsi farklı"; burada iddia "BOŞLUKSUZ" ve YALNIZ MHS için
/// geçerli. İkisini tek teste sıkıştırmak, zamanla birinin
/// diğerinin arkasına saklanması demek olurdu — bir gün genel test
/// yeşil diye MHS'nin de korunduğu sanılırdı.
///
/// NEDEN YALNIZ MHS: fiş numarasında boşluk resmi bir sorun,
/// denetimde "12345 nerede" sorusunun cevabı "sistem yakmış" olamaz.
/// Fatura numaraları (SAT/SFT) zaten transaction içindeydi. Teklif,
/// malzeme talebi, sekreterya ve e-fatura iç takip numarası —
/// onlarda boşluk kimseyi ilgilendirmiyor ve BİLEREK dokunulmadı
/// (Mehmet Karacabey kararı, 2026-08-23).
/// </summary>
[Collection("Integration")]
public sealed class MuhasebeFisiNumaraBoslugTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Fiş kaydı BAŞARISIZ olduğunda numara geri alınır: sonraki
    /// başarılı fiş aynı numarayı alır, sırada boşluk kalmaz.
    ///
    /// Hata bilerek üretiliyor: geçersiz satır (borç/alacak dengesiz)
    /// ile fiş reddediliyor. Numara o sırada zaten üretilmiş oluyor —
    /// transaction olmasaydı yanardı.
    /// </summary>
    /// <summary>
    /// Fiş kaydı BAŞARISIZ olduğunda numara geri alınır: sayaç
    /// ilerlemez, sırada boşluk kalmaz.
    ///
    /// HATA NUMARA ÜRETİMİNDEN SONRA ÜRETİLİYOR — bu testin can alıcı
    /// noktası. İlk sürümde dengesiz satırlarla fiş reddettiriyordum
    /// ve sonda testi hiçbir şey kanıtlamadı: doğrulama
    /// (`ValidateAndPrepareLinesAsync`) numara üretiminden ÖNCE
    /// çalışıyor, yani numara hiç üretilmiyordu ve sayaç zaten sıfır
    /// kalıyordu.
    ///
    /// Şimdi hata veritabanı katmanında: `ReferenceNumber` alanı 100
    /// karakterle sınırlı, daha uzunu `SaveChangesAsync` sırasında
    /// patlıyor. O noktada numara ÜRETİLMİŞ oluyor — transaction
    /// olmasaydı yanardı.
    /// </summary>
    [Fact]
    public async Task NumaraUretildiktenSonraKayitPatlarsa_NumaraYanmaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        /*
         * HESAP TESTİN KENDİSİ TARAFINDAN AÇILIYOR.
         *
         * İlk sürümde hesabı arayıp bulamazsam `return` ediyordum:
         * test veritabanında hiç hesap yok (ölçüldü: 0 satır), yani
         * test HER KOŞUDA sessizce erken dönüyor ve hiçbir şey
         * ölçmüyordu. Sonda bunu yakaladı — transaction kaldırıldığı
         * hâlde test yeşil kalıyordu.
         *
         * Sessiz kaçış kapısı, yeşil görünen ölü bir testtir.
         */
        var hesap = new AccountingAccount
        {
            CompanyId = proje.CompanyId,
            Code = $"900{Guid.NewGuid().ToString("N")[..5]}",
            Name = "Sonda hesabı",
            Nature = AccountingAccountNature.Debit,
            Level = 1,
            IsPostingAllowed = true
        };

        db.AccountingAccounts.Add(hesap);
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var istek = new
        {
            companyId = proje.CompanyId,
            voucherType = 1,
            voucherDate = DateTime.UtcNow,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = "Sonda: kayıt katmanında patlayacak fiş",

            // 100 KARAKTER SINIRINI AŞIYOR: doğrulamadan geçer,
            // SaveChangesAsync sırasında veritabanı reddeder.
            referenceNumber = new string('X', 400),

            sourceModule = (string?)null,
            sourceEntityId = (Guid?)null,

            // DENGELİ: doğrulama katmanını geçsin.
            lines = new[]
            {
                new
                {
                    accountingAccountId = hesap.Id,
                    description = "sonda borç",
                    debitAmount = 100m,
                    creditAmount = 0m,
                    currencyCode = "TRY",
                    exchangeRate = 1m
                },
                new
                {
                    accountingAccountId = hesap.Id,
                    description = "sonda alacak",
                    debitAmount = 0m,
                    creditAmount = 100m,
                    currencyCode = "TRY",
                    exchangeRate = 1m
                }
            }
        };

        var yanit = await client.PostAsJsonAsync("/api/accounting-vouchers", istek);
        var govde = await yanit.Content.ReadAsStringAsync();

        /*
         * FİŞ REDDEDİLMELİ AMA DOĞRU SEBEPLE.
         *
         * İlk sürümde yalnız "Created değil" diye bakıyordum ve test
         * her hatada yeşil kalıyordu — satırlarda `currencyCode`
         * eksik olduğu için istek MODEL DOĞRULAMASINDA (400)
         * reddediliyordu, controller'a bile ulaşmıyordu. Numara hiç
         * üretilmediği için sayaç da sıfırdı ve test "boşluk yok"
         * sanıyordu.
         *
         * Şimdi model doğrulaması hatası AÇIKÇA dışlanıyor: istek
         * kusurluysa test yanlış şeyi ölçüyor demektir.
         */
        Assert.NotEqual(HttpStatusCode.Created, yanit.StatusCode);
        Assert.DoesNotContain("validation errors", govde);

        // SIRA GERİ ALINDI MI: sayaç ilerlememiş olmalı.
        var sayac = await db.Database
            .SqlQuery<int>($"""
                SELECT COALESCE(MAX("LastNumber"), 0) AS "Value"
                FROM document_number_sequences
                WHERE "CompanyId" = {proje.CompanyId}
                  AND "DocumentType" = 'ACCOUNTING_VOUCHER'
                """)
            .SingleAsync();

        Assert.Equal(0, sayac);
    }
}
