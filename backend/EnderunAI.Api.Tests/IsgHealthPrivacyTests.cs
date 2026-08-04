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
/// Sağlık raporu gizliliği ve self-servis sızma testleri.
///
/// İki ayrı güvence:
///   1. Tıbbi detay (teşhis, kısıtlama, rapor dosyası) isg.health.view
///      olmadan HİÇ dönmez — rapor tarihi ve geçerliliği döner ki süre
///      takibi çalışsın.
///   2. "Benim İSG belgelerim" ucu yalnızca çağıranın kendi kaydını
///      döndürür; başkasının kaydına erişim yolu yoktur.
/// </summary>
[Collection("Integration")]
public sealed class IsgHealthPrivacyTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> CreateClientForRoleAsync(
        string roleName, Guid? linkedPersonnelId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "IsgSaglik!2026";
        var username = $"test-isg-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true,
            PersonnelId = linkedPersonnelId
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

    /// <summary>Tıbbi detayı dolu bir sağlık raporu olan personel kurar.</summary>
    private async Task<(Guid CompanyId, Guid PersonnelId)> CreatePersonnelWithHealthReportAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        db.IsgHealthReports.Add(new IsgHealthReport
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            ReportType = IsgHealthReportType.Periodic,
            ExamDate = new DateOnly(2026, 1, 15),
            ValidUntil = new DateOnly(2027, 1, 15),
            Result = IsgHealthResult.FitWithRestrictions,
            DoctorName = "Dr. Test",
            Restrictions = "Yüksekte çalışamaz",
            DoctorNotes = "Gizli teşhis notu",
            DocumentPath = "isg/2026/rapor.pdf"
        });

        await db.SaveChangesAsync();

        return (project.CompanyId, personnel.Id);
    }

    [Fact]
    public async Task IsgSorumlusu_SeesMedicalDetail()
    {
        var (_, personnelId) = await CreatePersonnelWithHealthReportAsync();
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var card = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/personel/{personnelId}");

        var report = card.GetProperty("healthReports").EnumerateArray().Single();

        Assert.Equal("Yüksekte çalışamaz", report.GetProperty("restrictions").GetString());
        Assert.Equal("Gizli teşhis notu", report.GetProperty("doctorNotes").GetString());
        Assert.True(report.GetProperty("hasDocument").GetBoolean());
        Assert.False(report.GetProperty("healthDetailHidden").GetBoolean());
    }

    [Fact]
    public async Task TeknikKoordinator_SeesDatesButNotMedicalDetail()
    {
        var (_, personnelId) = await CreatePersonnelWithHealthReportAsync();
        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var card = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/personel/{personnelId}");

        var report = card.GetProperty("healthReports").EnumerateArray().Single();

        // Süre takibi çalışmalı: tarih ve geçerlilik görünür.
        Assert.Equal("2026-01-15", report.GetProperty("examDate").GetString());
        Assert.Equal("2027-01-15", report.GetProperty("validUntil").GetString());
        Assert.Equal("Geçerli", report.GetProperty("validityStatusName").GetString());

        // Tıbbi detay projeksiyondan hiç çıkmaz.
        Assert.Equal(JsonValueKind.Null, report.GetProperty("restrictions").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("doctorNotes").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("hasDocument").ValueKind);
        Assert.True(report.GetProperty("healthDetailHidden").GetBoolean());
    }

    [Fact]
    public async Task TeknikKoordinator_CannotEditHealthReport()
    {
        var (_, personnelId) = await CreatePersonnelWithHealthReportAsync();

        Guid reportId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            reportId = await db.IsgHealthReports
                .Where(x => x.PersonnelId == personnelId)
                .Select(x => x.Id)
                .SingleAsync();
        }

        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        // Göremediği bir notu silmesine izin verilmemeli.
        var response = await client.PutAsJsonAsync(
            $"/api/isg/saglik-raporlari/{reportId}",
            new
            {
                isgOsgbContractId = (Guid?)null,
                reportType = 1,
                examDate = "2026-01-15",
                validUntil = "2027-01-15",
                result = 0,
                doctorName = "Dr. Test",
                restrictions = (string?)null,
                doctorNotes = (string?)null
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SantiyeSefi_CannotAccessIsgRecordsAtAll()
    {
        var (_, personnelId) = await CreatePersonnelWithHealthReportAsync();
        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.GetAsync($"/api/isg/personel/{personnelId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OwnCard_ReturnsOnlyCallersOwnRecords()
    {
        var (_, personnelId) = await CreatePersonnelWithHealthReportAsync();
        var (_, otherPersonnelId) = await CreatePersonnelWithHealthReportAsync();

        // Kendi kaydına bağlı, İSG yetkisi OLMAYAN bir kullanıcı.
        var client = await CreateClientForRoleAsync("Şantiye Şefi", personnelId);

        var card = await client.GetFromJsonAsync<JsonElement>("/api/isg/benim");

        Assert.Equal(personnelId, card.GetProperty("personnelId").GetGuid());
        Assert.NotEqual(otherPersonnelId, card.GetProperty("personnelId").GetGuid());
        Assert.Single(card.GetProperty("healthReports").EnumerateArray());

        // Kişi kendi kısıtlamasını görür — kendisini ilgilendirir.
        var report = card.GetProperty("healthReports").EnumerateArray().Single();
        Assert.Equal("Yüksekte çalışamaz", report.GetProperty("restrictions").GetString());
    }

    [Fact]
    public async Task OwnCard_WithoutPersonnelLink_ExplainsInsteadOfGuessing()
    {
        await CreatePersonnelWithHealthReportAsync();

        // Personel bağı olmayan kullanıcı: "en yakın personel" tahmin
        // edilmez, açık mesajla 404 döner.
        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.GetAsync("/api/isg/benim");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("personel kartına bağlı değil",
            payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public void IsgHealthView_IsNotGrantedToFieldRoles()
    {
        // Katalog seviyesinde de doğrula: saha rolleri bu izni hiç almasın.
        string[] fieldRoles =
        [
            "Şantiye Şefi", "Formen", "Sekreterya", "Teknik Ofis", "Teknik Koordinatör"
        ];

        foreach (var roleName in fieldRoles)
        {
            var role = RoleCatalog.Roles.Single(x => x.Name == roleName);

            Assert.DoesNotContain(
                PermissionCatalog.Keys.IsgHealthView, role.PermissionKeys);
        }
    }
}
