using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

public interface ISupplierInvoiceStockPoster
{
    /// <summary>
    /// Onaylanan ALIŞ faturasının kalemlerini depoya girer.
    /// Girilen kalem sayısını döner; stok girmeyen faturada 0.
    /// </summary>
    Task<int> PostAsync(SupplierInvoice invoice, CancellationToken cancellationToken);

    /// <summary>
    /// İade faturasının kalemlerini depodan çıkarır ve ortalama maliyeti
    /// geri sarar. Çıkarılan kalem sayısını döner.
    /// </summary>
    /// <param name="movementLabel">
    /// Stok hareketi açıklamasının başı. İptalde "Fatura iptali" yazar;
    /// depo geçmişine bakan kişi hareketin neden oluştuğunu görmeli.
    /// </param>
    Task<int> PostReturnAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken,
        string movementLabel = "Alış iadesi");
}

/// <summary>
/// Doğrudan alış faturasından stok girişi.
///
/// ÇİFT SAYMA KORUMASI: mal kabule bağlı faturada stok zaten mal kabul
/// kesinleştiğinde girmiştir; burada bir daha girilirse aynı malzeme
/// depoya iki kez eklenir ve ortalama maliyet bozulur. Bu yüzden
/// GoodsReceiptId dolu olan fatura hiç işlenmez.
///
/// Ortalama maliyet hesabı mal kabulle AYNI motordan geçer
/// (<see cref="WeightedAverageCostCalculator"/>) — malzeme hangi
/// kapıdan girerse girsin aynı maliyeti taşımalı.
/// </summary>
public sealed class SupplierInvoiceStockPoster(
    AppDbContext db,
    ICurrentUserService currentUser) : ISupplierInvoiceStockPoster
{
    public async Task<int> PostAsync(
        SupplierInvoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.InvoiceType != SupplierInvoiceType.Stock)
            return 0;

        if (invoice.GoodsReceiptId is not null)
            return 0;

        var items = await db.SupplierInvoiceItems
            .Where(x => x.SupplierInvoiceId == invoice.Id && x.InventoryItemId != null)
            .OrderBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            return 0;

        var now = DateTime.UtcNow;

        var inventoryItemIds = items
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        var inventoryItems = await db.InventoryItems
            .Where(x => inventoryItemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // Ortalama maliyet, malzemenin TÜM depolardaki toplam miktarı
        // üzerinden yürür — stok kartı tek maliyet taşır, depo başına
        // ayrı maliyet tutulmuyor.
        var priorQuantities = await db.WarehouseStocks
            .Where(x => inventoryItemIds.Contains(x.InventoryItemId))
            .GroupBy(x => x.InventoryItemId)
            .Select(g => new { InventoryItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.InventoryItemId, x => x.Quantity, cancellationToken);

        var warehouseStocks = await db.WarehouseStocks
            .Where(x => inventoryItemIds.Contains(x.InventoryItemId))
            .ToListAsync(cancellationToken);

        var posted = 0;

        foreach (var item in items)
        {
            var inventoryItemId = item.InventoryItemId!.Value;

            var warehouseId = item.WarehouseId ?? invoice.WarehouseId
                ?? throw new InvalidOperationException(
                    $"Kalem {item.LineNumber}: stok girişi için depo belirlenemedi.");

            var stock = warehouseStocks.SingleOrDefault(x =>
                x.WarehouseId == warehouseId && x.InventoryItemId == inventoryItemId);

            if (stock is null)
            {
                stock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    InventoryItemId = inventoryItemId,
                    CreatedByUserId = currentUser.UserId
                };

                warehouseStocks.Add(stock);
                db.WarehouseStocks.Add(stock);
            }

            stock.Quantity += item.Quantity;
            stock.UpdatedAtUtc = now;
            stock.UpdatedByUserId = currentUser.UserId;

            // Birim maliyet KDV HARİÇ ve TRY: dövizli faturada kur ile
            // çevrilir. KDV indirilecek olduğundan maliyete girmez.
            var unitCostTry = item.UnitPrice * invoice.ExchangeRate;

            var inventoryItem = inventoryItems[inventoryItemId];
            var priorQuantity = priorQuantities.GetValueOrDefault(inventoryItemId, 0m);

            inventoryItem.AverageUnitCost = WeightedAverageCostCalculator.Next(
                priorQuantity,
                inventoryItem.AverageUnitCost,
                item.Quantity,
                unitCostTry);

            inventoryItem.LastPurchasePrice = unitCostTry;
            inventoryItem.LastPurchaseDate = invoice.InvoiceDate;
            inventoryItem.UpdatedAtUtc = now;
            inventoryItem.UpdatedByUserId = currentUser.UserId;

            // Aynı üründen birden fazla kalem olabilir; sonraki kalemin
            // ortalaması güncel miktar üzerinden hesaplansın.
            priorQuantities[inventoryItemId] = priorQuantity + item.Quantity;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = invoice.CompanyId,
                WarehouseId = warehouseId,
                InventoryItemId = inventoryItemId,
                ProjectId = invoice.ProjectId,
                Type = StockMovementType.Receipt,
                Quantity = item.Quantity,
                ReferenceNumber = invoice.InvoiceNumber,
                MovementDate = invoice.InvoiceDate,
                Description =
                    $"Alış faturası {invoice.InternalNumber} — {item.Description}",
                UnitCost = unitCostTry,
                TotalCost = decimal.Round(item.Quantity * unitCostTry, 2),
                CreatedByUserId = currentUser.UserId
            });

            posted++;
        }

        return posted;
    }

    public async Task<int> PostReturnAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken,
        string movementLabel = "Alış iadesi")
    {
        if (invoice.InvoiceType != SupplierInvoiceType.Stock)
            return 0;

        var items = await db.SupplierInvoiceItems
            .Where(x => x.SupplierInvoiceId == invoice.Id && x.InventoryItemId != null)
            .OrderBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            return 0;

        var now = DateTime.UtcNow;

        var inventoryItemIds = items
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        var inventoryItems = await db.InventoryItems
            .Where(x => inventoryItemIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var priorQuantities = await db.WarehouseStocks
            .Where(x => inventoryItemIds.Contains(x.InventoryItemId))
            .GroupBy(x => x.InventoryItemId)
            .Select(g => new { InventoryItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.InventoryItemId, x => x.Quantity, cancellationToken);

        var warehouseStocks = await db.WarehouseStocks
            .Where(x => inventoryItemIds.Contains(x.InventoryItemId))
            .ToListAsync(cancellationToken);

        var posted = 0;

        foreach (var item in items)
        {
            var inventoryItemId = item.InventoryItemId!.Value;

            var warehouseId = item.WarehouseId ?? invoice.WarehouseId
                ?? throw new InvalidOperationException(
                    $"Kalem {item.LineNumber}: iade için depo belirlenemedi.");

            var stock = warehouseStocks.SingleOrDefault(x =>
                x.WarehouseId == warehouseId && x.InventoryItemId == inventoryItemId);

            var inventoryItem = inventoryItems[inventoryItemId];

            // Depoda o kadar mal yoksa iade edilemez: stok negatife
            // düşerse sayım da maliyet de anlamını yitirir. Malzeme
            // zaten kullanıldıysa bu bir stok düzeltmesidir, iade değil.
            if (stock is null || stock.Quantity < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"Kalem {item.LineNumber} ({inventoryItem.Code}): depoda " +
                    $"{stock?.Quantity ?? 0m:N4} {inventoryItem.Unit} var, " +
                    $"{item.Quantity:N4} iade edilemez. Malzeme kullanıldıysa " +
                    "iade yerine stok düzeltmesi yapın.");
            }

            stock.Quantity -= item.Quantity;
            stock.UpdatedAtUtc = now;
            stock.UpdatedByUserId = currentUser.UserId;

            // İade, malın GİRDİĞİ fiyatla çıkar; iade faturasının kalemleri
            // orijinal faturadan kopyalandığı için birim fiyat zaten odur.
            var unitCostTry = item.UnitPrice * invoice.ExchangeRate;
            var priorQuantity = priorQuantities.GetValueOrDefault(inventoryItemId, 0m);

            inventoryItem.AverageUnitCost = WeightedAverageCostCalculator.Remove(
                priorQuantity,
                inventoryItem.AverageUnitCost,
                item.Quantity,
                unitCostTry);

            inventoryItem.UpdatedAtUtc = now;
            inventoryItem.UpdatedByUserId = currentUser.UserId;

            priorQuantities[inventoryItemId] = priorQuantity - item.Quantity;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = invoice.CompanyId,
                WarehouseId = warehouseId,
                InventoryItemId = inventoryItemId,
                ProjectId = invoice.ProjectId,
                Type = StockMovementType.Return,
                Quantity = item.Quantity,
                ReferenceNumber = invoice.InvoiceNumber,
                MovementDate = invoice.InvoiceDate,
                Description =
                    $"{movementLabel} {invoice.InternalNumber} — {item.Description}",
                UnitCost = unitCostTry,
                TotalCost = decimal.Round(item.Quantity * unitCostTry, 2),
                CreatedByUserId = currentUser.UserId
            });

            posted++;
        }

        return posted;
    }
}
