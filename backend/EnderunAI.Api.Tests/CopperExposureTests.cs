using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Services.Market;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Bakır ve kur hareketinin kalan işe etkisi.
///
/// İki güvence var. Birincisi ayrıştırma: TL etkisi emtia, kur ve
/// birleşik artık olarak üçe bölünür; toplamları TL farkını birebir
/// vermeli, yoksa "bakır mı, kur mu" sorusu cevapsız kalır. İkincisi
/// dürüstlük: tonaj bilinmiyorsa etki SIFIR değil BOŞ döner — sıfır
/// "bakır riski yok" demektir ve yanlış güven verir.
/// </summary>
[Collection("Integration")]
public sealed class CopperExposureTests(DatabaseFixture fixture)
{
    private static readonly DateTime Baseline =
        new(2014, 5, 12, 0, 0, 0, DateTimeKind.Utc);

    private const decimal BaselinePrice = 10_000m;
    private const decimal BaselineRate = 2.00m;

    private const decimal CurrentPrice = 11_000m;
    private const decimal CurrentRate = 2.20m;

    private static CopperExposureService CreateService(AppDbContext db)
        => new(
            db,
            new CommodityPriceService(
                db,
                new UnusedSource(),
                new ExchangeRateService(
                    db, new UnusedTcmbClient(), NullLogger<ExchangeRateService>.Instance),
                NullLogger<CommodityPriceService>.Instance),
            NullLogger<CopperExposureService>.Instance);

    private sealed class UnusedSource : ICommodityPriceSource
    {
        public CommodityPriceSourceKind Kind => CommodityPriceSourceKind.Comex;
        public string Symbol => "HG=F";
        public string DisplayName => "COMEX bakır vadeli (LME değil)";

        public Task<CommodityFetchResult> GetDailyPricesAsync(
            int days, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommodityFetchResult([], "test"));
    }

    private sealed class UnusedTcmbClient : ITcmbRateClient
    {
        public Task<(TcmbBulletin? Bulletin, string? Error)> GetBulletinAsync(
            DateTime date, CancellationToken cancellationToken = default)
            => Task.FromResult<(TcmbBulletin?, string?)>((null, null));
    }

    private static async Task SetPriceAsync(
        AppDbContext db, DateTime date, decimal usdPerTon, decimal usdRate)
    {
        var utc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var existing = await db.CommodityPrices
            .SingleOrDefaultAsync(x => x.PriceDate == utc && x.Commodity == Commodity.Copper);

        if (existing is null)
        {
            existing = new CommodityPrice
            {
                PriceDate = utc,
                Commodity = Commodity.Copper,
                SourceKind = CommodityPriceSourceKind.Comex,
                SourceSymbol = "HG=F"
            };
            db.CommodityPrices.Add(existing);
        }

        existing.PriceUsdPerTon = usdPerTon;
        existing.UsdRate = usdRate;
        existing.PriceTryPerTon = decimal.Round(usdPerTon * usdRate, 2);

        await db.SaveChangesAsync();
    }

    /// <summary>Taban ve bugün için fiyat kurar, sözleşme tarihini tabana çeker.</summary>
    private async Task<Project> CreateProjectWithPricesAsync(
        AppDbContext db, ProjectContractType contractType)
    {
        var project = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        project.ContractType = contractType;
        project.ContractDate = Baseline;
        await db.SaveChangesAsync();

        await SetPriceAsync(db, Baseline, BaselinePrice, BaselineRate);
        await SetPriceAsync(db, DateTime.UtcNow.Date, CurrentPrice, CurrentRate);

        return project;
    }

