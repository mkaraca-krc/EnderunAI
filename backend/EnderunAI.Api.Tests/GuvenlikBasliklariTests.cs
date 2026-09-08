using System.Net;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// nosniff HER YANITTA — TEK KANONİK YERDEN (BAŞLIK/1).
///
/// ═══ ÖLÇÜLEN EKSİK ═══
///
/// `X-Content-Type-Options` hiçbir yerde yoktu; ne arka uçta ne
/// nginx'te. Dosya servis eden 13 kontrolcü bu başlık olmadan yanıt
/// veriyordu.
///
/// ═══ NEDEN ÇAĞIRARAK ═══
///
/// "Middleware kayıtlı mı" sorusu kaynaktan okunabilir; "başlık
/// GERÇEKTEN gidiyor mu" okunamaz (Kural 70). Middleware kayıtlı
/// ama yanlış yerde olsa, ya da `OnStarting` hiç koşmasa, kaynak
/// yine doğru görünürdü.
/// </summary>
[Collection("Integration")]
public sealed class GuvenlikBasliklariTests(DatabaseFixture fixture)
{
    /// <summary>
    /// KİMLİKSİZ YANITTA DA VAR.
    ///
    /// 401 dönen bir yanıt da tarayıcıya ulaşıyor ve gövdesi
    /// yorumlanıyor; başlık orada da olmalı.
    /// </summary>
    [Fact]
    public async Task KimliksizYanitta_NosniffVar()
    {
        var client = fixture.Factory.CreateClient();
        var yanit = await client.GetAsync("/api/personnel");

        // POZİTİF KONTROL: gerçekten kimlik kapısına takıldık;
        // 200 dönseydi başka bir şeyi ölçüyor olurduk.
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);

        Assert.True(
            yanit.Headers.TryGetValues(
                GuvenlikBasliklariMiddleware.NosniffBasligi, out var degerler),
            "nosniff başlığı yok.");

        Assert.Contains(GuvenlikBasliklariMiddleware.NosniffDegeri, degerler);
    }

    /// <summary>
    /// ANONİM VE BAŞARILI YANITTA DA VAR.
    ///
    /// Yalnız hata yollarında eklenen bir başlık, asıl korunması
    /// gereken başarılı yanıtı korumazdı.
    /// </summary>
    [Fact]
    public async Task AnonimBasariliYanitta_NosniffVar()
    {
        var client = fixture.Factory.CreateClient();
        var yanit = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        Assert.True(
            yanit.Headers.TryGetValues(
                GuvenlikBasliklariMiddleware.NosniffBasligi, out var degerler),
            "Başarılı yanıtta nosniff başlığı yok.");

        Assert.Contains(GuvenlikBasliklariMiddleware.NosniffDegeri, degerler);
    }

    /// <summary>
    /// GÖVDESİZ YANITTA DA VAR (204).
    ///
    /// `OnStarting` gövde yazılmadan da koşmalı. 204 dönen 21 uç var
    /// ve bir kez altı hafta boyunca kırık kaldıkları ölçüldü —
    /// gövdesiz yol bu projede ayrı bir sınıf.
    /// </summary>
    [Fact]
    public async Task GovdesizYanitta_NosniffVar()
    {
        var client = fixture.Factory.CreateClient();
        var yanit = await client.GetAsync("/api/health/govdesiz");

        Assert.Equal(HttpStatusCode.NoContent, yanit.StatusCode);

        Assert.True(
            yanit.Headers.TryGetValues(
                GuvenlikBasliklariMiddleware.NosniffBasligi, out var degerler),
            "Gövdesiz yanıtta nosniff başlığı yok.");

        Assert.Contains(GuvenlikBasliklariMiddleware.NosniffDegeri, degerler);
    }
}
