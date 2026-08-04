using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kaza ve ramak kala kayıt defteri: yasal alanlar, SGK bildirim
/// takibi ve dar izin.
/// </summary>
[Collection("Integration")]
public sealed class IsgIncidentTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid PersonnelId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        return new Context(project.CompanyId, project.Id, personnel.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "IsgKaza!2026";
        var username = $"test-kaza-{Guid.NewGuid():N}"[..40];
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

    private static object BuildPayload(
        Context context,
        int incidentType = 0,
        int severity = 3,
        bool sgkNotified = false,
        DateTime? sgkNotificationDate = null,
        DateTime? incidentDate = null,
        int lostWorkDays = 2) => new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            personnelId = context.PersonnelId,
            incidentDateTime = incidentDate ?? DateTime.UtcNow.AddDays(-1),
            incidentType,
            severity,
            description = "İskeleden düşme sonucu ayak bileği burkulması.",
            rootCause = "Korkuluk eksikliği",
            actionTaken = "Korkuluk tamamlandı, ekip bilgilendirildi",
            lostWorkDays,
            sgkNotified,
            sgkNotificationDate,
            sgkNotificationNumber = sgkNotified ? "SGK-2026-001" : null
        };

    [Fact]
    public async Task Create_StoresLegalFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var response = await client.PostAsJsonAsync(
            "/api/isg/kazalar", BuildPayload(context));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("İş kazası", payload.GetProperty("incidentTypeName").GetString());
        Assert.Equal("İş günü kaybı", payload.GetProperty("severityName").GetString());
        Assert.Equal("Açık", payload.GetProperty("statusName").GetString());
        Assert.Equal(2, payload.GetProperty("lostWorkDays").GetInt32());
    }

    [Fact]
    public async Task NearMiss_WithoutPersonnel_IsAllowed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        // Ramak kalada kimse yaralanmaz; personel zorunlu olsaydı kayıt
        // hiç girilmezdi.
        var response = await client.PostAsJsonAsync("/api/isg/kazalar", new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            personnelId = (Guid?)null,
            incidentDateTime = DateTime.UtcNow.AddDays(-1),
            incidentType = 1,
            severity = 0,
            description = "Yükseklikten malzeme düştü, kimse yoktu.",
            rootCause = (string?)null,
            actionTaken = (string?)null,
            lostWorkDays = 0,
            sgkNotified = false,
            sgkNotificationDate = (DateTime?)null,
            sgkNotificationNumber = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ramak kala", payload.GetProperty("incidentTypeName").GetString());
        // Ramak kala SGK bildirim kuralına girmez.
        Assert.False(payload.GetProperty("sgkNotificationOverdue").GetBoolean());
    }

    [Fact]
    public async Task OldAccidentWithoutNotification_IsFlaggedOverdue()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        // 10 gün önceki kaza, bildirilmemiş → yasal süre (3 gün) geçti.
        var response = await client.PostAsJsonAsync("/api/isg/kazalar",
            BuildPayload(context, incidentDate: DateTime.UtcNow.AddDays(-10)));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("sgkNotificationOverdue").GetBoolean());
    }

    [Fact]
    public async Task NotifiedAccident_IsNotFlaggedOverdue()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var response = await client.PostAsJsonAsync("/api/isg/kazalar",
            BuildPayload(
                context,
                incidentDate: DateTime.UtcNow.AddDays(-10),
                sgkNotified: true,
                sgkNotificationDate: DateTime.UtcNow.AddDays(-9)));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(payload.GetProperty("sgkNotificationOverdue").GetBoolean());
    }

    [Fact]
    public async Task NotifiedWithoutDate_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        // "Bildirildi" işaretlenip tarih girilmemesi, denetimde
        // ispatlanamayan bir beyan olurdu.
        var response = await client.PostAsJsonAsync("/api/isg/kazalar",
            BuildPayload(context, sgkNotified: true, sgkNotificationDate: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClosingWithoutNote_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var create = await client.PostAsJsonAsync(
            "/api/isg/kazalar", BuildPayload(context));
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/isg/kazalar/{id}", new
        {
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            personnelId = context.PersonnelId,
            incidentDateTime = DateTime.UtcNow.AddDays(-1),
            incidentType = 0,
            severity = 3,
            description = "İskeleden düşme.",
            rootCause = (string?)null,
            actionTaken = (string?)null,
            lostWorkDays = 2,
            sgkNotified = false,
            sgkNotificationDate = (DateTime?)null,
            sgkNotificationNumber = (string?)null,
            status = 2,
            closureNote = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClosingWithNote_SetsClosedTimestamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var create = await client.PostAsJsonAsync(
            "/api/isg/kazalar", BuildPayload(context));
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/isg/kazalar/{id}", new
        {
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            personnelId = context.PersonnelId,
            incidentDateTime = DateTime.UtcNow.AddDays(-1),
            incidentType = 0,
            severity = 3,
            description = "İskeleden düşme.",
            rootCause = "Korkuluk eksikliği",
            actionTaken = "Korkuluk tamamlandı",
            lostWorkDays = 2,
            sgkNotified = true,
            sgkNotificationDate = DateTime.UtcNow.AddDays(-1),
            sgkNotificationNumber = "SGK-2026-001",
            status = 2,
            closureNote = "Önlem alındı, dosya kapatıldı."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Kapalı", payload.GetProperty("statusName").GetString());
        Assert.NotEqual(JsonValueKind.Null, payload.GetProperty("closedAtUtc").ValueKind);
    }

    [Fact]
    public async Task TeknikKoordinator_CannotSeeIncidentRegister()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // İSG kaydı girebiliyor ama kaza defterini göremiyor.
        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var response = await client.GetAsync(
            $"/api/isg/kazalar?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FatalIncident_UsesRedSeverityColor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("İSG Sorumlusu");

        var response = await client.PostAsJsonAsync("/api/isg/kazalar",
            BuildPayload(context, severity: 5));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Ölümlü", payload.GetProperty("severityName").GetString());
        Assert.Equal("red", payload.GetProperty("severityColor").GetString());
    }

    [Fact]
    public void NotificationOverdue_IsOnlyForRealAccidents()
    {
        // Saf kural kontrolü: ramak kala ve meslek hastalığı SGK
        // bildirim kuralına girmez.
        var old = DateTime.UtcNow.AddDays(-30);

        Assert.True(IsgIncidentService.IsNotificationOverdue(new IsgIncident
        {
            IncidentType = IsgIncidentType.Accident,
            IncidentDateTime = old,
            SgkNotified = false
        }));

        Assert.False(IsgIncidentService.IsNotificationOverdue(new IsgIncident
        {
            IncidentType = IsgIncidentType.NearMiss,
            IncidentDateTime = old,
            SgkNotified = false
        }));

        Assert.False(IsgIncidentService.IsNotificationOverdue(new IsgIncident
        {
            IncidentType = IsgIncidentType.OccupationalIllness,
            IncidentDateTime = old,
            SgkNotified = false
        }));

        // Süre henüz dolmadıysa gecikme yok.
        Assert.False(IsgIncidentService.IsNotificationOverdue(new IsgIncident
        {
            IncidentType = IsgIncidentType.Accident,
            IncidentDateTime = DateTime.UtcNow.AddDays(-1),
            SgkNotified = false
        }));
    }
}
