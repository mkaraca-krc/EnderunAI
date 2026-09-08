using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÖDEME PLANI — UÇTAKİ İZİN KAPISI (ÖP/1b).
///
/// KATALOG DÜZEYİ AYRI DOSYADA (`OdemePlaniIzinTests`): orası
/// "hangi rol hangi anahtarı taşıyor" sorusunu RoleCatalog
/// üzerinden sorar. Burası anahtarın UÇTA gerçekten aranıp
/// aranmadığını sorar. Katalog doğru olup uçta attribute
/// unutulsaydı oradaki testler yeşil kalırdı.
///
/// ARAYÜZ KAPISI BAŞKA YERDE SINANIYOR (tests/odeme-plani-ekran.test.tsx:
/// "yalnız hazırlama izniyle karar düğmeleri görünmez"). Burada
/// sınanan SUNUCU kapısı ve ikisi bilerek ayrı:
///
/// Arayüzdeki gizleme yalnız kolaylıktır — kullanıcıya işe yaramayan
/// bir düğme göstermemek için. Gerçek kapı burada. İkisi tek testte
/// birleştirilseydi, arayüz kapısı bir gün kaldırıldığında sunucu
/// kapısının hâlâ durup durmadığı görülmezdi; testin yeşili yanlış
/// yerden gelirdi.
///
/// ÖN MUHASEBE HAZIRLAR, ONAYLAYAMAZ. Ödeme onayı Genel Müdür'ün
/// işi (İ2) ve Admin'e bile kendiliğinden gitmiyor.
/// </summary>
[Collection("Integration")]
public sealed class OdemePlaniUcIzinTests(DatabaseFixture fixture)
{
    private static int _hafta;

    private async Task<(Guid PlanId, Guid SatirId)> PlanKurAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hafta = new DateTime(2028, 1, 3, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(7 * Interlocked.Increment(ref _hafta));

        var plan = new OdemePlani
        {
            CompanyId = Guid.NewGuid(),
            HaftaBaslangici = hafta,
            OdemeGunu = hafta.AddDays(4),
            Durum = OdemePlaniDurumu.Onayda,
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

        return (plan.Id, satir.Id);
    }

    /// <summary>
    /// ÖN MUHASEBE KARAR UCUNA 403 ALIR.
    ///
    /// Ekranı açabiliyor olması onay verebileceği anlamına gelmez.
    /// </summary>
    [Fact]
    public async Task OnMuhasebe_KararUcuna_403_Alir()
    {
        var (_, satirId) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-onmuhasebe", ["Ön Muhasebe"]);

        var response = await client.PostAsJsonAsync(
            $"/api/odeme-planlari/satirlar/{satirId}/karar",
            new { karar = (int)OdemeSatirKarari.Onaylandi, onaylananTutar = 10_000m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// AYNI KULLANICI HAZIRLAMA UÇLARINI KULLANABİLİR.
    ///
    /// Kural "Ön Muhasebe ödeme planına dokunamaz" DEĞİL, "karar
    /// veremez". Bu iddia olmasaydı izni tamamen kapatan bir değişiklik
    /// de testi yeşil bırakırdı.
    /// </summary>
    [Fact]
    public async Task OnMuhasebe_PlaniOkuyabilir()
    {
        var (planId, _) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-onmuh-oku", ["Ön Muhasebe"]);

        var response = await client.GetAsync($"/api/odeme-planlari/{planId}");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// YETKİ/3 · YT1 — ADMİN'İN GERÇEKTE NE ALDIĞI, ÇAĞIRARAK.
    ///
    /// ═══ BU TEST BİR KARAR DEĞİL, BİR ÖLÇÜMDÜR ═══
    ///
    /// Bu dosyanın kendi başlığı *"Ödeme onayı Genel Müdür'ün işi ve
    /// Admin'e bile kendiliğinden gitmiyor"* diyor. Veritabanı da öyle
    /// diyor: katalogda 147 izin var, Genel Müdür 147'sini taşıyor,
    /// Admin 146'sını — eksik olan tam da `payment.plan.approve`.
    ///
    /// Ama `PermissionAuthorizationMiddleware` Admin rolünü gören her
    /// isteği izin kontrolüne HİÇ SOKMADAN geçiriyor. Yani niyet ile
    /// gerçek ayrışmış olabilir ve bunu kod okuyarak değil ÇAĞIRARAK
    /// bilmek gerekiyor (Kural 70).
    ///
    /// Test bugünkü DAVRANIŞI yazıyor, doğru olduğunu iddia etmiyor.
    /// Hangisinin doğru olduğu Mehmet'in kararı (YETKİ/3 · YT4):
    ///   · Admin her izne sahip olmalıysa çözüm veritabanına o izni
    ///     EKLEMEK — arayüzü ya da middleware'i değil.
    ///   · Admin ayrıcalıklı olmamalıysa çözüm middleware'deki
    ///     kısayolu kaldırmak.
    /// Karar verilince bu testin iddiası da onunla birlikte değişir.
    /// </summary>
    [Fact]
    public async Task YalnizAdmin_KararUcunda_403_ALMIYOR()
    {
        var (_, satirId) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-yalniz-admin", ["Admin"]);

        var response = await client.PostAsJsonAsync(
            $"/api/odeme-planlari/satirlar/{satirId}/karar",
            new { karar = (int)OdemeSatirKarari.Onaylandi, onaylananTutar = 10_000m });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// KARŞIT AYAK — ÖLÇÜM TOTOLOJİ OLMASIN (Kural 48).
    ///
    /// Yukarıdaki test tek başına "uç zaten kimseye 403 vermiyor"
    /// durumunda da yeşil kalırdı. Bu ayak, aynı uç aynı gövdeyle
    /// izinsiz bir role GERÇEKTEN 403 veriyor mu diye soruyor.
    /// İkisi birlikte, Admin'in ayrıcalıklı olduğunu gösteriyor.
    /// </summary>
    [Fact]
    public async Task AyniUc_IzinsizRole_403_Veriyor()
    {
        var (_, satirId) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-karsit", ["Depo Sorumlusu"]);

        var response = await client.PostAsJsonAsync(
            $"/api/odeme-planlari/satirlar/{satirId}/karar",
            new { karar = (int)OdemeSatirKarari.Onaylandi, onaylananTutar = 10_000m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>GENEL MÜDÜR KARAR UCUNDA 403 ALMAZ.</summary>
    [Fact]
    public async Task GenelMudur_KararUcunda_403_Almaz()
    {
        var (_, satirId) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-gm", ["Genel Müdür"]);

        var response = await client.PostAsJsonAsync(
            $"/api/odeme-planlari/satirlar/{satirId}/karar",
            new { karar = (int)OdemeSatirKarari.Onaylandi, onaylananTutar = 10_000m });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// FİNANS SORUMLUSU DA ONAYLAYAMAZ.
    ///
    /// Finansın tamamına yetkili olmak ödeme onayı vermeye yetmiyor:
    /// hazırlayan ile onaylayan ayrı kişiler (K4'ün rol düzeyindeki
    /// karşılığı).
    /// </summary>
    [Fact]
    public async Task FinansSorumlusu_KararUcuna_403_Alir()
    {
        var (_, satirId) = await PlanKurAsync();

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "op-finans", ["Finans Sorumlusu"]);

        var response = await client.PostAsJsonAsync(
            $"/api/odeme-planlari/satirlar/{satirId}/karar",
            new { karar = (int)OdemeSatirKarari.Onaylandi, onaylananTutar = 10_000m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
