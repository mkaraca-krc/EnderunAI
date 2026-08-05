using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Hakedis;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Saha günlük verisinin icmale bağlanması ve işveren portalındaki
/// fiziksel ilerleme.
///
/// İki güvence: yalnızca ONAYLI raporlar gerçekleşmeye birikir, ve
/// portal yanıtında hiçbir tutar/fiyat bilgisi bulunmaz.
/// </summary>
[Collection("Integration")]
public sealed class SiteProgressAndPortalTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid ProjectId,
        Guid SiteId,
        Guid BoqId,
        Guid PanoItemId,
        Guid TavaItemId,
        Guid SectionId,
        string PortalToken);

    /// <summary>
    /// İki kalemli onaylı icmal (sözleşme tabanı), bir şantiye ve
    /// işveren portalı bağlantısı kurar.
    ///
    /// Pano:  4 adet × 25.000 = 100.000 (ağırlık %68,49)
    /// Tava: 100 metre × 460  =  46.000 (ağırlık %31,51)
    /// </summary>
    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Panolar"
        };
        db.ProjectHakedisSections.Add(section);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = "Test Şantiye"
        };
        db.ProjectSites.Add(site);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"ICM-{suffix}",
            Name = "Sözleşme İcmali",
            RevisionNumber = 1,
            Status = ProjectBoqStatus.Approved,
            IsCurrentRevision = true,
            IsContractBaseline = true,
            CurrencyCode = "TRY",
            TotalAmount = 146_000m
        };

        var pano = new ProjectBoqItem
        {
            ProjectBoq = boq,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 1,
            PositionCode = "P.01",
            Description = "Ana dağıtım panosu",
            Unit = "Adet",
            ContractQuantity = 4m,
            MaterialUnitPrice = 25_000m,
            UnitPrice = 25_000m,
            TotalAmount = 100_000m
        };

        var tava = new ProjectBoqItem
        {
            ProjectBoq = boq,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 2,
            PositionCode = "KT.01",
            Description = "Kablo tavası",
            Unit = "Metre",
            ContractQuantity = 100m,
            MaterialUnitPrice = 460m,
            UnitPrice = 460m,
            TotalAmount = 46_000m
        };

        boq.Items.Add(pano);
        boq.Items.Add(tava);
        db.ProjectBoqs.Add(boq);

        var link = new EmployerPortalLink
        {
            ProjectId = project.Id,
            Token = $"tok-{Guid.NewGuid():N}",
            EmployerName = "Test İşveren"
        };
        db.EmployerPortalLinks.Add(link);

        await db.SaveChangesAsync();

        return new Context(
            project.Id, site.Id, boq.Id, pano.Id, tava.Id, section.Id, link.Token);
    }

    /// <summary>Belirtilen kalemden bir günlük rapor girer.</summary>
    private async Task<Guid> AddReportAsync(
        Context context, DateTime date, Guid boqItemId, decimal quantity,
        bool approved)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var report = new ProjectSiteDailyReport
        {
            ProjectSiteId = context.SiteId,
            ReportDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Status = approved
                ? ProjectSiteDailyReportStatus.Approved
                : ProjectSiteDailyReportStatus.Draft,
            ApprovedAtUtc = approved ? DateTime.UtcNow : null
        };

        report.WorkItems.Add(new ProjectSiteDailyReportWorkItem
        {
            ProjectBoqItemId = boqItemId,
            Description = "Saha imalatı",
            Quantity = quantity,
            Unit = "Adet"
        });

        db.ProjectSiteDailyReports.Add(report);
        await db.SaveChangesAsync();

        return report.Id;
    }

    private async Task<ContractSummaryProgressView> BuildProgressAsync(Guid projectId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IContractSummaryProgressService>();

        return await service.BuildAsync(projectId, CancellationToken.None);
    }

    [Fact]
    public async Task FieldRealization_OnlyCountsApprovedReports()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 1m, approved: true);
        await AddReportAsync(context, new DateTime(2026, 3, 2), context.PanoItemId, 1m, approved: true);

        // Taslak rapor SAYILMAZ: henüz kimse doğrulamadı.
        await AddReportAsync(context, new DateTime(2026, 3, 3), context.PanoItemId, 5m, approved: false);

        var view = await BuildProgressAsync(context.ProjectId);

        var pano = view.Sections
            .SelectMany(x => x.Items)
            .Single(x => x.BoqItemId == context.PanoItemId);

        Assert.Equal(2m, pano.FieldQuantity);
        Assert.Equal(50m, pano.FieldRate);
    }

    [Fact]
    public async Task ProjectRate_IsWeightedByContractAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Pano %100 (100.000 ağırlık), tava %0 (46.000 ağırlık).
        await AddReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 4m, approved: true);

        var view = await BuildProgressAsync(context.ProjectId);

        // 100.000 / 146.000 = %68,49 — ağırlıksız ortalama %50 derdi.
        Assert.Equal(68.49m, view.FieldRate);
        Assert.Equal(146_000m, view.ContractAmount);
    }

    /// <summary>
    /// Sözleşme üstü imalatta kalem oranı 100'ü aşabilir ama bütünün
    /// yüzdesi 100'de sınırlanır — "işin %130'u bitti" anlamsızdır.
    /// </summary>
    [Fact]
    public async Task OverProduction_DoesNotPushProjectRateAboveHundred()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 8m, approved: true);
        await AddReportAsync(context, new DateTime(2026, 3, 2), context.TavaItemId, 100m, approved: true);

        var view = await BuildProgressAsync(context.ProjectId);

        var pano = view.Sections
            .SelectMany(x => x.Items)
            .Single(x => x.BoqItemId == context.PanoItemId);

        Assert.Equal(200m, pano.FieldRate);
        Assert.Equal(100m, view.FieldRate);
    }

    [Fact]
    public async Task Portal_ShowsPhysicalProgress()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 2m, approved: true);

        var client = fixture.Factory.CreateClient();

        var progress = await client.GetFromJsonAsync<JsonElement>(
            $"/api/portal/{context.PortalToken}/ilerleme");

        Assert.True(progress.GetProperty("hasProgress").GetBoolean());

        // Pano 2/4 = %50, ağırlık 100.000/146.000 → 34,25
        Assert.Equal(34.25m, progress.GetProperty("completionRate").GetDecimal());

        var section = progress.GetProperty("sections").EnumerateArray().Single();
        Assert.Equal("Panolar", section.GetProperty("name").GetString());
        Assert.Equal(2, section.GetProperty("itemCount").GetInt32());
        Assert.Equal(0, section.GetProperty("completedItemCount").GetInt32());
    }

    /// <summary>
    /// SIZMA TESTİ: portal yanıtının tamamında hiçbir tutar, birim fiyat
    /// veya ağırlık alanı bulunmamalı. Yüzde sunucuda tutarla
    /// ağırlıklandırılıyor ama ağırlığın kendisi dışarı çıkmıyor.
    /// </summary>
    [Fact]
    public async Task Portal_NeverLeaksAmountsOrPrices()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 2m, approved: true);

        var client = fixture.Factory.CreateClient();

        var body = await client.GetStringAsync(
            $"/api/portal/{context.PortalToken}/ilerleme");

        foreach (var forbidden in new[]
                 {
                     "amount", "Amount", "price", "Price", "unitPrice",
                     "contractAmount", "tutar", "fiyat", "weight"
                 })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }

        // Sözleşme tutarının kendisi de metin olarak geçmemeli.
        Assert.DoesNotContain("146000", body);
        Assert.DoesNotContain("100000", body);
        Assert.DoesNotContain("25000", body);
    }

    [Fact]
    public async Task Portal_SaysSoWhenProjectHasNoContractSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var link = new EmployerPortalLink
        {
            ProjectId = project.Id,
            Token = $"tok-{Guid.NewGuid():N}"
        };
        db.EmployerPortalLinks.Add(link);
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();

        var progress = await client.GetFromJsonAsync<JsonElement>(
            $"/api/portal/{link.Token}/ilerleme");

        // İcmalsiz projede yüzde uydurulmuyor.
        Assert.False(progress.GetProperty("hasProgress").GetBoolean());
    }

    [Fact]
    public async Task Portal_RejectsRevokedLink()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await db.EmployerPortalLinks
                .SingleAsync(x => x.Token == context.PortalToken);

            link.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/portal/{context.PortalToken}/ilerleme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QuickPick_ReturnsSummaryItemsAndFrequentOnes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Tava kalemi iki kez girilmiş: sık kullanılanlarda çıkmalı.
        await AddReportAsync(context, DateTime.UtcNow.Date.AddDays(-2), context.TavaItemId, 10m, approved: true);
        await AddReportAsync(context, DateTime.UtcNow.Date.AddDays(-1), context.TavaItemId, 15m, approved: false);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-sites/{context.SiteId}/daily-reports/icmal-kalemleri");

        Assert.True(result.GetProperty("hasContractSummary").GetBoolean());
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());

        var frequent = result.GetProperty("frequent").EnumerateArray().ToList();
        Assert.Single(frequent);
        Assert.Equal(context.TavaItemId, frequent[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task QuickPick_FiltersBySearchTerm()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-sites/{context.SiteId}/daily-reports/icmal-kalemleri?search=tava");

        var items = result.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("KT.01", items[0].GetProperty("positionCode").GetString());
    }

    /// <summary>
    /// İcmali olmayan projede hızlı seçim boş döner ve arayüz serbest
    /// metne düşer — icmalsiz proje çalışmaya devam etmeli.
    /// </summary>
    [Fact]
    public async Task QuickPick_IsEmptyWhenProjectHasNoSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"S-{suffix}",
            Name = "İcmalsiz şantiye"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-sites/{site.Id}/daily-reports/icmal-kalemleri");

        Assert.False(result.GetProperty("hasContractSummary").GetBoolean());
        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Serbest metin kalemi (icmal bağı olmayan) hâlâ kaydedilebilmeli:
    /// icmalde olmayan iş de günlük rapora yazılabilmeli.
    /// </summary>
    [Fact]
    public async Task DailyReport_StillAcceptsFreeTextWorkItem()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/project-sites/{context.SiteId}/daily-reports",
            new
            {
                reportDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                weatherCondition = "Açık",
                engineerCount = 1,
                foremanCount = 1,
                craftsmanCount = 2,
                workerCount = 5,
                otherCount = 0,
                notes = "Serbest metin denemesi",
                workItems = new object[]
                {
                    new
                    {
                        description = "İcmalde olmayan ilave iş",
                        quantity = 3m,
                        unit = "Adet",
                        projectBoqItemId = (Guid?)null
                    },
                    new
                    {
                        description = "İcmale bağlı iş",
                        quantity = 1m,
                        unit = "Adet",
                        projectBoqItemId = context.PanoItemId
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var items = await db.ProjectSiteDailyReportWorkItems
            .AsNoTracking()
            .Where(x => x.DailyReport.ProjectSiteId == context.SiteId)
            .ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.ProjectBoqItemId is null);
        Assert.Contains(items, x => x.ProjectBoqItemId == context.PanoItemId);
    }
}
