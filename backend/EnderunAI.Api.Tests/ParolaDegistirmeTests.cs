using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KENDİ PAROLASINI DEĞİŞTİRME — SİSTEMDE İLK KEZ.
///
/// Ölçüldü (2026-09-03): parola değiştirmenin TEK yolu yönetici
/// sıfırlamasıydı. Kullanıcı kendi parolasını değiştiremiyordu.
///
/// Bu dosya üç ayrı iddiayı sınıyor ve üçü de ayrı sondalarla
/// kanıtlandı: eski parola kontrolü, uzunluk politikası ve
/// DEĞİŞİKLİKTEN SONRA ESKİ JETONUN REDDİ.
/// </summary>
[Collection("Integration")]
public sealed class ParolaDegistirmeTests(DatabaseFixture fixture)
{
    private const string Uzun = "CokUzunGecerliParola2026";   // 24 karakter
    private const string Uzun2 = "IkinciCokUzunParola2026x";  // 24 karakter

    /// <summary>
    /// Testin kendi kullanıcısını kurar — admin hesabına dokunmak,
    /// sonraki testlerin girişini bozardı.
    /// </summary>
    private static async Task<(Guid Id, string Ad)> KullaniciAsync(
        DatabaseFixture fixture, string ek, string parola)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var ad = $"pd-{ek}".ToLowerInvariant();
        var p = hash.Hash(parola);

        var user = new AppUser
        {
            Username = ad,
            FullName = $"Parola Testi {ek}",
            PasswordHash = p.Hash,
            PasswordSalt = p.Salt,
            IsActive = true,

            /*
             * ÇALIŞMA SAATİ MUAFİYETİ — TESTİN KONUSU DEĞİL.
             *
             * Giriş ucu rol bazlı çalışma saati penceresi uyguluyor ve
             * pencere dışında 403 dönüyor. Bu testler PAROLA
             * davranışını ölçüyor; testin sonucu koşulduğu SAATE bağlı
             * olsaydı, gece koşan bir tam tur sebepsiz kırmızı verirdi.
             *
             * Muafiyet sistemin kendi alanı, testin uydurduğu bir
             * kapı değil.
             */
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (user.Id, ad);
    }

