using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İcmal satırında dört fiyat ve kâr.
///
/// Asıl güvence: KÂR yalnızca sözleşme eksi GERÇEKLEŞEN maliyettir.
/// Referans fiyat ve şirket ortalaması karara yardımcı bilgidir; birini
/// maliyet yerine koymak, henüz gerçekleşmemiş bir rakamı gerçekleşmiş
/// gibi gösterir ve zararı kârmış gibi raporlar.
///
/// İkinci güvence: şirket ortalaması yalnızca ETİKETLİ maliyetten ve en
/// az iki projeden hesaplanır. Dağıtılmış tutar bir tahmindir;
/// tahminlerin ortalamasını "şirketin gerçek maliyeti" diye sunmak,
/// sonraki tekliflerin yanlış fiyatlanmasına yol açar.
/// </summary>
[Collection("Integration")]
public sealed class BoqProfitTests(DatabaseFixture fixture)
{
    /// <summary>Ek ödemeyi gören/görmeyen kullanıcıyı taklit eder.</summary>
    private sealed class FixedVisibility(bool canView) : IExtraPaymentVisibilityService
    {
        public Task<bool> CanViewExtraPaymentAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(canView);
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid BranchId,
        Guid EmployerId,
        Guid ProjectId,
        Guid SectionId,
        Guid BoqId,
        Guid PositionId,
        /// <summary>Poza bağlı satır: 1000 m x 300 = 300.000.</summary>
        Guid LinkedItem,
        /// <summary>Poza bağlı olmayan satır: 10 Ad x 10.000 = 100.000.</summary>
        Guid LooseItem);

    private static BoqProfitService CreateService(
        AppDbContext db, bool canSeeExtraPayments = true)
        => new(
            db,
            new BoqItemCostService(db, new FixedVisibility(canSeeExtraPayments)),
            new PositionPriceService(db));

