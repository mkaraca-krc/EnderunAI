using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Services.Finance;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÖDEME PLANININ UÇTAKİ DAVRANIŞI (ÖP/1a · K2, K3, K4, K8, K10).
///
/// Saf kural testleri kararların doğru olduğunu gösteriyor; burası o
/// kararların SERVİSE BAĞLANDIĞINI gösteriyor. İkisi ayrı: kural
/// doğru olup çağrılmıyor olabilir — ÇEK/2'de tam bu ayrımı
/// kaçırmıştım (Kural 45).
/// </summary>
[Collection("Integration")]
public sealed class OdemePlaniDavranisTests(DatabaseFixture fixture)
{
    private static (AppDbContext Db, OdemePlaniService Servis) Kur(IServiceScope scope)
        => (scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<OdemePlaniService>());

    private static async Task<OdemePlani> PlanAsync(
        AppDbContext db, Guid hazirlayan, DateTime hafta)
    {
        var plan = new OdemePlani
        {
            CompanyId = Guid.NewGuid(),
            HaftaBaslangici = hafta,
            OdemeGunu = hafta.AddDays(4),
            Durum = OdemePlaniDurumu.Onayda,
            HazirlayanUserId = hazirlayan
        };
        db.OdemePlanlari.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private static async Task<OdemePlaniSatiri> SatirAsync(
        AppDbContext db, OdemePlani plan, Guid hazirlayan, decimal tutar = 10_000m)
    {
        var satir = new OdemePlaniSatiri
        {
            OdemePlaniId = plan.Id,
            CurrentAccountId = Guid.NewGuid(),
            OnerilenTutar = tutar,
            Yontem = OdemeYontemi.HavaleEft,
            Oncelik = 1,
            CashAccountId = Guid.NewGuid(),
            CreatedByUserId = hazirlayan
        };
        db.OdemePlaniSatirlari.Add(satir);
        await db.SaveChangesAsync();
        return satir;
    }

    // ═══ K4 — HAZIRLAYAN ≠ ONAYLAYAN ═══

    /// <summary>
    /// HAZIRLAYAN KENDİ SATIRINI ONAYLAYAMAZ — kod düzeyinde.
    /// </summary>
    [Fact]
    public async Task K4_HazirlayanOnaylayamaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirKararVerAsync(
                satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
                hazirlayan, CancellationToken.None));

