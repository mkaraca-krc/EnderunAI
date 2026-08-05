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
/// Personel 360 ekranındaki İSG bölümü.
///
/// Eğitim ve sertifika sayıları daha önce sabit sıfır dönüyordu; artık
/// gerçek kayıtlardan geliyor. Ancak 360 ekranı personnel.view ile
/// açılıyor, İSG kayıtları isg.view ile korunuyor — bu yüzden izni
/// olmayana bölüm doldurulmadan dönüyor. Sağlık raporu bu ekrana hiç
/// girmiyor.
/// </summary>
[Collection("Integration")]
public sealed class Personnel360IsgTests(DatabaseFixture fixture)
{
    private async Task<Guid> CreatePersonnelWithIsgRecordsAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.IsgTrainings.Add(new IsgTraining
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            TrainingType = IsgTrainingType.Basic,
            Topic = "Temel İSG",
            TrainingDate = today.AddDays(-100),
            DurationHours = 16m,
            ValidUntil = today.AddDays(200)
        });

        // Biri geçerli, biri süresi dolmuş: sayımın ayrıştığı görülsün.
        db.IsgCertificates.Add(new IsgCertificate
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            CertificateType = IsgCertificateType.FirstAid,
            IssueDate = today.AddDays(-200),
            ExpiryDate = today.AddDays(100)
        });

        db.IsgCertificates.Add(new IsgCertificate
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            CertificateType = IsgCertificateType.WorkingAtHeight,
            IssueDate = today.AddDays(-500),
            ExpiryDate = today.AddDays(-10)
        });

        // Sağlık raporu da var: 360'a SIZMAMASI gerekiyor.
        db.IsgHealthReports.Add(new IsgHealthReport
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            ReportType = IsgHealthReportType.Periodic,
            ExamDate = today.AddDays(-30),
            ValidUntil = today.AddDays(300),
            Result = IsgHealthResult.FitWithRestrictions,
            Restrictions = "Yüksekte çalışamaz"
        });

        await db.SaveChangesAsync();

        return personnel.Id;
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Isg360!2026";
        var username = $"test-360-{Guid.NewGuid():N}"[..40];
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
    public async Task Personnel360_ReportsRealIsgCountsForAuthorizedRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personnelId = await CreatePersonnelWithIsgRecordsAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/personnel-360/{personnelId}");

        var hr = result.GetProperty("humanResources");

        Assert.Equal(1, hr.GetProperty("trainingCount").GetInt32());
        Assert.Equal(1, hr.GetProperty("completedTrainingCount").GetInt32());
        Assert.Equal(2, hr.GetProperty("certificateCount").GetInt32());
        Assert.Equal(1, hr.GetProperty("validCertificateCount").GetInt32());
        Assert.Equal(1, hr.GetProperty("expiredCertificateCount").GetInt32());
        Assert.False(hr.GetProperty("isgGizli").GetBoolean());

        Assert.Equal(1, result.GetProperty("trainings").GetArrayLength());
        Assert.Equal(2, result.GetProperty("certificates").GetArrayLength());
    }

    [Fact]
    public async Task Personnel360_HidesIsgSectionFromRoleWithoutIsgPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personnelId = await CreatePersonnelWithIsgRecordsAsync(suffix);

        // İK Sorumlusu personel kartını görür ama İSG izni yoktur.
        var client = await CreateClientForRoleAsync("İK Sorumlusu");

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/personnel-360/{personnelId}");

        var hr = result.GetProperty("humanResources");

        Assert.Equal(0, hr.GetProperty("trainingCount").GetInt32());
        Assert.Equal(0, hr.GetProperty("certificateCount").GetInt32());
        Assert.True(hr.GetProperty("isgGizli").GetBoolean());

        Assert.Equal(0, result.GetProperty("trainings").GetArrayLength());
        Assert.Equal(0, result.GetProperty("certificates").GetArrayLength());
    }

    [Fact]
    public async Task Personnel360_NeverExposesHealthReportData()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personnelId = await CreatePersonnelWithIsgRecordsAsync(suffix);

        // Sağlık detayını görebilen en yetkili rol bile 360 üzerinden
        // tıbbi veri almamalı: bu ekran tıbbi veri taşımıyor.
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var response = await client.GetAsync($"/api/hr/personnel-360/{personnelId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Yüksekte çalışamaz", body);
        Assert.DoesNotContain("healthReport", body, StringComparison.OrdinalIgnoreCase);
    }
}
