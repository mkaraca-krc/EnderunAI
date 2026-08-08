using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İş programı kaynak ataması ve çakışma uyarısı (G5).
///
/// Korunan iş kuralları:
/// - Ayrı bir "ekip" kavramı YOK: taşeron zaten taşeron sözleşmesi,
///   personel zaten personeldir. Üçüncü bir kavram aynı kişiyi iki
///   yerde tutmayı gerektirirdi.
/// - Çakışma HATA DEĞİL uyarıdır; bir ustabaşı gerçekten iki işi
///   birden yürütebilir. Engellenmez, görünür kılınır — özellikle iki
///   aktivite de kritik yoldaysa.
/// - Taşeron önerisi mevcut sözleşme–kısım bağından gelir; "hangi kısım
///   hangi taşeronda" bilgisi sistemde zaten vardı, iş programı onu
///   tekrar sormaz.
/// </summary>
[Collection("Integration")]
public sealed class ScheduleResourceTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid ProjectId,
        Guid ScheduleId,
        Guid SectionId,
        Guid PersonnelId,
        Guid OtherPersonnelId,
        Guid ContractId);

    private static readonly DateOnly Mon02 = new(2026, 3, 2);
    private static readonly DateOnly Wed04 = new(2026, 3, 4);
    private static readonly DateOnly Thu05 = new(2026, 3, 5);
    private static readonly DateOnly Sat07 = new(2026, 3, 7);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid projectId, sectionId, personnelId, otherPersonnelId, contractId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            project.PlannedStartDate =
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

            var section = new ProjectHakedisSection
            {
                ProjectId = project.Id,
                Order = 1,
                Name = "Panolar",
                IsActive = true
            };

            db.ProjectHakedisSections.Add(section);

            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, project.CompanyId, suffix);

            var other = await TestDataFactory.CreatePersonnelAsync(
                db, project.CompanyId, $"{suffix}b");

            // Taşeron: cariye taşeron rolü verilip sözleşme açılıyor.
            var account = new CurrentAccount
            {
                CompanyId = project.CompanyId,
                Code = $"TSR-{suffix}",
                Title = $"X Taşeron {suffix}",
                Roles = CurrentAccountRoles.Subcontractor
            };

            db.CurrentAccounts.Add(account);
            await db.SaveChangesAsync();

            var contract = new SubcontractorContract
            {
                CompanyId = project.CompanyId,
                CurrentAccountId = account.Id,
                ProjectId = project.Id,
                ContractNumber = $"TS-{suffix}",
                WorkDescription = "Pano montajı",
                ContractType = ProjectContractType.UnitPrice,
                ContractAmount = 250_000m,
                CurrencyCode = "TRY",
                StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = SubcontractorContractStatus.Active
            };

            db.SubcontractorContracts.Add(contract);
            await db.SaveChangesAsync();

            db.Set<SubcontractorContractSection>().Add(
                new SubcontractorContractSection
                {
                    SubcontractorContractId = contract.Id,
                    ProjectHakedisSectionId = section.Id,
                    SectionAmount = 250_000m,
                    Order = 1
                });

            await db.SaveChangesAsync();

            projectId = project.Id;
            sectionId = section.Id;
            personnelId = personnel.Id;
            otherPersonnelId = other.Id;
            contractId = contract.Id;
        }

        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/is-programi",
            new { seedFromSections = true });

        created.EnsureSuccessStatusCode();

        var scheduleId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        return new Context(
            projectId, scheduleId, sectionId,
            personnelId, otherPersonnelId, contractId);
    }

    private async Task<JsonElement> ScheduleAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/is-programi");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("schedule");
    }

    private static JsonElement Bar(JsonElement schedule, string name) =>
        schedule.GetProperty("activities").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == name);

    private async Task<Guid> AddActivityAsync(
        HttpClient client, Guid scheduleId, string name,
        DateOnly start, DateOnly end)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new { name, plannedStartDate = start, plannedEndDate = end });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> AssignPersonnelAsync(
        HttpClient client, Guid activityId, Guid personnelId, string? role = null) =>
        await client.PostAsJsonAsync(
            $"/api/is-programi/aktiviteler/{activityId}/kaynaklar",
            new
            {
                kind = (int)ScheduleResourceKind.Personnel,
                personnelId,
                role
            });

    // ---------------- Atama ----------------

    [Fact]
    public async Task AssignedPersonnel_AppearsOnTheBar()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var response = await AssignPersonnelAsync(
            client, barId, context.PersonnelId, "Ekip şefi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var resources = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("resources").EnumerateArray().ToList();

        var resource = Assert.Single(resources);

        Assert.Equal((int)ScheduleResourceKind.Personnel,
            resource.GetProperty("kind").GetInt32());
        Assert.Equal("Personel", resource.GetProperty("kindName").GetString());
        Assert.Equal("Ekip şefi", resource.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AssignedSubcontractor_ShowsTheAccountTitle()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/aktiviteler/{barId}/kaynaklar",
            new
            {
                kind = (int)ScheduleResourceKind.Subcontractor,
                subcontractorContractId = context.ContractId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var resource = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("resources").EnumerateArray().Single();

        Assert.Equal("Taşeron", resource.GetProperty("kindName").GetString());
        Assert.Contains("Taşeron", resource.GetProperty("name").GetString()!);
    }

    [Fact]
    public async Task SameResourceTwiceOnOneActivity_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        await AssignPersonnelAsync(client, barId, context.PersonnelId);

        var response = await AssignPersonnelAsync(client, barId, context.PersonnelId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PersonnelKindWithoutPersonnel_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/aktiviteler/{barId}/kaynaklar",
            new { kind = (int)ScheduleResourceKind.Personnel });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Başka projenin taşeron sözleşmesi atanamaz.</summary>
    [Fact]
    public async Task SubcontractorFromAnotherProject_IsRejected()
    {
        var first = await CreateContextAsync();
        var second = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, first.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/aktiviteler/{barId}/kaynaklar",
            new
            {
                kind = (int)ScheduleResourceKind.Subcontractor,
                subcontractorContractId = second.ContractId
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovedResource_DisappearsFromTheBar()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var created = await AssignPersonnelAsync(client, barId, context.PersonnelId);

        var assignmentId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.DeleteAsync(
            $"/api/is-programi/kaynaklar/{assignmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("resources").EnumerateArray());
    }

    // ---------------- Çakışma ----------------

    /// <summary>
    /// Aynı personel çakışan tarihli iki aktivitede: uyarı üretilir ama
    /// atama ENGELLENMEZ.
    /// </summary>
    [Fact]
    public async Task OverlappingAssignments_ProduceAWarningWithoutBlocking()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await AddActivityAsync(
            client, context.ScheduleId, "Kablo çekimi", Mon02, Thu05);

        var second = await AddActivityAsync(
            client, context.ScheduleId, "Test", Wed04, Sat07);

        await AssignPersonnelAsync(client, first, context.PersonnelId);

        var response = await AssignPersonnelAsync(
            client, second, context.PersonnelId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Single(payload.GetProperty("conflicts").EnumerateArray());
        Assert.Contains("çakışma", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ConflictList_ReportsTheOverlapWindow()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await AddActivityAsync(
            client, context.ScheduleId, "Kablo çekimi", Mon02, Thu05);

        var second = await AddActivityAsync(
            client, context.ScheduleId, "Test", Wed04, Sat07);

        await AssignPersonnelAsync(client, first, context.PersonnelId);
        await AssignPersonnelAsync(client, second, context.PersonnelId);

        var response = await client.GetAsync(
            $"/api/is-programi/{context.ScheduleId}/kaynak-cakismalari");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var conflict = payload.GetProperty("items").EnumerateArray().Single();

        Assert.Equal("2026-03-04", conflict.GetProperty("overlapStart").GetString());
        Assert.Equal("2026-03-05", conflict.GetProperty("overlapFinish").GetString());
        Assert.Equal(2, conflict.GetProperty("overlapWorkDays").GetInt32());
    }

    [Fact]
    public async Task SequentialAssignments_DoNotConflict()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await AddActivityAsync(
            client, context.ScheduleId, "Kablo çekimi", Mon02, Wed04);

        var second = await AddActivityAsync(
            client, context.ScheduleId, "Test", Thu05, Sat07);

        await AssignPersonnelAsync(client, first, context.PersonnelId);
        await AssignPersonnelAsync(client, second, context.PersonnelId);

        var response = await client.GetAsync(
            $"/api/is-programi/{context.ScheduleId}/kaynak-cakismalari");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(payload.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task DifferentPeople_DoNotConflict()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await AddActivityAsync(
            client, context.ScheduleId, "Kablo çekimi", Mon02, Thu05);

        var second = await AddActivityAsync(
            client, context.ScheduleId, "Test", Wed04, Sat07);

        await AssignPersonnelAsync(client, first, context.PersonnelId);
        await AssignPersonnelAsync(client, second, context.OtherPersonnelId);

        var response = await client.GetAsync(
            $"/api/is-programi/{context.ScheduleId}/kaynak-cakismalari");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(payload.GetProperty("items").EnumerateArray());
    }

    // ---------------- Öneriler ----------------

    /// <summary>
    /// Kısmı kapsayan taşeron sözleşmesi öneri listesinin başında ve
    /// işaretli gelir.
    /// </summary>
    [Fact]
    public async Task SubcontractorCoveringTheSection_IsSuggestedFirst()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = Bar(await ScheduleAsync(client, context.ProjectId), "Panolar")
            .GetProperty("id").GetGuid();

        var response = await client.GetAsync(
            $"/api/is-programi/aktiviteler/{barId}/kaynak-onerileri");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = payload.GetProperty("subcontractors").EnumerateArray().First();

        Assert.Equal(context.SectionId, payload.GetProperty("sectionId").GetGuid());
        Assert.True(first.GetProperty("coversSection").GetBoolean());
        Assert.Equal(context.ContractId, first.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Kısma bağlı olmayan çubukta kısım bilgisi yoktur; öneri gelir
    /// ama hiçbiri "kısmı kapsıyor" diye işaretlenmez.
    /// </summary>
    [Fact]
    public async Task UnlinkedActivity_HasNoSectionMatch()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var barId = await AddActivityAsync(
            client, context.ScheduleId, "Serbest iş", Mon02, Sat07);

        var response = await client.GetAsync(
            $"/api/is-programi/aktiviteler/{barId}/kaynak-onerileri");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("sectionId").ValueKind);
        Assert.All(
            payload.GetProperty("subcontractors").EnumerateArray(),
            x => Assert.False(x.GetProperty("coversSection").GetBoolean()));
    }
}
