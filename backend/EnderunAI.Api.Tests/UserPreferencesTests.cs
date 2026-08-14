using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Arayüz tercihleri (menü daraltma + kısayollar).
///
/// EN ÖNEMLİ TEST BURADA: bir kullanıcının tercihi başka bir
/// kullanıcıya ASLA sızmamalı ve başkasının tercihine yazamamalı.
/// Uç kullanıcı kimliğini yalnızca oturumdan okuyor; bu testler o
/// sözün tutulduğunu doğruluyor.
/// </summary>
[Collection("Integration")]
public sealed class UserPreferencesTests(DatabaseFixture fixture)
{
    private sealed record PreferenceResponse(
        bool SidebarCollapsed,
        List<string> FavoritePaths);

    private async Task<(HttpClient Client, Guid UserId)> CreateUserClientAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Preference!2026";
        var username = $"test-pref-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Tercih Testi",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);

        // Rol verilmiyor: arayüz tercihi HİÇBİR izne bağlı olmamalı.
        // İzne bağlansaydı, izni olmayan kullanıcı menüsünü
        // daraltamaz hale gelirdi.
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return (client, user.Id);
    }

    /// <summary>
    /// Tercih belirtmemiş kullanıcı 404 DEĞİL varsayılan alır: aksi
    /// halde her ekran açılışında arayüzün hata yolunu koşması
    /// gerekirdi.
    /// </summary>
    [Fact]
    public async Task Kayit_Yoksa_Varsayilan_Doner()
    {
        var (client, _) = await CreateUserClientAsync();

        var response = await client.GetAsync("/api/user-preferences");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preference = await response.Content
            .ReadFromJsonAsync<PreferenceResponse>();

        Assert.NotNull(preference);
        Assert.False(preference!.SidebarCollapsed);
        Assert.Empty(preference.FavoritePaths);
    }

    [Fact]
    public async Task Tercih_Kaydedilir_Ve_Geri_Okunur()
    {
        var (client, _) = await CreateUserClientAsync();

        var save = await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = true,
            favoritePaths = new[] { "/finans/kasa", "/hakedis" }
        });

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var read = await client.GetFromJsonAsync<PreferenceResponse>(
            "/api/user-preferences");

        Assert.NotNull(read);
        Assert.True(read!.SidebarCollapsed);
        Assert.Equal(new[] { "/finans/kasa", "/hakedis" }, read.FavoritePaths);
    }

    /// <summary>
    /// İKİNCİ KAYIT ÜSTÜNE YAZAR, ikinci satır açmaz. Kullanıcı başına
    /// tek satır kuralı kısmi tekil dizinle korunuyor; ikinci kayıt
    /// denemesi burada 500'e düşerdi.
    /// </summary>
    [Fact]
    public async Task Tekrar_Kaydetmek_Ikinci_Satir_Acmaz()
    {
        var (client, userId) = await CreateUserClientAsync();

        await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = true,
            favoritePaths = new[] { "/finans/kasa" }
        });

        var second = await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = false,
            favoritePaths = new[] { "/hakedis" }
        });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.UserUiPreferences
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.False(rows[0].SidebarCollapsed);
        Assert.Equal(["/hakedis"], rows[0].FavoritePaths);
    }

    /// <summary>
    /// ASIL GÜVENLİK TESTİ: iki kullanıcının tercihi birbirine
    /// karışmaz. Uç bir kullanıcı kimliği parametresi kabul etseydi,
    /// herkes herkesin kısayollarını okuyup değiştirebilirdi.
    /// </summary>
    [Fact]
    public async Task Kullanicinin_Tercihi_Digerine_Sizmaz()
    {
        var (first, _) = await CreateUserClientAsync();
        var (second, _) = await CreateUserClientAsync();

        await first.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = true,
            favoritePaths = new[] { "/insan-kaynaklari/bordro" }
        });

        var otherPreference = await second
            .GetFromJsonAsync<PreferenceResponse>("/api/user-preferences");

        Assert.NotNull(otherPreference);
        Assert.False(otherPreference!.SidebarCollapsed);
        Assert.Empty(otherPreference.FavoritePaths);
    }

    [Fact]
    public async Task Kimlik_Dogrulanmadan_Erisilemez()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/user-preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// DIŞ BAĞLANTI FAVORİYE ALINAMAZ: kabul edilseydi kısayol çubuğu
    /// başka bir siteye giden bağlantıya dönüşebilirdi.
    /// </summary>
    [Fact]
    public async Task Uygulama_Disi_Yol_Kaydedilmez()
    {
        var (client, _) = await CreateUserClientAsync();

        await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = false,
            favoritePaths = new[]
            {
                "https://baska-site.example/veri",
                "//baska-site.example",
                "/finans/kasa"
            }
        });

        var read = await client.GetFromJsonAsync<PreferenceResponse>(
            "/api/user-preferences");

        Assert.Equal(["/finans/kasa"], read!.FavoritePaths);
    }

    /// <summary>
    /// Tavan: sınırsız favori tek satırı megabaytlarca büyütebilirdi.
    /// </summary>
    [Fact]
    public async Task Favori_Sayisi_Tavanla_Sinirli()
    {
        var (client, _) = await CreateUserClientAsync();

        var many = Enumerable.Range(1, 40)
            .Select(index => $"/sayfa-{index}")
            .ToArray();

        await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = false,
            favoritePaths = many
        });

        var read = await client.GetFromJsonAsync<PreferenceResponse>(
            "/api/user-preferences");

        Assert.Equal(20, read!.FavoritePaths.Count);
        Assert.Equal("/sayfa-1", read.FavoritePaths[0]);
    }

    /// <summary>
    /// Aynı yol iki kez gönderilirse kısayol listesi çiftlenmez.
    /// </summary>
    [Fact]
    public async Task Ayni_Yol_Tekrar_Eklenmez()
    {
        var (client, _) = await CreateUserClientAsync();

        await client.PutAsJsonAsync("/api/user-preferences", new
        {
            sidebarCollapsed = false,
            favoritePaths = new[] { "/finans/kasa", "/finans/kasa" }
        });

        var read = await client.GetFromJsonAsync<PreferenceResponse>(
            "/api/user-preferences");

        Assert.Equal(["/finans/kasa"], read!.FavoritePaths);
    }
}