        Assert.Contains("onaylayamaz", hata.Message);
    }

    [Fact]
    public async Task K4_BaskasiOnaylayabilir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(OdemeSatirKarari.Onaylandi, sonra!.Karar);

        // K2 ANLIK GÖRÜNTÜSÜ YAZILDI MI — onayın kendisi kadar önemli.
        Assert.Equal(10_000m, sonra.OnayliTutar);
        Assert.Equal(satir.CurrentAccountId, sonra.OnayliCurrentAccountId);
        Assert.Equal(1, sonra.OnayliOncelik);
    }

    // ═══ K2 — ONAYDAN SONRA DEĞİŞEN SATIR ÖDENMEZ ═══

    /// <summary>
    /// PAKETİN EN KRİTİK KURALI. Onaydan sonra tutar değiştirilip
    /// ödenebilseydi onay hiçbir şey ifade etmezdi.
    /// </summary>
    [Fact]
    public async Task K2_OnaydanSonraDegisenSatir_Odenmez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        // ONAYDAN SONRA TUTAR DEĞİŞTİRİLİYOR.
        var izlenen = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        izlenen!.OnaylananTutar = 99_000m;
        await db.SaveChangesAsync();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirOdemeKaydetAsync(
                satir.Id, 99_000m, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("Tutar", hata.Message);

        // SATIR YENİDEN ONAYA DÖNDÜ — sadece hata vermek yetmez.
        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(OdemeSatirKarari.Bekliyor, sonra!.Karar);
        Assert.Equal(0m, sonra.OdenenTutar);
    }

    /// <summary>ÖNCELİK DEĞİŞİRSE DE ÖDENMEZ (K7) — sıra bir ödeme kararıdır.</summary>
    [Fact]
    public async Task K2_OncelikDegisirse_Odenmez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        var izlenen = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        izlenen!.Oncelik = 9;
        await db.SaveChangesAsync();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirOdemeKaydetAsync(
                satir.Id, 10_000m, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("Öncelik", hata.Message);
    }

    /// <summary>DEĞİŞMEYEN SATIR ÖDENİR — kural fazla geniş olmasın.</summary>
    [Fact]
    public async Task K2_DegismeyenSatir_Odenir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        await servis.SatirOdemeKaydetAsync(
            satir.Id, 10_000m, Guid.NewGuid(), CancellationToken.None);

        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(OdemeSatirOdemeDurumu.Odendi, sonra!.OdemeDurumu);
        Assert.Equal(10_000m, sonra.OdenenTutar);
    }

    // ═══ K3 — ÖDENEN ≤ ONAYLANAN ═══

    [Fact]
    public async Task K3_OnaylanandanFazlasi_Odenemez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirOdemeKaydetAsync(
                satir.Id, 10_000.01m, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("sınırı aşıyor", hata.Message);

        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(0m, sonra!.OdenenTutar);
    }

    /// <summary>AZ ÖDEMEK SERBEST — kısmi ödeme reddedilmemeli.</summary>
    [Fact]
    public async Task K3_KismiOdeme_Serbest()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        await servis.SatirOdemeKaydetAsync(
            satir.Id, 4_000m, Guid.NewGuid(), CancellationToken.None);

        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(OdemeSatirOdemeDurumu.KismenOdendi, sonra!.OdemeDurumu);
        Assert.Equal(4_000m, sonra.OdenenTutar);
    }

    // ═══ K8 — YAŞLANMA ═══

    /// <summary>
    /// ÜÇ HAFTAYI AŞAN ONAYLA PARA ÇIKMAZ. Satır "Bekliyor"a döner.
    /// </summary>
    [Fact]
    public async Task K8_EskiOnaylaOdemeYapilamaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        // KARAR ANINI GERİYE ÇEK — altı hafta önce onaylanmış gibi.
        var izlenen = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        izlenen!.KararAnUtc = DateTime.UtcNow.AddDays(-42);
        await db.SaveChangesAsync();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirOdemeKaydetAsync(
                satir.Id, 10_000m, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("onay düştü", hata.Message);

        var sonra = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        Assert.Equal(OdemeSatirKarari.Bekliyor, sonra!.Karar);
    }

    // ═══ K10 — KAPANIŞ SEBEBİ ═══

    [Fact]
    public async Task K10_SebepsizSatirVarken_PlanKapanmaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.KapatAsync(plan.Id, hazirlayan, CancellationToken.None));

        Assert.Contains("kapanış sebebi", hata.Message);
    }

    [Fact]
    public async Task K10_SebepVerilince_PlanKapanir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (db, servis) = Kur(scope);

        var hazirlayan = Guid.NewGuid();
        var hafta = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
        var plan = await PlanAsync(db, hazirlayan, hafta);
        var satir = await SatirAsync(db, plan, hazirlayan);

        await servis.SatirKararVerAsync(
            satir.Id, OdemeSatirKarari.Onaylandi, null, null, null,
            Guid.NewGuid(), CancellationToken.None);

        var izlenen = await db.OdemePlaniSatirlari.FindAsync(satir.Id);
        izlenen!.KapanisSebebi = OdemeKapanisSebebi.ParaYetmedi;
        await db.SaveChangesAsync();

        await servis.KapatAsync(plan.Id, hazirlayan, CancellationToken.None);

        var sonra = await db.OdemePlanlari.FindAsync(plan.Id);
        Assert.Equal(OdemePlaniDurumu.Kapandi, sonra!.Durum);
    }

    // ═══ HAFTALIK TETİKLEYİCİ ═══

    /// <summary>
    /// PAZARTESİ 05:00 HESABI. Arka plan servisini çalıştırmadan
    /// ölçülüyor — zaman dışarıdan veriliyor.
    /// </summary>
    [Theory]
    [InlineData("2026-08-27T10:00:00Z", "2026-08-31T05:00:00Z")]  // perşembe -> pazartesi
    [InlineData("2026-08-31T04:00:00Z", "2026-08-31T05:00:00Z")]  // pazartesi sabahı
    [InlineData("2026-08-31T06:00:00Z", "2026-09-07T05:00:00Z")]  // pazartesi, saat geçti
    public void HaftalikTetikleyici_SonrakiPazartesi(string simdi, string beklenen)
    {
        var s = DateTime.Parse(simdi, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal
            | System.Globalization.DateTimeStyles.AssumeUniversal);

        var b = DateTime.Parse(beklenen, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal
            | System.Globalization.DateTimeStyles.AssumeUniversal);

        Assert.Equal(b - s,
            HaftalikOdemePlaniBackgroundService.SonrakiTuraKalan(s));
    }
}
