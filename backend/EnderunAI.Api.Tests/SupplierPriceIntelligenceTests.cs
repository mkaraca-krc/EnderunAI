using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Purchasing;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tedarikçi fiyat zekâsı (K6): pozun reçetesindeki malzemelerin
/// GERÇEK alış fiyatı.
///
/// Zincir: poz → reçete → reçete malzemesi → stok kartı → alış
/// faturası. Bu paketin asıl güvencesi zincir koptuğunda SAYI
/// ÜRETİLMEMESİ. Eksik bir toplam, "bu poz ucuza mal oluyor" diye
/// okunur ve teklif fiyatı yanlış kurulur; hiç rakam vermemek buna
/// göre çok daha güvenli.
/// </summary>
[Collection("Integration")]
public sealed class SupplierPriceIntelligenceTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid PositionId, Guid SupplierId, Guid InventoryItemId);

    /// <summary>
    /// Tek malzemeli bir reçetesi olan poz kurar.
    /// </summary>
    /// <param name="linkStockCard">Reçete satırı stok kartına bağlansın mı.</param>
    private async Task<Context> CreateContextAsync(
        string suffix, bool linkStockCard)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, supplier) = await TestDataFactory.CreateCompanyStackAsync(
            db, suffix);

        var inventoryItem = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"STK-{suffix}",
            Name = "NYY 3x2,5 kablo",
            Unit = "MTR"
        };

        db.InventoryItems.Add(inventoryItem);

        var position = new EngineeringPosition
        {
            CompanyId = company.Id,
            Code = $"POZ-{suffix}",
            Name = "NYY kablo çekilmesi",
            Unit = "MTR",
            Source = EngineeringPositionSource.Official,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync();

        var recipe = new EngineeringRecipe
        {
            EngineeringPositionId = position.Id,
            Version = 1
        };

        db.EngineeringRecipes.Add(recipe);
        await db.SaveChangesAsync();

        db.EngineeringRecipeMaterials.Add(new EngineeringRecipeMaterial
        {
            EngineeringRecipeId = recipe.Id,
            InventoryItemId = linkStockCard ? inventoryItem.Id : null,
            MaterialCode = inventoryItem.Code,
            MaterialName = inventoryItem.Name,
            Quantity = 1m,
            Unit = "MTR",
            WastePercent = 0m
        });

        await db.SaveChangesAsync();

        return new Context(company.Id, position.Id, supplier.Id, inventoryItem.Id);
    }

    /// <summary>
    /// Onaylı bir alış faturası yazar.
    /// </summary>
    private async Task PostInvoiceAsync(
        Context context,
        DateTime invoiceDate,
        decimal quantity,
        decimal unitPrice,
        SupplierInvoiceStatus status = SupplierInvoiceStatus.Approved,
        decimal exchangeRate = 1m)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var subtotal = decimal.Round(quantity * unitPrice, 2);

        db.SupplierInvoices.Add(new SupplierInvoice
        {
            CompanyId = context.CompanyId,
            SupplierCurrentAccountId = context.SupplierId,
            InvoiceNumber = $"FTR-{Guid.NewGuid():N}"[..16],
            // Şirket başına benzersiz; boş bırakılırsa ikinci fatura
            // tekillik kısıtına takılır.
            InternalNumber = $"IC-{Guid.NewGuid():N}"[..16],
            InvoiceDate = DateTime.SpecifyKind(invoiceDate.Date, DateTimeKind.Utc),
            CurrencyCode = exchangeRate == 1m ? "TRY" : "USD",
            ExchangeRate = exchangeRate,
            Status = status,
            Subtotal = subtotal,
            GrandTotal = subtotal,
            Items =
            [
                new SupplierInvoiceItem
                {
                    LineNumber = 1,
                    InventoryItemId = context.InventoryItemId,
                    Description = "NYY 3x2,5 kablo",
                    Quantity = quantity,
                    Unit = "MTR",
                    UnitPrice = unitPrice,
                    LineSubtotal = subtotal,
                    LineTotal = subtotal
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    private async Task<Services.Purchasing.PositionPurchaseIntelligence?> AnalyzeAsync(
        Context context, int? months = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<SupplierPriceIntelligenceService>();

        return await service.AnalyzeAsync(
            context.CompanyId, context.PositionId, months, null, default);
    }

    /// <summary>
    /// Son alış ve ağırlıklı ortalama ayrı ayrı doğru hesaplanmalı:
    /// ikisi farklı sorulara cevap verir ("bugün kaça alırım" ve
    /// "dönem boyunca kaça aldık").
    /// </summary>
    [Fact]
    public async Task ComputesLastPurchaseAndWeightedAverage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, linkStockCard: true);

        var today = DateTime.UtcNow.Date;

        // 100 mtr × 10 TL ve 300 mtr × 14 TL
        // Ağırlıklı ortalama = (1.000 + 4.200) / 400 = 13
        await PostInvoiceAsync(context, today.AddDays(-60), 100m, 10m);
        await PostInvoiceAsync(context, today.AddDays(-10), 300m, 14m);

        var result = await AnalyzeAsync(context);

        Assert.NotNull(result);

        var material = Assert.Single(result!.Materials);

        Assert.True(material.HasStockLink);
        Assert.Equal(14m, material.LastPurchaseUnitPrice);
        Assert.Equal(13m, material.WeightedAverageUnitPrice);
        Assert.Equal(400m, material.PurchasedQuantity);
        Assert.Equal(2, material.InvoiceCount);

        // Reçetede 1 mtr olduğu için poz maliyeti birim fiyata eşit
        Assert.Equal(14m, result.LastPurchaseMaterialCost);
        Assert.Equal(13m, result.WeightedAverageMaterialCost);
        Assert.Equal(1, result.PricedMaterialCount);
    }

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: reçete satırı stok kartına bağlı
    /// değilse fiyat aranmaz ve poz toplamı üretilmez.
    /// </summary>
    [Fact]
    public async Task WithoutStockLink_ProducesNoNumber()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, linkStockCard: false);

        await PostInvoiceAsync(context, DateTime.UtcNow.Date.AddDays(-5), 100m, 10m);

        var result = await AnalyzeAsync(context);

        Assert.NotNull(result);

        var material = Assert.Single(result!.Materials);

        Assert.False(material.HasStockLink);
        Assert.Null(material.LastPurchaseUnitPrice);
        Assert.Null(material.WeightedAverageUnitPrice);

        Assert.Null(result.LastPurchaseMaterialCost);
        Assert.Null(result.WeightedAverageMaterialCost);
        Assert.Equal(0, result.LinkedMaterialCount);
        Assert.Contains(result.Warnings, x => x.Contains("stok kartına bağlı değil"));
    }

    /// <summary>
    /// Taslak fatura henüz gerçek bir alış değildir; ortalamaya
    /// girmemeli.
    /// </summary>
    [Fact]
    public async Task DraftInvoice_IsExcluded()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, linkStockCard: true);

        await PostInvoiceAsync(
            context, DateTime.UtcNow.Date.AddDays(-5), 100m, 999m,
            SupplierInvoiceStatus.Draft);

        var result = await AnalyzeAsync(context);

        Assert.NotNull(result);
        Assert.Null(result!.Materials[0].LastPurchaseUnitPrice);
        Assert.Contains(result.Warnings, x => x.Contains("onaylı"));
    }

    /// <summary>
    /// Pencere dışındaki alış ortalamaya girmemeli: enflasyonlu bir
    /// ortamda iki yıl önceki fiyat bugünkü maliyeti olduğundan düşük
    /// gösterir.
    /// </summary>
    [Fact]
    public async Task PurchasesOutsideWindow_AreExcluded()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, linkStockCard: true);

        await PostInvoiceAsync(
            context, DateTime.UtcNow.Date.AddMonths(-30), 100m, 5m);
        await PostInvoiceAsync(
            context, DateTime.UtcNow.Date.AddDays(-3), 100m, 20m);

        var result = await AnalyzeAsync(context, months: 12);

        Assert.NotNull(result);
        Assert.Equal(20m, result!.Materials[0].WeightedAverageUnitPrice);
        Assert.Equal(1, result.Materials[0].InvoiceCount);
    }

    /// <summary>
    /// Dövizli fatura kendi günündeki kurla TL'ye çevrilmeli; bugünkü
    /// kurla çarpmak ne fiyat ne kur değişimini doğru gösteren üçüncü
    /// bir sayı üretirdi.
    /// </summary>
    [Fact]
    public async Task ForeignCurrencyInvoice_IsConvertedWithItsOwnRate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, linkStockCard: true);

        // 100 birim × 2 USD, kur 30 → TL birim fiyat 60
        await PostInvoiceAsync(
            context, DateTime.UtcNow.Date.AddDays(-5), 100m, 2m,
            SupplierInvoiceStatus.Approved, exchangeRate: 30m);

        var result = await AnalyzeAsync(context);

        Assert.NotNull(result);
        Assert.Equal(60m, result!.Materials[0].LastPurchaseUnitPrice);
        Assert.Equal(60m, result.LastPurchaseMaterialCost);
    }

    /// <summary>
    /// Reçetesi olmayan poz için malzeme analizi yapılamaz; uyarı
    /// verilip boş dönülmeli, sıfır maliyet üretilmemeli.
    /// </summary>
    [Fact]
    public async Task WithoutRecipe_WarnsAndReturnsNoCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId, positionId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(
                db, suffix);

            var position = new EngineeringPosition
            {
                CompanyId = company.Id,
                Code = $"POZ-{suffix}",
                Name = "Reçetesiz poz",
                Unit = "AD",
                Source = EngineeringPositionSource.Official,
                Discipline = EngineeringPositionDiscipline.Electrical,
                Status = EngineeringPositionStatus.Active
            };

            db.EngineeringPositions.Add(position);
            await db.SaveChangesAsync();

            companyId = company.Id;
            positionId = position.Id;
        }

        using var verify = fixture.Factory.Services.CreateScope();
        var service = verify.ServiceProvider
            .GetRequiredService<SupplierPriceIntelligenceService>();

        var result = await service.AnalyzeAsync(companyId, positionId, null, null, default);

        Assert.NotNull(result);
        Assert.Null(result!.EngineeringRecipeId);
        Assert.Null(result.LastPurchaseMaterialCost);
        Assert.Empty(result.Materials);
        Assert.Contains(result.Warnings, x => x.Contains("reçetesi yok"));
    }
}
