using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Purchasing;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PROJE MALZEME İHTİYACI VE EKSİK:
///
///   eksik = ihtiyaç − depo mevcudu − açık talepler
///
/// Testlerin asıl güvencesi ÇİFT SAYIM: aynı ihtiyaç iki kez talep
/// edilmemeli. Bir kez talep açıldıktan sonra o miktar "açık talep"
/// olarak düşülür ve ikinci çalıştırmada eksik kalmaz.
/// </summary>
[Collection("Integration")]
public sealed class ProjectMaterialRequirementTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid ProjectId,
        Guid CompanyId,
        Guid CableItemId,
        Guid WarehouseId);

    /// <summary>
    /// Bir proje kurar: icmalde 100 birim poz, pozun reçetesinde 2
    /// birim kablo + %5 fire → 210 birim ihtiyaç.
    /// </summary>
    private async Task<Context> CreateProjectAsync(
        decimal stockQuantity = 0m,
        bool secondPositionSharesCable = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var cable = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"KBL-{suffix}",
            Name = "NYA Kablo",
            Unit = "m",
            Brand = "Öznur",
            Type = InventoryItemType.Material
        };

        db.InventoryItems.Add(cable);

        var warehouse = new Warehouse
        {
            CompanyId = project.CompanyId,
            BranchId = project.BranchId,
            ProjectId = project.Id,
            Code = $"DEPO-{suffix}",
            Name = "Şantiye deposu",
            Type = WarehouseType.Site
        };

        db.Warehouses.Add(warehouse);

        if (stockQuantity > 0)
        {
            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = cable.Id,
                Quantity = stockQuantity
            });
        }

        var position = new EngineeringPosition
        {
            CompanyId = project.CompanyId,
            Code = $"POZ-A-{suffix}",
            Name = "Kablo çekimi",
            Unit = "m",
            Source = EngineeringPositionSource.Enderun,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        var withoutRecipe = new EngineeringPosition
        {
            CompanyId = project.CompanyId,
            Code = $"POZ-C-{suffix}",
            Name = "Reçetesiz iş",
            Unit = "m",
            Source = EngineeringPositionSource.Enderun,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        db.EngineeringPositions.AddRange(position, withoutRecipe);

        db.EngineeringRecipes.Add(new EngineeringRecipe
        {
            EngineeringPositionId = position.Id,
            Version = 1,
            IsDefault = true,
            Materials =
            {
                new EngineeringRecipeMaterial
                {
                    InventoryItemId = cable.Id,
                    MaterialCode = cable.Code,
                    MaterialName = cable.Name,
                    Quantity = 2m,
                    Unit = "m",
                    WastePercent = 5m
                }
            }
        });

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{suffix}",
            Name = "Sözleşme icmali",
            RevisionNumber = 1,
            Status = ProjectBoqStatus.Approved,
            Items =
            {
                new ProjectBoqItem
                {
                    LineNumber = 1,
                    EngineeringPositionId = position.Id,
                    PositionCode = position.Code,
                    Description = position.Name,
                    Unit = "m",
                    ContractQuantity = 100m
                },
                new ProjectBoqItem
                {
                    LineNumber = 2,
                    EngineeringPositionId = withoutRecipe.Id,
                    PositionCode = withoutRecipe.Code,
                    Description = withoutRecipe.Name,
                    Unit = "m",
                    ContractQuantity = 500m
                }
            }
        };

        if (secondPositionSharesCable)
        {
            var second = new EngineeringPosition
            {
                CompanyId = project.CompanyId,
                Code = $"POZ-B-{suffix}",
                Name = "İkinci kablo işi",
                Unit = "m",
                Source = EngineeringPositionSource.Enderun,
                Discipline = EngineeringPositionDiscipline.Electrical,
                Status = EngineeringPositionStatus.Active
            };

            db.EngineeringPositions.Add(second);

            db.EngineeringRecipes.Add(new EngineeringRecipe
            {
                EngineeringPositionId = second.Id,
                Version = 1,
                IsDefault = true,
                Materials =
                {
                    new EngineeringRecipeMaterial
                    {
                        InventoryItemId = cable.Id,
                        MaterialCode = cable.Code,
                        MaterialName = cable.Name,
                        Quantity = 1m,
                        Unit = "m",
                        WastePercent = 0m
                    }
                }
            });

            boq.Items.Add(new ProjectBoqItem
            {
                LineNumber = 3,
                EngineeringPositionId = second.Id,
                PositionCode = second.Code,
                Description = second.Name,
                Unit = "m",
                ContractQuantity = 40m
            });
        }

        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        return new Context(project.Id, project.CompanyId, cable.Id, warehouse.Id);
    }

    private async Task<T> WithScopeAsync<T>(
        Func<IProjectMaterialRequirementService, IProjectMaterialRequestBridge, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        return await action(
            scope.ServiceProvider.GetRequiredService<IProjectMaterialRequirementService>(),
            scope.ServiceProvider.GetRequiredService<IProjectMaterialRequestBridge>());
    }

    [Fact]
    public async Task Ihtiyac_IcmaldenVeReceteden_Hesaplanir()
    {
        var context = await CreateProjectAsync();

        var result = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        var line = Assert.Single(result.Lines);

        Assert.Equal(210m, line.RequiredQuantity);
        Assert.Equal(0m, line.StockQuantity);
        Assert.Equal(210m, line.ShortageQuantity);
        Assert.True(line.CanRequest);

        // Reçetesiz poz ihtiyaca SIFIR kattı ama uyarı olarak duruyor.
        Assert.Equal(1, result.PositionsWithoutRecipe);
        Assert.Single(result.MissingRecipes);
    }

    /// <summary>Depodaki mevcut eksikten düşer.</summary>
    [Fact]
    public async Task DepoMevcudu_EksiktenDuser()
    {
        var context = await CreateProjectAsync(stockQuantity: 60m);

        var result = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        var line = Assert.Single(result.Lines);

        Assert.Equal(210m, line.RequiredQuantity);
        Assert.Equal(60m, line.StockQuantity);
        Assert.Equal(150m, line.ShortageQuantity);
    }

    /// <summary>Depo ihtiyacı aşarsa eksik NEGATİFE düşmez, sıfırdır.</summary>
    [Fact]
    public async Task DepoIhtiyaciAsarsa_EksikSifir()
    {
        var context = await CreateProjectAsync(stockQuantity: 500m);

        var result = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        Assert.Equal(0m, Assert.Single(result.Lines).ShortageQuantity);
    }

    /// <summary>Aynı malzeme iki pozdan: 100×2×1,05 + 40×1 = 250.</summary>
    [Fact]
    public async Task AyniMalzemeIkiPozdan_TekSatirdaToplanir()
    {
        var context = await CreateProjectAsync(secondPositionSharesCable: true);

        var result = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        var line = Assert.Single(result.Lines);

        Assert.Equal(250m, line.RequiredQuantity);
        Assert.Equal(2, line.SourceLineCount);
    }

    /// <summary>
    /// ASIL GÜVENCE — ÇİFT SAYIM: talep açıldıktan sonra o miktar açık
    /// talep olarak düşülür; ikinci çalıştırmada eksik kalmaz ve aynı
    /// ihtiyaç ikinci kez talep edilemez.
    /// </summary>
    [Fact]
    public async Task AyniIhtiyac_IkinciKezTalepEdilemez()
    {
        var context = await CreateProjectAsync();

        var created = await WithScopeAsync((_, bridge) => bridge.CreateAsync(
            context.ProjectId,
            new CreateMaterialRequestFromRequirementRequest(
                "Şantiye Şefi", null, 1,
                [new MaterialRequestBridgeLine(context.CableItemId, 210m)]),
            CancellationToken.None));

        Assert.Equal(1, created.ItemCount);
        Assert.Equal(210m, created.TotalQuantity);

        // İkinci okumada eksik sıfır: talep açık durumda bekliyor.
        var second = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        var line = Assert.Single(second.Lines);

        Assert.Equal(210m, line.OpenRequestedQuantity);
        Assert.Equal(0m, line.ShortageQuantity);

        // İkinci talep denemesi REDDEDİLİR.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithScopeAsync((_, bridge) => bridge.CreateAsync(
                context.ProjectId,
                new CreateMaterialRequestFromRequirementRequest(
                    "Şantiye Şefi", null, 1,
                    [new MaterialRequestBridgeLine(context.CableItemId, 210m)]),
                CancellationToken.None)));

        Assert.Contains("eksik kalmamış", error.Message);
    }

    /// <summary>
    /// İstemciden gelen miktar EKSİKLE SINIRLANIR. Ekranın gördüğü
    /// eksik bayat olabilir; sayıya güvenilseydi aynı ihtiyaç iki kez
    /// talep edilirdi.
    /// </summary>
    [Fact]
    public async Task EksigiAsanMiktar_Kirpilir()
    {
        var context = await CreateProjectAsync(stockQuantity: 200m);

        var created = await WithScopeAsync((_, bridge) => bridge.CreateAsync(
            context.ProjectId,
            new CreateMaterialRequestFromRequirementRequest(
                "Şantiye Şefi", null, 1,
                [new MaterialRequestBridgeLine(context.CableItemId, 999m)]),
            CancellationToken.None));

        // 210 ihtiyaç − 200 depo = 10 eksik.
        Assert.Equal(10m, created.TotalQuantity);
        Assert.Contains(created.Adjustments, x => x.Contains("indirildi"));
    }

    /// <summary>
    /// Talep kalemi kaynağına bağlı açılır: stok kartı ve marka.
    /// Marka kartta varsa istenen marka olarak taşınır — marka kuralı
    /// zincirde yaşıyor.
    /// </summary>
    [Fact]
    public async Task TalepKalemi_StokKartiVeMarkaylaAcilir()
    {
        var context = await CreateProjectAsync();

        var created = await WithScopeAsync((_, bridge) => bridge.CreateAsync(
            context.ProjectId,
            new CreateMaterialRequestFromRequirementRequest(
                "Şantiye Şefi", null, 1,
                [new MaterialRequestBridgeLine(context.CableItemId, 0m)]),
            CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.PurchaseRequestItems
            .SingleAsync(x => x.PurchaseRequestId == created.PurchaseRequestId);

        Assert.Equal(context.CableItemId, item.InventoryItemId);
        Assert.Equal("Öznur", item.RequestedBrand);
        Assert.False(item.BrandIrrelevant);

        // Miktar sıfır gönderildiğinde kalan eksiğin tamamı istenir.
        Assert.Equal(210m, item.Quantity);

        var request = await db.PurchaseRequests
            .SingleAsync(x => x.Id == created.PurchaseRequestId);

        Assert.Equal(PurchaseRequestStatus.Draft, request.Status);
        Assert.Equal(context.ProjectId, request.ProjectId);
    }

    /// <summary>
    /// İPTAL EDİLEN talep açık sayılmaz: iptalden sonra eksik geri
    /// gelmeli, yoksa malzeme hiç talep edilemez hâle gelirdi.
    /// </summary>
    [Fact]
    public async Task IptalEdilenTalep_AcikSayilmaz()
    {
        var context = await CreateProjectAsync();

        var created = await WithScopeAsync((_, bridge) => bridge.CreateAsync(
            context.ProjectId,
            new CreateMaterialRequestFromRequirementRequest(
                "Şantiye Şefi", null, 1,
                [new MaterialRequestBridgeLine(context.CableItemId, 210m)]),
            CancellationToken.None));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var request = await db.PurchaseRequests
                .SingleAsync(x => x.Id == created.PurchaseRequestId);

            request.Status = PurchaseRequestStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var result = await WithScopeAsync((service, _) =>
            service.GetAsync(context.ProjectId, false, CancellationToken.None));

        var line = Assert.Single(result.Lines);

        Assert.Equal(0m, line.OpenRequestedQuantity);
        Assert.Equal(210m, line.ShortageQuantity);
    }
}
