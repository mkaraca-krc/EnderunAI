using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kullanıcı ↔ personel bağı. Self-servis ekranlarının ("benim İSG
/// belgelerim") dayanağı bu bağ; tekil olmazsa bir personelin verisi
/// iki kullanıcıya açılır.
/// </summary>
[Collection("Integration")]
public sealed class UserPersonnelLinkTests(DatabaseFixture fixture)
{
    private async Task<Guid> CreatePersonnelAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        return personnel.Id;
    }

    private static object BuildUserPayload(string username, Guid? personnelId) => new
    {
        username,
        fullName = "Test Kullanıcı",
        honorific = (string?)null,
        email = $"{username}@test.local",
        roleNames = new[] { "İSG Sorumlusu" },
        password = "TestSifre12345",
        isActive = true,
        allowedPermissions = Array.Empty<string>(),
        deniedPermissions = Array.Empty<string>(),
        projectSiteIds = Array.Empty<Guid>(),
        workHoursExempt = false,
        personnelId
    };

    [Fact]
    public async Task Create_LinksUserToPersonnel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personnelId = await CreatePersonnelAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/user-management/users",
            BuildUserPayload($"isgtest{suffix}", personnelId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var user = payload.GetProperty("user");

        Assert.Equal(personnelId, user.GetProperty("personnelId").GetGuid());
        Assert.Contains("Personel", user.GetProperty("personnelName").GetString()!);
    }

    [Fact]
    public async Task Create_SamePersonnelTwice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personnelId = await CreatePersonnelAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/user-management/users",
                BuildUserPayload($"isgbir{suffix}", personnelId))).StatusCode);

        // Aynı personel kartı ikinci kullanıcıya bağlanamaz; bağlansaydı
        // "kendi kaydım" ekranı iki kişiye açılırdı.
        var second = await client.PostAsJsonAsync("/api/user-management/users",
            BuildUserPayload($"isgiki{suffix}", personnelId));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownPersonnel_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/user-management/users",
            BuildUserPayload($"isgyok{suffix}", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutPersonnelLink_IsAllowed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Her kullanıcı personel değildir; bağ zorunlu değil.
        var response = await client.PostAsJsonAsync("/api/user-management/users",
            BuildUserPayload($"isgbos{suffix}", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("user");

        Assert.Equal(JsonValueKind.Null, user.GetProperty("personnelId").ValueKind);
    }

    [Fact]
    public void IsgSorumlusuRole_HasHealthPermission_ButTeknikKoordinatorDoesNot()
    {
        var isgSorumlusu = RoleCatalog.Roles
            .Single(x => x.Name == "İSG Sorumlusu");
        var teknikKoordinator = RoleCatalog.Roles
            .Single(x => x.Name == "Teknik Koordinatör");

        // Sağlık raporunun tıbbi detayı ve kaza defteri dar kapıdır.
        Assert.Contains(PermissionCatalog.Keys.IsgHealthView, isgSorumlusu.PermissionKeys);
        Assert.Contains(PermissionCatalog.Keys.IsgIncidentView, isgSorumlusu.PermissionKeys);

        Assert.DoesNotContain(
            PermissionCatalog.Keys.IsgHealthView, teknikKoordinator.PermissionKeys);
        Assert.DoesNotContain(
            PermissionCatalog.Keys.IsgIncidentView, teknikKoordinator.PermissionKeys);

        // Saha kaydı girebilmeli.
        Assert.Contains(PermissionCatalog.Keys.IsgView, teknikKoordinator.PermissionKeys);
        Assert.Contains(PermissionCatalog.Keys.IsgCreate, teknikKoordinator.PermissionKeys);
    }
}
