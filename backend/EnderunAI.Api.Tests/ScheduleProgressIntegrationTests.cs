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
/// İş programının icmal ve saha zincirine bağlanması (G3).
///
/// Bu paketin bütün iddiası tek cümlede: Gantt'ın "gerçekleşen" çubuğu
/// AYRI BİR VERİ DEĞİLDİR — şantiye şefinin günlük rapora yazdığı
/// miktar, icmal kalemi üzerinden kısma birikir ve doğrudan çubuğa
/// yansır. Bu testler o zincirin uçtan uca çalıştığını doğruluyor.
///
/// İkinci güvence: yalnızca ONAYLI günlük rapor sayılır. Taslak rapor
/// da sayılsaydı, henüz kimsenin doğrulamadığı bir miktar iş
/// programını ilerlemiş gösterirdi.
/// </summary>
[Collection("Integration")]
public sealed class ScheduleProgressIntegrationTests(DatabaseFixture fixture)
{
    private sealed record Chain(
        Guid ProjectId, Guid SectionId, Guid BoqItemId, Guid SiteId);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>
    /// Zinciri kurar: proje → kısım → sözleşme icmali → icmal kalemi
    /// (100 birim) → şantiye.
    /// </summary>
    private async Task<Chain> CreateChainAsync(bool withBoq = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        project.PlannedStartDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        project.PlannedEndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        project.UsesContractSummary = true;

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Kolon Kablo",
            IsActive = true
        };

