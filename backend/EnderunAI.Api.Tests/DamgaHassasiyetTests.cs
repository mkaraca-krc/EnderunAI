using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DAMGA/1 — PAROLA DAMGASI SANİYEYLE DEĞİL MİLİSANİYEYLE KARŞILAŞTIRILIR.
///
/// ═══ ÖLÇÜLEN İKİ KUSUR (2026-09-11) ═══
///
/// Damga saniyeye yukarı yuvarlanıyor, jeton `iat`'ı saniye taşıyordu:
///   1. Aynı saniyede basılan GİRİŞ jetonu reddediliyordu: hesap açılıp ya
///      da parola sıfırlanıp hemen giriş yapılınca giriş 200, ilk istek 401
///      (20 denemede 10–11).
///   2. Parola değişim ucunun "gelecekten" (sonraki saniyeyle) bastığı
///      jeton, AYNI saniyedeki ikinci değişikliği aşıyordu — ve zamanla
///      düşmüyordu (+5 dk'da 7/7).
/// Giriş koşulu insan eliyle ulaşılamaz, jeton değişimi yapanın elinde
/// (Kural 83); yine de kusur gerçek ve kalıcı.
///
/// ═══ ÇÖZÜM (Mehmet Bey onayı) ═══
///
/// Jetona imzalı `uretim_ms` iddiası; damgayla milisaniye tabanında TAM
/// karşılaştırma. Kendi parola değişimi jetonu `damga + 1 ms` ile basar.
/// GEÇİŞ: `uretim_ms` taşımayan ESKİ jeton bugünkü saniye kuralıyla
/// değerlendirilir — yayın anında oturumda olan kimse atılmaz.
/// </summary>
[Collection("Integration")]
public sealed class DamgaHassasiyetTests(DatabaseFixture fixture)
{
    private static string Ip() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    /// <summary>Bir sonraki saniyenin başına kadar bekler (ilk 50 ms).</summary>
    private static async Task SaniyeBasinaHizala()
    {
        var kalan = 1000 - DateTime.UtcNow.Millisecond;
        await Task.Delay(kalan + 5);
    }

