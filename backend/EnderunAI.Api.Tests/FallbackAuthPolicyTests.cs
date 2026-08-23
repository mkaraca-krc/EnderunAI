using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// VARSAYILAN KİMLİK DOĞRULAMA — GERÇEKTEN AÇIK MI.
///
/// AuthorizeGuardTests kaynak kodu okuyor: her controller [Authorize]
/// taşıyor mu. Bu test ÇALIŞAN UYGULAMAYA soruyor: işaret ne olursa
/// olsun, anonim istek gerçekten reddediliyor mu.
///
/// İkisi ayrı şeyi kanıtlıyor ve ikisi de gerekli. Kaynak taraması
/// `[Authorize]` yazısını görür ama politikanın çalıştığını görmez;
/// bu test politikayı görür ama hangi controller'ın işaretsiz
/// kaldığını söylemez.
/// </summary>
[Collection("Integration")]
public sealed class FallbackAuthPolicyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Kimlik doğrulama isteyen uçlar: hiçbiri anonim veri döndürmemeli.
    /// Perakende burada özellikle var — 2026-08-15/23 arasında
    /// [Authorize] eksikti ve modülün tamamı anonime açıktı.
    /// </summary>
    [Theory]
    [InlineData("/api/perakende")]
    [InlineData("/api/perakende/kaynaklar")]
    [InlineData("/api/perakende/raporlar/gun-sonu")]
    [InlineData("/api/finance/dashboard")]
    [InlineData("/api/progress-payments")]
    [InlineData("/api/hr/compensation-components")]
    public async Task AnonimIstek_Reddedilmeli(string yol)
    {
        var client = fixture.Factory.CreateClient();

        var yanit = await client.GetAsync(yol);

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);

        // VERİ SIZMIYOR: 401 gövdesi kayıt taşımamalı. Durum kodu
        // doğru olup gövdenin dolu gelmesi de bir sızıntı olurdu.
        var govde = await yanit.Content.ReadAsStringAsync();

        Assert.DoesNotContain("\"items\"", govde);
        Assert.DoesNotContain("\"total\"", govde);
    }

    /// <summary>
    /// BİLEREK ANONİM KALANLAR ÇALIŞMAYA DEVAM ETMELİ.
    ///
    /// Sağlık kontrolü özellikle kritik: safe-deploy servisleri
    /// yeniden başlattıktan sonra token olmadan bu uca bakıyor.
    /// Kapanırsa HER DEPLOY sağlık kontrolünde patlar — koruma,
    /// yayının kendisini kilitlememeli.
    /// </summary>
    [Fact]
    public async Task SaglikKontrolu_AnonimCalismaliDevam()
    {
        var client = fixture.Factory.CreateClient();

        var yanit = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Contains("ok", await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GirisUcu_AnonimErisilebilirOlmali()
    {
        var client = fixture.Factory.CreateClient();

        // Yanlış parola bilerek: aranan şey 401 (kimlik reddi) DEĞİL,
        // ucun kapalı olmaması. Kapalı olsaydı istek daha giriş
        // mantığına ulaşmadan reddedilirdi ve kimse giremezdi.
        var yanit = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "olmayan-kullanici", password = "yanlis" });

        Assert.NotEqual(HttpStatusCode.NotFound, yanit.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, yanit.StatusCode);
    }
}
