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
/// İSG paneli ve brifing kaynakları.
///
/// İki güvence: sayılar seed edilen veriyle birebir, ve kaza rakamları
/// yalnızca kaza defterini görebilene dönüyor — sayı bile kendi başına
/// bilgi taşır.
/// </summary>
[Collection("Integration")]
public sealed class IsgDashboardTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid PersonnelId);

    /// <summary>
    /// Bir personel + süresi dolmuş sağlık raporu + 10 gün sonra dolacak
    /// sertifika + bildirilmemiş eski kaza kurar.
    /// </summary>
    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.IsgHealthReports.Add(new IsgHealthReport
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            ReportType = IsgHealthReportType.Periodic,
            ExamDate = today.AddDays(-400),
            ValidUntil = today.AddDays(-5),
            Result = IsgHealthResult.Fit
        });

        db.IsgCertificates.Add(new IsgCertificate
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            CertificateType = IsgCertificateType.WorkingAtHeight,
            IssueDate = today.AddDays(-300),
            ExpiryDate = today.AddDays(10)
        });

        db.IsgIncidents.Add(new IsgIncident
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            PersonnelId = personnel.Id,
            IncidentDateTime = DateTime.UtcNow.AddDays(-20),
            IncidentType = IsgIncidentType.Accident,
            Severity = IsgIncidentSeverity.LostWorkday,
            Description = "Test kazası",
            LostWorkDays = 3,
            SgkNotified = false,
            Status = IsgIncidentStatus.Open
        });

        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, personnel.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "IsgPanel!2026";
        var username = $"test-panel-{Guid.NewGuid():N}"[..40];
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
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    [Fact]
    public async Task Dashboard_CountsExpiryStatesCorrectly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var panel = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/dashboard?companyId={context.CompanyId}");

        Assert.Equal(1,
            panel.GetProperty("saglikRaporu").GetProperty("suresiDoldu").GetInt32());

        // Süresi dolmuş rapor "geçerli rapor" saymaz; personel eksikli.
        Assert.Equal(1,
            panel.GetProperty("saglikRaporu").GetProperty("eksikPersonel").GetInt32());

        Assert.Equal(1,
            panel.GetProperty("sertifika").GetProperty("yakindaDoluyor").GetInt32());

        // Hiç eğitim kaydı yok: temel eğitimi eksik personel 1.
        Assert.Equal(1,
            panel.GetProperty("egitim").GetProperty("temelEgitimiEksikPersonel").GetInt32());

        Assert.Equal(30, panel.GetProperty("uyariEsigiGun").GetInt32());
    }

    [Fact]
    public async Task Dashboard_ShowsIncidentCountsToAuthorizedRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var panel = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/dashboard?companyId={context.CompanyId}");

        var kaza = panel.GetProperty("kaza");

        Assert.Equal(1, kaza.GetProperty("acikKayit").GetInt32());
        Assert.Equal(1, kaza.GetProperty("agirKayit").GetInt32());
        // 20 gün önceki bildirilmemiş kaza: yasal süre geçti.
        Assert.Equal(1, kaza.GetProperty("sgkBildirimiGecikmis").GetInt32());
        Assert.Equal(3, kaza.GetProperty("buYilKayipIsGunu").GetInt32());
    }

    [Fact]
    public async Task Dashboard_HidesIncidentCountsFromUnauthorizedRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // İSG kaydı görebiliyor ama kaza defterine yetkisi yok.
        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var panel = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/dashboard?companyId={context.CompanyId}");

        // Süre takibi görünür.
        Assert.Equal(1,
            panel.GetProperty("saglikRaporu").GetProperty("suresiDoldu").GetInt32());

        // Kaza rakamı hiç dönmez — sayı bile bilgi taşır.
        Assert.Equal(JsonValueKind.Null, panel.GetProperty("kaza").ValueKind);
        Assert.True(panel.GetProperty("kazaGizli").GetBoolean());
    }

    [Fact]
    public async Task Dashboard_RequiresIsgPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.GetAsync(
            $"/api/isg/dashboard?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Briefing_ProducesIsgItemsWhenDataExists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var briefing = await client.GetFromJsonAsync<JsonElement>("/api/hizir/briefing");

        var titles = briefing.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(titles, x => x.Contains("sağlık raporu"));
        Assert.Contains(titles, x => x.Contains("SGK"));
    }

    [Fact]
    public async Task Briefing_HidesIncidentItemFromUnauthorizedRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateContextAsync(suffix);

        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var briefing = await client.GetFromJsonAsync<JsonElement>("/api/hizir/briefing");

        var titles = briefing.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString() ?? string.Empty)
            .ToList();

        // Süre uyarısını görür...
        Assert.Contains(titles, x => x.Contains("sağlık raporu"));
        // ...ama kaza maddesini görmez; kaynak hiç çalıştırılmaz.
        Assert.DoesNotContain(titles, x => x.Contains("SGK"));
        Assert.DoesNotContain(titles, x => x.Contains("ramak kala"));
    }
}