    private static async Task<HttpClient> GirisAsync(
        DatabaseFixture fixture, string ad, string parola)
    {
        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, ad, parola);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task EskiParolaYanlissa_Reddedilir()
    {
        var (_, ad) = await KullaniciAsync(fixture, "A1", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        var yanit = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "YanlisParolaAmaUzun2026",
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task EskiParolaYanlissa_POLITIKAYI_OGRETMEZ()
    {
        /*
         * BİLGİ SIZINTISI TESTİ.
         *
         * Uzunluk kontrolü eski parola kontrolünden ÖNCE gelseydi,
         * eski parolayı bilmeyen biri de "en az 12 karakter" bilgisini
         * alırdı. Uç, kendisine yetkisi olmayan birine bilgi veren bir
         * yüzeye dönüşürdü.
         *
         * Hem eski parola YANLIŞ hem yeni parola KISA gönderiliyor:
         * dönen mesaj politikadan söz etmemeli.
         */
        var (_, ad) = await KullaniciAsync(fixture, "A2", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        var yanit = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "YanlisParolaAmaUzun2026",
            newPassword = "kisa",
            newPasswordConfirm = "kisa"
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.DoesNotContain("12", govde);
        Assert.DoesNotContain("karakter", govde);
    }

    [Fact]
    public async Task KisaParola_Reddedilir()
    {
        var (_, ad) = await KullaniciAsync(fixture, "A3", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        // 11 karakter — eşiğin bir altı. Sınır DEĞERİ sınanıyor:
        // "kisa" gibi bir değer, eşik 5'e düşse de reddedilirdi.
        var yanit = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = "OnBirKarak",
            newPasswordConfirm = "OnBirKarak"
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            ParolaPolitikasi.AsgariUzunluk.ToString(),
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TekrarEslesmiyorsa_Reddedilir()
    {
        var (_, ad) = await KullaniciAsync(fixture, "A4", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        var yanit = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2 + "x"
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Degisiklikten_Sonra_EskiParolayla_Giris_Yapilamaz()
    {
        var (_, ad) = await KullaniciAsync(fixture, "A5", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        var degistir = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        Assert.Equal(HttpStatusCode.OK, degistir.StatusCode);

        var temiz = fixture.Factory.CreateClient();

        var eski = await temiz.PostAsJsonAsync("/api/auth/login", new
        {
            username = ad,
            password = Uzun
        });

        Assert.NotEqual(HttpStatusCode.OK, eski.StatusCode);

        var yeni = await temiz.PostAsJsonAsync("/api/auth/login", new
        {
            username = ad,
            password = Uzun2
        });

        Assert.Equal(HttpStatusCode.OK, yeni.StatusCode);
    }

    [Fact]
    public async Task Degisiklikten_Sonra_ESKI_JETON_Reddedilir()
    {
        /*
         * ASIL İDDİA: "dar olan kazanır".
         *
         * Parola değiştirmenin sebebi genelde "bu parolayı başkası
         * biliyor"dur. Eski oturum yaşamaya devam ederse değişiklik
         * amacına ulaşmaz: parolayı bilen kişinin AÇIK OTURUMU 12 saat
         * daha çalışır.
         *
         * İKİNCİ İSTEMCİ, İKİNCİ OTURUM: aynı kullanıcının başka bir
         * cihazdaki oturumunu temsil ediyor.
         */
        var (_, ad) = await KullaniciAsync(fixture, "A6", Uzun);

        var digerCihaz = await GirisAsync(fixture, ad, Uzun);
        var kendi = await GirisAsync(fixture, ad, Uzun);

        // Diğer cihaz şu an çalışıyor — POZİTİF KONTROL.
        var once = await digerCihaz.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, once.StatusCode);

        var degistir = await kendi.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        Assert.Equal(HttpStatusCode.OK, degistir.StatusCode);

        // Diğer cihazın jetonu artık geçersiz.
        var sonra = await digerCihaz.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
    }

    [Fact]
    public async Task Cevapta_Donen_YENI_JETON_AYNI_SANIYEDE_De_Calisir()
    {
        /*
         * SINIR DURUMU — MEHMET'İN İSTEDİĞİ TEST.
         *
         * `iat` SANİYE çözünürlüğünde. Damga 12:00:00.700'de yazılıp
         * jeton aynı saniyede üretilirse, ham karşılaştırmada jeton
         * "değişimden önce" görünür ve kullanıcının KENDİ yeni jetonu
         * reddedilirdi.
         *
         * Bu hata canlıda ARA SIRA görünür (saniye sınırına denk
         * geldiğinde), testte hiç — sınır durumu ayrıca sınanmazsa.
         *
         * Damga saniyeye yuvarlanıyor; aynı saniye geçerli, önceki
         * saniye değil.
         */
        var (_, ad) = await KullaniciAsync(fixture, "A7", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        var degistir = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        Assert.Equal(HttpStatusCode.OK, degistir.StatusCode);

        var govde = JsonDocument.Parse(await degistir.Content.ReadAsStringAsync());
        var yeniJeton = govde.RootElement.GetProperty("token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(yeniJeton));

        var yeniIstemci = fixture.Factory.CreateClient();
        yeniIstemci.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", yeniJeton);

        var me = await yeniIstemci.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    /// <summary>
    /// `iat` iddiası OLMAYAN, ama imzası ve süresi geçerli bir jeton
    /// üretir — bu sürümden ÖNCE üretilmiş jetonların birebir aynısı.
    /// </summary>
    private static string IatsizJeton(Guid kullaniciId, string kullaniciAdi)
    {
        var anahtar = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(
                TestWebApplicationFactory.TestJwtSecret));

        var jeton = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "EnderunAI",
            audience: "EnderunAI.Web",
            claims:
            [
                new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    kullaniciId.ToString()),
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier,
                    kullaniciId.ToString()),
                new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName,
                    kullaniciAdi),
                // `iat` BİLEREK YOK — sınanan şey tam olarak bu.
            ],
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                anahtar,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .WriteToken(jeton);
    }

    [Fact]
    public async Task IatSiz_Jeton_REDDEDILIR()
    {
        /*
         * ═══ ÜRETİMDE EN ÇOK KOŞACAK YOL ═══
         *
         * Bu sürüm yayınlandığı anda canlıdaki TÜM jetonlarda `iat`
         * yok — hiçbiri onu taşımıyordu. Yani fail-closed dalı,
         * deploy anında dört kullanıcının hepsinde çalışacak.
         *
         * Testsiz bırakılamaz: en çok koşan yol, en az sınanan yol
         * olamaz.
         *
         * JETON GEÇERLİ İMZALI VE SÜRESİ DOLMAMIŞ — reddin sebebi
         * imza ya da süre değil, YALNIZCA `iat` yokluğu.
         */
        var (id, ad) = await KullaniciAsync(fixture, "B1", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        // Parola değişimi damgayı yazar; damga yoksa kısıt da yoktur.
        var degistir = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        Assert.Equal(HttpStatusCode.OK, degistir.StatusCode);

        var eskiSurumJetonu = fixture.Factory.CreateClient();
        eskiSurumJetonu.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IatsizJeton(id, ad));

        var yanit = await eskiSurumJetonu.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task IatSiz_Jeton_Damga_YOKKEN_Kabul_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL — bu olmadan yukarıdaki test boştur.
         *
         * `iat`siz jeton, parolası HİÇ DEĞİŞMEMİŞ bir kullanıcı için
         * geçerli olmalı; aksi hâlde yukarıdaki kırmızı "iat yokluğu"
         * yüzünden değil, jetonun başka bir kusuru yüzünden gelirdi
         * (yanlış imza, eksik iddia, yanlış audience…).
         *
         * Yani bu test, sabotajın DOĞRU YERE vurduğunu kanıtlıyor.
         */
        var (id, ad) = await KullaniciAsync(fixture, "B2", Uzun);

        var istemci = fixture.Factory.CreateClient();
        istemci.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IatsizJeton(id, ad));

        var yanit = await istemci.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task AYNI_SANIYEDEKI_ESKI_Jeton_Da_Reddedilir()
    {
        /*
         * ═══ SONDANIN YAKALADIĞI KUSURUN BEKÇİSİ ═══
         *
         * İlk tasarımda damga AŞAĞI yuvarlanıyordu. Sonuç: parola
         * değişimiyle AYNI SANİYEDE üretilmiş eski bir jeton hayatta
         * kalıyordu. Kendi yeni jetonunu kurtarmak için açılan pay,
         * saldırganın jetonunu da kurtarıyordu.
         *
         * Sonda turunda `Degisiklikten_Sonra_ESKI_JETON_Reddedilir`
         * üç sabotajda düşüp birinde düşmeyince anlaşıldı: kusur
         * sabotajda değil, ZAMANLAMADAYDI. Testte her şey aynı
         * saniyeye düşüyordu; canlıda ARA SIRA düşerdi.
         *
         * Bu test o sınırı doğrudan çiviliyor: `iat` tam olarak
         * değişim saniyesi olan bir jeton REDDEDİLMELİ.
         *
         * Zamanlamaya bağlı olmayan bir sınama: değerler doğrudan
         * veriliyor, koşunun hızına bağlı değil.
         */
        var (id, ad) = await KullaniciAsync(fixture, "B4", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var damga = await db.Users.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.PasswordChangedAtUtc)
            .SingleAsync();

        Assert.NotNull(damga);

        IOturumGecerliligi taze = new OturumGecerliligi();

        // `iat` = değişim saniyesinin BAŞI — eski jetonun taşıyacağı
        // değerin ta kendisi.
        var ayniSaniye = new DateTime(
            damga!.Value.Ticks - (damga.Value.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc);

        Assert.False(await taze.GecerliAsync(id, ayniSaniye, db));

        // POZİTİF KONTROL: ucun ürettiği sınır saniyesi geçerli olmalı
        // — yoksa kullanıcı kendi yeni jetonuyla içeri giremezdi.
        Assert.True(await taze.GecerliAsync(
            id, IOturumGecerliligi.JetonSaniyesi(damga.Value), db));
    }

    [Fact]
    public async Task Onbellek_BOSKEN_De_Eski_Jeton_Reddedilir()
    {
        /*
         * ═══ MEHMET'İN SABOTAJI: "SERVİSİ YENİDEN BAŞLAT" ═══
         *
         * Gerçek yeniden başlatma bir testte yapılamaz; sınanan
         * MEKANİZMA ise yapılabilir: önbellek BOŞKEN koruma devam
         * ediyor mu?
         *
         * İlk tasarımda bellek KAYNAKTI ve boş önbellek "kısıt yok"
         * demekti — yani bu test kırmızı verirdi. Şimdi bellek bir
         * ÖNBELLEK: kayıt yoksa veritabanından okunuyor.
         *
         * Taze bir `OturumGecerliligi` örneği, yeni başlamış bir
         * sürecin belleğini temsil ediyor.
         */
        var (id, ad) = await KullaniciAsync(fixture, "B3", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // YENİ SÜREÇ: önbelleği tamamen boş bir örnek.
        IOturumGecerliligi tazeSurec = new OturumGecerliligi();

        var damga = await db.Users.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.PasswordChangedAtUtc)
            .SingleAsync();

        Assert.NotNull(damga);

        // Değişimden bir dakika ÖNCE üretilmiş bir jeton.
        var eski = damga!.Value.AddMinutes(-1);
        Assert.False(await tazeSurec.GecerliAsync(id, eski, db));

        // Değişimden sonra üretilmiş bir jeton — POZİTİF KONTROL.
        // Sınır bir SONRAKİ saniye olduğu için jeton da onunla
        // üretiliyor (ucun yaptığının aynısı).
        var yeni = IOturumGecerliligi.JetonSaniyesi(damga.Value);
        Assert.True(await tazeSurec.GecerliAsync(id, yeni, db));
    }

    [Fact]
    public async Task YoneticiSifirlamasi_Da_ESKI_JETONU_Reddeder()
    {
        /*
         * ═══ SIFIRLAMANIN ASIL SENARYOSU ═══
         *
         * Bir yöneticinin parolayı sıfırladığı durum, tam olarak
         * "parola başkasının elinde" durumudur. Oturum düşmezse
         * sıfırlama işini YAPMAMIŞ olur: parolayı bilen kişinin açık
         * oturumu 12 saat daha çalışır.
         *
         * ÖLÇÜLDÜ (2026-09-04): sıfırlama yolu karmayı yazıyor,
         * damgayı YAZMIYORDU. Bu test o boşluğun bekçisi.
         */
        var (id, ad) = await KullaniciAsync(fixture, "B5", Uzun);

        // Kullanıcının açık oturumu.
        var kullanici = await GirisAsync(fixture, ad, Uzun);

        var once = await kullanici.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, once.StatusCode);   // POZİTİF KONTROL

        var admin = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var sifirla = await admin.PostAsJsonAsync(
            $"/api/user-management/users/{id}/reset-password",
            new { newPassword = Uzun2 });

        Assert.Equal(HttpStatusCode.OK, sifirla.StatusCode);

        // Kullanıcının eski jetonu artık geçersiz.
        var sonra = await kullanici.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
    }

    [Fact]
    public async Task DenetimKaydi_AyirtEdilebilir_Ve_Parola_YAZILMAZ()
    {
        var (id, ad) = await KullaniciAsync(fixture, "A8", Uzun);
        var client = await GirisAsync(fixture, ad, Uzun);

        await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = Uzun,
            newPassword = Uzun2,
            newPasswordConfirm = Uzun2
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var olaylar = await db.Set<SecurityAuditEvent>()
            .AsNoTracking()
            .Where(x => x.ActorUserId == id)
            .ToListAsync();

        // AYIRT EDİLEBİLİR: "Updated" değil, kendi eylem adı.
        Assert.Contains(olaylar, x => x.Action == "PasswordChanged");

        /*
         * DENETİM KAYDI, KORUDUĞU SIRRI ELE VEREMEZ.
         *
         * Bu ders portal jetonunda ödendi: 256 bitlik anahtarın tamamı
         * denetim kaydına düz metin yazılıyordu. Burada ne parola ne
         * karması yazılmalı.
         */
        foreach (var olay in olaylar)
        {
            var metin = (olay.DetailsJson ?? string.Empty) +
                        (olay.EntityType ?? string.Empty);

            Assert.DoesNotContain(Uzun, metin);
            Assert.DoesNotContain(Uzun2, metin);
        }

        var kullanici = await db.Users.AsNoTracking()
            .SingleAsync(x => x.Id == id);

        Assert.DoesNotContain(
            kullanici.PasswordHash,
            string.Join("", olaylar.Select(x => x.DetailsJson ?? "")));
    }

    [Fact]
    public async Task YoneticiSifirlamasi_Da_12_Karakter_Zorunlu_Tutuyor()
    {
        /*
         * KURAL TEK YERDE OLMALI. Bu test, `ParolaPolitikasi`'nın
         * gerçekten HER İKİ yoldan da çağrıldığını sabitliyor —
         * kuralın ikinci bir kopyası doğarsa burada görünür.
         *
         * (Ölçüldü: politika tek kaynağa çekilirken
         * `UserManagementController` içinde İKİ kopya bulundu.)
         */
        var (id, _) = await KullaniciAsync(fixture, "A9", Uzun);
        var admin = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await admin.PostAsJsonAsync(
            $"/api/user-management/users/{id}/reset-password",
            new { newPassword = "OnBirKarak" });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            ParolaPolitikasi.AsgariUzunluk.ToString(),
            await yanit.Content.ReadAsStringAsync());
    }
}
