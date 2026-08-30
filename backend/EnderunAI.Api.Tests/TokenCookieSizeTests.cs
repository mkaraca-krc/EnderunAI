using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Bayrak yalnız çerez boyutunu çözmekle kalmamalı; izinleri
    /// TOKEN'DAN okuyan tüketiciler de onu tanımalı. Tanımazsa tam
    /// yetkili kullanıcı "hiç izni yok" görünür ve
    /// <c>HasPermission</c>'a dayanan her şey — elden tutar maskesi
    /// dahil — sessizce kapanır. Bu testin ilk hâli canlıya
    /// çıkmadan üç entegrasyon testini kırdı; kilitli kalsın.
    /// </summary>
    [Fact]
    public void FullPermissionFlag_GrantsPermissionsToTokenConsumers()
    {
        var token = CreateService().Create(
            CreateUser(), ["Admin"], AllPermissionKeys());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new JwtSecurityTokenHandler().ReadJwtToken(token).Claims,
            "Test"));

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };

        var currentUser = new CurrentUserService(accessor);

        Assert.True(currentUser.HasPermission(PermissionCatalog.Keys.DashboardView));
        Assert.Equal(
            PermissionCatalog.Permissions.Count,
            currentUser.Permissions.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // GERÇEK ROLLER — VEKİL DEĞİL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// PAYLI EŞİK. 4096 tarayıcının sınırı; uçurumun kenarında değil,
    /// YAKLAŞIRKEN uyarı almak istiyoruz. Aradaki 596 bayt, bir sonraki
    /// izin paketinin sınırı sessizce aşmasını engelleyen pay.
    /// </summary>
    private const int PayliEsik = 3500;

    public static TheoryData<string> KatalogRolleri()
    {
        var veri = new TheoryData<string>();

        foreach (var rol in RoleCatalog.Roles)
            veri.Add(rol.Name);

        return veri;
    }

    /// <summary>
    /// KATALOGDAKİ HER ROLÜN JETONU EŞİĞİN ALTINDA OLMALI.
    ///
    /// NEDEN BU TEST GEREKTİ — ÜSTTEKİ TESTLER VARDI VE ATEŞLEMEDİLER.
    ///
    /// Yukarıdaki testler her zaman `AllPermissionKeys()` geçiyor, yani
    /// kataloğun TAMAMINI. O küme her zaman "hepsine sahip" bayrağını
    /// tetikliyor ve jeton küçük çıkıyor; test yeşil kalıyor. Yani
    /// testler Admin'in GERÇEK anahtar kümesini değil, onun YERİNE
    /// GEÇEN bir vekili sınıyordu.
    ///
    /// Vekil, "Admin = kataloğun tamamı" doğru olduğu sürece gerçeği
    /// temsil ediyordu. ÖP/1a'da `payment.plan.approve` Admin'den
    /// çıkarıldı (ödeme onayı teknik bir rolün işi değil — İ2) ve
    /// Admin 141'den 140'a düştü. Vekil o anda gerçeği temsil etmeyi
    /// BIRAKTI, ama testler aynı vekili sınamaya devam ettiği için
    /// yeşil kaldılar.
    ///
    /// Canlıda olan: 140 izin jetona tek tek yazıldı, jeton 4096 baytı
    /// aştı, tarayıcı çerezi SESSİZCE attı, giriş döngüye girdi.
    ///
    /// Bu test artık `RoleCatalog.Roles` üzerinden koşuyor — vekil
    /// yok, rollerin kendisi. Yeni bir rol eklendiğinde ya da bir
    /// rolden izin çıkarıldığında kendiliğinden kapsıyor.
    /// </summary>
    [Theory]
    [MemberData(nameof(KatalogRolleri))]
    public void HerRolunJetonu_EsigiAsmiyor(string rolAdi)
    {
        var rol = RoleCatalog.Roles.Single(x => x.Name == rolAdi);

        var token = CreateService().Create(
            CreateUser(), [rol.Name], rol.PermissionKeys);

        var cerezBaytlari = CookieName.Length + token.Length;

        Assert.True(
            cerezBaytlari < PayliEsik,
            $"\"{rolAdi}\" rolünün jetonu {cerezBaytlari} bayt — paylı eşik "
            + $"{PayliEsik}, tarayıcı sınırı {CookieByteLimit}. "
            + $"Rolde {rol.PermissionKeys.Count} izin var.\n\n"
            + "SEBEP MUHTEMELEN İZİN ÇIKARMAK: tam yetkili bir rolden TEK "
            + "bir izin çıkarmak \"hepsine sahip\" bayrağını devre dışı "
            + "bırakır ve bütün liste jetona yazılır. Jeton maliyeti izin "
            + "sayısıyla düzgün artmaz — bayrak bir uçurum yaratır.\n\n"
            + "Bu sınırı aşan jeton tarayıcı tarafından SESSİZCE atılır: "
            + "giriş 200 döner, oturum hiç açılmaz, kullanıcı login "
            + "ekranına geri düşer.");
    }

    /// <summary>
    /// EN BÜYÜK ROLÜN PAYI RAPORLANIYOR.
    ///
    /// Yalnız "eşiğin altında" demek yetmiyor: eşiğe ne kadar
    /// yaklaşıldığı görünmezse, bir paket sınırı bir hamlede aşar ve
    /// kimse yaklaştığını fark etmez.
    /// </summary>
    [Fact]
    public void EnBuyukRolunPayi_Raporlanir()
    {
        var enBuyuk = RoleCatalog.Roles
            .Select(rol => new
            {
                rol.Name,
                Bayt = CookieName.Length + CreateService()
                    .Create(CreateUser(), [rol.Name], rol.PermissionKeys).Length
            })
            .OrderByDescending(x => x.Bayt)
            .First();

        Assert.True(
            enBuyuk.Bayt < PayliEsik,
            $"En büyük jeton \"{enBuyuk.Name}\" rolünde: {enBuyuk.Bayt} bayt. "
            + $"Paylı eşiğe kalan: {PayliEsik - enBuyuk.Bayt} bayt.");
    }

    // ═══════════════════════════════════════════════════════════════
    // REDDETME MUHAFIZI (Ş3)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// SINIRI AŞAN JETON ÜRETİLMEZ — SESSİZ KALMAZ.
    ///
    /// Tarayıcı 4096 baytı aşan çerezi sessizce atıyor: giriş 200
    /// dönüyor, Set-Cookie gidiyor, çerez yok sayılıyor, kullanıcı
    /// login ekranına geri düşüyor ve HİÇBİR KATMAN ne olduğunu
    /// söylemiyor. Canlıda bu teşhis saatler aldı (2026-08-29).
    ///
    /// Bu test o sessizliği kilitliyor: eşiği aşan jeton üretilmiyor,
    /// açık hata çıkıyor. Kullanıcı yine giremez ama NEDEN giremediği
    /// bellidir.
    ///
    /// KATALOG YETMEZ, ÇÜNKÜ TAM KATALOG BAYRAĞA DÜŞER. Eşiği aşmak
    /// için kataloğun DIŞINDA uydurma anahtarlar veriliyor: bunlar ne
    /// bayrağı tetikler ne de tümleyene sığar, liste olarak yazılırlar.
    /// </summary>
    [Fact]
    public void EsigiAsanJeton_UretilmezVeAcikHataVerir()
    {
        // Katalog dışı anahtarlar: bayrak tetiklenmez, tümleyen
        // kısalmaz, liste kaçınılmaz olarak büyür.
        var sisirilmis = Enumerable
            .Range(0, 400)
            .Select(i => $"uydurma.izin.cok.uzun.anahtar.{i:D4}")
            .ToArray();

        var hata = Assert.Throws<InvalidOperationException>(
            () => CreateService().Create(CreateUser(), ["Admin"], sisirilmis));

        Assert.Contains("çerez eşiğini aşıyor", hata.Message);
        Assert.Contains("SESSİZCE", hata.Message);
        Assert.Contains("JETON/2", hata.Message);
    }

    /// <summary>
    /// SINIRIN ALTINDAKİ JETON ÜRETİLİYOR — muhafız fazla geniş olmasın.
    ///
    /// Bu iddia olmasaydı, her jetonu reddeden bir muhafız da üstteki
    /// testi geçerdi ve kimse giriş yapamazdı.
    /// </summary>
    [Fact]
    public void SinirAltindakiJeton_Uretilir()
    {
        var token = CreateService().Create(
            CreateUser(), ["Finans Sorumlusu"], AllPermissionKeys().Take(20));

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(CookieName.Length + token.Length < PayliEsik);
    }

    /// <summary>
    /// HER ROLÜN KODLAMASI VE BAYT SAYISI — RAPOR.
    ///
    /// Yeni bir izin anahtarı eklendiğinde katalog büyür ve roller
    /// kodlama değiştirebilir. Bu test her rolün hangi kodlamayı
    /// kullandığını ve kaç bayt ettiğini ÇIKTIYA yazıyor; sayıyı
    /// varsaymak yerine okumak için.
    /// </summary>
    [Fact]
    public void HerRolun_KodlamasiVeBoyutu_Raporlanir()
    {
        var satirlar = RoleCatalog.Roles
            .Select(rol =>
            {
                var claims = JetonIzinKodlamasi.Yaz(rol.PermissionKeys);

                var kodlama =
                    claims.Any(c => c.Type == JetonIzinKodlamasi.TumleyenAlani) ? "TUMLEYEN"
                    : claims.Any(c => c.Type == JetonIzinKodlamasi.HepsiAlani) ? "BAYRAK"
                    : "LISTE";

                var bayt = CookieName.Length + CreateService()
                    .Create(CreateUser(), [rol.Name], rol.PermissionKeys).Length;

                return $"{rol.Name,-24} {rol.PermissionKeys.Count,4} izin  "
                     + $"{kodlama,-9} {bayt,5} bayt";
            })
            .ToArray();

        var rapor = $"KATALOG: {PermissionCatalog.Permissions.Count} izin"
            + Environment.NewLine
            + string.Join(Environment.NewLine, satirlar);

        /*
         * RAPOR ÇIKTIYA YAZILIYOR.
         *
         * İlk hâli yalnız BAŞARISIZLIK mesajında taşıyordu; test
         * geçince sayılar hiç görünmüyordu — yani "raporla" denen
         * şeyi yapmıyordu. Ölçüm, ancak okunabildiği yerde ölçümdür.
         */
        Console.WriteLine(rapor);

        Assert.True(satirlar.Length > 10, rapor);
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
