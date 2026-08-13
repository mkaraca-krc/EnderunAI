using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hakediş kâr marjı.
///
/// Asıl güvence: İHZARAT kâra girmez. İhzarat, henüz yapılmamış imalatın
/// malzeme bedelidir; onu kâr saymak malzemeyi erken alan projeyi kârlı
/// gösterir ve imalat yapıldığı dönemde zarar çıkarır.
///
/// İkinci güvence: iki maliyet tabanı (tarih bazlı ölçüm ve imalata
/// düşen dağıtım) ayrı ayrı duruyor. Toplanır ya da birine indirgenirse
/// hangi soruya cevap verildiği kaybolur.
/// </summary>
[Collection("Integration")]
public sealed class HakedisProfitTests(DatabaseFixture fixture)
{
    private sealed class FixedVisibility(bool canView) : IExtraPaymentVisibilityService
    {
        public Task<bool> CanViewExtraPaymentAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(canView);
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid ProjectId,
        Guid SectionId,
        Guid BoqItemId,
        Guid ProgressPaymentId);

    private static readonly DateTime PeriodStart =
        DateTime.SpecifyKind(new DateTime(2026, 3, 1), DateTimeKind.Utc);

    private static readonly DateTime PeriodEnd =
        DateTime.SpecifyKind(new DateTime(2026, 3, 31), DateTimeKind.Utc);

    private static HakedisProfitService CreateService(
        AppDbContext db, bool canSeeExtraPayments = true)
        => new(
            db,
            new BoqItemCostService(db, new FixedVisibility(canSeeExtraPayments)),
            // Maliyet ortak okuyucudan geliyor: hakediş kârı ile proje
            // maliyet analizi aynı kaynağı okuyor.
            new EnderunAI.Api.Services.Projects.ProjectRealizedCostReader(db),
            new FixedVisibility(canSeeExtraPayments));

