using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Giriş token'ı çereze yazılıyor ve tarayıcılar ad+değer toplamı
/// 4096 baytı aşan çerezi SESSİZCE atıyor — ne sunucuda hata çıkıyor
/// ne de istemcide. Canlıda tam olarak bu oldu: kataloğun tamamına
/// sahip kullanıcıda token 5391 bayta çıktı, giriş 200 döndü ama
/// oturum hiç açılmadı; kullanıcı login ekranına geri düştü.
///
/// Bu testler o sınırı kilitliyor. Kırmızıya dönerlerse yeni izinler
/// token'ı yine sınıra dayamış demektir; çözüm izin eklemeyi bırakmak
/// değil, listeyi token'dan çıkarmaktır.
/// </summary>
public sealed class TokenCookieSizeTests
{
    /// <summary>Tarayıcıların çerez başına kabul ettiği üst sınır.</summary>
    private const int CookieByteLimit = 4096;

    private const string CookieName = "enderun_token=";

    private static TokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = new string('k', 64),
            })
            .Build();

        return new TokenService(configuration);
    }

    private static AppUser CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        Username = "yetkili.kullanici",
        FullName = "Yetkili Kullanıcı Adı Uzunca Olsun",
        Email = "yetkili.kullanici@enderunenerji.com",
    };

    private static string[] AllPermissionKeys() =>
        PermissionCatalog.Permissions.Select(item => item.Key).ToArray();

    [Fact]
    public void FullPermissionToken_FitsInBrowserCookieLimit()
    {
        var token = CreateService().Create(
            CreateUser(), ["Admin"], AllPermissionKeys());

        var cookieBytes = CookieName.Length + token.Length;

        Assert.True(
            cookieBytes < CookieByteLimit,
            $"Tam yetkili token çerez sınırını aşıyor: {cookieBytes} bayt " +
            $"(sınır {CookieByteLimit}). Bu haliyle tarayıcı çerezi atar ve " +
            "kullanıcı giriş yapamaz.");
    }

    [Fact]
    public void FullPermissionToken_CarriesFlagInsteadOfEveryKey()
    {
        var token = CreateService().Create(
            CreateUser(), ["Admin"], AllPermissionKeys());

        var payload = DecodePayload(token);

        Assert.Contains("all_permissions", payload);

        // Liste yazılmamalı: yazılırsa boyut kazancı yok demektir.
        Assert.DoesNotContain("\"permissions\"", payload);
    }

    [Fact]
    public void PartialPermissionToken_StillCarriesTheList()
    {
        // Kısmi yetkideki kullanıcı izinlerini token'dan okuyor;
        // bayrak optimizasyonu onları kapsamamalı, yoksa yetkisi
        // olan sayfalarda "yetkisiz" ekranına düşerler.
        var subset = AllPermissionKeys().Take(10).ToArray();

        var token = CreateService().Create(
            CreateUser(), ["Finans Sorumlusu"], subset);

        var payload = DecodePayload(token);

        Assert.Contains("\"permissions\"", payload);
        Assert.DoesNotContain("all_permissions", payload);
        Assert.Contains(subset[0], payload);
    }

    [Fact]
    public void WidestCustomRoleToken_StaysWellUnderTheLimit()
    {
        // Canlıdaki en geniş özel rol 44 izinde. Sınıra yaklaşan
        // ikinci bir yol açılırsa bu test uyarır.
        var subset = AllPermissionKeys().Take(44).ToArray();

        var token = CreateService().Create(
            CreateUser(), ["Teknik Koordinatör"], subset);

        var cookieBytes = CookieName.Length + token.Length;

        Assert.True(
            cookieBytes < CookieByteLimit,
            $"Kısmi yetkili token çerez sınırına dayandı: {cookieBytes} bayt.");
    }

    private static string DecodePayload(string token)
    {
        var payload = token.Split('.')[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');

        return System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(payload));
    }
}
