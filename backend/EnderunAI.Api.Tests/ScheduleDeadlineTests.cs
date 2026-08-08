using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Termin, gecikme cezası ve iş programı uyarıları (G4).
///
/// Korunan iş kuralları:
/// - Sözleşme termini, planlanan bitişten AYRI bir alandır. Planlanan
///   bitiş bizim takvimimizdir ve program düzenlendikçe kayar; termin
///   sözleşmede yazar ve kaymaz. Tek alanda tutulsalardı planı öteleyen
///   her düzenleme ceza hesabını da sessizce sıfırlardı.
/// - Cezası tanımsız sözleşmede ceza HESAPLANMAZ. Sıfır TL göstermek
///   "ceza yok" demektir; hesaplanamadığını söylemek başka şeydir.
/// - Ceza TUTARI, iş programını okuma yetkisiyle görünmez. schedule.view
///   neredeyse her rolde var; ceza tutarından sözleşme bedeli geri
///   hesaplanabilir.
/// </summary>
[Collection("Integration")]
public sealed class ScheduleDeadlineTests(DatabaseFixture fixture)
{
    private const decimal Contract = 10_000_000m;

    private static readonly DateOnly LateStart = new(2026, 1, 5);
    private static readonly DateOnly LateEnd = new(2026, 1, 10);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>
    /// Süresi geçmiş, hiç ilerleme girilmemiş tek çubuklu bir program.
    /// Gecikme kaçınılmaz.
    /// </summary>
    private async Task<Guid> CreateLateProjectAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            project.ContractAmount = Contract;
            project.PlannedStartDate =
                new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            project.PlannedEndDate =
                new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

            await db.SaveChangesAsync();

            var client = await ClientAsync();

            var created = await client.PostAsJsonAsync(
                $"/api/projects/{project.Id}/is-programi",
                new { seedFromSections = false });

            created.EnsureSuccessStatusCode();

