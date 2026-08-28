using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Services.Finance;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// EŞZAMANLI ÖDEME YARIŞI (ÖP/1a · S6 · Kural 54).
///
/// SAF KURAL SONDALARI BU DELİĞİ GÖREMEDİ. S1–S4'ün dördü de geçti —
/// K2 ve K3 doğruydu. Delik KURALLARIN ARASINDAYDI: okuma ile yazma
/// arasında kilit yoktu, K3 BAYAT `OdenenTutar` üzerinden
/// hesaplıyordu.
///
/// İki eşzamanlı istek K2'yi "onaylandığı gibi" geçiyor, K3'ü KENDİ
/// payına geçiyor ve toplamda ONAYLANANDAN FAZLA ödeme yazılıyordu.
/// PostgreSQL Read Committed'da iki işlem çakışmadan tamamlanır:
/// veritabanı hata vermez, satır "ödendi" görünür ama iki kez
/// ödenmiştir.
///
/// BU TESTİN İŞİ: kilidi yarın biri kaldırdığında kırmızıya dönmek.
/// Düzeltmeden önce böyle bir test YOKTU — asıl eksik oydu.
/// </summary>
[Collection("Integration")]
public sealed class OdemePlaniYarisTests(DatabaseFixture fixture)
{
    private const decimal OnaylananTutar = 10_000m;

    private async Task<Guid> OnayliSatirAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var hazirlayan = Guid.NewGuid();

        var plan = new OdemePlani
        {
            CompanyId = Guid.NewGuid(),
            HaftaBaslangici = new DateTime(2026, 12, 7, 0, 0, 0, DateTimeKind.Utc)
                .AddDays(Random.Shared.Next(0, 500) * 7),
            OdemeGunu = new DateTime(2026, 12, 11, 0, 0, 0, DateTimeKind.Utc),
            Durum = OdemePlaniDurumu.Onayda,
            HazirlayanUserId = hazirlayan
        };
        db.OdemePlanlari.Add(plan);
        await db.SaveChangesAsync();