    private static async Task<Seed> SeedAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var position = new EngineeringPosition
        {
            CompanyId = project.CompanyId,
            Code = $"35.200.4001-{suffix}",
            OfficialCode = "35.200.4001",
            Name = "NYY kablo çekilmesi",
            Unit = "m",
            Source = EngineeringPositionSource.Official,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active,
            OfficialInstitution = "ÇŞB"
        };
        db.EngineeringPositions.Add(position);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Name = "Kuvvetli Akım",
            Order = 1
        };
        db.ProjectHakedisSections.Add(section);
        await db.SaveChangesAsync();

        // Aynı poz iki kurumda birden fiyatlı: yan yana görünmeli.
        db.PositionUnitPrices.AddRange(
            new PositionUnitPrice
            {
                EngineeringPositionId = position.Id,
                Year = 2026,
                Institution = PositionPriceInstitution.Csb,
                Component = PositionPriceComponent.Total,
                UnitPrice = 250m
            },
            new PositionUnitPrice
            {
                EngineeringPositionId = position.Id,
                Year = 2026,
                Institution = PositionPriceInstitution.Tedas,
                Component = PositionPriceComponent.Total,
                UnitPrice = 260m
            });

        var (boqId, linked, loose) = await AddBoqAsync(
            db, project, section.Id, position.Id, suffix);

        return new Seed(
            project.CompanyId,
            project.BranchId,
            project.EmployerCurrentAccountId!.Value,
            project.Id,
            section.Id,
            boqId,
            position.Id,
            linked,
            loose);
    }

    private static async Task<(Guid BoqId, Guid Linked, Guid Loose)> AddBoqAsync(
        AppDbContext db,
        Project project,
        Guid? sectionId,
        Guid positionId,
        string suffix,
        decimal quantity = 1000m,
        decimal unitPrice = 300m)
    {
        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{suffix}",
            Name = "Sözleşme icmali",
            IsCurrentRevision = true
        };
        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        var linked = new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = sectionId,
            EngineeringPositionId = positionId,
            LineNumber = 1,
            PositionCode = "35.200.4001",
            Description = "NYY kablo çekilmesi",
            Unit = "m",
            ContractQuantity = quantity,
            UnitPrice = unitPrice,
            MaterialUnitPrice = 200m,
            LaborUnitPrice = 100m,
            TotalAmount = quantity * unitPrice
        };

        var loose = new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = sectionId,
            LineNumber = 2,
            PositionCode = "B-1",
            Description = "Kütüphaneye bağlanmamış imalat",
            Unit = "Ad",
            ContractQuantity = 10m,
            UnitPrice = 10_000m,
            TotalAmount = 100_000m
        };

        db.ProjectBoqItems.AddRange(linked, loose);
        await db.SaveChangesAsync();

        return (boq.Id, linked.Id, loose.Id);
    }

    /// <summary>
    /// Aynı şirkette, aynı poza bağlı BAŞKA bir proje: etiketli maliyet
    /// ve onaylı metrajıyla birlikte. Şirket ortalamasının kaynağı bu.
    /// </summary>
    private static async Task AddHistoricalProjectAsync(
        AppDbContext db,
        Seed seed,
        string suffix,
        decimal taggedCost,
        decimal measuredQuantity,
        bool approved = true,
        bool tagCostToItem = true)
    {
        var project = new Project
        {
            CompanyId = seed.CompanyId,
            BranchId = seed.BranchId,
            EmployerCurrentAccountId = seed.EmployerId,
            Code = $"PRJ-{suffix}",
            Name = $"Geçmiş proje {suffix}",
            CurrencyCode = "TRY",
            Status = ProjectStatus.Completed
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Name = "Kuvvetli Akım",
            Order = 1
        };
        db.ProjectHakedisSections.Add(section);
        await db.SaveChangesAsync();

        var (boqId, linked, _) = await AddBoqAsync(
            db, project, section.Id, seed.PositionId, suffix);

        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = project.Id,
            ProjectHakedisSectionId = section.Id,
            ProjectBoqItemId = tagCostToItem ? linked : null,
            CostType = ProjectCostType.Material,
            CostClass = ProjectCostClass.Material,
            CostDate = DateTime.UtcNow,
            Amount = taggedCost,
            Description = "geçmiş maliyet"
        });

        var measurement = new ProjectMeasurement
        {
            CompanyId = seed.CompanyId,
            ProjectId = project.Id,
            ProjectBoqId = boqId,
            MeasurementNumber = $"MTR-{suffix}",
            MeasurementDate = DateTime.UtcNow,
            Status = approved
                ? ProjectMeasurementStatus.Approved
                : ProjectMeasurementStatus.Draft
        };
        db.ProjectMeasurements.Add(measurement);
        await db.SaveChangesAsync();

        db.ProjectMeasurementItems.Add(new ProjectMeasurementItem
        {
            ProjectMeasurementId = measurement.Id,
            ProjectBoqItemId = linked,
            EngineeringPositionId = seed.PositionId,
            LineNumber = 1,
            PositionCode = "35.200.4001",
            Description = "NYY kablo çekilmesi",
            Unit = "m",
            ContractQuantity = 1000m,
            CurrentQuantity = measuredQuantity,
            CumulativeQuantity = measuredQuantity,
            UnitPrice = 300m
        });

        await db.SaveChangesAsync();
    }

    private static void AddCost(
        AppDbContext db, Seed seed, decimal amount, Guid? boqItemId, bool withSection = true)
    {
        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = seed.ProjectId,
            ProjectHakedisSectionId = withSection ? seed.SectionId : null,
            ProjectBoqItemId = boqItemId,
            CostType = ProjectCostType.Material,
            CostClass = ProjectCostClass.Material,
            CostDate = DateTime.UtcNow,
            Amount = amount,
            Description = "test"
        });
    }

    // -----------------------------------------------------------------
    // Dört fiyat
    // -----------------------------------------------------------------

    [Fact]
    public async Task ContractAndReferencePrices_AppearSideBySide()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);

        Assert.NotNull(result);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        // 1 — Sözleşme
        Assert.Equal(300m, line.ContractUnitPrice);
        Assert.Equal(200m, line.ContractMaterialUnitPrice);
        Assert.Equal(100m, line.ContractLaborUnitPrice);
        Assert.Equal(300_000m, line.ContractTotal);

        // 2 — Referans: iki kurum da ayrı ayrı, toplanmadan.
        Assert.Equal(2, line.References.Count);
        Assert.Equal(250m, line.References.Single(x => x.InstitutionName == "ÇŞB").UnitPrice);
        Assert.Equal(260m, line.References.Single(x => x.InstitutionName == "TEDAŞ").UnitPrice);
    }

    [Fact]
    public async Task LineWithoutLibraryLink_HasNoReferenceAndSaysWhy()
    {
        // Uydurma referans yerine "bağlı değil" demeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LooseItem);

        Assert.Empty(line.References);
        Assert.False(line.CompanyAverage.HasEnoughData);
        Assert.Contains("kütüphaneye bağlı değil", line.CompanyAverage.Explanation);
    }

    // -----------------------------------------------------------------
    // Kâr
    // -----------------------------------------------------------------

    [Fact]
    public async Task Profit_IsContractMinusActualCost()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 240_000m, seed.LinkedItem);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.Equal(240_000m, line.ActualCost);
        Assert.Equal(60_000m, line.Profit);
        Assert.Equal(20m, line.ProfitMarginPercent);

        // Proje toplamı: 400.000 sözleşme, 240.000 maliyet.
        Assert.Equal(400_000m, result.ContractTotal);
        Assert.Equal(160_000m, result.Profit);
        Assert.Equal(40m, result.ProfitMarginPercent);
    }

    [Fact]
    public async Task Loss_IsReportedAsNegative_NotClamped()
    {
        // Zarar sıfıra kırpılırsa sapan poz görünmez olur.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 360_000m, seed.LinkedItem);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.Equal(-60_000m, line.Profit);
        Assert.Equal(-20m, line.ProfitMarginPercent);
    }

    [Fact]
    public async Task MeasuredAndAllocatedCost_StayVisiblySeparate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 60_000m, seed.LinkedItem);

        // Kısma yazılmış 40.000; 300.000:100.000 oranıyla 30.000 / 10.000.
        AddCost(db, seed, 40_000m, null);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.Equal(60_000m, line.MeasuredCost);
        Assert.Equal(30_000m, line.AllocatedCost);
        Assert.Equal(90_000m, line.ActualCost);
        Assert.Equal(210_000m, line.Profit);

        // Kârın ne kadarı ölçüme, ne kadarı tahmine dayanıyor.
        Assert.Equal(0.6667m, decimal.Round(line.MeasuredRatio, 4));

        Assert.Equal(60_000m, result.MeasuredCostTotal);
        Assert.Equal(40_000m, result.AllocatedCostTotal);
    }

    [Fact]
    public async Task UnassignedCost_IsFlaggedAsOptimisticProfit()
    {
        // Poza bağlanamayan maliyet satır kârına girmiyor; bu sessiz
        // kalırsa proje kârı olduğundan iyi görünür.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 25_000m, null, withSection: false);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);

        Assert.Equal(25_000m, result!.UnassignedCost);
        Assert.Equal(400_000m, result.Profit);
        Assert.Contains(result.Assumptions, x => x.Contains("iyimser"));
    }

    // -----------------------------------------------------------------
    // Şirket gerçek ortalaması
    // -----------------------------------------------------------------

    [Fact]
    public async Task CompanyAverage_NeedsTwoProjects()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await SeedAsync(db, suffix);

        // Tek geçmiş proje: 100.000 / 500 m = 200 TL/m — ama yetmez.
        await AddHistoricalProjectAsync(
            db, seed, $"{suffix}a", taggedCost: 100_000m, measuredQuantity: 500m);

        var single = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var singleLine = single!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.False(singleLine.CompanyAverage.HasEnoughData);
        Assert.Null(singleLine.CompanyAverage.AverageUnitCost);
        Assert.Contains("Yalnızca 1 projede", singleLine.CompanyAverage.Explanation);

        // İkinci proje: 90.000 / 300 m = 300 TL/m. Ortalama 250.
        await AddHistoricalProjectAsync(
            db, seed, $"{suffix}b", taggedCost: 90_000m, measuredQuantity: 300m);

        var both = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = both!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.True(line.CompanyAverage.HasEnoughData);
        Assert.Equal(2, line.CompanyAverage.ProjectCount);
        Assert.Equal(250m, line.CompanyAverage.AverageUnitCost);
        Assert.Equal(200m, line.CompanyAverage.MinUnitCost);
        Assert.Equal(300m, line.CompanyAverage.MaxUnitCost);
    }

    [Fact]
    public async Task CompanyAverage_IgnoresAllocatedCost()
    {
        // Dağıtılmış tutar bir tahmindir; ortalamaya girerse tahminlerin
        // ortalaması "şirketin gerçek maliyeti" diye sunulmuş olur.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await SeedAsync(db, suffix);

        await AddHistoricalProjectAsync(
            db, seed, $"{suffix}a", 100_000m, 500m, tagCostToItem: false);
        await AddHistoricalProjectAsync(
            db, seed, $"{suffix}b", 90_000m, 300m, tagCostToItem: false);

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.False(line.CompanyAverage.HasEnoughData);
        Assert.Contains("etiketlenmiş gerçekleşme yok", line.CompanyAverage.Explanation);
    }

    [Fact]
    public async Task CompanyAverage_IgnoresUnapprovedMeasurement()
    {
        // Onaylanmamış metraj değişebilir; birim maliyetin paydası olamaz.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await SeedAsync(db, suffix);

        await AddHistoricalProjectAsync(db, seed, $"{suffix}a", 100_000m, 500m);
        await AddHistoricalProjectAsync(
            db, seed, $"{suffix}b", 90_000m, 300m, approved: false);

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.False(line.CompanyAverage.HasEnoughData);
        Assert.Contains("Yalnızca 1 projede", line.CompanyAverage.Explanation);
    }

    [Fact]
    public async Task CompanyAverage_ExcludesCurrentProject()
    {
        // Projenin kendi maliyeti kendi karşılaştırma ölçütü olamaz.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, 240_000m, seed.LinkedItem);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var line = result!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.False(line.CompanyAverage.HasEnoughData);
        Assert.Contains("başka projede kullanılmamış", line.CompanyAverage.Explanation);
    }

    // -----------------------------------------------------------------
    // Gizlilik
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExtraPayment_IsExcludedForUnauthorisedViewer()
    {
        // Yetkisiz kullanıcı resmi bordro bazlı maliyeti görür; elden
        // ödeme kâr rakamına da girmez.
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
            ProjectBoqItemId = seed.LinkedItem,
            WorkDate = DateTime.UtcNow.Date,
            // 100.000 resmi + 20.000 elden.
            TotalLaborCost = 120_000m,
            CompensationCost = 20_000m
        });

        await db.SaveChangesAsync();

        var authorised = await CreateService(db).GetAsync(seed.ProjectId, 2026);
        var authorisedLine = authorised!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.True(authorised.IncludesExtraPayments);
        Assert.Equal(120_000m, authorisedLine.ActualCost);
        Assert.Equal(180_000m, authorisedLine.Profit);

        var restricted = await CreateService(db, canSeeExtraPayments: false)
            .GetAsync(seed.ProjectId, 2026);
        var restrictedLine = restricted!.Lines.Single(x => x.BoqItemId == seed.LinkedItem);

        Assert.False(restricted.IncludesExtraPayments);
        Assert.Equal(100_000m, restrictedLine.ActualCost);
        Assert.Equal(200_000m, restrictedLine.Profit);
        Assert.Contains(restricted.Assumptions, x => x.Contains("resmi bordro"));
    }

    [Fact]
    public async Task UnknownProject_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Null(await CreateService(db).GetAsync(Guid.NewGuid()));
    }
}
