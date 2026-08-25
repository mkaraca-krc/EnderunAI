using System.Net;
using EnderunAI.Api.Hubs;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// M3/0 — GERÇEK ZAMANLI İSKELET.
///
/// Bu turda hub yalnız BAĞLANIYOR; mesaj, kanal ve okundu bilgisi
/// sonraki fazlarda. İskeletin ayrı deploy edilmesinin sebebi:
/// altyapının canlıda çalıştığını, üstüne veri modeli koymadan ÖNCE
/// görmek.
///
/// KİMLİK ÇEREZDEN, SORGU DİZESİNDEN DEĞİL. `access_token` sorgu
/// parametresi token'ı URL'e sokar; erişim kaydına, tarayıcı
/// geçmişine ve proxy kayıtlarına düşer. Portal token'ında
/// yaşananın aynısı olurdu.
/// </summary>
[Collection("Integration")]
public sealed class MesajHubTests(DatabaseFixture fixture)
{
    private const string HubYolu = "/api/hubs/mesaj";

    /// <summary>
    /// KİMLİKSİZ BAĞLANTI REDDEDİLİR.
    ///
    /// SignalR el sıkışması (`/negotiate`) kimlik ister; `[Authorize]`
    /// ve FallbackPolicy birlikte kimliksiz isteği 401 ile keser.
    /// </summary>
    [Fact]
    public async Task Kimliksiz_HubaBaglanamaz()
    {
        var client = fixture.Factory.CreateClient();

        var yanit = await client.PostAsync($"{HubYolu}/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    /// <summary>
    /// KİMLİKLİ BAĞLANTI KABUL EDİLİR — başlıkla.
    ///
    /// Test istemcisi WebSocket kurmuyor; el sıkışmasının kimlik
    /// kapısını geçtiğini doğruluyor. Gerçek WebSocket yükseltmesi
    /// nginx'in işi ve orada ayrıca ölçüldü.
    /// </summary>
    [Fact]
    public async Task Kimlikli_HubElSikismasiniGecer()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsync($"{HubYolu}/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();

        // Bağlantı kimliği dönüyorsa el sıkışma gerçekten tamamlandı.
        Assert.Contains("connectionId", govde, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ÇEREZLE DE GEÇER — WebSocket'in tek yolu bu.
    ///
    /// Tarayıcı el sıkışmasında özel başlık gönderemez; çerez
    /// gönderir. Bu test, `OnMessageReceived` olayının çerezi
    /// GERÇEKTEN okuduğunu kanıtlıyor — başlık HİÇ gönderilmiyor.
    /// </summary>
    [Fact]
    public async Task Cerezle_HubElSikismasiniGecer()
    {
        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client);

        var istek = new HttpRequestMessage(
            HttpMethod.Post, $"{HubYolu}/negotiate?negotiateVersion=1");

        // BAŞLIK YOK — yalnız çerez.
        istek.Headers.Add("Cookie", $"enderun_token={token}");

        var yanit = await client.SendAsync(istek);

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    /// <summary>
    /// ÇEREZ YALNIZ HUB YOLUNDA OKUNUR.
    ///
    /// Çerez okumayı tüm API'ye açmak CSRF yüzeyini genişletirdi:
    /// çerez `sameSite=lax` ve bu tam koruma değil. REST uçları
    /// başlık istemeye devam etmeli.
    ///
    /// Bu test o sınırı tutuyor: aynı çerezle bir REST ucu çağrılıyor
    /// ve REDDEDİLMELİ.
    /// </summary>
    [Fact]
    public async Task Cerez_RestUcundaKabulEdilmez()
    {
        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client);

        var istek = new HttpRequestMessage(HttpMethod.Get, "/api/tasks?pageSize=1");
        istek.Headers.Add("Cookie", $"enderun_token={token}");

        var yanit = await client.SendAsync(istek);

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    /// <summary>
    /// Kullanıcı grubu adı KİMLİKTEN türer, addan değil.
    ///
    /// Ad kullanılsaydı iki aynı adlı kişi tek gruba düşerdi ve
    /// birinin mesajı diğerine giderdi.
    /// </summary>
    [Fact]
    public void KullaniciGrubu_KimliktenTurer()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal($"kullanici:{a}", MesajHub.KullaniciGrubu(a));
        Assert.NotEqual(MesajHub.KullaniciGrubu(a), MesajHub.KullaniciGrubu(b));
    }
}
