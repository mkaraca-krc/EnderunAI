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
/// ARAYÜZÜN SÜPER KULLANICI SİNYALİ: hasAllPermissions.
///
/// Arayüz bugüne kadar rol ADINA bakıyordu ("Admin" / "Genel Müdür").
/// Rol yeniden adlandırılsa ya da başka bir role tüm izinler verilse
/// menü, sayfa kapısı ve butonlar yanlış davranırdı. Bayrak artık
/// backend'den geliyor ve kuralı token üretimiyle AYNI yerden
/// (PermissionCatalog.HasEveryPermission) okunuyor.
/// </summary>
[Collection("Integration")]
public sealed class SessionPermissionFlagTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> ClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Yetki!2026Test";
        var username = $"test-yetki-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    [Fact]
    public async Task TumIzinleriOlanRol_BayragiTrueDoner()
    {
        var client = await ClientForRoleAsync("Genel Müdür");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.True(me.GetProperty("hasAllPermissions").GetBoolean());
    }

    /// <summary>
    /// Dar rolde bayrak FALSE: arayüz bu kullanıcıya yalnız izinli
    /// öğeleri gösterecek.
    /// </summary>
    [Fact]
    public async Task DarRol_BayragiFalseDoner()
    {
        var client = await ClientForRoleAsync("Formen");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.False(me.GetProperty("hasAllPermissions").GetBoolean());

        // İzin listesi yine dönüyor: arayüz tek tek anahtarlarla da
        // kontrol yapabiliyor.
        Assert.NotEmpty(me.GetProperty("permissions").EnumerateArray());
    }

    /// <summary>
    /// Giriş yanıtı da aynı bayrağı taşıyor: arayüz oturumu /auth/me'yi
    /// beklemeden şekillendirebilsin ve iki uç aynı sinyali versin.
    /// </summary>
    [Fact]
    public async Task GirisYaniti_AyniBayragiTasir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Yetki!2026Giris";
        var username = $"test-giris-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Test Giriş",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == "Sekreterya");
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { username, password });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(
            body.GetProperty("user").GetProperty("hasAllPermissions").GetBoolean());
    }

    /// <summary>
    /// BAYRAK ROL ADINDAN BAĞIMSIZ: adı "Admin" olmayan ama katalogdaki
    /// her izne sahip bir rol de true alır. Eski davranışta bu kullanıcı
    /// arayüzde sıradan bir kullanıcı gibi kısıtlanırdı.
    /// </summary>
    [Fact]
    public async Task AdiFarkliAmaTumIzinliRol_BayragiTrueAlir()
    {
        var roleName = $"Test Tam Yetki {Guid.NewGuid():N}"[..24];

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var role = new AppRole { Name = roleName, Description = "Test" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissionIds = await db.Permissions.Select(x => x.Id).ToListAsync();

            db.RolePermissions.AddRange(permissionIds.Select(permissionId =>
                new RolePermission { RoleId = role.Id, PermissionId = permissionId }));

            await db.SaveChangesAsync();
        }

        var client = await ClientForRoleAsync(roleName);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.True(me.GetProperty("hasAllPermissions").GetBoolean());
    }

    /// <summary>
    /// ARAYÜZ GÜVENLİK SINIRI DEĞİLDİR: bayrak false olan kullanıcı için
    /// uç, arayüzde düğme olsun olmasın reddediyor.
    /// </summary>
    [Fact]
    public async Task UcYetkisi_ArayuzdenBagimsizReddeder()
    {
        var client = await ClientForRoleAsync("Formen");

        var response = await client.GetAsync("/api/expenses/kayitlar?companyId=" + Guid.NewGuid());

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
