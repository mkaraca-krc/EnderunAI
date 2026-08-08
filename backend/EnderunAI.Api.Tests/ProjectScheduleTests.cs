using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Schedule;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İş programı: veri modeli, CRUD ve yetki kapıları (G2).
///
/// Bu testlerin koruduğu iş kuralları:
/// - İş programı AYRI BİR İŞ KALEMİ LİSTESİ DEĞİLDİR. Ana çubuklar
///   icmal kısımlarından doğar ve aynı kısmın iki çubuğu olamaz;
///   olsaydı ilerleme iki kez sayılmış gibi görünürdü.
/// - Döngüsel bağımlılık VERİTABANINA HİÇ GİRMEZ. Giren bir döngü
///   bütün programı hesaplanamaz yapardı.
/// - Baseline değiştirilebilir ama iz bırakır ve gerekçesiz
///   değiştirilemez: referans tarih değişince gecikme ölçüsü de değişir.
/// - Düzenleme yetkisi dar, okuma geniş.
/// </summary>
[Collection("Integration")]
public sealed class ProjectScheduleTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid ProjectId, string Suffix);

    private static readonly DateOnly Mon02 = new(2026, 3, 2);
    private static readonly DateOnly Wed04 = new(2026, 3, 4);
    private static readonly DateOnly Sat07 = new(2026, 3, 7);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync(int sectionCount = 3)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        project.PlannedStartDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        project.PlannedEndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < sectionCount; index++)
        {
            db.ProjectHakedisSections.Add(new ProjectHakedisSection
            {
                ProjectId = project.Id,
                Order = index + 1,
                Name = $"Kısım {index + 1}",
                IsActive = true
            });
        }

        await db.SaveChangesAsync();

        return new Context(project.Id, suffix);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> CreateScheduleAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/is-programi",
            new { seedFromSections = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ViewAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/is-programi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await JsonAsync(response);
    }

    private static JsonElement Activity(JsonElement view, string name) =>
        view.GetProperty("schedule").GetProperty("activities")
            .EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == name);

    private async Task<Guid> AddActivityAsync(
        HttpClient client,
        Guid scheduleId,
        string name,
        DateOnly start,
        DateOnly end,
        Guid? parentId = null,
        decimal? manualProgress = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name,
                plannedStartDate = start,
                plannedEndDate = end,
                parentActivityId = parentId,
                manualProgressRate = manualProgress
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    // ---------------- Program açma ----------------

    /// <summary>
    /// Programı olmayan proje 404 vermez: ekranın kullanıcıyı
    /// yönlendirebilmesi için "yok" bilgisi ve kısım sayısı döner.
    /// </summary>
    [Fact]
    public async Task MissingSchedule_ReportsAbsenceInsteadOfNotFound()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var view = await ViewAsync(client, context.ProjectId);

        Assert.False(view.GetProperty("hasSchedule").GetBoolean());
        Assert.Equal(3, view.GetProperty("sectionCount").GetInt32());
    }

    /// <summary>
    /// Kısımsız projede mesaj, iş programının kısımlardan doğduğunu
    /// söylemeli — boş ekran, nedenini söylemeyen bir ekrandan iyidir.
    /// </summary>
    [Fact]
    public async Task ProjectWithoutSections_ExplainsWhereTheBarsComeFrom()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var view = await ViewAsync(client, context.ProjectId);

        Assert.Equal(0, view.GetProperty("sectionCount").GetInt32());
        Assert.Contains(
            "kısım", view.GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatingSchedule_SeedsOneBarPerSection()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CreateScheduleAsync(client, context.ProjectId);

        var view = await ViewAsync(client, context.ProjectId);
        var activities = view.GetProperty("schedule").GetProperty("activities");

        Assert.True(view.GetProperty("hasSchedule").GetBoolean());
        Assert.Equal(3, activities.GetArrayLength());
        Assert.All(activities.EnumerateArray(), x =>
            Assert.NotEqual(Guid.Empty, x.GetProperty("sectionId").GetGuid()));
    }

    [Fact]
    public async Task SecondScheduleForTheSameProject_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CreateScheduleAsync(client, context.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{context.ProjectId}/is-programi",
            new { seedFromSections = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Zaten çubuğu olan kısım ikinci kez eklenmez.</summary>
    [Fact]
    public async Task SeedingAgain_DoesNotDuplicateExistingSections()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/kisimlardan-olustur", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await JsonAsync(response))
            .GetProperty("addedActivityCount").GetInt32());

        var view = await ViewAsync(client, context.ProjectId);

        Assert.Equal(3, view.GetProperty("schedule")
            .GetProperty("activities").GetArrayLength());
    }

    [Fact]
    public async Task SameSectionTwice_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);
        var view = await ViewAsync(client, context.ProjectId);

        var sectionId = view.GetProperty("schedule").GetProperty("activities")
            .EnumerateArray().First().GetProperty("sectionId").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "İkinci çubuk",
                plannedStartDate = Mon02,
                plannedEndDate = Sat07,
                projectHakedisSectionId = sectionId
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("zaten bir çubuk",
            (await JsonAsync(response)).GetProperty("message").GetString()!);
    }

    // ---------------- Aktivite hiyerarşisi ----------------

    [Fact]
    public async Task SubActivity_IsListedUnderItsParent()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var parentId = await AddActivityAsync(
            client, scheduleId, "Panolar", Mon02, Sat07);

        await AddActivityAsync(
            client, scheduleId, "Kablo çekimi", Mon02, Wed04, parentId);

        var view = await ViewAsync(client, context.ProjectId);
        var activities = view.GetProperty("schedule").GetProperty("activities")
            .EnumerateArray().ToList();

        Assert.Equal(2, activities.Count);
        Assert.Equal("Panolar", activities[0].GetProperty("name").GetString());
        Assert.Equal("Kablo çekimi", activities[1].GetProperty("name").GetString());
        Assert.Equal(parentId, activities[1].GetProperty("parentActivityId").GetGuid());
    }

    /// <summary>
    /// İki seviye yeter. Derinleşen ağaç Gantt'ı okunmaz yapar ve
    /// ilerleme toplamayı belirsizleştirir.
    /// </summary>
    [Fact]
    public async Task ThirdLevel_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var parentId = await AddActivityAsync(
            client, scheduleId, "Panolar", Mon02, Sat07);

        var childId = await AddActivityAsync(
            client, scheduleId, "Kablo çekimi", Mon02, Wed04, parentId);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "Torklama",
                plannedStartDate = Mon02,
                plannedEndDate = Wed04,
                parentActivityId = childId
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// İcmale bağlı çubukta gerçekleşme saha raporundan gelir; elle
    /// girilen bir yüzde onu sessizce ezerdi.
    /// </summary>
    [Fact]
    public async Task ManualProgressOnASectionLinkedBar_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);
        var view = await ViewAsync(client, context.ProjectId);

        var activity = view.GetProperty("schedule").GetProperty("activities")
            .EnumerateArray().First();

        var response = await client.PutAsJsonAsync(
            $"/api/is-programi/aktiviteler/{activity.GetProperty("id").GetGuid()}",
            new
            {
                name = activity.GetProperty("name").GetString(),
                plannedStartDate = Mon02,
                plannedEndDate = Sat07,
                projectHakedisSectionId = activity.GetProperty("sectionId").GetGuid(),
                manualProgressRate = 40m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("saha raporundan",
            (await JsonAsync(response)).GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ManualProgressOnAnUnlinkedBar_IsAccepted()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        await AddActivityAsync(
            client, scheduleId, "Serbest iş", Mon02, Sat07, manualProgress: 40m);

        var view = await ViewAsync(client, context.ProjectId);

        Assert.Equal(40m, Activity(view, "Serbest iş")
            .GetProperty("manualProgressRate").GetDecimal());
    }

    [Fact]
    public async Task EndBeforeStart_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "Ters kayıt",
                plannedStartDate = Sat07,
                plannedEndDate = Mon02
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Bağımlılık ----------------

    /// <summary>
    /// Uçtan uca: bağ kurulunca ardılın tarihi gerçekten kayıyor ve
    /// kaydığı ekranda görünüyor.
    /// </summary>
    [Fact]
    public async Task Dependency_ShiftsTheSuccessorDates()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var first = await AddActivityAsync(
            client, scheduleId, "Kablo tavası", Mon02, Wed04);

        var second = await AddActivityAsync(
            client, scheduleId, "Kablo çekimi", Mon02, Wed04);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar",
            new
            {
                predecessorActivityId = first,
                successorActivityId = second,
                type = (int)ScheduleDependencyType.FinishToStart,
                lagWorkDays = 0
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var view = await ViewAsync(client, context.ProjectId);
        var successor = Activity(view, "Kablo çekimi");

        Assert.Equal("2026-03-05",
            successor.GetProperty("plannedStart").GetString());
        Assert.Equal("2026-03-07",
            successor.GetProperty("plannedEnd").GetString());
        Assert.Equal(3, successor.GetProperty("shiftedWorkDays").GetInt32());
    }

    /// <summary>
    /// Döngü VERİTABANINA HİÇ GİRMEZ: reddedilir ve bağ sayısı
    /// değişmez.
    /// </summary>
    [Fact]
    public async Task CyclicDependency_IsRejectedAndNotPersisted()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var a = await AddActivityAsync(client, scheduleId, "A", Mon02, Wed04);
        var b = await AddActivityAsync(client, scheduleId, "B", Mon02, Wed04);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar",
            new
            {
                predecessorActivityId = a,
                successorActivityId = b,
                type = 0,
                lagWorkDays = 0
            });

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar",
            new
            {
                predecessorActivityId = b,
                successorActivityId = a,
                type = 0,
                lagWorkDays = 0
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Döngüsel",
            (await JsonAsync(response)).GetProperty("message").GetString()!);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(1, await db.ScheduleDependencies
            .CountAsync(x => x.ProjectScheduleId == scheduleId));
    }

    [Fact]
    public async Task DuplicateDependency_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var a = await AddActivityAsync(client, scheduleId, "A", Mon02, Wed04);
        var b = await AddActivityAsync(client, scheduleId, "B", Mon02, Wed04);

        var body = new
        {
            predecessorActivityId = a,
            successorActivityId = b,
            type = 0,
            lagWorkDays = 0
        };

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar", body);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Aktivite silinince bağları da gider; artık kalan bağ olmaz.</summary>
    [Fact]
    public async Task DeletingAnActivity_RemovesItsDependencies()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var a = await AddActivityAsync(client, scheduleId, "A", Mon02, Wed04);
        var b = await AddActivityAsync(client, scheduleId, "B", Mon02, Wed04);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar",
            new
            {
                predecessorActivityId = a,
                successorActivityId = b,
                type = 0,
                lagWorkDays = 0
            });

        var response = await client.DeleteAsync($"/api/is-programi/aktiviteler/{a}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.ScheduleDependencies
            .CountAsync(x => x.ProjectScheduleId == scheduleId));
    }

    [Fact]
    public async Task DeletingAParentWithChildren_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var parentId = await AddActivityAsync(
            client, scheduleId, "Panolar", Mon02, Sat07);

        await AddActivityAsync(
            client, scheduleId, "Montaj", Mon02, Wed04, parentId);

        var response = await client.DeleteAsync(
            $"/api/is-programi/aktiviteler/{parentId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Baseline ----------------

    [Fact]
    public async Task Baseline_LocksTheComputedDates()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        await AddActivityAsync(client, scheduleId, "Panolar", Mon02, Sat07);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var view = await ViewAsync(client, context.ProjectId);
        var activity = Activity(view, "Panolar");

        Assert.Equal("2026-03-02", activity.GetProperty("baselineStart").GetString());
        Assert.Equal("2026-03-07", activity.GetProperty("baselineEnd").GetString());
        Assert.Equal(0, activity.GetProperty("baselineSlipWorkDays").GetInt32());
        Assert.Equal(1, view.GetProperty("schedule")
            .GetProperty("baselineRevisionNumber").GetInt32());
    }

    /// <summary>
    /// Baseline sabittir: plan sonradan kaydığında baseline oynamaz ve
    /// sapma görünür olur.
    /// </summary>
    [Fact]
    public async Task MovingThePlanAfterBaseline_ShowsTheSlip()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);
        var activityId = await AddActivityAsync(
            client, scheduleId, "Panolar", Mon02, Sat07);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = (string?)null });

        await client.PutAsJsonAsync(
            $"/api/is-programi/aktiviteler/{activityId}",
            new
            {
                name = "Panolar",
                plannedStartDate = new DateOnly(2026, 3, 9),
                plannedEndDate = new DateOnly(2026, 3, 14)
            });

        var view = await ViewAsync(client, context.ProjectId);
        var activity = Activity(view, "Panolar");

        Assert.Equal("2026-03-07", activity.GetProperty("baselineEnd").GetString());
        Assert.Equal(6, activity.GetProperty("baselineSlipWorkDays").GetInt32());
    }

    /// <summary>
    /// İkinci baseline gerekçesiz kaydedilemez: referans tarih
    /// değişince gecikme ölçüsü de değişir.
    /// </summary>
    [Fact]
    public async Task SecondBaselineWithoutAReason_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);
        await AddActivityAsync(client, scheduleId, "Panolar", Mon02, Sat07);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = (string?)null });

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("gerekçe",
            (await JsonAsync(response)).GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BaselineRevisions_AreLogged()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);
        await AddActivityAsync(client, scheduleId, "Panolar", Mon02, Sat07);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = (string?)null });

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline",
            new { reason = "İşveren süre uzatımı verdi." });

        var response = await client.GetAsync(
            $"/api/is-programi/{scheduleId}/baseline-gecmisi");

        var history = (await JsonAsync(response)).EnumerateArray().ToList();

        Assert.Equal(2, history.Count);
        Assert.Equal(2, history[0].GetProperty("revisionNumber").GetInt32());
        Assert.Equal("İşveren süre uzatımı verdi.",
            history[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task BaselineOnAnEmptySchedule_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/baseline", new { reason = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Takvim ve tatil ----------------

    /// <summary>
    /// Tatil süreyi UZATIR: aynı süre, araya giren tatille daha geç
    /// biter.
    /// </summary>
    [Fact]
    public async Task Holiday_PushesTheFinishDate()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var first = await AddActivityAsync(client, scheduleId, "A", Mon02, Wed04);

        await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/bagimliliklar",
            new
            {
                predecessorActivityId = first,
                successorActivityId = await AddActivityAsync(
                    client, scheduleId, "B", Mon02, Mon02),
                type = 0,
                lagWorkDays = 0
            });

        var before = Activity(await ViewAsync(client, context.ProjectId), "B");
        Assert.Equal("2026-03-05", before.GetProperty("plannedStart").GetString());

        var response = await client.PutAsJsonAsync(
            $"/api/is-programi/{scheduleId}/tatiller",
            new
            {
                holidays = new[]
                {
                    new { date = new DateOnly(2026, 3, 5), name = "Şantiye kapalı" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = Activity(await ViewAsync(client, context.ProjectId), "B");
        Assert.Equal("2026-03-06", after.GetProperty("plannedStart").GetString());
    }

    [Fact]
    public async Task WorkWeekWithoutAnyDay_IsRejected()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        var response = await client.PutAsJsonAsync(
            $"/api/is-programi/{scheduleId}", new { workWeek = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Takvim günü modunda pazar da çalışma günüdür.</summary>
    [Fact]
    public async Task CalendarDayMode_CountsSundays()
    {
        var context = await CreateContextAsync(sectionCount: 0);
        var client = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(client, context.ProjectId);

        await client.PutAsJsonAsync(
            $"/api/is-programi/{scheduleId}",
            new { workWeek = (int)WorkWeekDays.AllDays });

        await AddActivityAsync(
            client, scheduleId, "A", Mon02, new DateOnly(2026, 3, 8));

        var view = await ViewAsync(client, context.ProjectId);

        Assert.Equal(7, Activity(view, "A")
            .GetProperty("durationWorkDays").GetInt32());
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// Okuma geniş, düzenleme dar: yalnızca schedule.view olan kullanıcı
    /// programı görür ama tek bir çubuk bile ekleyemez.
    /// </summary>
    [Fact]
    public async Task ViewOnlyUser_CanReadButNotEdit()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();

        var scheduleId = await CreateScheduleAsync(admin, context.ProjectId);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.ProjectsView);

        var read = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/is-programi");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "İzinsiz çubuk",
                plannedStartDate = Mon02,
                plannedEndDate = Sat07
            });

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task UserWithoutScheduleView_CannotRead()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();

        await CreateScheduleAsync(admin, context.ProjectId);

        var client = await CreateClientWithPermissionsAsync(
            PermissionCatalog.Keys.ProjectsView);

        var read = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/is-programi");

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    private async Task<HttpClient> CreateClientWithPermissionsAsync(
        params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string username;
        const string password = "TestSchedule!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider
                .GetRequiredService<EnderunAI.Api.Security.PasswordService>();

            var role = new AppRole { Name = $"TestSchedule-{suffix}" };
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

            username = $"sched-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Test Program Kullanıcısı",
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