            var scheduleId = (await created.Content
                .ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var activity = await client.PostAsJsonAsync(
                $"/api/is-programi/{scheduleId}/aktiviteler",
                new
                {
                    name = "Busbar montajı",
                    plannedStartDate = LateStart,
                    plannedEndDate = LateEnd
                });

            activity.EnsureSuccessStatusCode();

            return project.Id;
        }
    }

    private async Task SetPenaltyAsync(
        HttpClient client,
        Guid projectId,
        DelayPenaltyKind kind,
        decimal value,
        decimal? capRate = null,
        DateTime? deadline = null)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/termin",
            new
            {
                contractDeadlineDate = deadline
                    ?? new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                delayPenaltyKind = (int)kind,
                delayPenaltyValue = value,
                delayPenaltyCapRate = capRate
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> PenaltyAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync(
            $"/api/projects/{projectId}/gecikme-cezasi");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------------- Termin ----------------

    /// <summary>
    /// Sözleşme termini girilince o esas alınır; planlanan bitiş değil.
    /// </summary>
    [Fact]
    public async Task ContractDeadline_OverridesThePlannedEndDate()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        await SetPenaltyAsync(
            client, projectId, DelayPenaltyKind.None, 0m,
            deadline: new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync(
            $"/api/projects/{projectId}/is-programi");

        var schedule = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("schedule");

        Assert.True(schedule.GetProperty("hasContractDeadline").GetBoolean());
        Assert.Equal("2026-02-20", schedule.GetProperty("deadline").GetString());
    }

    [Fact]
    public async Task DeadlineBeforeProjectStart_IsRejected()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/termin",
            new
            {
                contractDeadlineDate =
                    new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                delayPenaltyKind = 0,
                delayPenaltyValue = 0m,
                delayPenaltyCapRate = (decimal?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PenaltyKindWithoutAValue_IsRejected()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/termin",
            new
            {
                contractDeadlineDate =
                    new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                delayPenaltyKind = (int)DelayPenaltyKind.RateOfContractPerDay,
                delayPenaltyValue = 0m,
                delayPenaltyCapRate = (decimal?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CapRateAboveHundred_IsRejected()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/termin",
            new
            {
                contractDeadlineDate =
                    new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                delayPenaltyKind = (int)DelayPenaltyKind.RateOfContractPerDay,
                delayPenaltyValue = 0.1m,
                delayPenaltyCapRate = 150m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Ceza hesabı ----------------

    /// <summary>
    /// Binde 1 günlük ceza, 10 milyonluk sözleşmede günde 10.000 TL.
    /// </summary>
    [Fact]
    public async Task RatePenalty_ProducesADailyAmountAndAnEstimate()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        await SetPenaltyAsync(
            client, projectId, DelayPenaltyKind.RateOfContractPerDay, 0.1m);

        var payload = await PenaltyAsync(client, projectId);
        var penalty = payload.GetProperty("penalty");

        Assert.True(payload.GetProperty("delayCalendarDays").GetInt32() > 0);
        Assert.True(penalty.GetProperty("applicable").GetBoolean());
        Assert.Equal(10_000m, penalty.GetProperty("dailyAmount").GetDecimal());
        Assert.True(penalty.GetProperty("amount").GetDecimal() > 0m);
    }

    /// <summary>Tavan varsa hesap orada durur.</summary>
    [Fact]
    public async Task CapStopsTheEstimate()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        await SetPenaltyAsync(
            client, projectId, DelayPenaltyKind.RateOfContractPerDay,
            value: 1m, capRate: 10m);

        var penalty = (await PenaltyAsync(client, projectId))
            .GetProperty("penalty");

        Assert.True(penalty.GetProperty("capApplied").GetBoolean());
        Assert.Equal(1_000_000m, penalty.GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// Cezası tanımsız sözleşmede hesaplanmaz — sıfır göstermek "ceza
    /// yok" demek olurdu.
    /// </summary>
    [Fact]
    public async Task UndefinedPenalty_IsNotCalculated()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        var penalty = (await PenaltyAsync(client, projectId))
            .GetProperty("penalty");

        Assert.False(penalty.GetProperty("applicable").GetBoolean());
        Assert.Equal(0m, penalty.GetProperty("amount").GetDecimal());
        Assert.Contains("tanımlı değil",
            penalty.GetProperty("note").GetString()!);
    }

    // ---------------- Uyarılar ----------------

    [Fact]
    public async Task LateProject_AppearsInTheAlerts()
    {
        var projectId = await CreateLateProjectAsync();
        var client = await ClientAsync();

        await SetPenaltyAsync(
            client, projectId, DelayPenaltyKind.RateOfContractPerDay, 0.1m);

        var response = await client.GetAsync(
            $"/api/is-programi/uyarilar?projectId={projectId}");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var alert = payload.GetProperty("items").EnumerateArray().Single();

        Assert.True(payload.GetProperty("showsPenalty").GetBoolean());
        Assert.True(alert.GetProperty("deadlineAtRisk").GetBoolean());
        Assert.True(alert.GetProperty("delayWorkDays").GetInt32() > 0);
        Assert.True(alert.GetProperty("penalty")
            .GetProperty("amount").GetDecimal() > 0m);
    }

    /// <summary>
    /// Geleceğe planlanmış, terminine daha çok olan proje uyarı
    /// üretmez — her projeyi listeleyen bir uyarı ekranı okunmaz.
    /// </summary>
    [Fact]
    public async Task HealthyProject_ProducesNoAlert()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid projectId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            project.PlannedStartDate = DateTime.UtcNow.Date.AddDays(60);
            project.PlannedEndDate = DateTime.UtcNow.Date.AddDays(200);

            await db.SaveChangesAsync();
            projectId = project.Id;
        }

        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/is-programi",
            new { seedFromSections = false });

        var scheduleId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "Gelecekteki iş",
                plannedStartDate = start,
                plannedEndDate = start.AddDays(30)
            });

        var response = await client.GetAsync(
            $"/api/is-programi/uyarilar?projectId={projectId}");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(payload.GetProperty("items").EnumerateArray());
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// İş programını okuma yetkisi, TUTAR görme yetkisi değildir:
    /// uyarılar görünür ama ceza tutarı boş gelir ve ekran bunu
    /// showsPenalty ile bilir.
    /// </summary>
    [Fact]
    public async Task ScheduleViewerWithoutHakedisView_SeesNoPenaltyAmount()
    {
        var projectId = await CreateLateProjectAsync();
        var admin = await ClientAsync();

        await SetPenaltyAsync(
            admin, projectId, DelayPenaltyKind.RateOfContractPerDay, 0.1m);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(
            $"/api/is-programi/uyarilar?projectId={projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var alert = payload.GetProperty("items").EnumerateArray().Single();

        Assert.False(payload.GetProperty("showsPenalty").GetBoolean());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("penalty").ValueKind);
        Assert.True(alert.GetProperty("delayWorkDays").GetInt32() > 0);
    }

    /// <summary>Ceza ucu ayrı bir kapıda: hakediş görüntüleme.</summary>
    [Fact]
    public async Task PenaltyEndpoint_RequiresHakedisView()
    {
        var projectId = await CreateLateProjectAsync();

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(
            $"/api/projects/{projectId}/gecikme-cezasi");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Termin ve ceza ayarı yalnızca düzenleme yetkisiyle.</summary>
    [Fact]
    public async Task DeadlineEndpoint_RequiresScheduleManage()
    {
        var projectId = await CreateLateProjectAsync();

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.HakedisView);

        var response = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/termin",
            new
            {
                contractDeadlineDate =
                    new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                delayPenaltyKind = 0,
                delayPenaltyValue = 0m,
                delayPenaltyCapRate = (decimal?)null
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientWithPermissionsAsync(
        params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string username;
        const string password = "TestDeadline!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider
                .GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestDeadline-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            username = $"deadline-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Test Termin Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
