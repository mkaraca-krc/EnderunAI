using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
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
/// GÜNLÜK/1 DELİĞİ — MESAİ ARA KATMANININ 401'İ GÜNLÜĞE DÜŞMÜYORDU.
///
/// ═══ ÖLÇÜLEN (2026-09-10) ═══
///
/// GÜNLÜK/1 "her 401/403 kararı tek satır" diye yayına alındı. Sekiz
/// sebep JWT katmanını ve izin ara katmanını kapsıyordu; ama
/// `WorkHourAccessMiddleware` oturumu 401 ile keserken HİÇBİR SATIR
/// yazmıyordu. Muaf olmayan bir personelin mesaisi bitip oturumu
/// düştüğünde günlük sessiz kalırdı — "bu kullanıcı neden çıkarıldı?"
/// sorusu, tam da en sık sorulacağı durumda cevapsızdı.
///
/// BU TEST GERÇEK HATTAN ÖLÇÜYOR: satır, uygulamanın kendi günlük
/// sağlayıcısına düşüyor mu. Birim testi `ErisimGunlugu.Ret`in yazdığını
/// gösteriyordu; ara katmanın onu ÇAĞIRDIĞINI göstermiyordu.
/// </summary>
[Collection("Integration")]
public sealed class ErisimGunluguMesaiTests(DatabaseFixture fixture)
{
    private sealed class Toplayici : ILoggerProvider
    {
        public ConcurrentQueue<string> Satirlar { get; } = new();

        public ILogger CreateLogger(string kategori) => new Gunlukcu(Satirlar);

        public void Dispose() { }

        private sealed class Gunlukcu(ConcurrentQueue<string> satirlar) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => satirlar.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: mesaisi biten personelin oturumu kesilir ve
    /// günlükte İZ KALMAZ — 9 Eylül gecesi gibi, sebep tahminle aranır.
    /// </summary>
    [Fact]
    public async Task MesaiKesintisi_GunlugeSebepSatiriDusurur()
    {
        var toplayici = new Toplayici();
        var fabrika = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(l => l.AddProvider(toplayici)));

        // ── DÜZENEK KONTROLÜ: toplayıcı bu hattın satırlarını GÖREBİLİYOR ──
        // Jetonsuz istek -> JetonYok. Bu satır görünmüyorsa aşağıdaki
        // "MesaiDisi yok" gözlemi hiçbir şey kanıtlamaz (Kural 48).
        var jetonsuz = fabrika.CreateClient();
        var ret = await jetonsuz.GetAsync("/api/auth/work-hours-status");
        Assert.Equal(HttpStatusCode.Unauthorized, ret.StatusCode);
        Assert.Contains(toplayici.Satirlar, s =>
            s.Contains("sebep=JetonYok") && s.Contains("yol=/api/auth/work-hours-status"));

        // ── Penceresiz rol + açık geçici erişim: giriş yapabilen kullanıcı ──
        string kullaniciAdi;
        Guid id;
        const string parola = "GunlukMesai!2026";
        using (var scope = fabrika.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parolaServisi = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var rol = new AppRole { Name = $"GunlukMesai-{Guid.NewGuid():N}"[..40] };
            db.Roles.Add(rol);
            await db.SaveChangesAsync();

            kullaniciAdi = $"gunluk-mesai-{Guid.NewGuid():N}"[..40];
            var ozet = parolaServisi.Hash(parola);
            var kullanici = new AppUser
            {
                Username = kullaniciAdi,
                FullName = "Gunluk Mesai",
                PasswordHash = ozet.Hash,
                PasswordSalt = ozet.Salt,
                IsActive = true
            };
            db.Users.Add(kullanici);
            await db.SaveChangesAsync();
            id = kullanici.Id;

            db.UserRoles.Add(new UserRole { UserId = id, RoleId = rol.Id });
            db.UserDataScopes.Add(new UserDataScope { UserId = id, ScopeType = DataScopeType.All });
            db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
            {
                UserId = id,
                GrantedByUserId = id,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }

        var istemci = fabrika.CreateClient();
        var jeton = await AuthHelper.LoginAsync(istemci, kullaniciAdi, parola);
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);

        using (var scope = fabrika.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var erisim = await db.TemporaryAccessGrants.SingleAsync(g => g.UserId == id);
            erisim.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var kesinti = await istemci.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, kesinti.StatusCode);

        Assert.Contains(toplayici.Satirlar, s =>
            s.Contains("ERISIM-RET sebep=MesaiDisi") &&
            s.Contains($"kullanici={id}") &&
            s.Contains("yol=/api/auth/me"));
    }
}