        db.ProjectHakedisSections.Add(section);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"STY-{suffix}",
            Name = "Merkez Şantiye"
        };

        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var boqItemId = Guid.Empty;

        if (withBoq)
        {
            var boq = new ProjectBoq
            {
                CompanyId = project.CompanyId,
                ProjectId = project.Id,
                BoqNumber = $"ICM-{suffix}",
                Name = "Sözleşme icmali",
                Status = ProjectBoqStatus.Approved,
                IsCurrentRevision = true,
                IsContractBaseline = true,
                CurrencyCode = "TRY"
            };

            db.ProjectBoqs.Add(boq);
            await db.SaveChangesAsync();

            var item = new ProjectBoqItem
            {
                ProjectBoqId = boq.Id,
                ProjectHakedisSectionId = section.Id,
                LineNumber = 1,
                PositionCode = "1.01",
                Description = "NYY 4x50 kablo çekimi",
                Unit = "m",
                ContractQuantity = 100m,
                UnitPrice = 500m,
                TotalAmount = 50_000m
            };

            db.ProjectBoqItems.Add(item);
            await db.SaveChangesAsync();

            boqItemId = item.Id;
        }

        return new Chain(project.Id, section.Id, boqItemId, site.Id);
    }

    /// <summary>Onaylı ya da taslak günlük rapor yazar.</summary>
    private async Task AddDailyReportAsync(
        Chain chain, decimal quantity, DateTime date, bool approved = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var report = new ProjectSiteDailyReport
        {
            ProjectSiteId = chain.SiteId,
            ReportDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            Status = approved
                ? ProjectSiteDailyReportStatus.Approved
                : ProjectSiteDailyReportStatus.Draft
        };

        db.ProjectSiteDailyReports.Add(report);
        await db.SaveChangesAsync();

        db.ProjectSiteDailyReportWorkItems.Add(new ProjectSiteDailyReportWorkItem
        {
            DailyReportId = report.Id,
            ProjectBoqItemId = chain.BoqItemId,
            Description = "Kablo çekimi",
            Unit = "m",
            Quantity = quantity
        });

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ScheduleAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/is-programi");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("schedule");
    }

    private async Task<Guid> CreateScheduleAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/is-programi",
            new { seedFromSections = true });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static JsonElement Bar(JsonElement schedule, string name) =>
        schedule.GetProperty("activities").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == name);

    // ---------- Zincir ----------

    /// <summary>
    /// Günlük rapordaki 40 m, 100 m'lik icmal kaleminin bağlı olduğu
    /// kısma birikir ve Gantt çubuğu %40 gösterir.
    /// </summary>
    [Fact]
    public async Task FieldReport_FlowsIntoTheGanttBar()
    {
        var chain = await CreateChainAsync();
        await AddDailyReportAsync(chain, 40m, new DateTime(2026, 3, 3));

        var client = await ClientAsync();
        await CreateScheduleAsync(client, chain.ProjectId);

        var bar = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo");

        Assert.Equal(40m, bar.GetProperty("progressRate").GetDecimal());
        Assert.Equal((int)ScheduleProgressSource.Section,
            bar.GetProperty("progressSource").GetInt32());
        Assert.Equal("Saha raporu (icmal kısmı)",
            bar.GetProperty("progressSourceName").GetString());
    }

    /// <summary>Onaylı raporlar birikir.</summary>
    [Fact]
    public async Task ApprovedReports_Accumulate()
    {
        var chain = await CreateChainAsync();
        await AddDailyReportAsync(chain, 30m, new DateTime(2026, 3, 3));
        await AddDailyReportAsync(chain, 25m, new DateTime(2026, 3, 4));

        var client = await ClientAsync();
        await CreateScheduleAsync(client, chain.ProjectId);

        var bar = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo");

        Assert.Equal(55m, bar.GetProperty("progressRate").GetDecimal());
    }

    /// <summary>
    /// TASLAK rapor sayılmaz: henüz kimsenin doğrulamadığı bir miktar
    /// iş programını ilerlemiş gösteremez.
    /// </summary>
    [Fact]
    public async Task DraftReport_DoesNotCount()
    {
        var chain = await CreateChainAsync();
        await AddDailyReportAsync(chain, 40m, new DateTime(2026, 3, 3), approved: true);
        await AddDailyReportAsync(chain, 50m, new DateTime(2026, 3, 4), approved: false);

        var client = await ClientAsync();
        await CreateScheduleAsync(client, chain.ProjectId);

        var bar = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo");

        Assert.Equal(40m, bar.GetProperty("progressRate").GetDecimal());
    }

    /// <summary>
    /// Alt aktivite doğrudan icmal SATIRINA bağlanabilir; oran o
    /// satırdan gelir.
    /// </summary>
    [Fact]
    public async Task SubActivityLinkedToABoqItem_TakesItsRate()
    {
        var chain = await CreateChainAsync();
        await AddDailyReportAsync(chain, 75m, new DateTime(2026, 3, 3));

        var client = await ClientAsync();
        var scheduleId = await CreateScheduleAsync(client, chain.ProjectId);

        var parentId = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo")
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/is-programi/{scheduleId}/aktiviteler",
            new
            {
                name = "Kablo çekimi",
                plannedStartDate = new DateOnly(2026, 3, 2),
                plannedEndDate = new DateOnly(2026, 3, 7),
                parentActivityId = parentId,
                projectBoqItemId = chain.BoqItemId
            });

        response.EnsureSuccessStatusCode();

        var bar = Bar(await ScheduleAsync(client, chain.ProjectId), "Kablo çekimi");

        Assert.Equal(75m, bar.GetProperty("progressRate").GetDecimal());
        Assert.Equal((int)ScheduleProgressSource.BoqItem,
            bar.GetProperty("progressSource").GetInt32());
    }

    /// <summary>
    /// Projenin bütünündeki yüzde, icmalin tutar ağırlıklı oranıdır.
    /// </summary>
    [Fact]
    public async Task ProjectProgress_ComesFromTheContractSummary()
    {
        var chain = await CreateChainAsync();
        await AddDailyReportAsync(chain, 40m, new DateTime(2026, 3, 3));

        var client = await ClientAsync();
        await CreateScheduleAsync(client, chain.ProjectId);

        var schedule = await ScheduleAsync(client, chain.ProjectId);

        Assert.True(schedule.GetProperty("hasContractSummary").GetBoolean());
        Assert.Equal(40m, schedule.GetProperty("progressRate").GetDecimal());
        Assert.Equal(0m, schedule.GetProperty("employerRate").GetDecimal());
    }

    // ---------- Neden ölçülemiyor ----------

    /// <summary>
    /// İcmalsiz projede yüzde ölçülemez; ekran bunu boş bırakmak yerine
    /// SEBEBİNİ yazmalı, aksi halde hata sanılır.
    /// </summary>
    [Fact]
    public async Task ProjectWithoutContractSummary_ExplainsWhyProgressIsMissing()
    {
        var chain = await CreateChainAsync(withBoq: false);

        var client = await ClientAsync();
        await CreateScheduleAsync(client, chain.ProjectId);

        var schedule = await ScheduleAsync(client, chain.ProjectId);
        var warnings = schedule.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString()!)
            .ToList();

        Assert.False(schedule.GetProperty("hasContractSummary").GetBoolean());
        Assert.Contains(warnings, x => x.Contains("sözleşme icmali tanımlı değil"));

        var bar = Bar(schedule, "Kolon Kablo");

        Assert.Equal((int)ScheduleProgressSource.None,
            bar.GetProperty("progressSource").GetInt32());
        Assert.Equal("Ölçülemiyor",
            bar.GetProperty("progressSourceName").GetString());
    }

    // ---------- Tahmini bitiş ----------

    /// <summary>
    /// Fiili gecikme PLANI DEĞİŞTİRMEZ: planlanan tarihler yerinde
    /// kalır, gecikme ayrı bir tahmin olarak çıkar. Planı gecikmeye
    /// göre güncelleyen bir sistemde hiçbir zaman geç kalınmış olmaz.
    /// </summary>
    [Fact]
    public async Task Delay_ProducesAForecastWithoutMovingThePlan()
    {
        var chain = await CreateChainAsync();

        var client = await ClientAsync();
        var scheduleId = await CreateScheduleAsync(client, chain.ProjectId);

        var barId = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo")
            .GetProperty("id").GetGuid();

        // Çubuğu geçmişe alıp hiç ilerleme girmiyoruz: süresi dolmuş,
        // %0'da duran bir iş.
        await client.PutAsJsonAsync(
            $"/api/is-programi/aktiviteler/{barId}",
            new
            {
                name = "Kolon Kablo",
                plannedStartDate = new DateOnly(2026, 1, 5),
                plannedEndDate = new DateOnly(2026, 1, 10),
                projectHakedisSectionId = chain.SectionId
            });

        var schedule = await ScheduleAsync(client, chain.ProjectId);
        var bar = Bar(schedule, "Kolon Kablo");

        Assert.Equal("2026-01-05", bar.GetProperty("plannedStart").GetString());
        Assert.Equal("2026-01-10", bar.GetProperty("plannedEnd").GetString());

        Assert.True(bar.GetProperty("slipWorkDays").GetInt32() > 0);
        Assert.True(schedule.GetProperty("delayWorkDays").GetInt32() > 0);
        Assert.NotNull(schedule.GetProperty("forecastFinish").GetString());
    }

    /// <summary>
    /// Plana göre gidiyorsa gecikme üretilmez — her sapmayı alarma
    /// çeviren bir ekran okunmaz hale gelir.
    /// </summary>
    [Fact]
    public async Task FutureWork_ProducesNoDelay()
    {
        var chain = await CreateChainAsync();

        var client = await ClientAsync();
        var scheduleId = await CreateScheduleAsync(client, chain.ProjectId);

        var barId = Bar(await ScheduleAsync(client, chain.ProjectId), "Kolon Kablo")
            .GetProperty("id").GetGuid();

        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        await client.PutAsJsonAsync(
            $"/api/is-programi/aktiviteler/{barId}",
            new
            {
                name = "Kolon Kablo",
                plannedStartDate = start,
                plannedEndDate = start.AddDays(10),
                projectHakedisSectionId = chain.SectionId
            });

        var schedule = await ScheduleAsync(client, chain.ProjectId);

        Assert.Equal(0, schedule.GetProperty("delayWorkDays").GetInt32());
        Assert.Equal("Henüz başlamadı.",
            Bar(schedule, "Kolon Kablo").GetProperty("forecastNote").GetString());
    }
}