        var satir = new OdemePlaniSatiri
        {
            OdemePlaniId = plan.Id,
            CurrentAccountId = Guid.NewGuid(),
            OnerilenTutar = OnaylananTutar,
            Yontem = OdemeYontemi.HavaleEft,
            Oncelik = 1,
            CashAccountId = Guid.NewGuid(),
            CreatedByUserId = hazirlayan
        };
        db.OdemePlaniSatirlari.Add(satir);
        await db.SaveChangesAsync();

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        return satir.Id;
    }

    /// <summary>
    /// BAYAT OKUMA İLE ÖDEME YAPILAMAZ — S6'NIN ASIL İDDİASI.
    ///
    /// İKİ TESTİM ÇÜRÜDÜ, BU ÜÇÜNCÜSÜ:
    ///
    /// (1) İki `Task` salıp "toplam aşmadı" demek: kilit
    ///     KALDIRILDIĞINDA da yeşil verdi — okuma ile yazma arası
    ///     mikrosaniyeler, yarış penceresi hiç açılmadı.
    ///
    /// (2) Dışarıdan kilitleyip "bloke oluyor mu" demek: o da kilit
    ///     kaldırılınca yeşil verdi. Sebebi PostgreSQL'in kendisi —
    ///     `FOR UPDATE` ile kilitli satıra yapılan `UPDATE` ZATEN
    ///     bloke olur. Test "açık kilit" ile "örtük yazma kilidi"
    ///     arasını ayırt edemiyordu.
    ///
    /// AYIRT EDİCİ SORU ŞU: servis, kilidi aldıktan SONRA mı okuyor?
    ///
    /// Kurgu: satır dışarıdan kilitlenir, servis çağrılır (bloke
    /// olur), kilit sahibi `OdenenTutar`'ı SINIRA çeker ve commit
    /// eder, servis devam eder.
    ///   - Kilitten SONRA okuyorsa: taze değeri görür, K3 reddeder.
    ///   - Kilitten ÖNCE okuduysa: bayat sıfırı görür, ödemeyi YAZAR
    ///     ve dışarıdaki ödemenin üstüne biner.
    ///
    /// Yani: servisin HATA VERMESİ doğru davranıştır.
    /// </summary>
    [Fact]
    public async Task BayatOkumaylaOdemeYapilamaz()
    {
        var satirId = await OnayliSatirAsync();

        using var kilitKapsami = fixture.Factory.Services.CreateScope();
        var kilitDb = kilitKapsami.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var tutulanIslem = await kilitDb.Database.BeginTransactionAsync();

        await kilitDb.Database.ExecuteSqlRawAsync(
            "SELECT \"Id\" FROM odeme_plani_satirlari WHERE \"Id\" = {0} FOR UPDATE",
            [satirId]);

        // Servis arka planda başlıyor ve kilitte bekliyor.
        var servisGorevi = Task.Run(async () =>
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

            try
            {
                await servis.SatirOdemeKaydetAsync(
                    satirId, OnaylananTutar, Guid.NewGuid(), CancellationToken.None);
                return (Gecti: true, Mesaj: string.Empty);
            }
            catch (Exception ex) { return (Gecti: false, Mesaj: ex.Message); }
        });

        // Servisin kilide gelmesi için kısa bir pay.
        await Task.Delay(700);

        // KİLİT SAHİBİ SINIRA ÇEKİYOR ve bırakıyor.
        await kilitDb.Database.ExecuteSqlRawAsync(
            "UPDATE odeme_plani_satirlari SET \"OdenenTutar\" = {0} WHERE \"Id\" = {1}",
            [OnaylananTutar, satirId]);

        await tutulanIslem.CommitAsync();

        var sonuc = await servisGorevi;

        Assert.False(sonuc.Gecti,
            "Servis, kilidi beklerken BAYAT `OdenenTutar` ile ödeme yazdı. " +
            "Demek ki okuma kilitten ÖNCE yapılıyor — iki eşzamanlı ödeme " +
            "toplamda onaylananı aşabilir (S6).");

        Assert.Contains("sınırı aşıyor", sonuc.Mesaj);

        using var kontrol = fixture.Factory.Services.CreateScope();
        var kontrolDb = kontrol.ServiceProvider.GetRequiredService<AppDbContext>();

        var satir = await kontrolDb.OdemePlaniSatirlari
            .AsNoTracking().FirstAsync(x => x.Id == satirId);

        // Dışarıdaki ödeme yerinde; servis üstüne binmemiş.
        Assert.Equal(OnaylananTutar, satir.OdenenTutar);
    }

    /// <summary>
    /// KİLİT MEŞRU ÖDEMEYİ ENGELLEMİYOR — kural fazla geniş olmasın.
    /// Kilit tutulmuyorken ödeme normal geçmeli.
    /// </summary>
    [Fact]
    public async Task Kilit_MesruOdemeyiEngellemez()
    {
        var satirId = await OnayliSatirAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        await servis.SatirOdemeKaydetAsync(
            satirId, 4_000m, Guid.NewGuid(), CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var satir = await db.OdemePlaniSatirlari
            .AsNoTracking().FirstAsync(x => x.Id == satirId);

        Assert.Equal(4_000m, satir.OdenenTutar);
    }

    /// <summary>
    /// KİLİT İŞLEM DIŞINDA ALINAMAZ — gürültülü hata.
    ///
    /// `FOR UPDATE` işlem dışında yalnız o ifade boyunca tutar; sessiz
    /// geçseydi "kilit var" görüntüsü altında aynı delik sürerdi.
    /// </summary>
    [Fact]
    public async Task Kilit_IslemDisinda_GurultuluHataVerir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var kilit = scope.ServiceProvider.GetRequiredService<IOdemeSatirKilidi>();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kilit.KilitleAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("işlem dışında alınamaz", hata.Message);
    }
}
