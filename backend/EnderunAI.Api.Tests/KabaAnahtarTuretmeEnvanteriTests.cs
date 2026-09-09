using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YOLDAN TÜRETMEYE DÜŞEN UÇLARIN ENVANTERİ (GÖRÜNÜRLÜK/1 · Adım 3, A.1).
///
/// ═══ ÖNCEKİ ÖLÇÜM YANLIŞ AYAĞI ÖLÇTÜ ═══
///
/// İlk turda `PUT /api/accounting-accounts/{id}` ölçüldü ve "sızıntı
/// yok" denildi. O uç `[RequirePermission(AccountingEdit)]` TAŞIYOR;
/// middleware nitelik bulunca yoldan türetmeyi HİÇ ÇALIŞTIRMIYOR.
/// Yani türetmenin devreye girmediği ayak ölçülüp sonuç genellendi —
/// ölçtüğümü sandığım şeyle ölçtüğüm şey ayrıştı (Kural 65).
///
/// TEHLİKELİ YAPILANDIRMA: NİTELİĞİ OLMAYAN, yolundan kaba bir
/// anahtara (`*.manage`) türetilen uç. Orada gereken izin KABA
/// anahtardır; kullanıcının İNCE izindeki Deny kaydı o soruya cevap
/// değildir.
///
/// ═══ ENVANTER NASIL ÇIKARILIYOR: ÇAĞIRARAK ═══
///
/// Middleware 403 verirken yanıt gövdesine `requiredPermission` alanını
/// KENDİSİ yazıyor. Yani hangi ucun neye türetildiği tahmin edilmiyor,
/// UÇTAN OKUNUYOR (Kural 70).
///
/// Bunun için İZNİ SIFIR bir kullanıcı gerekiyor: katalogda olmayan,
/// hiçbir izni bulunmayan bir rol açılıyor. Var olan bir rol
/// kullanılsaydı, o rolün taşıdığı anahtarlarda 403 hiç gelmez ve uç
/// envanterde SESSİZCE eksik kalırdı.
/// </summary>
[Collection("Integration")]
public sealed class KabaAnahtarTuretmeEnvanteriTests(
    DatabaseFixture fixture,
    ITestOutputHelper cikti)
{
    private static readonly string[] KabaAnahtarlar =
    [
        PermissionCatalog.Keys.ProjectsManage,
        PermissionCatalog.Keys.PurchasingManage,
        PermissionCatalog.Keys.AttendanceManage,
        PermissionCatalog.Keys.PayrollManage,
        PermissionCatalog.Keys.HakedisManage,
        PermissionCatalog.Keys.FinanceManage,
        PermissionCatalog.Keys.AccountingManage
    ];

    /// <summary>İzni SIFIR kullanıcı — katalog dışı boş rol.</summary>
    private async Task<HttpClient> IzinsizIstemciAsync()
    {
        const string parola = "TestBos!2026Secure";
        const string rolAdi = "sonda-bos-rol";

        string kullaniciAdi;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ps = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var rol = await db.Roles.SingleOrDefaultAsync(x => x.Name == rolAdi);
            if (rol is null)
            {
                rol = new AppRole { Name = rolAdi, Description = "Sonda: izni sıfır" };
                db.Roles.Add(rol);
                await db.SaveChangesAsync();
            }

            kullaniciAdi = $"sonda-bos-{Guid.NewGuid():N}"[..30];
            var ozet = ps.Hash(parola);
            var k = new AppUser
            {
                Username = kullaniciAdi,
                FullName = "Sonda Bos",
                PasswordHash = ozet.Hash,
                PasswordSalt = ozet.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };
            db.Users.Add(k);
            db.UserRoles.Add(new UserRole { UserId = k.Id, RoleId = rol.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = k.Id,
                ScopeType = DataScopeType.All
            });
            await db.SaveChangesAsync();
        }

        var istemci = fixture.Factory.CreateClient();
        var jeton = await AuthHelper.LoginAsync(istemci, kullaniciAdi, parola);
        istemci.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jeton);
        return istemci;
    }

    [Fact]
    public async Task Envanter_YoldanTuretmeyeDusenUclar()
    {
        var kaynak = fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        /*
         * KAPSAM ÖLÇÜMÜ — ENVANTERE NE GİRDİ, NE GİRMEDİ.
         *
         * Middleware YOL üzerinde çalışıyor; denetleyici eylemine
         * karşılık gelmeyen yollar da ondan geçer. Envanterin neyi
         * kapsadığını VARSAYMAK yerine basıyoruz.
         */
        var tumUclar = kaynak.Endpoints.ToList();
        var rotaliOlanlar = tumUclar.OfType<RouteEndpoint>().ToList();
        cikti.WriteLine($"KAPSAM: toplam uç {tumUclar.Count} · RouteEndpoint {rotaliOlanlar.Count}");
        cikti.WriteLine("KAPSAM: api/ ile başlayan RouteEndpoint " +
            rotaliOlanlar.Count(x => (x.RoutePattern.RawText ?? "")
                .StartsWith("api/", StringComparison.OrdinalIgnoreCase)));
        cikti.WriteLine("KAPSAM: api/hubs içeren -> " + string.Join(", ",
            rotaliOlanlar.Select(x => x.RoutePattern.RawText ?? "")
                .Where(x => x.Contains("hub", StringComparison.OrdinalIgnoreCase))));
        cikti.WriteLine("KAPSAM: RouteEndpoint OLMAYAN uçlar -> " + string.Join(", ",
            tumUclar.Where(x => x is not RouteEndpoint)
                .Select(x => x.DisplayName ?? "(adsız)").Take(8)));

        // Niteliği ve [AllowAnonymous]'ı OLMAYAN api/ uçları:
        // middleware bunlarda yoldan türetmeye düşüyor.
        var adaylar = kaynak.Endpoints
            .OfType<RouteEndpoint>()
            /*
             * BAŞTAKİ EĞİK ÇİZGİ KIRPILIYOR — İLK SÜZGEÇ HUB'LARI KAÇIRDI.
             *
             * Denetleyici rotaları `api/...` diye gelirken hub rotaları
             * `/api/hubs/...` diye geliyor. `StartsWith("api/")` üç hub
             * ucunu sessizce envanter dışında bıraktı; middleware ise
             * onlardan da geçiyor.
             */
            .Where(uc => ((uc.RoutePattern.RawText ?? "").TrimStart('/'))
                .StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Where(uc => uc.Metadata.GetOrderedMetadata<RequirePermissionAttribute>().Count == 0)
            /*
             * `[AllowAnonymous]` DIŞLANMIYOR — İLK ÖLÇÜMÜN BOŞLUĞU BUYDU.
             *
             * `PermissionAuthorizationMiddleware` AllowAnonymous'a HİÇ
             * BAKMIYOR: kimliği olan bir istek o uca girse de yoldan
             * türetme çalışır. Bu uçları dışlamak, türetmeye düşen
             * yüzeyin bir kısmını ölçmeden bırakmaktı.
             *
             * `UcKapisiDenetimi` onları haklı olarak dışlıyor — onun
             * sorusu "beyan var mı", buranınki "türetme çalışıyor mu".
             * Aynı süzgeci iki farklı soru için kullanmak, Kural 65'in
             * bir başka hâli.
             */
            .ToList();

        var istemci = await IzinsizIstemciAsync();

        var turetilen = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var turetmeyen = new List<string>();

        foreach (var uc in adaylar)
        {
            var sablon = uc.RoutePattern.RawText!;
            var yol = "/" + System.Text.RegularExpressions.Regex.Replace(
                sablon, @"\{[^}]+\}", Guid.NewGuid().ToString());

            var yontemler = uc.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                ?? ["GET"];
            var yontem = yontemler.Contains("GET") ? "GET" : yontemler.First();

            using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol);
            if (yontem is not ("GET" or "HEAD" or "DELETE"))
                istek.Content = JsonContent.Bos();

            using var yanit = await istemci.SendAsync(istek);

            if (yanit.StatusCode != HttpStatusCode.Forbidden)
            {
                turetmeyen.Add($"{yontem} {sablon} -> HTTP {(int)yanit.StatusCode}");
                continue;
            }

            var govde = await yanit.Content.ReadAsStringAsync();
            using var belge = JsonDocument.Parse(govde);
            var gereken = belge.RootElement.TryGetProperty("requiredPermission", out var g)
                ? g.GetString()
                : null;

            if (gereken is null) continue;

            if (!turetilen.TryGetValue(gereken, out var liste))
                turetilen[gereken] = liste = [];
            liste.Add($"{yontem} {sablon}");
        }

        /*
         * POZİTİF KONTROL (Kural 48) — BOŞ SONUÇ, YOKLUK KANITI DEĞİL.
         *
         * "Hiçbir uç kaba anahtara türetilmiyor" sonucu, izinsiz
         * kullanıcının 401 alıyor olmasıyla da AYNI görünürdü. Bu yüzden
         * NİTELİK TAŞIYAN bir uç aynı kullanıcıyla çağrılıyor: 403
         * gelmeli. Gelmezse ölçüm bozuktur ve envanter hiçbir şey
         * söylemez.
         */
        using (var kontrol = await istemci.GetAsync("/api/accounting-accounts"))
        {
            var govde = await kontrol.Content.ReadAsStringAsync();
            cikti.WriteLine(
                $"POZİTİF KONTROL: nitelikli uç -> HTTP {(int)kontrol.StatusCode} " +
                $"{govde[..Math.Min(120, govde.Length)]}");

            Assert.True(
                kontrol.StatusCode == HttpStatusCode.Forbidden,
                $"POZİTİF KONTROL DÜŞTÜ: izni sıfır kullanıcı nitelikli uçtan " +
                $"HTTP {(int)kontrol.StatusCode} aldı, 403 değil. Kimlik ya da " +
                "kapı çalışmıyor; envanterin boş çıkması hiçbir şey kanıtlamaz.");
        }

        /*
         * EŞLEŞMEYEN YOL — ENVANTERİN SON BOŞLUĞU.
         *
         * Middleware YOL üzerinde çalışıyor: hiçbir uca eşleşmeyen bir
         * yol da ondan geçer ve `GetEndpoint()` null döndüğü için
         * DOĞRUDAN yoldan türetmeye düşer. Böyle yollar sonsuz kümedir,
         * numaralandırılamaz — o yüzden TEMSİLCİ bir tanesi çağrılıyor.
         */
        foreach (var (yontem, yol) in new[]
                 {
                     ("GET", "/api/muhasebe/olmayan-uc"),
                     ("GET", "/api/finans/olmayan-uc"),
                     ("GET", "/api/hakedis/olmayan-uc"),
                     // YAZMA YÖNTEMİ: `isRead` false olunca türetme KABA
                     // anahtarı döndürüyor. Dalın ulaşılabilir olup
                     // olmadığını ayıran ölçüm bu.
                     ("POST", "/api/muhasebe/olmayan-uc"),
                     ("POST", "/api/finans/olmayan-uc"),
                     ("POST", "/api/hakedis/olmayan-uc"),
                     ("POST", "/api/puantaj/olmayan-uc"),
                     ("POST", "/api/bordro/olmayan-uc"),
                     ("POST", "/api/satin-alma/olmayan-uc"),
                     ("POST", "/api/projeler/olmayan-uc")
                 })
        {
            using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol);
            if (yontem != "GET") istek.Content = JsonContent.Bos();
            using var yanit = await istemci.SendAsync(istek);
            var govde = await yanit.Content.ReadAsStringAsync();
            cikti.WriteLine(
                $"EŞLEŞMEYEN YOL {yontem} {yol} -> HTTP {(int)yanit.StatusCode} " +
                govde[..Math.Min(140, govde.Length)]);
        }

        cikti.WriteLine($"=== NİTELİKSİZ api/ UÇ SAYISI: {adaylar.Count} ===");
        cikti.WriteLine($"403 vermeyen (izin aranmayan) : {turetmeyen.Count}");
        foreach (var t in turetmeyen) cikti.WriteLine($"    {t}");
        cikti.WriteLine("");
        cikti.WriteLine("=== TÜRETİLEN İZNE GÖRE ===");
        foreach (var (anahtar, liste) in turetilen.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var kaba = KabaAnahtarlar.Contains(anahtar, StringComparer.OrdinalIgnoreCase)
                ? "  <<< KABA ANAHTAR"
                : "";
            cikti.WriteLine($"{anahtar}  ({liste.Count} uç){kaba}");
            foreach (var u in liste.Take(6)) cikti.WriteLine($"    {u}");
        }

        var kabaSayisi = turetilen
            .Where(x => KabaAnahtarlar.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .Sum(x => x.Value.Count);

        cikti.WriteLine("");
        cikti.WriteLine($"KABA ANAHTARA TÜRETİLEN UÇ SAYISI: {kabaSayisi}");

        /*
         * ÇIRA: BUGÜNKÜ SAYI 0, ARTARSA KIRMIZI.
         *
         * ═══ KIRMIZI AYAĞI ÜRETİLEMİYOR — VE SEBEBİ ÖLÇÜLDÜ ═══
         *
         * Bu iddianın kırmızı ayağı "niteliksiz, kalıba düşen bir uç
         * ekle" olurdu. DENENDİ (A5, 2026-09-09): uygulama HİÇ AÇILMADI.
         *
         *     UÇ KAPISI — UYGULAMA AÇILAMAZ.
         *     BEYANSIZ UÇ (2): ... api/accounting/sonda-kaliba-dusen
         *                      ... api/zzz-sonda-kaliba-dusmeyen
         *
         * Yani kırmızı koşul, `UcKapisi` ayaktayken KURULAMIYOR. Testin
         * kırmızı ayağını gösteremiyor olmam testin zayıflığı değil,
         * ASIL KAPININ daha erken davranmasının sonucu.
         *
         * BU YÜZDEN BU İDDİA SAVUNMA DEĞİL, İKİNCİ BİR OKUMADIR.
         * Savunma `UcKapisi`'nde ve orada kırmızı-yeşil GÖSTERİLDİ.
         * Ayrı bir "beyansız uç çırası" yazılmadı: ikinci bir kapı
         * olurdu ve iki kapı bir gün ayrışır (Kural 79).
         *
         * Bu satır yine de duruyor, çünkü ucuz: `UcKapisi` bir gün
         * bayrakla kapatılırsa ya da açılış sırası değişirse, sayının
         * artışını burada görürüz.
         */
        Assert.True(
            kabaSayisi == 0,
            $"KABA ANAHTARA TÜRETİLEN UÇ SAYISI {kabaSayisi} (çizgi 0).\n" +
            "Niteliği olmayan bir uç, yolundan bir *.manage anahtarına " +
            "türetiliyor. O uçta kullanıcının İNCE izindeki Deny kaydı " +
            "hiçbir şey ifade etmez; kaba anahtar karar verir.\n" +
            "Uca RequirePermission ekleyin ya da neden kaba anahtarın " +
            "doğru kapı olduğunu yazın.");
    }
}

file static class JsonContent
{
    public static HttpContent Bos() =>
        new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
}
