using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İcmal satırı bazında gerçekleşen maliyet.
///
/// Asıl güvence ÖLÇÜLMÜŞ ile DAĞITILMIŞ ayrımının korunması. Ölçülmüş,
/// girişte poza etiketlenmiş gerçek maliyettir; dağıtılmış, kısma
/// yazılmış ama poza etiketlenmemiş tutarın sözleşme oranında bölünmüş
/// payıdır — yani bir tahmindir. İkisini toplayıp tek rakam sunmak,
/// sapan pozu görünmez kılar.
/// </summary>
[Collection("Integration")]
public sealed class BoqItemCostTests(DatabaseFixture fixture)
{
    /// <summary>Ek ödemeyi gören/görmeyen kullanıcıyı taklit eder.</summary>
    private sealed class FixedVisibility(bool canView) : IExtraPaymentVisibilityService
    {
        public Task<bool> CanViewExtraPaymentAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(canView);
    }

    private sealed record Fixture(
        Guid ProjectId,
        Guid SectionId,
        Guid ItemA,
        Guid ItemB);

    /// <summary>
    /// Tek kısım, iki icmal satırı: A 300.000, B 100.000 (3:1 oran).
    /// </summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db, string suffix)
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
            IsCurrentRevision = true
        };
        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        var itemA = new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 1,
            PositionCode = "A-1",
            Description = "Kablo çekilmesi",
            Unit = "m",
            ContractQuantity = 1000,
            UnitPrice = 300,
            TotalAmount = 300_000
        };

        var itemB = new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 2,
            PositionCode = "B-1",
            Description = "Pano montajı",
            Unit = "Ad",
            ContractQuantity = 10,
            UnitPrice = 10_000,
            TotalAmount = 100_000
        };

        db.ProjectBoqItems.AddRange(itemA, itemB);
        await db.SaveChangesAsync();

        return new Fixture(project.Id, section.Id, itemA.Id, itemB.Id);
    }

    private static void AddCost(
        AppDbContext db,
        Fixture fixture,
        ProjectCostClass costClass,
        decimal amount,
        Guid? boqItemId,
        bool withSection = true)
    {
        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = fixture.ProjectId,
            ProjectHakedisSectionId = withSection ? fixture.SectionId : null,
            ProjectBoqItemId = boqItemId,
            CostType = ProjectCostType.Material,
            CostClass = costClass,
            CostDate = DateTime.UtcNow,
            Amount = amount,
            Description = "test"
        });
    }

    private static BoqItemCostService CreateService(AppDbContext db, bool canSeeExtraPayments)
        => new(db, new FixedVisibility(canSeeExtraPayments));

    [Fact]
    public async Task TaggedCost_IsMeasured_NotAllocated()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, ProjectCostClass.Material, 50_000, seed.ItemA);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        var a = snapshot.ByBoqItem[seed.ItemA];

        Assert.Equal(50_000, a.MeasuredMaterial);
        Assert.Equal(0, a.AllocatedTotal);
        Assert.Equal(1m, a.MeasuredRatio);

        // Etiketli maliyet diğer satıra sızmamalı.
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemB].Total);
    }

    [Fact]
    public async Task UntaggedSectionCost_IsAllocatedByContractShare()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        // Kısma yazılmış 40.000; A:B sözleşme oranı 3:1 → 30.000 / 10.000.
        AddCost(db, seed, ProjectCostClass.Material, 40_000, null);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        Assert.Equal(30_000, snapshot.ByBoqItem[seed.ItemA].AllocatedMaterial);
        Assert.Equal(10_000, snapshot.ByBoqItem[seed.ItemB].AllocatedMaterial);

        // Dağıtılan hiçbir kuruş ölçülmüş sayılmamalı.
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemA].MeasuredTotal);
        Assert.Equal(0m, snapshot.ByBoqItem[seed.ItemA].MeasuredRatio);

        Assert.Contains(snapshot.Assumptions, x => x.Contains("dağıtıldı"));
    }

    [Fact]
    public async Task AllocationSum_EqualsSectionPool()
    {
        // Dağıtım hiçbir tutarı kaybetmemeli ya da uydurmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, ProjectCostClass.Material, 12_345.67m, null);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        var distributed =
            snapshot.ByBoqItem[seed.ItemA].AllocatedMaterial
            + snapshot.ByBoqItem[seed.ItemB].AllocatedMaterial;

        Assert.Equal(12_345.67m, distributed);
    }

    [Fact]
    public async Task MixedCost_KeepsMeasuredAndAllocatedSeparate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, ProjectCostClass.Material, 20_000, seed.ItemA);
        AddCost(db, seed, ProjectCostClass.Material, 40_000, null);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);
        var a = snapshot.ByBoqItem[seed.ItemA];

        Assert.Equal(20_000, a.MeasuredMaterial);
        Assert.Equal(30_000, a.AllocatedMaterial);
        Assert.Equal(50_000, a.Total);

        // Toplamın %40'ı ölçüm, gerisi tahmin.
        Assert.Equal(0.4m, a.MeasuredRatio);
    }

    [Fact]
    public async Task CostWithoutSection_IsReportedAsUnassigned_NotSpread()
    {
        // Hiçbir kısma bağlanmamış maliyet için dağıtım tabanı yok;
        // rastgele bölmek yerine ayrı gösterilmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, ProjectCostClass.Overhead, 7_500, null, withSection: false);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        Assert.Equal(7_500, snapshot.UnassignedCost);
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemA].Total);
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemB].Total);
        Assert.Contains(snapshot.Assumptions, x => x.Contains("bağlanmamış"));
    }

    [Fact]
    public async Task Labor_TaggedToBoqItem_LandsAsMeasuredLabor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db,
            (await db.Projects.AsNoTracking().SingleAsync(x => x.Id == seed.ProjectId)).CompanyId,
            Guid.NewGuid().ToString("N")[..8]);

        db.HrProjectLaborCosts.Add(new HrProjectLaborCost
        {
            CompanyId = personnel.CompanyId,
            ProjectId = seed.ProjectId,
            PersonnelId = personnel.Id,
            ProjectBoqItemId = seed.ItemA,
            WorkDate = DateTime.UtcNow.Date,
            TotalLaborCost = 9_000,
            CompensationCost = 0
        });

        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        Assert.Equal(9_000, snapshot.ByBoqItem[seed.ItemA].MeasuredLabor);
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemA].MeasuredMaterial);
    }

    [Fact]
    public async Task ExtraPayment_IsExcludedForUnauthorisedViewer()
    {
        // GİZLİLİK: elden ödeme yetkisiz kullanıcının rakamına girmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db,
            (await db.Projects.AsNoTracking().SingleAsync(x => x.Id == seed.ProjectId)).CompanyId,
            Guid.NewGuid().ToString("N")[..8]);

        db.HrProjectLaborCosts.Add(new HrProjectLaborCost
        {
            CompanyId = personnel.CompanyId,
            ProjectId = seed.ProjectId,
            PersonnelId = personnel.Id,
            ProjectBoqItemId = seed.ItemA,
            WorkDate = DateTime.UtcNow.Date,
            // 10.000 resmi + 2.000 elden.
            TotalLaborCost = 12_000,
            CompensationCost = 2_000
        });

        await db.SaveChangesAsync();

        var authorised = await CreateService(db, true).GetAsync(seed.ProjectId);
        Assert.Equal(12_000, authorised.ByBoqItem[seed.ItemA].MeasuredLabor);
        Assert.True(authorised.IncludesExtraPayments);

        var restricted = await CreateService(db, false).GetAsync(seed.ProjectId);
        Assert.Equal(10_000, restricted.ByBoqItem[seed.ItemA].MeasuredLabor);
        Assert.False(restricted.IncludesExtraPayments);
        Assert.Contains(restricted.Assumptions, x => x.Contains("resmi bordro"));
    }

    [Fact]
    public async Task SubcontractorLabor_CountsAsLaborNotOverhead()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        AddCost(db, seed, ProjectCostClass.SubcontractorLabor, 15_000, seed.ItemB);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        Assert.Equal(15_000, snapshot.ByBoqItem[seed.ItemB].MeasuredLabor);
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemB].MeasuredOther);
    }

    [Fact]
    public async Task SectionWithoutContractAmount_IsNotSpreadRandomly()
    {
        // Sözleşme tutarı sıfırsa oran hesaplanamaz; pay uydurulmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var items = await db.ProjectBoqItems
            .Where(x => x.Id == seed.ItemA || x.Id == seed.ItemB)
            .ToListAsync();

        foreach (var item in items)
            item.TotalAmount = 0;

        await db.SaveChangesAsync();

        AddCost(db, seed, ProjectCostClass.Material, 5_000, null);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db, true).GetAsync(seed.ProjectId);

        Assert.Equal(5_000, snapshot.UnassignedCost);
        Assert.Equal(0, snapshot.ByBoqItem[seed.ItemA].Total);
    }
}