    [Fact]
    public async Task Impact_SplitsCopperFxAndCombinedEffects()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);
        var service = CreateService(db);

        await service.SaveExposureAsync(
            project.Id, new CopperExposureInput(100m, null, null));

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.NotNull(impact);
        Assert.Equal(100m, impact.RemainingTons);
        Assert.Equal(CopperTonnageSource.Manual, impact.TonnageSource);

        // Bakır %10, kur %10.
        Assert.Equal(10m, impact.CopperChangePercent);
        Assert.Equal(10m, impact.FxChangePercent);

        // 100 ton üzerinden:
        //   bakır  = 100 x (11.000 − 10.000) x 2,00 =  200.000
        //   kur    = 100 x 10.000 x (2,20 − 2,00)   =  200.000
        //   artık  = 100 x 1.000 x 0,20             =   20.000
        Assert.Equal(200_000m, impact.CopperEffect);
        Assert.Equal(200_000m, impact.FxEffect);
        Assert.Equal(20_000m, impact.CombinedEffect);
        Assert.Equal(420_000m, impact.TotalEffect);

        // Bileşenlerin toplamı TL farkına birebir eşit olmalı.
        var expected = 100m * (CurrentPrice * CurrentRate - BaselinePrice * BaselineRate);
        Assert.Equal(expected, impact.TotalEffect);
    }

    [Fact]
    public async Task Impact_LumpSum_IsFlaggedAsCostRisk()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);
        var service = CreateService(db);

        await service.SaveExposureAsync(
            project.Id, new CopperExposureInput(10m, null, null));

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.True(impact!.IsCostRisk);
        Assert.Equal("Anahtar teslim (götürü)", impact.ContractTypeName);
    }

    [Fact]
    public async Task Impact_UnitPrice_IsInformationalOnly()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.UnitPrice);
        var service = CreateService(db);

        await service.SaveExposureAsync(
            project.Id, new CopperExposureInput(10m, null, null));

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.False(impact!.IsCostRisk);
        Assert.Contains(impact.Assumptions, x => x.Contains("bilgi amaçlı"));

        // Etki yine hesaplanır; yalnızca yorumu değişir.
        Assert.NotNull(impact.TotalEffect);
    }

    [Fact]
    public async Task Impact_WithoutTonnage_ReturnsNullNotZero()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);
        var service = CreateService(db);

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.Equal(CopperTonnageSource.Unknown, impact!.TonnageSource);
        Assert.Null(impact.RemainingTons);
        Assert.Null(impact.TotalEffect);
        Assert.Null(impact.CopperEffect);

        // Yüzde değişimler yine gösterilebilir; eksik olan yalnızca tonaj.
        Assert.Equal(10m, impact.CopperChangePercent);
        Assert.Contains(impact.Assumptions, x => x.Contains("tonajı bilinmiyor"));
    }

    [Fact]
    public async Task Impact_DerivesTonnageFromBoqCoefficients()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{Guid.NewGuid():N}"[..12],
            Name = "Keşif",
            IsCurrentRevision = true
        };
        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        db.ProjectBoqItems.AddRange(
            new ProjectBoqItem
            {
                ProjectBoqId = boq.Id,
                LineNumber = 1,
                PositionCode = "K-1",
                Description = "NYY 4x16 kablo",
                Unit = "MTR",
                ContractQuantity = 5_000m,
                // 5.000 m x 0,6 kg = 3.000 kg
                CopperKgPerUnit = 0.6m
            },
            new ProjectBoqItem
            {
                ProjectBoqId = boq.Id,
                LineNumber = 2,
                PositionCode = "M-1",
                Description = "Pano montajı",
                Unit = "AD",
                ContractQuantity = 20m
                // Katsayı yok: hesaba hiç girmemeli.
            });

        await db.SaveChangesAsync();

        var service = CreateService(db);
        var impact = await service.GetForProjectAsync(project.Id);

        Assert.Equal(CopperTonnageSource.BillOfQuantities, impact!.TonnageSource);
        Assert.Equal(3m, impact.RemainingTons);
        Assert.Equal(3m * 420_000m / 100m, impact.TotalEffect);
    }

    [Fact]
    public async Task Impact_ManualTonnage_OverridesBoqDerivation()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{Guid.NewGuid():N}"[..12],
            Name = "Keşif",
            IsCurrentRevision = true
        };
        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        db.ProjectBoqItems.Add(new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            LineNumber = 1,
            PositionCode = "K-1",
            Description = "Kablo",
            Unit = "MTR",
            ContractQuantity = 5_000m,
            CopperKgPerUnit = 0.6m
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Sahayı bilen kişi 3 ton yerine 1,5 ton diyor: elle girilen kazanır.
        await service.SaveExposureAsync(
            project.Id, new CopperExposureInput(1.5m, null, "Yarısı döşendi"));

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.Equal(CopperTonnageSource.Manual, impact!.TonnageSource);
        Assert.Equal(1.5m, impact.RemainingTons);
    }

    [Fact]
    public async Task Impact_WithoutBaselinePrice_DoesNotInventOne()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        project.ContractType = ProjectContractType.LumpSum;
        // Sözleşme tarihi arşivdeki ilk fiyattan çok önce.
        project.ContractDate = new DateTime(1999, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        await SetPriceAsync(db, DateTime.UtcNow.Date, CurrentPrice, CurrentRate);

        var service = CreateService(db);
        await service.SaveExposureAsync(
            project.Id, new CopperExposureInput(50m, null, null));

        var impact = await service.GetForProjectAsync(project.Id);

        Assert.Null(impact!.BaselineUsdPerTon);
        Assert.Null(impact.TotalEffect);
        Assert.Contains(impact.Assumptions, x => x.Contains("bakır fiyatı bulunamadı"));
    }

    [Fact]
    public async Task SaveExposure_NegativeTonnage_IsRejected()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveExposureAsync(
                project.Id, new CopperExposureInput(-5m, null, null)));
    }

    [Fact]
    public async Task Portfolio_ExcludesArchivedAndClosedProjects()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var active = await CreateProjectWithPricesAsync(db, ProjectContractType.LumpSum);

        var archived = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);
        archived.CompanyId = active.CompanyId;
        archived.IsArchived = true;
        await db.SaveChangesAsync();

        var completed = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);
        completed.CompanyId = active.CompanyId;
        completed.Status = ProjectStatus.Completed;
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var portfolio = await service.GetPortfolioAsync(active.CompanyId);

        Assert.Contains(portfolio, x => x.ProjectId == active.Id);
        Assert.DoesNotContain(portfolio, x => x.ProjectId == archived.Id);
        Assert.DoesNotContain(portfolio, x => x.ProjectId == completed.Id);
    }
}
