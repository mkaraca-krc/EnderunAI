using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Services.Finance;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// SATIR DÜZENLEME VE EKLEME/SİLME AYRI KURALLARA TABİ (ÖP/1b · D1/D2).
///
/// D1 — DÜZENLEME onaya sunulduktan sonra da SERBEST. Muhasebecinin
/// hatasını düzeltmesinin tek makul yolu bu, ve risk zaten K2'nin
/// kapsamında. Kapatsaydık K2'nin "değişiklik onayı düşürür" yarısı
/// HİÇ tetiklenemez, paketin en kritik kuralı ölü koda dönerdi.
///
/// D2 — EKLEME/SİLME onaya sunulduktan sonra KAPALI. Yeni satır
/// K2'nin GÖREMEDİĞİ yerden girer: karşılaştırılacak bir onay anlık
/// görüntüsü yoktur. Onaylanan satırlar yerinde durur ama yanlarına
/// yenisi eklenirse bütçe onaylanandan büyür.
///
/// Unutulan ödemenin yolu K5'tir (plan dışı ödeme) — sebebi zorunlu,
/// ertesi haftanın planının başında görünür.
/// </summary>
[Collection("Integration")]
public sealed class OdemePlaniSatirDuzenlemeTests(DatabaseFixture fixture)
{
    private static int _hafta;

    private static async Task<(OdemePlani Plan, OdemePlaniSatiri Satir)> KurAsync(
        AppDbContext db, OdemePlaniDurumu durum)
    {
        var hafta = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(7 * Interlocked.Increment(ref _hafta));

        var plan = new OdemePlani
        {
            CompanyId = Guid.NewGuid(),
            HaftaBaslangici = hafta,
            OdemeGunu = hafta.AddDays(4),
            Durum = durum,
            HazirlayanUserId = Guid.NewGuid()
        };
        db.OdemePlanlari.Add(plan);
        await db.SaveChangesAsync();

        var satir = new OdemePlaniSatiri
        {
            OdemePlaniId = plan.Id,
            CurrentAccountId = Guid.NewGuid(),
            OnerilenTutar = 10_000m,
            Yontem = OdemeYontemi.HavaleEft,
            Oncelik = 1
        };
        db.OdemePlaniSatirlari.Add(satir);
        await db.SaveChangesAsync();

        return (plan, satir);
    }

    // ═══ D1 — DÜZENLEME SERBEST ═══

    /// <summary>
    /// ONAYDAKİ PLANDA SATIR DÜZENLENEBİLİR.
    ///
    /// Bu testin kırmızıya dönmesi, K2'nin değişiklik yarısının
    /// tetiklenemez hâle geldiği anlamına gelir.
    /// </summary>
    [Fact]
    public async Task D1_OnaydakiPlanda_SatirDuzenlenebilir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var (_, satir) = await KurAsync(db, OdemePlaniDurumu.Onayda);

        await servis.SatirGuncelleAsync(
            satir.Id, 12_500m, OdemeYontemi.HavaleEft, null, 2, null,
            "düzeltildi", Guid.NewGuid(), CancellationToken.None);

        var sonra = await db.OdemePlaniSatirlari.AsNoTracking()
            .FirstAsync(x => x.Id == satir.Id);

        Assert.Equal(12_500m, sonra.OnerilenTutar);
        Assert.Equal(2, sonra.Oncelik);
    }

    /// <summary>KAPANMIŞ PLAN İSTİSNA — değiştirilemez (D5).</summary>
    [Fact]
    public async Task D1_KapanmisPlanda_SatirDuzenlenemez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var (_, satir) = await KurAsync(db, OdemePlaniDurumu.Kapandi);

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirGuncelleAsync(
                satir.Id, 12_500m, OdemeYontemi.HavaleEft, null, 2, null,
                null, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("Kapanmış planın satırı", hata.Message);
    }

    // ═══ D2 — EKLEME/SİLME KAPALI ═══

    /// <summary>
    /// ONAYDAKİ PLANA SATIR EKLENEMEZ — K2'nin kör noktası.
    /// </summary>
    [Fact]
    public async Task D2_OnaydakiPlana_SatirEklenemez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var (plan, _) = await KurAsync(db, OdemePlaniDurumu.Onayda);

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirEkleAsync(
                plan.Id, Guid.NewGuid(), 5_000m, OdemeYontemi.HavaleEft,
                null, 1, null, null, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("yalnız taslakta", hata.Message);

        var sayi = await db.OdemePlaniSatirlari
            .CountAsync(x => x.OdemePlaniId == plan.Id);

        Assert.Equal(1, sayi);
    }

    /// <summary>ONAYDAKİ PLANDAN SATIR SİLİNEMEZ.</summary>
    [Fact]
    public async Task D2_OnaydakiPlandan_SatirSilinemez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var (_, satir) = await KurAsync(db, OdemePlaniDurumu.Onayda);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.SatirSilAsync(satir.Id, Guid.NewGuid(), CancellationToken.None));

        var sonra = await db.OdemePlaniSatirlari.AsNoTracking()
            .FirstAsync(x => x.Id == satir.Id);

        Assert.False(sonra.IsDeleted);
    }

    /// <summary>TASLAKTA EKLEME SERBEST — kural fazla geniş olmasın.</summary>
    [Fact]
    public async Task D2_TaslaktaSatirEklenebilir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var servis = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var (plan, _) = await KurAsync(db, OdemePlaniDurumu.Taslak);

        var yeniId = await servis.SatirEkleAsync(
            plan.Id, Guid.NewGuid(), 5_000m, OdemeYontemi.Nakit,
            null, 2, null, "ek satır", Guid.NewGuid(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, yeniId);

        var sayi = await db.OdemePlaniSatirlari
            .CountAsync(x => x.OdemePlaniId == plan.Id);

        Assert.Equal(2, sayi);
    }
}
