using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// ASGARİ SEVİYE UYARILARININ TEK HESABI.
///
/// Hem ekran ucu, hem bildirim taraması, hem Hızır brifingi buradan
/// okur. Üç ayrı yerde kopyalansaydı — ki S8 öncesi tam olarak öyleydi —
/// aynı malzeme için üç farklı "kritik" tanımı doğardı.
///
/// EŞİK: mevcut ≤ asgari. Eşitlik dahil; asgariye dokunmuş stok zaten
/// ikmal edilmeli, bir birim daha çıkması beklenmemeli.
///
/// BAKİYESİ OLMAYAN SEVİYE DE UYARIR: `warehouse_stocks` satırı yoksa
/// mevcut sıfır sayılır. Sol birleşim yerine iç birleşim kullanılsaydı
/// stoğu tamamen tükenmiş malzeme — yani en acil hâl — hiç görünmezdi.
/// </summary>
public sealed class StockLevelAlertService(AppDbContext db)
{
    /// <summary>
    /// Seviye tanımlı satırlar ve fiili durumları.
    /// </summary>
    /// <param name="belowMinimumOnly">
    /// true ise yalnızca asgarinin altına düşenler döner.
    /// </param>
    public async Task<IReadOnlyList<WarehouseStockLevelRow>> BuildAsync(
        Guid? companyId,
        Guid? warehouseId,
        bool belowMinimumOnly,
        CancellationToken cancellationToken)
    {
        var query = db.WarehouseStockLevels.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.Warehouse.CompanyId == companyId.Value);

        if (warehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == warehouseId.Value);

        var rows = await query
            .OrderBy(x => x.Warehouse.Name)
            .ThenBy(x => x.InventoryItem.Name)
            .Select(x => new
            {
                x.Id,
                x.WarehouseId,
                WarehouseCode = x.Warehouse.Code,
                WarehouseName = x.Warehouse.Name,
                x.InventoryItemId,
                ItemCode = x.InventoryItem.Code,
                ItemName = x.InventoryItem.Name,
                x.InventoryItem.Unit,
                x.MinimumQuantity,
                x.MaximumQuantity,
                x.Note,
                x.InventoryItem.AverageUnitCost,
                x.InventoryItem.PreferredSupplierCurrentAccountId,
                PreferredSupplierTitle = x.InventoryItem.PreferredSupplierCurrentAccount != null
                    ? x.InventoryItem.PreferredSupplierCurrentAccount.Title
                    : null,

                // Sol birleşim: bakiye satırı yoksa sıfır.
                CurrentQuantity = db.WarehouseStocks
                    .Where(s => s.WarehouseId == x.WarehouseId &&
                                s.InventoryItemId == x.InventoryItemId)
                    .Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x =>
            {
                var below = x.CurrentQuantity <= x.MinimumQuantity;

                // Öneri yalnız azami tanımlıysa ve gerçekten eksik varsa.
                decimal? suggested = null;
                if (below && x.MaximumQuantity is decimal max)
                {
                    var gap = max - x.CurrentQuantity;
                    if (gap > 0m)
                        suggested = decimal.Round(gap, 4);
                }

                return new WarehouseStockLevelRow(
                    x.Id,
                    x.WarehouseId,
                    x.WarehouseCode,
                    x.WarehouseName,
                    x.InventoryItemId,
                    x.ItemCode,
                    x.ItemName,
                    x.Unit,
                    x.MinimumQuantity,
                    x.MaximumQuantity,
                    x.Note,
                    x.CurrentQuantity,
                    below,
                    x.CurrentQuantity <= 0m,
                    suggested,
                    x.AverageUnitCost,
                    suggested.HasValue
                        ? decimal.Round(suggested.Value * x.AverageUnitCost, 2)
                        : null,
                    x.PreferredSupplierCurrentAccountId,
                    x.PreferredSupplierTitle);
            })
            .Where(x => !belowMinimumOnly || x.IsBelowMinimum)
            .ToList();
    }
}
