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

[Collection("Integration")]
public sealed class PermissionAndScopeTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, Guid UserId)> CreateUserWithRolesAsync(
        string usernameSuffix,
        string password,
        string[] roleNames,
        IEnumerable<Guid>? siteScopeIds = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var username = $"test-{usernameSuffix}-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {usernameSuffix}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            // Bu testler izin/kapsam mantığını doğruluyor, mesai saati
            // mantığını değil — testin çalıştığı saatten bağımsız
            // deterministik olması için kullanıcı mesai istisnalı yapılır.
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roles = await db.Roles
            .Where(role => roleNames.Contains(role.Name))
            .ToListAsync();

        db.UserRoles.AddRange(roles.Select(role => new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        }));

        var siteIds = siteScopeIds?.ToArray() ?? [];
        if (siteIds.Length > 0)
        {
            foreach (var siteId in siteIds)
            {
                db.UserDataScopes.Add(new UserDataScope
                {
                    UserId = user.Id,
                    ScopeType = DataScopeType.Site,
                    ProjectSiteId = siteId
                });
            }
        }
        else
        {
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });
        }

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return (client, user.Id);
    }

    [Fact]
    public async Task Formen_CannotDeleteSiteReportPhoto_Returns403()
    {
        // Formen rolünde site-reports.delete izni yok — bu uca erişim
        // reddedilmeli (RequirePermission attribute üzerinden, gerçek
        // DB'den okunan izinlerle).
        var (client, _) = await CreateUserWithRolesAsync(
            "formen",
            "Formen!2026Test",
            ["Formen"]);

        var response = await client.DeleteAsync(
            $"/api/project-sites/{Guid.NewGuid()}/daily-reports/{Guid.NewGuid()}/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SantiyeSefi_CannotAccessUnassignedSite_ButCanAccessAssignedSite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            var assignedSite = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"ATANAN-{suffix}",
                Name = "Atanan Şantiye"
            };
            var otherSite = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"BASKA-{suffix}",
                Name = "Başka Şantiye"
            };
            db.ProjectSites.AddRange(assignedSite, otherSite);
            await db.SaveChangesAsync();

            var (client, _) = await CreateUserWithRolesAsync(
                "santiyesefi",
                "SantiyeSefi!2026Test",
                ["Şantiye Şefi"],
                [assignedSite.Id]);

            // Atanmadığı şantiyenin günlük rapor listesine erişim
            // veri kapsamı ihlali nedeniyle 404 dönmeli (kaynağın
            // varlığını sızdırmamak için NotFound kullanılıyor).
            var unassignedResponse = await client.GetAsync(
                $"/api/project-sites/{otherSite.Id}/daily-reports");
            Assert.Equal(HttpStatusCode.NotFound, unassignedResponse.StatusCode);

            // Atandığı şantiyeye erişim serbest olmalı.
            var assignedResponse = await client.GetAsync(
                $"/api/project-sites/{assignedSite.Id}/daily-reports");
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        }
    }

    [Fact]
    public async Task SantiyeSefi_CannotCreateNewSite_LacksSitesCreatePermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var (client, _) = await CreateUserWithRolesAsync(
            "santiyesefi2",
            "SantiyeSefi2!2026Test",
            ["Şantiye Şefi"]);

        var response = await client.PostAsJsonAsync($"/api/projects/{project.Id}/sites", new
        {
            code = $"YENI-{suffix}",
            name = "Yeni Şantiye",
            location = (string?)null,
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
