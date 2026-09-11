using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ROL/1 (JETON/1'in rol yüzü) — ROL KAPISI JETONDAN DEĞİL, TAZE ANLIK GÖRÜNTÜDEN.
///
/// ═══ ÖLÇÜLEN KUSUR ═══
///
/// `UserManagement`, `AccessRequests`, `PermissionMatrix`, `SecurityAudit`
/// sınıf düzeyinde `[Authorize(Roles = "Admin,Genel Müdür")]` taşıyordu.
/// ASP.NET bu kapıyı JETONDAKİ rolden okur; jeton 12 saat yaşar ve arada
/// tazelenmez. Çağrılarak ölçüldü: Admin rolü alınan kullanıcı AYNI
/// jetonla kullanıcı yönetimi ucundan 200 aldı; yeni Admin yapılan kullanıcı
/// yeniden girişe kadar reddedildi.
///
/// ═══ KURAL (Mehmet Karacabey, 11 Eylül) ═══
///
/// İzin ve rol aynı kusurun iki yüzü; kaynakta çözülür (Kural 79). JETON/1
/// "jetondaki eskiyen yetki artık karar vermiyor" diyecekse bu, rol için
/// de doğru olmalı.
/// </summary>
[Collection("Integration")]
public sealed class RolTekKaynakTests(DatabaseFixture fixture)
{
    private sealed record Kisi(HttpClient Istemci, Guid Id);

    /// <summary>
    /// Kendi rolü `user-management.view` taşıyan (ürünün toggle yolundan —
    /// elle verilme kaydıyla, ikinci host tehlikesine karşı) ve istenirse
    /// Admin rolünü de taşıyan kullanıcı. İzin başka rolden geldiği için
    /// rol kapısı TEK başına ölçülür.
    /// </summary>
    private async Task<Kisi> KisiAsync(bool admin)
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        const string parola = "RolTekKaynak!2026";
        Guid rolId, id;
        var ad = $"rol1-{ek}";
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sifre = scope.ServiceProvider.GetRequiredService<PasswordService>();
            var rol = new AppRole { Name = $"Rol1-{ek}" };
            db.Roles.Add(rol);
            await db.SaveChangesAsync();
            rolId = rol.Id;
            var ozet = sifre.Hash(parola);
            var u = new AppUser
            {
                Username = ad, FullName = "Rol1 Test", PasswordHash = ozet.Hash, PasswordSalt = ozet.Salt,
                IsActive = true, WorkHoursExempt = true
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            id = u.Id;
            db.UserRoles.Add(new UserRole { UserId = id, RoleId = rol.Id });
            if (admin) db.UserRoles.Add(new UserRole { UserId = id, RoleId = await AdminRolIdAsync(db) });
            db.UserDataScopes.Add(new UserDataScope { UserId = id, ScopeType = DataScopeType.All });
            await db.SaveChangesAsync();
        }
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var t = await yonetici.PostAsJsonAsync("/api/user-management/permission-matrix/toggle",
            new { roleId = rolId, permissionKey = PermissionCatalog.Keys.UserManagementView, granted = true });
        Assert.True(t.IsSuccessStatusCode, $"toggle: {(int)t.StatusCode}");

        await Task.Delay(1_100); // parola damgası saniye sınırı (DAMGA/1)
        var istemci = fixture.Factory.CreateClient();
        var jeton = await AuthHelper.LoginAsync(istemci, ad, parola);
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return new Kisi(istemci, id);
    }

    private static Task<Guid> AdminRolIdAsync(AppDbContext db) =>
        db.Roles.Where(r => r.Name == "Admin").Select(r => r.Id).SingleAsync();

    private async Task AdminAsync(Guid kullaniciId, bool ver)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await AdminRolIdAsync(db);
        if (ver) db.UserRoles.Add(new UserRole { UserId = kullaniciId, RoleId = adminId });
        else db.UserRoles.Remove(await db.UserRoles.SingleAsync(x => x.UserId == kullaniciId && x.RoleId == adminId));
        await db.SaveChangesAsync();
    }

    private static async Task<HttpStatusCode> KullaniciYonetimi(HttpClient c) =>
        (await c.GetAsync("/api/user-management/users")).StatusCode;

    /// <summary>
    /// KIRMIZIYA DÖNERSE: Admin rolü alınan kişi, jetonu ölene kadar (12 sa)
    /// kullanıcı, izin matrisi, erişim talebi ve güvenlik denetimi ekranlarına
    /// girmeye devam eder — izni başka bir rolden geliyorsa.
    /// </summary>
    [Fact]
    public async Task R1_AdminRoluAlininca_AyniJetonlaReddedilir()
    {
        var k = await KisiAsync(admin: true);
        Assert.Equal(HttpStatusCode.OK, await KullaniciYonetimi(k.Istemci)); // zemin

        await AdminAsync(k.Id, ver: false);

        Assert.Equal(HttpStatusCode.Forbidden, await KullaniciYonetimi(k.Istemci));
    }

    /// <summary>POZİTİF KONTROL: Admin'i duran kişi girmeye devam eder.</summary>
    [Fact]
    public async Task R2_AdminDuranKisi_Girer()
    {
        var k = await KisiAsync(admin: true);
        Assert.Equal(HttpStatusCode.OK, await KullaniciYonetimi(k.Istemci));
        Assert.Equal(HttpStatusCode.OK, await KullaniciYonetimi(k.Istemci));
    }

    /// <summary>
    /// TERS YÖN. KIRMIZIYA DÖNERSE: yeni Admin yapılan kişi yeniden girişe
    /// kadar reddedilir — Genel Müdür yetki verir, etkisi saatler sonra görünür.
    /// </summary>
    [Fact]
    public async Task R3_AdminVerilince_AyniJetonlaHemenGirer()
    {
        var k = await KisiAsync(admin: false);
        Assert.Equal(HttpStatusCode.Forbidden, await KullaniciYonetimi(k.Istemci)); // zemin: rolü yok

        await AdminAsync(k.Id, ver: true);

        Assert.Equal(HttpStatusCode.OK, await KullaniciYonetimi(k.Istemci));
    }

    private sealed class Toplayici : ILoggerProvider
    {
        public ConcurrentQueue<string> Satirlar { get; } = new();
        public ILogger CreateLogger(string kategori) => new Gunlukcu(Satirlar);
        public void Dispose() { }
        private sealed class Gunlukcu(ConcurrentQueue<string> s) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel l, EventId e, TState st, Exception? ex, Func<TState, Exception?, string> f) => s.Enqueue(f(st, ex));
        }
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: rol reddi yine SESSİZ olur — "bu kullanıcı neden
    /// giremedi?" sorusu günlükten cevaplanamaz (GÜNLÜK/1).
    /// </summary>
    [Fact]
    public async Task R4_RolReddi_GunlugeSayiliSebepleDuser()
    {
        var toplayici = new Toplayici();
        await using var fabrika = fixture.Factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(toplayici)));

        // Düzenek kontrolü: toplayıcı bu hattın satırlarını görüyor.
        var jetonsuz = fabrika.CreateClient();
        await jetonsuz.GetAsync("/api/user-management/users");
        Assert.Contains(toplayici.Satirlar, s => s.Contains("sebep=JetonYok") && s.Contains("/api/user-management/users"));

        var k = await KisiAsync(admin: false);
        var c = fabrika.CreateClient();
        c.DefaultRequestHeaders.Authorization = k.Istemci.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/user-management/users")).StatusCode);

        Assert.Contains(toplayici.Satirlar, s =>
            s.Contains("ERISIM-RET sebep=RolYok") && s.Contains($"kullanici={k.Id}"));
    }

    /// <summary>
    /// YAPISAL MUHAFAZA — yeni yüzeyler korumalı doğsun.
    ///
    /// Arka uç kaynaklarında JETONDAN rol okuyan hiçbir biçim kalmaz:
    /// `[Authorize(Roles = …)]`, `RequireRole(`, `User.IsInRole(`, ve
    /// jetondan rol talebi toplamak. Rol kapısı `[RolGerekli(…)]` ile yazılır.
    ///
    /// TARAMA SAĞLIĞI: taranan dosya sayısı ve bilinen dört `[RolGerekli(`
    /// yazılır; ikisinden biri tutmazsa "0 ihlal" geçersizdir.
    ///
    /// KIRMIZIYA DÖNERSE: biri jetondan rol okuyan yeni bir kapı eklemiştir;
    /// o kapı rolü alınan kişiyi 12 saat daha içeri alır.
    /// </summary>
    [Fact]
    public void Muhafaza_JetondanRolOkunmaz()
    {
        var kok = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EnderunAI.Api"));
        var dosyalar = Directory.EnumerateFiles(kok, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .ToList();

        var yasak = new (string Ad, Regex Desen)[]
        {
            ("[Authorize(Roles=…)]", new Regex(@"^\s*\[\s*Authorize\s*\([^)\]]*\bRoles\s*=")),
            ("RequireRole(", new Regex(@"RequireRole\s*\(")),
            ("User.IsInRole(", new Regex(@"\b(User|Principal|HttpContext\.User)\??\.IsInRole\s*\(")),
            ("jetondan rol talebi", new Regex(@"FindAll\s*\(\s*(ClaimTypes\.Role|""role""|""roles"")")),
        };

        var ihlaller = new List<string>();
        var rolGerekli = 0;
        foreach (var dosya in dosyalar)
        {
            var satirlar = File.ReadAllLines(dosya);
            for (var i = 0; i < satirlar.Length; i++)
            {
                var s = satirlar[i];
                if (s.TrimStart().StartsWith("//") || s.TrimStart().StartsWith("*") || s.TrimStart().StartsWith("///")) continue;
                if (Regex.IsMatch(s, @"^\s*\[\s*RolGerekli\s*\(")) rolGerekli++;
                foreach (var (ad, desen) in yasak)
                    if (desen.IsMatch(s)) ihlaller.Add($"{Path.GetRelativePath(kok, dosya)}:{i + 1} — {ad}");
            }
        }

        Assert.True(dosyalar.Count > 500, $"TARAMA SAĞLIĞI: yalnız {dosyalar.Count} dosya tarandı — kök yanlış.");
        Assert.True(rolGerekli == 4,
            $"TARAMA SAĞLIĞI: bilinen dört [RolGerekli( bulunamadı ({rolGerekli}). Tarayıcı kör olabilir; " +
            "yeni bir rol kapısı eklendiyse bu sayı bilinçli güncellenir.");
        Assert.True(ihlaller.Count == 0, "JETONDAN ROL OKUNUYOR:\n" + string.Join("\n", ihlaller));
    }
}