    /// <summary>
    /// Sözleşme: 1000 m × 300 = 300.000.
    /// Hakediş: bu dönem 500 m → 150.000 imalat.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        AppDbContext db,
        string suffix,
        decimal headerCurrentAmount = 150_000m,
        decimal priceDifference = 0m,
        bool withPeriodDates = true,
        bool linkLineToBoq = true)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Name = "Kuvvetli Akım",
            Order = 1
        };
        db.ProjectHakedisSections.Add(section);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{suffix}",
            Name = "Sözleşme icmali",
            IsCurrentRevision = true,
            IsContractBaseline = true,
            TotalAmount = 300_000m
        };
        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        var boqItem = new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 1,
            PositionCode = "A-1",
            Description = "Kablo çekilmesi",
            Unit = "m",
            ContractQuantity = 1_000m,
            UnitPrice = 300m,
            TotalAmount = 300_000m
        };
        db.ProjectBoqItems.Add(boqItem);

        var payment = new ProgressPayment
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            ProgressPaymentNumber = $"HK-{suffix}",
            PeriodNumber = 1,
            ProgressPaymentDate = PeriodEnd,
            PeriodStartDate = withPeriodDates ? PeriodStart : null,
            PeriodEndDate = withPeriodDates ? PeriodEnd : null,
            Status = ProgressPaymentStatus.Approved,
            ContractAmount = 300_000m,
            CurrentAmount = headerCurrentAmount,
            CumulativeAmount = headerCurrentAmount,
            CumulativeWorkAmount = 150_000m,
            PriceDifferenceAmount = priceDifference
        };
        db.ProgressPayments.Add(payment);
        await db.SaveChangesAsync();

        db.ProgressPaymentItems.Add(new ProgressPaymentItem
        {
            ProgressPaymentId = payment.Id,
            ProjectBoqItemId = linkLineToBoq ? boqItem.Id : null,
            LineNumber = 1,
            PositionCode = "A-1",
            Description = "Kablo çekilmesi",
            Unit = "m",
            ContractQuantity = 1_000m,
            CurrentQuantity = 500m,
            CumulativeQuantity = 500m,
            UnitPrice = 300m,
            CurrentAmount = 150_000m,
            CumulativeAmount = 150_000m
        });

        await db.SaveChangesAsync();

        return new Seed(
            project.CompanyId, project.Id, section.Id, boqItem.Id, payment.Id);
    }

    private static void AddCost(
        AppDbContext db,
        Seed seed,
        decimal amount,
        DateTime date,
        Guid? boqItemId,
        bool withSection = true)
    {
        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = seed.ProjectId,
            ProjectHakedisSectionId = withSection ? seed.SectionId : null,
            ProjectBoqItemId = boqItemId,
            CostType = ProjectCostType.Material,
            CostClass = ProjectCostClass.Material,
            CostDate = date,
            Amount = amount,
            Description = "test"
        });
    }

    [Fact]
    public async Task Profit_IsProductionRevenueMinusCost()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        // 100.000 / 500 m = 200 TL/m → bu dönem 500 m × 200 = 100.000.
        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.NotNull(result);
        Assert.Equal(150_000m, result!.ProductionRevenue);
        Assert.Equal(0m, result.AdvanceMaterialMovement);

        Assert.Equal(100_000m, result.CostByProduction);
        Assert.Equal(50_000m, result.ProfitByProduction);
        Assert.Equal(33.33m, result.MarginByProductionPercent);

        var line = Assert.Single(result.Lines);
        Assert.Equal(200m, line.UnitCost);
        Assert.Equal(100_000m, line.PeriodCost);
        Assert.Equal(50_000m, line.Profit);
        Assert.Equal(1m, line.MeasuredRatio);
    }

    [Fact]
    public async Task CostBases_StaySeparateWhenCostFallsOutsidePeriod()
    {
        // Peşin alınan malzeme: imalata düşer ama dönem tarihine düşmez.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        AddCost(db, seed, 20_000m, PeriodStart.AddMonths(-1), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        // İmalata düşen: 120.000 / 500 = 240 TL/m × 500 = 120.000.
        Assert.Equal(120_000m, result!.CostByProduction);
        Assert.Equal(30_000m, result.ProfitByProduction);

        // Tarih bazlı: yalnızca dönem içindeki 100.000.
        Assert.Equal(100_000m, result.CostByDate);
        Assert.Equal(50_000m, result.ProfitByDate);

        Assert.Contains("kaydırır", result.CostByDateBasis);
    }

    [Fact]
    public async Task AdvanceMaterial_IsExcludedFromProfit()
    {
        // Hakediş tutarı 200.000 ama imalat 150.000: aradaki 50.000
        // ihzarattır ve kâr tabanına girmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(
            db, Guid.NewGuid().ToString("N")[..8], headerCurrentAmount: 200_000m);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.Equal(200_000m, result!.HakedisAmount);
        Assert.Equal(150_000m, result.ProductionRevenue);
        Assert.Equal(50_000m, result.AdvanceMaterialMovement);

        // Kâr ihzarat üzerinden değil, imalat üzerinden.
        Assert.Equal(50_000m, result.ProfitByProduction);
        Assert.Contains(result.Assumptions, x => x.Contains("ihzarat"));
    }

    [Fact]
    public async Task PriceDifference_CountsAsRevenue()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(
            db, Guid.NewGuid().ToString("N")[..8], priceDifference: 10_000m);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.Equal(10_000m, result!.PriceDifferenceAmount);

        // 150.000 + 10.000 − 100.000
        Assert.Equal(60_000m, result.ProfitByProduction);
        Assert.Contains(result.Assumptions, x => x.Contains("Fiyat farkı"));
    }

    [Fact]
    public async Task LineWithoutBoqLink_HasNoCostAndIsFlagged()
    {
        // Poza bağlanmamış satırın maliyeti hesaplanamaz; sıfır maliyet
        // varsayilirsa kâr olduğundan büyük görünür.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(
            db, Guid.NewGuid().ToString("N")[..8], linkLineToBoq: false);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        var line = Assert.Single(result!.Lines);

        Assert.Null(line.UnitCost);
        Assert.Null(line.Profit);
        Assert.Contains("bağlı değil", line.CostBasis);

        Assert.Equal(150_000m, result.RevenueWithoutCost);
        Assert.Contains(result.Assumptions, x => x.Contains("iyimser"));
    }

    [Fact]
    public async Task MissingPeriodDates_ExplainsInsteadOfShowingZero()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(
            db, Guid.NewGuid().ToString("N")[..8], withPeriodDates: false);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.Null(result!.CostByDate);
        Assert.Null(result.ProfitByDate);
        Assert.Contains("tarihi girilmemiş", result.CostByDateBasis);

        // İmalata düşen taraf hesaplanmaya devam eder.
        Assert.Equal(100_000m, result.CostByProduction);
    }

    [Fact]
    public async Task ExtraPayment_IsExcludedForUnauthorisedViewer()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, seed.CompanyId, Guid.NewGuid().ToString("N")[..8]);

        db.HrProjectLaborCosts.Add(new HrProjectLaborCost
        {
            CompanyId = seed.CompanyId,
            ProjectId = seed.ProjectId,
            PersonnelId = personnel.Id,
            ProjectBoqItemId = seed.BoqItemId,
            WorkDate = PeriodStart.AddDays(10),
            // 80.000 resmi + 20.000 elden.
            TotalLaborCost = 100_000m,
            CompensationCost = 20_000m
        });

        await db.SaveChangesAsync();

        var authorised = await CreateService(db).GetAsync(seed.ProgressPaymentId);
        Assert.True(authorised!.IncludesExtraPayments);
        Assert.Equal(100_000m, authorised.CostByProduction);
        Assert.Equal(100_000m, authorised.CostByDate);

        var restricted = await CreateService(db, canSeeExtraPayments: false)
            .GetAsync(seed.ProgressPaymentId);

        Assert.False(restricted!.IncludesExtraPayments);
        Assert.Equal(80_000m, restricted.CostByProduction);
        Assert.Equal(80_000m, restricted.CostByDate);
        Assert.Contains(restricted.Assumptions, x => x.Contains("resmi bordro"));
    }

    [Fact]
    public async Task Cumulative_IncludesCostThatCouldNotBeAssigned()
    {
        // Poza da kısma da bağlanamayan maliyet satır kârına giremez ama
        // projenin kümülatif kârından düşmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 100_000m, PeriodStart.AddDays(14), seed.BoqItemId);
        AddCost(db, seed, 25_000m, PeriodStart.AddDays(15), null, withSection: false);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.Equal(150_000m, result!.CumulativeRevenue);
        Assert.Equal(125_000m, result.CumulativeCost);
        Assert.Equal(25_000m, result.CumulativeProfit);

        // Satır tarafı bu tutarı görmez; ikisi bilerek farklı.
        Assert.Equal(100_000m, result.CostByProduction);
    }

    [Fact]
    public async Task Revenue_IsBeforeDeductionsAndVat()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var result = await CreateService(db).GetAsync(seed.ProgressPaymentId);

        Assert.Contains(result!.Assumptions,
            x => x.Contains("KDV hariç") && x.Contains("kesinti öncesi"));
    }

    [Fact]
    public async Task UnknownPayment_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Null(await CreateService(db).GetAsync(Guid.NewGuid()));
    }
}
