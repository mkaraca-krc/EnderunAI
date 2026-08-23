using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İŞVEREN PORTALI — SÜRE VE BAŞARISIZ DENEME KAYDI.
///
/// Portal, sistemin kimlik doğrulaması olmayan TEK veri kapısı.
/// Token 256 bit, hız sınırı var ve veri kapsamı projeye kilitli;
/// eksik olan iki şey kapatıldı:
///   1. Bağlantı artık SÜRESİZ değil (varsayılan 6 ay, uzatılabilir).
///   2. Başarısız denemeler güvenlik kaydına düşüyor.
///
/// SÜRESİ GEÇEN BAĞLANTI 404 DÖNER, 401 DEĞİL: 401 "böyle bir
/// bağlantı vardı ama artık geçerli değil" bilgisini verirdi ve
/// token arayan birine "bu token bir zamanlar geçerliydi" ipucu
/// olurdu. Testler durum kodunu bu yüzden AÇIKÇA sınıyor.
/// </summary>
[Collection("Integration")]
public sealed class PortalLinkExpiryTests(DatabaseFixture fixture)
{
    private static async Task<EmployerPortalLink> BaglantiKurAsync(
        AppDbContext db,
        string suffix,
        DateTime expiresAtUtc,
        bool revoked = false)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"PORT{suffix}");

        var link = new EmployerPortalLink
        {
            ProjectId = proje.Id,
            Token = $"test-token-{suffix}-{Guid.NewGuid():N}",
            EmployerName = "Test İşveren",
            ExpiresAtUtc = expiresAtUtc,
            IsActive = !revoked,
            RevokedAtUtc = revoked ? DateTime.UtcNow : null
        };

        db.EmployerPortalLinks.Add(link);
        await db.SaveChangesAsync();

        return link;
    }

    private static async Task<int> DenetimSayisiAsync(
        AppDbContext db, string action) =>
        await db.SecurityAuditEvents.CountAsync(x => x.Action == action);

    // ---------------------------------------------------------------
    // 1) Süre
    // ---------------------------------------------------------------

    [Fact]
    public async Task SuresiGecmisBaglanti_404Doner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddDays(-1));

        var client = fixture.Factory.CreateClient();

        var yanit = await client.GetAsync($"/api/portal/{link.Token}");

        // 404 — 401 DEĞİL. Varlığını da ele vermemeli.
        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task IptalEdilmisBaglanti_404Doner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(6), revoked: true);

        var client = fixture.Factory.CreateClient();

        var yanit = await client.GetAsync($"/api/portal/{link.Token}");

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    /// <summary>
    /// KARŞI KONTROL: koruma meşru erişimi kapatmamalı. Bu test
    /// olmasaydı "her zaman 404 dön" de diğer testleri geçerdi.
    /// </summary>
    [Fact]
    public async Task GecerliBaglanti_CalismayaDevamEder()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(6));

        var client = fixture.Factory.CreateClient();

        var yanit = await client.GetAsync($"/api/portal/{link.Token}");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    /// <summary>
    /// Erişim izi: yönetim ekranı "bu bağlantı kullanılıyor mu"
    /// sorusunu buradan cevaplıyor.
    /// </summary>
    [Fact]
    public async Task GecerliErisim_SayacVeSonErisimiGunceller()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(6));

        Assert.Equal(0, link.AccessCount);

        var client = fixture.Factory.CreateClient();
        await client.GetAsync($"/api/portal/{link.Token}");
        await client.GetAsync($"/api/portal/{link.Token}");

        var guncel = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.Id == link.Id);

        Assert.Equal(2, guncel.AccessCount);
        Assert.NotNull(guncel.LastAccessedAtUtc);
    }

    // ---------------------------------------------------------------
    // 2) Başarısız deneme kaydı
    // ---------------------------------------------------------------

    [Fact]
    public async Task BasarisizDeneme_GuvenlikKaydinaDuser_TokenTamYazilmaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var once = await DenetimSayisiAsync(db, "PortalTokenRejected");

        // 43 karakterlik gerçekçi bir token: önek kesme sınanabilsin.
        var sahteToken = "SAHTE123" + Guid.NewGuid().ToString("N") + "abc";

        var client = fixture.Factory.CreateClient();
        var yanit = await client.GetAsync($"/api/portal/{sahteToken}");

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);

        var sonra = await DenetimSayisiAsync(db, "PortalTokenRejected");
        Assert.Equal(once + 1, sonra);

        var kayit = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "PortalTokenRejected")
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();

        // TOKEN'IN TAMAMI HİÇBİR ALANDA OLMAMALI. Güvenlik kaydı,
        // koruduğu sırrı ele veren bir yer olamaz.
        var tumKayit = JsonSerializer.Serialize(kayit);
        Assert.DoesNotContain(sahteToken, tumKayit);

        // Ama tanımaya yetecek kadar önek VAR.
        Assert.Contains("SAHTE123", kayit.DetailsJson!);
        Assert.Contains("bilinmeyen_token", kayit.DetailsJson!);
    }

    /// <summary>
    /// Sebep ayrımı YALNIZ denetim kaydında: dışarıya dönen yanıt
    /// her durumda 404. İçeride ayırmak gerekiyor çünkü "süresi
    /// dolmuş bağlantıyı açan işveren" ile "token arayan yabancı"
    /// farklı olaylardır.
    /// </summary>
    [Fact]
    public async Task SuresiGecmisDeneme_SebebiyleKaydediliyor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddDays(-1));

        var client = fixture.Factory.CreateClient();
        await client.GetAsync($"/api/portal/{link.Token}");

        var kayit = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "PortalTokenRejected")
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();

        Assert.Contains("suresi_gecmis", kayit.DetailsJson!);
        Assert.DoesNotContain(link.Token, JsonSerializer.Serialize(kayit));
    }

    // ---------------------------------------------------------------
    // 3) Uzatma ve iptal denetim kaydı
    // ---------------------------------------------------------------

    [Fact]
    public async Task Uzatma_DenetimKaydinaDuser_VeTarihiIleriAlir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddDays(10));

        var eskiTarih = link.ExpiresAtUtc;
        var once = await DenetimSayisiAsync(db, "PortalLinkExtended");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            $"/api/projects/{link.ProjectId}/employer-portal-link/extend",
            new { months = 6, reason = "İşveren raporlaması sürüyor." });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var guncel = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.Id == link.Id);

        Assert.True(guncel.ExpiresAtUtc > eskiTarih);
        Assert.Equal(1, guncel.ExtensionCount);
        Assert.NotNull(guncel.LastExtendedAtUtc);

        Assert.Equal(once + 1, await DenetimSayisiAsync(db, "PortalLinkExtended"));

        var kayit = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "PortalLinkExtended")
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();

        // KİM, NE ZAMAN, NEDEN.
        Assert.NotNull(kayit.ActorUserId);
        Assert.Contains("İşveren raporlaması sürüyor.", kayit.DetailsJson!);

        // Token denetim kaydına GİRMİYOR.
        Assert.DoesNotContain(link.Token, JsonSerializer.Serialize(kayit));
    }

    [Fact]
    public async Task Iptal_DenetimKaydinaDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(6));

        var once = await DenetimSayisiAsync(db, "PortalLinkRevoked");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            $"/api/projects/{link.ProjectId}/employer-portal-link/revoke",
            new { reason = "Proje tamamlandı." });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal(once + 1, await DenetimSayisiAsync(db, "PortalLinkRevoked"));

        var kayit = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.Action == "PortalLinkRevoked")
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();

        Assert.NotNull(kayit.ActorUserId);
        Assert.Contains("Proje tamamlandı.", kayit.DetailsJson!);

        // İptal sonrası portal gerçekten kapanmalı — kayıt tutmak
        // tek başına yetmez.
        var portalClient = fixture.Factory.CreateClient();
        var portalYanit = await portalClient.GetAsync($"/api/portal/{link.Token}");

        Assert.Equal(HttpStatusCode.NotFound, portalYanit.StatusCode);
    }

    /// <summary>
    /// Süresi geçmiş bağlantı uzatılırken tarih BUGÜNDEN ileri
    /// alınmalı. Eski tarihe eklenseydi kullanıcı "uzattım" der,
    /// portal 404 dönmeye devam ederdi.
    /// </summary>
    [Fact]
    public async Task SuresiGecmisBaglantiUzatilinca_YenidenCalisir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(-8));

        var portalClient = fixture.Factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await portalClient.GetAsync($"/api/portal/{link.Token}")).StatusCode);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            $"/api/projects/{link.ProjectId}/employer-portal-link/extend",
            new { months = 6, reason = "Yeniden açıldı." });

        Assert.Equal(
            HttpStatusCode.OK,
            (await portalClient.GetAsync($"/api/portal/{link.Token}")).StatusCode);
    }

    // ---------------------------------------------------------------
    // 4) Oluşturma varsayılanı ve yönetim ekranının alanları
    // ---------------------------------------------------------------

    [Fact]
    public async Task YeniBaglanti_AltiAylikOlusur_VeDurumBilgisiDoner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"YENI{suffix}");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsync(
            $"/api/projects/{proje.Id}/employer-portal-link", null);

        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        var kayit = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == proje.Id && x.IsActive);

        // 6 ay: sınırları gevşek tutuyorum, ay uzunlukları değişiyor.
        var beklenen = DateTime.UtcNow.AddMonths(6);
        Assert.True(kayit.ExpiresAtUtc > beklenen.AddDays(-2));
        Assert.True(kayit.ExpiresAtUtc < beklenen.AddDays(2));

        // Ekranın ihtiyaç duyduğu alanlar uçta VAR.
        var govde = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{proje.Id}/employer-portal-link");

        var link = govde.GetProperty("link");

        Assert.Equal("aktif", link.GetProperty("durum").GetString());
        Assert.Equal(0, link.GetProperty("accessCount").GetInt32());
        Assert.True(link.GetProperty("kalanGun").GetInt32() > 150);
        Assert.True(link.TryGetProperty("expiresAtUtc", out _));
        Assert.True(link.TryGetProperty("lastAccessedAtUtc", out _));
    }

    /// <summary>
    /// Ekranın "sarı" göstereceği durum sunucudan geliyor — tarayıcının
    /// saatine bırakılsaydı saati geri alınmış bir makinede bağlantı
    /// geçerli görünürdü.
    /// </summary>
    [Fact]
    public async Task SonuYaklasanBaglanti_YaklasiyorDurumuDoner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddDays(10));

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var govde = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{link.ProjectId}/employer-portal-link");

        Assert.Equal(
            "yaklasiyor",
            govde.GetProperty("link").GetProperty("durum").GetString());
    }

    // ---------------------------------------------------------------
    // 5) TOKEN HİÇBİR KAYDA DÜŞMEZ — denetim kesicisi dahil
    // ---------------------------------------------------------------

    /// <summary>
    /// DENETİM KESİCİSİ DE TOKEN YAZMAZ.
    ///
    /// `AuditSaveChangesInterceptor` EmployerPortalLink'i izliyor ve
    /// özet alanı `EmployerEmail ?? Token` idi: e-postası olmayan
    /// bağlantılarda 256 bitlik anahtarın TAMAMI düz metin olarak
    /// security_audit_events'e yazılıyordu. Canlıda 4 kayıtta
    /// bulundu (2026-08-23).
    ///
    /// Token üç yerde maskeleniyor artık: nginx erişim kaydı,
    /// PortalTokenRejected olayı ve BURASI. Üçü de ayrı kod yolu;
    /// biri düzeltilince diğeri kendiliğinden düzelmiyor — bu yüzden
    /// ayrı test.
    /// </summary>
    [Fact]
    public async Task BaglantiOlusturuldugunda_TokenDenetimKaydinaYazilmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"TKN{suffix}");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsync(
            $"/api/projects/{proje.Id}/employer-portal-link", null);

        var link = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == proje.Id && x.IsActive);

        // E-POSTA YOK: eski kodda tam token yazılan durum tam buydu.
        Assert.Null(link.EmployerEmail);

        var kayitlar = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.EntityType == "EmployerPortalLink" &&
                        x.EntityId == link.Id)
            .ToListAsync();

        Assert.NotEmpty(kayitlar);

        foreach (var kayit in kayitlar)
        {
            Assert.DoesNotContain(link.Token, kayit.DetailsJson ?? string.Empty);
        }
    }

    /// <summary>
    /// Portal açılışı DENETİM KAYDI ÜRETMEZ.
    ///
    /// Erişim sayacı bir denetim olayı değildir. SaveChanges ile
    /// güncellenseydi kesici her açılışta bir "Updated" satırı yazar,
    /// kayıt bu gürültüyle dolar ve asıl olaylar (oluşturma, uzatma,
    /// iptal) içinde kaybolurdu.
    /// </summary>
    [Fact]
    public async Task PortalAcilisi_DenetimKaydiUretmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var link = await BaglantiKurAsync(
            db, suffix, DateTime.UtcNow.AddMonths(6));

        var once = await db.SecurityAuditEvents
            .CountAsync(x => x.EntityType == "EmployerPortalLink");

        var client = fixture.Factory.CreateClient();
        await client.GetAsync($"/api/portal/{link.Token}");
        await client.GetAsync($"/api/portal/{link.Token}");

        var sonra = await db.SecurityAuditEvents
            .CountAsync(x => x.EntityType == "EmployerPortalLink");

        Assert.Equal(once, sonra);

        // Sayaç YİNE DE ilerlemiş olmalı — gürültüyü kesmek izi
        // kaybetmek anlamına gelmemeli.
        var guncel = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.Id == link.Id);

        Assert.Equal(2, guncel.AccessCount);
    }

    /// <summary>
    /// YENİDEN ÜRETİLEN BAĞLANTI YENİ TOKEN ALIR, ESKİSİ ÖLÜR.
    ///
    /// Bir token bir kez yandıysa (denetim kaydına düz metin
    /// yazıldığı 2026-08-23 olayında olduğu gibi, ya da e-postayla
    /// dolaştığı için) bir daha güvenli sayılamaz. Pasif bir
    /// bağlantıyı eski tokenıyla canlandırmak, yanmış sırrı yeniden
    /// kullanıma sokmak olurdu.
    /// </summary>
    [Fact]
    public async Task YenidenUretim_YeniTokenVerir_EskisiOlur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"YEN{suffix}");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var portalClient = fixture.Factory.CreateClient();

        await client.PostAsync($"/api/projects/{proje.Id}/employer-portal-link", null);

        var eski = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == proje.Id && x.IsActive);

        Assert.Equal(
            HttpStatusCode.OK,
            (await portalClient.GetAsync($"/api/portal/{eski.Token}")).StatusCode);

        // İkinci üretim.
        await client.PostAsync($"/api/projects/{proje.Id}/employer-portal-link", null);

        var yeni = await db.EmployerPortalLinks
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == proje.Id && x.IsActive);

        Assert.NotEqual(eski.Token, yeni.Token);

        // ESKİ TOKEN ARTIK ÇALIŞMIYOR.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await portalClient.GetAsync($"/api/portal/{eski.Token}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await portalClient.GetAsync($"/api/portal/{yeni.Token}")).StatusCode);

        // İki tokenın hiçbiri denetim kaydına düz metin girmemeli.
        var kayitlar = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.EntityType == "EmployerPortalLink")
            .Select(x => x.DetailsJson)
            .ToListAsync();

        foreach (var kayit in kayitlar)
        {
            Assert.DoesNotContain(eski.Token, kayit ?? string.Empty);
            Assert.DoesNotContain(yeni.Token, kayit ?? string.Empty);
        }
    }
}
