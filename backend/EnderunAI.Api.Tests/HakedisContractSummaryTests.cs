using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hakedişin iki katmanlı çalışması.
///
/// Kritik iş kuralı: hakediş kalemleri icmalden gelir ve "bu dönem"
/// alanı sahadan biriken onaylı miktarla ÖN DOLDURULUR — ama bu yalnızca
/// bir öneridir. Kesinleşen hakediş işverenle anlaşılan resmî rakamdır
/// ve saha verisiyle aynı olmak zorunda değildir. İkisi de saklanır.
/// </summary>
[Collection("Integration")]
public sealed class HakedisContractSummaryTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid SiteId,
        Guid PanoItemId, Guid TavaItemId);

    /// <summary>
    /// Pano: 4 adet × 25.000, Tava: 100 metre × 460.
    /// </summary>
    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"S-{suffix}",
            Name = "Şantiye"
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

        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, site.Id, pano.Id, tava.Id);
    }

    private async Task AddApprovedSiteReportAsync(
        Context context, DateTime date, Guid boqItemId, decimal quantity)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var report = new ProjectSiteDailyReport
        {
            ProjectSiteId = context.SiteId,
            ReportDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Status = ProjectSiteDailyReportStatus.Approved,
            ApprovedAtUtc = DateTime.UtcNow
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
    }

    private static object BuildHakedisRequest(
        Context context, int period, string suffix,
        (Guid BoqItemId, string Code, string Unit, decimal Contract,
         decimal Current, decimal UnitPrice)[] lines) => new
    {
        companyId = context.CompanyId,
        projectId = context.ProjectId,
        projectMeasurementId = (Guid?)null,
        progressPaymentNumber = $"HK-{suffix}-{period}",
        periodNumber = period,
        periodStartDate = (DateOnly?)null,
        periodEndDate = (DateOnly?)null,
        progressPaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
        priceDifferenceAmount = 0m,
        vatRate = 20m,
        withholdingNumerator = 0,
        withholdingDenominator = 10,
        description = (string?)null,
        notes = (string?)null,
        items = lines.Select(line => new
        {
            engineeringPositionId = (Guid?)null,
            positionCode = line.Code,
            description = line.Code,
            unit = line.Unit,
            contractQuantity = line.Contract,
            currentQuantity = line.Current,
            unitPrice = line.UnitPrice,
            measurementReference = (string?)null,
            notes = (string?)null,
            projectBoqItemId = line.BoqItemId
        }).ToArray(),
        deductions = Array.Empty<object>(),
        paymentPlans = Array.Empty<object>(),
        advanceMaterials = Array.Empty<object>()
    };

    [Fact]
    public async Task SummaryDraft_PrefillsCurrentQuantityFromApprovedSiteData()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 1m);
        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 2), context.PanoItemId, 1m);

        var draft = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/icmal-taslagi?projectId={context.ProjectId}&periodNumber=1");

        Assert.True(draft.GetProperty("hasContractSummary").GetBoolean());

        var items = draft.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var pano = items.Single(x =>
            x.GetProperty("projectBoqItemId").GetGuid() == context.PanoItemId);

        Assert.Equal(2m, pano.GetProperty("suggestedCurrentQuantity").GetDecimal());
        Assert.Equal(4m, pano.GetProperty("contractQuantity").GetDecimal());
        Assert.Equal(0m, pano.GetProperty("previousQuantity").GetDecimal());

        // Sahada hiç iş yapılmayan kalemde öneri sıfır — uydurulmuyor.
        var tava = items.Single(x =>
            x.GetProperty("projectBoqItemId").GetGuid() == context.TavaItemId);
        Assert.Equal(0m, tava.GetProperty("suggestedCurrentQuantity").GetDecimal());
    }

    /// <summary>
    /// Teknik ofis öneriyi serbestçe değiştirebilir; kesinleşen rakam
    /// işverenle mutabık kalınandır. İKİSİ DE saklanır.
    /// </summary>
    [Fact]
    public async Task Hakedis_StoresBothFieldAndEmployerQuantities()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Sahada 3 adet yapıldı.
        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 3m);

        // İşveren yalnızca 2 adet kabul etti.
        var response = await client.PostAsJsonAsync(
            "/api/progress-payments",
            BuildHakedisRequest(context, 1, suffix,
            [
                (context.PanoItemId, "P.01", "Adet", 4m, 2m, 25_000m)
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var hakedisId = payload.GetProperty("id").GetGuid();

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/{hakedisId}");

        var line = detail.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(2m, line.GetProperty("currentQuantity").GetDecimal());
        Assert.Equal(3m, line.GetProperty("fieldQuantity").GetDecimal());
        Assert.Equal(3m, line.GetProperty("cumulativeFieldQuantity").GetDecimal());
        // İşveren 1 adet eksik kabul etti.
        Assert.Equal(-1m, line.GetProperty("fieldDifference").GetDecimal());
    }

    /// <summary>
    /// DONDURMA: hakediş taslaktan çıktıktan sonra onaylanan geç bir
    /// günlük rapor, geçmiş hakedişin saha rakamını DEĞİŞTİRMEZ. Fark
    /// bir sonraki döneme taşınır.
    /// </summary>
    [Fact]
    public async Task LateApprovedReport_DoesNotChangeFinalisedHakedis()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 2m);

        var created = await client.PostAsJsonAsync(
            "/api/progress-payments",
            BuildHakedisRequest(context, 1, suffix,
            [
                (context.PanoItemId, "P.01", "Adet", 4m, 2m, 25_000m)
            ]));

        var hakedisId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Hakediş taslaktan çıkıyor: artık düzenlenemez.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{hakedisId}/submit", null))
                .StatusCode);

        // Geç onaylanan rapor.
        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 5), context.PanoItemId, 1m);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/{hakedisId}");

        var line = detail.GetProperty("items").EnumerateArray().Single();

        // DONMUŞ: hâlâ 2, 3 değil.
        Assert.Equal(2m, line.GetProperty("fieldQuantity").GetDecimal());
        Assert.Equal(2m, line.GetProperty("cumulativeFieldQuantity").GetDecimal());

        // Fark bir sonraki dönemin önerisine taşındı.
        var nextDraft = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/icmal-taslagi?projectId={context.ProjectId}&periodNumber=2");

        var pano = nextDraft.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("projectBoqItemId").GetGuid() == context.PanoItemId);

        Assert.Equal(1m, pano.GetProperty("suggestedCurrentQuantity").GetDecimal());
        Assert.Equal(2m, pano.GetProperty("previousQuantity").GetDecimal());
    }

    [Fact]
    public async Task DifferenceReport_ShowsPendingWorkAndAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 3m);

        await client.PostAsJsonAsync(
            "/api/progress-payments",
            BuildHakedisRequest(context, 1, suffix,
            [
                (context.PanoItemId, "P.01", "Adet", 4m, 2m, 25_000m)
            ]));

        var report = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/saha-isveren-farki?projectId={context.ProjectId}");

        Assert.True(report.GetProperty("hasContractSummary").GetBoolean());

        // 1 adet devreden × 25.000
        Assert.Equal(25_000m, report.GetProperty("totalPendingAmount").GetDecimal());
        Assert.Equal(1, report.GetProperty("differingItemCount").GetInt32());

        var pano = report.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("positionCode").GetString() == "P.01");

        Assert.Equal(3m, pano.GetProperty("fieldQuantity").GetDecimal());
        Assert.Equal(2m, pano.GetProperty("employerQuantity").GetDecimal());
        Assert.Equal(1m, pano.GetProperty("pendingQuantity").GetDecimal());
        Assert.Equal(75m, pano.GetProperty("fieldRate").GetDecimal());
        Assert.Equal(50m, pano.GetProperty("employerRate").GetDecimal());
    }

    [Fact]
    public async Task ProgressView_ShowsContractFieldEmployerAndRemaining()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddApprovedSiteReportAsync(context, new DateTime(2026, 3, 1), context.PanoItemId, 3m);

        await client.PostAsJsonAsync(
            "/api/progress-payments",
            BuildHakedisRequest(context, 1, suffix,
            [
                (context.PanoItemId, "P.01", "Adet", 4m, 2m, 25_000m)
            ]));

        var view = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/icmal-ilerleme");

        Assert.True(view.GetProperty("hasContractSummary").GetBoolean());
        Assert.Equal(146_000m, view.GetProperty("contractAmount").GetDecimal());

        // Saha: 3/4 pano → 75.000; işveren: 2/4 → 50.000
        Assert.Equal(75_000m, view.GetProperty("fieldAmount").GetDecimal());
        Assert.Equal(50_000m, view.GetProperty("employerAmount").GetDecimal());

        var item = view.GetProperty("sections").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Single(x => x.GetProperty("positionCode").GetString() == "P.01");

        Assert.Equal(4m, item.GetProperty("contractQuantity").GetDecimal());
        Assert.Equal(3m, item.GetProperty("fieldQuantity").GetDecimal());
        Assert.Equal(2m, item.GetProperty("employerQuantity").GetDecimal());
        // Kalan sözleşme miktarı işveren kabulüne göre: 4 − 2
        Assert.Equal(2m, item.GetProperty("remainingQuantity").GetDecimal());
    }

    /// <summary>
    /// GERİYE UYUM: icmali olmayan projede hakediş bugünkü gibi elle
    /// girilir. Saha alanları sıfır kalır, hiçbir akış bozulmaz.
    /// </summary>
    [Fact]
    public async Task ProjectWithoutSummary_KeepsWorkingWithManualLines()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/progress-payments", new
        {
            companyId = project.CompanyId,
            projectId = project.Id,
            projectMeasurementId = (Guid?)null,
            progressPaymentNumber = $"HK-{suffix}",
            periodNumber = 1,
            periodStartDate = (DateOnly?)null,
            periodEndDate = (DateOnly?)null,
            progressPaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            priceDifferenceAmount = 0m,
            vatRate = 20m,
            withholdingNumerator = 0,
            withholdingDenominator = 10,
            description = (string?)null,
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "ELLE.01",
                    description = "Elle girilen poz",
                    unit = "Adet",
                    contractQuantity = 10m,
                    currentQuantity = 4m,
                    unitPrice = 1_000m,
                    measurementReference = (string?)null,
                    notes = (string?)null
                }
            },
            deductions = Array.Empty<object>(),
            paymentPlans = Array.Empty<object>(),
            advanceMaterials = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hakedisId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/{hakedisId}");

        var line = detail.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(4m, line.GetProperty("currentQuantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("fieldQuantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null,
            line.GetProperty("projectBoqItemId").ValueKind);
        Assert.Equal(4_000m, detail.GetProperty("currentAmount").GetDecimal());

        // İcmal olmayan projede taslak önerisi de dürüstçe boş döner.
        var draft = await client.GetFromJsonAsync<JsonElement>(
            $"/api/progress-payments/icmal-taslagi?projectId={project.Id}&periodNumber=2");

        Assert.False(draft.GetProperty("hasContractSummary").GetBoolean());
    }
}
