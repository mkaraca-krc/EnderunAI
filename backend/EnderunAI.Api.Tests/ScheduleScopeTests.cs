using System.Net;
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
/// İş programının VERİ KAPSAMI (G7).
///
/// Korunan ayrım: izin ile kapsam ayrı kapılardır. schedule.view "iş
/// programı okuyabilir" der; kapsam "hangi projelerin" sorusunu
/// cevaplar. Sahaya geniş okuma vermenin bedeli, herkesin bütün
/// projeleri görmesi olmamalı.
///
/// İkinci güvence: ŞANTİYE kapsamlı kullanıcı (Şantiye Şefi, Formen)
/// projeye şantiyesi üzerinden ulaşır. Standart proje kapsamı süzgeci
/// yalnızca şirket/şube/proje kapsamına baktığı için bu kullanıcıya
/// hiçbir proje göstermezdi — planı uygulayan kişi kendi terminini
/// göremezdi.
/// </summary>
[Collection("Integration")]
public sealed class ScheduleScopeTests(DatabaseFixture fixture)
{
    private sealed record Site(Guid ProjectId, Guid SiteId, Guid ScheduleId);

    private async Task<HttpClient> AdminAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>Programı ve tek şantiyesi olan bir proje kurar.</summary>
    private async Task<Site> CreateProjectWithScheduleAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid projectId, siteId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            project.PlannedStartDate =
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

            var site = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"STY-{suffix}",
                Name = "Şantiye"
            };

            db.ProjectSites.Add(site);
            await db.SaveChangesAsync();

            projectId = project.Id;
            siteId = site.Id;
        }

        var client = await AdminAsync();

        var created = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/is-programi",
            new { seedFromSections = false });

        created.EnsureSuccessStatusCode();

        var scheduleId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "Busbar",
                plannedStartDate = new DateOnly(2026, 1, 5),
                plannedEndDate = new DateOnly(2026, 1, 10)
            });

        return new Site(projectId, siteId, scheduleId);
    }

    /// <summary>
    /// schedule.view izinli, YALNIZCA verilen şantiyeye kapsamlı bir
    /// kullanıcı — Şantiye Şefi'nin kapsam kurulumunun aynısı.
    /// </summary>
    private async Task<HttpClient> SiteScopedClientAsync(Guid siteId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string username;
        const string password = "TestScope!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider
                .GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestScope-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permission = await db.Permissions.SingleAsync(
                x => x.Key == PermissionCatalog.Keys.ScheduleView);

            db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            });

            username = $"scope-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Test Şantiye Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            // Kapsam YALNIZCA şantiye: şirket/şube/proje satırı yok.
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.Site,
                ProjectSiteId = siteId
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------------- Liste ----------------

    /// <summary>
    /// Şantiye kapsamlı kullanıcı KENDİ projesinin programını listede
    /// görür — projeye şantiyesinden çıkılıyor.
    /// </summary>
    [Fact]
    public async Task SiteScopedUser_SeesOwnProjectSchedule()
    {
        var site = await CreateProjectWithScheduleAsync();
        var client = await SiteScopedClientAsync(site.SiteId);

        var payload = await JsonAsync(await client.GetAsync("/api/is-programi"));
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(site.ProjectId, items[0].GetProperty("projectId").GetGuid());
    }

    /// <summary>Başkasının projesi listeye HİÇ girmez.</summary>
    [Fact]
    public async Task SiteScopedUser_DoesNotSeeOtherProjects()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var payload = await JsonAsync(await client.GetAsync("/api/is-programi"));

        var projectIds = payload.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("projectId").GetGuid())
            .ToList();

        Assert.Contains(mine.ProjectId, projectIds);
        Assert.DoesNotContain(other.ProjectId, projectIds);
    }

    /// <summary>Sınırsız kapsamlı kullanıcı hepsini görür.</summary>
    [Fact]
    public async Task GlobalUser_SeesEveryProject()
    {
        var first = await CreateProjectWithScheduleAsync();
        var second = await CreateProjectWithScheduleAsync();

        var client = await AdminAsync();

        var payload = await JsonAsync(await client.GetAsync("/api/is-programi"));

        var projectIds = payload.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("projectId").GetGuid())
            .ToList();

        Assert.Contains(first.ProjectId, projectIds);
        Assert.Contains(second.ProjectId, projectIds);
    }

    // ---------------- Detay ----------------

    [Fact]
    public async Task SiteScopedUser_CanOpenOwnProjectSchedule()
    {
        var site = await CreateProjectWithScheduleAsync();
        var client = await SiteScopedClientAsync(site.SiteId);

        var response = await client.GetAsync(
            $"/api/projects/{site.ProjectId}/is-programi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Kapsam dışı projeye URL'den gidilemez. İzin var, kapsam yok:
    /// 403.
    /// </summary>
    [Fact]
    public async Task SiteScopedUser_CannotOpenAnotherProjectSchedule()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var response = await client.GetAsync(
            $"/api/projects/{other.ProjectId}/is-programi");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SiteScopedUser_CannotReadAnotherScheduleBaselineHistory()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var response = await client.GetAsync(
            $"/api/is-programi/{other.ScheduleId}/baseline-gecmisi");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SiteScopedUser_CannotReadAnotherScheduleConflicts()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var response = await client.GetAsync(
            $"/api/is-programi/{other.ScheduleId}/kaynak-cakismalari");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------- Uyarılar ----------------

    /// <summary>
    /// Uyarı listesi de kapsamlı: başka projenin gecikmesi buradan
    /// sızmamalı.
    /// </summary>
    [Fact]
    public async Task Alerts_AreScopedToVisibleProjects()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var payload = await JsonAsync(await client.GetAsync("/api/is-programi/uyarilar"));

        var projectIds = payload.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("projectId").GetGuid())
            .ToList();

        Assert.DoesNotContain(other.ProjectId, projectIds);
    }

    /// <summary>
    /// Kapsam dışı proje kimliği sorulursa boş döner — var olduğu bile
    /// belli olmamalı.
    /// </summary>
    [Fact]
    public async Task Alerts_ForAnUnreachableProject_AreEmpty()
    {
        var mine = await CreateProjectWithScheduleAsync();
        var other = await CreateProjectWithScheduleAsync();

        var client = await SiteScopedClientAsync(mine.SiteId);

        var payload = await JsonAsync(await client.GetAsync(
            $"/api/is-programi/uyarilar?projectId={other.ProjectId}"));

        Assert.Empty(payload.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// Ceza tutarı bu kullanıcıda hiç yazılmaz: hakediş görüntüleme
    /// yetkisi yok.
    /// </summary>
    [Fact]
    public async Task SiteScopedUser_SeesNoPenaltyAmounts()
    {
        var site = await CreateProjectWithScheduleAsync();
        var client = await SiteScopedClientAsync(site.SiteId);

        var payload = await JsonAsync(await client.GetAsync("/api/is-programi/uyarilar"));

        Assert.False(payload.GetProperty("showsPenalty").GetBoolean());
    }
}