    private async Task<string> Giris(string ad, string parola)
    {
        var c = fixture.Factory.CreateClient();
        using var m = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        { Content = JsonContent.Create(new { username = ad, password = parola }), Headers = { { "X-Forwarded-For", Ip() } } };
        var y = await c.SendAsync(m);
        Assert.True(y.IsSuccessStatusCode, $"giriş {(int)y.StatusCode}");
        return (await y.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<string> Degistir(string jeton, string eski, string yeni)
    {
        var c = fixture.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        var y = await c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = eski, newPassword = yeni, newPasswordConfirm = yeni });
        Assert.True(y.IsSuccessStatusCode, $"değişim {(int)y.StatusCode}");
        return (await y.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<HttpStatusCode> Me(string jeton)
    {
        var c = fixture.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return (await c.GetAsync("/api/auth/me")).StatusCode;
    }

    /// <summary>
    /// `uretim_ms` TAŞIMAYAN eski biçimli jeton — yayın anında oturumda olan
    /// herkesin elindeki jetonun şekli.
    /// </summary>
    private static string EskiBicimliJeton(Guid kullaniciId, string ad, DateTime iat)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestWebApplicationFactory.TestJwtSecret));
        var jeton = new JwtSecurityToken(
            issuer: "EnderunAI", audience: "EnderunAI.Web",
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, kullaniciId.ToString()),
                new(ClaimTypes.NameIdentifier, kullaniciId.ToString()),
                new(JwtRegisteredClaimNames.UniqueName, ad),
                new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(iat).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ],
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jeton);
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: parola değişim ucunun döndürdüğü jeton, aynı saniyedeki
    /// ikinci değişikliği aşar ve jeton ömrü boyunca geçerli kalır.
    /// </summary>
    [Fact]
    public async Task H1_AyniSaniyedeIkiDegisim_D1Reddedilir_D2Gecer()
    {
        var u = await TestUserFactory.KullaniciKurAsync(fixture, "damga-h1", ["Admin"]);
        await Task.Delay(1_100);
        var jeton = await Giris(u.KullaniciAdi, TestUserFactory.Parola);
        var parola = TestUserFactory.Parola;
        int d1Gecen = 0, d2Kalan = 0;
        for (var i = 0; i < 5; i++)
        {
            await SaniyeBasinaHizala();
            var p1 = $"DamgaH1a!{Guid.NewGuid():N}"[..22];
            var p2 = $"DamgaH1b!{Guid.NewGuid():N}"[..22];
            var d1 = await Degistir(jeton, parola, p1);
            var d2 = await Degistir(d1, p1, p2);
            if (await Me(d1) == HttpStatusCode.OK) d1Gecen++;
            if (await Me(d2) != HttpStatusCode.OK) d2Kalan++;
            jeton = d2; parola = p2;
        }
        Assert.True(d2Kalan == 0, $"POZİTİF KONTROL: meşru yeni jeton (D2) {d2Kalan}/5 kez reddedildi.");
        Assert.True(d1Gecen == 0, $"Eski D1 jetonu ikinci değişiklikten sonra {d1Gecen}/5 kez GEÇTİ.");
    }

    /// <summary>
    /// GERİLEME KORUMASI. KIRMIZIYA DÖNERSE: parola değişimi, parolayı bilen
    /// birinin girişle açtığı oturumu düşürmez — parola değiştirmenin asıl vaadi.
    /// </summary>
    [Fact]
    public async Task H2_GiristeBasilanEskiJeton_DegisimdenSonraReddedilir()
    {
        var u = await TestUserFactory.KullaniciKurAsync(fixture, "damga-h2", ["Admin"]);
        await Task.Delay(1_100);
        var g = await Giris(u.KullaniciAdi, TestUserFactory.Parola);
        Assert.Equal(HttpStatusCode.OK, await Me(g)); // zemin
        await SaniyeBasinaHizala();
        var yeni = await Degistir(g, TestUserFactory.Parola, $"DamgaH2!{Guid.NewGuid():N}"[..22]);
        Assert.Equal(HttpStatusCode.Unauthorized, await Me(g));
        Assert.Equal(HttpStatusCode.OK, await Me(yeni));
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: hesap açılıp / parola sıfırlanıp hemen giriş yapan
    /// kullanıcı "girdim, geri atıldım" yaşar — giriş 200, ilk istek 401.
    /// </summary>
    [Fact]
    public async Task H3_OlusturVeSifirla_HemenGiris_IlkIstekGecer()
    {
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        int olusturRed = 0, sifirlaRed = 0;
        for (var i = 0; i < 5; i++)
        {
            await SaniyeBasinaHizala();
            var ad = $"damga-h3-{Guid.NewGuid():N}"[..28];
            const string p = "DamgaIlkGiris!2026x";
            var o = await yonetici.PostAsJsonAsync("/api/user-management/users",
                new { username = ad, fullName = "Damga H3", roleNames = new[] { "Admin" }, password = p, isActive = true, workHoursExempt = true });
            Assert.True(o.IsSuccessStatusCode, $"oluşturma {(int)o.StatusCode}");
            if (await Me(await Giris(ad, p)) != HttpStatusCode.OK) olusturRed++;
        }
        var k = await TestUserFactory.KullaniciKurAsync(fixture, "damga-h3s", ["Admin"]);
        for (var i = 0; i < 5; i++)
        {
            await SaniyeBasinaHizala();
            var p = $"DamgaSifir!{Guid.NewGuid():N}"[..22];
            var s = await yonetici.PostAsJsonAsync($"/api/user-management/users/{k.Id}/reset-password", new { newPassword = p });
            Assert.True(s.IsSuccessStatusCode, $"sıfırlama {(int)s.StatusCode}");
            if (await Me(await Giris(k.KullaniciAdi, p)) != HttpStatusCode.OK) sifirlaRed++;
        }
        Assert.True(olusturRed == 0 && sifirlaRed == 0,
            $"Hemen girişte ilk istek reddedildi: oluşturma {olusturRed}/5, sıfırlama {sifirlaRed}/5.");
    }

    /// <summary>
    /// GEÇİŞ. KIRMIZIYA DÖNERSE: yayın anında oturumda olan herkes (elinde
    /// `uretim_ms`'siz eski jeton) atılır — ya da tersine, eski jeton parola
    /// değişikliğinden sonra geçmeye başlar.
    /// </summary>
    [Fact]
    public async Task H4_GecisEskiBicimliJeton_SaniyeKuraliylaDegerlendirilir()
    {
        var u = await TestUserFactory.KullaniciKurAsync(fixture, "damga-h4", ["Admin"]);
        await Task.Delay(1_100);
        var eski = EskiBicimliJeton(u.Id, u.KullaniciAdi, DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, await Me(eski)); // oturumda olan atılmıyor

        await Task.Delay(1_100);
        var g = await Giris(u.KullaniciAdi, TestUserFactory.Parola);
        await Task.Delay(1_100);
        await Degistir(g, TestUserFactory.Parola, $"DamgaH4!{Guid.NewGuid():N}"[..22]);
        Assert.Equal(HttpStatusCode.Unauthorized, await Me(eski)); // değişimden sonra düşüyor
    }

    /// <summary>KIRMIZIYA DÖNERSE: yeni jetonlar milisaniye iddiası taşımaz ve H1/H3 anlamsızlaşır.</summary>
    [Fact]
    public async Task H5_YeniJeton_UretimMilisaniyesiTasir()
    {
        var u = await TestUserFactory.KullaniciKurAsync(fixture, "damga-h5", ["Admin"]);
        await Task.Delay(1_100);
        var jeton = new JwtSecurityTokenHandler().ReadJwtToken(await Giris(u.KullaniciAdi, TestUserFactory.Parola));
        var ms = jeton.Claims.FirstOrDefault(c => c.Type == "uretim_ms")?.Value;
        Assert.False(string.IsNullOrEmpty(ms), "Jeton uretim_ms taşımıyor.");
        var fark = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - long.Parse(ms!));
        Assert.True(fark < 60_000, $"uretim_ms şimdiye {fark} ms uzak.");
    }
}
