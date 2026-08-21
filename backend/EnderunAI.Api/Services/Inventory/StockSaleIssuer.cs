using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>Satışta depodan çıkacak tek satır.</summary>
public sealed record StockSaleLine(
    Guid InventoryItemId,
    decimal Quantity,
    string Description,
    string MovementNumber);

/// <summary>
/// Çıkış anında DONDURULAN maliyet. Satış belgesi bu değeri kendi
/// satırına yazar; sonraki alımlar ortalamayı değiştirse de belge
/// kendi maliyetini taşımaya devam eder.
/// </summary>
public sealed record StockSaleCost(
    Guid InventoryItemId,
    decimal UnitCost,
    decimal TotalCost);

/// <summary>
/// SATIŞTA STOK ÇIKIŞININ TEK KAPISI.
///
/// Perakende fişi ve stoklu satış faturası aynı işi yapıyor: malı
/// depodan düşmek, negatif stoğu engellemek, hareketi belgelemek ve
/// maliyeti dondurmak. İki yerde ayrı ayrı yazılsaydı kurallar zamanla
/// ayrışırdı — biri negatif stoğu engellerken diğeri engellemez,
/// biri maliyeti dondururken diğeri bugünkü ortalamayı kullanırdı.
/// Aradaki fark ancak mizan tutmadığında görülürdü.
///
/// NEGATİF STOK YASAĞI BURADA: olmayan mal satılamaz, istisna yok.
/// Kontrol düşüşten ÖNCE ve aynı değişken üzerinde yapılıyor.
/// </summary>
public interface IStockSaleIssuer
{
    /// <summary>
    /// Malı depodan düşer, hareketi yazar ve dondurulmuş maliyeti
    /// döndürür. Stok yetersizse hiçbir satır işlenmeden hata verir.
    /// </summary>
    Task<IReadOnlyList<StockSaleCost>> IssueAsync(
        Guid companyId,
        Guid warehouseId,
        IReadOnlyList<StockSaleLine> lines,
        DateTime movementDate,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// İadede malı depoya geri alır. Maliyet DIŞARIDAN veriliyor:
    /// orijinal satışta dondurulan değer kullanılmalı, bugünkü
    /// ortalama değil — aynı mal geri geldiğinde hayali kâr doğmasın.
    /// </summary>
    Task ReturnAsync(
        Guid companyId,
        Guid warehouseId,
        IReadOnlyList<StockSaleLine> lines,
        IReadOnlyDictionary<Guid, decimal> unitCostByInventoryItem,
        DateTime movementDate,
        Guid? userId,
        CancellationToken cancellationToken);
}

public sealed class StockSaleIssuer(
    AppDbContext db,
    IStockCountLockService countLock) : IStockSaleIssuer
{
    public async Task<IReadOnlyList<StockSaleCost>> IssueAsync(
        Guid companyId,
        Guid warehouseId,
        IReadOnlyList<StockSaleLine> lines,
        DateTime movementDate,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var costs = new List<StockSaleCost>(lines.Count);

        foreach (var line in lines)
        {
            if (line.Quantity <= 0m)
            {
                throw new InvalidOperationException(
                    $"{line.Description}: satış miktarı sıfırdan büyük olmalıdır.");
            }

            // SAYIM KİLİDİ: sayılan bölgeye hareket girmez.
            await countLock.EnsureNotLockedAsync(
                warehouseId, line.InventoryItemId, cancellationToken);

            var stock = await db.WarehouseStocks
                .Include(x => x.InventoryItem)
                .SingleOrDefaultAsync(
                    x => x.WarehouseId == warehouseId
                        && x.InventoryItemId == line.InventoryItemId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"{line.Description}: bu depoda stok kaydı yok, satılamaz.");

            /*
             * PROJEYE BAĞLI KART SATILAMAZ (S9).
             *
             * Bağ, kartın hangi işe ait olduğunu söyler. Satış da bir
             * çıkıştır: X projesi için özel imal edilmiş armatürün
             * tezgâhtan satılması o işi malzemesiz bırakır ve kimse
             * fark etmez — çünkü stok düşmüş, muhasebe tutmuş, yalnız
             * malzeme yanlış yere gitmiştir.
             *
             * Depodan projeye çıkışta uygulanan kuralın (bkz.
             * InventoryController.Issue) aynısı: bağı olan kart, kendi
             * projesi dışına çıkamaz.
             */
            if (stock.InventoryItem.ProjectId.HasValue)
            {
                throw new InvalidOperationException(
                    $"{line.Description}: bu kart bir projeye bağlı ve satılamaz. "
                    + "Satılacaksa önce malzeme kartındaki proje bağı kaldırılmalı.");
            }

            // NEGATİF STOK KESİN YASAK — olmayan mal satılamaz.
            if (stock.Quantity < line.Quantity)
            {
                throw new InvalidOperationException(
                    $"{line.Description}: stok yetersiz, satış tamamlanamadı. "
                    + $"Depodaki {stock.Quantity:0.##}, istenen {line.Quantity:0.##}.");
            }

            stock.Quantity -= line.Quantity;
            stock.UpdatedAtUtc = DateTime.UtcNow;

            // MALİYET ÇIKIŞ ANINDA DONDURULUR.
            var unitCost = stock.InventoryItem.AverageUnitCost;
            var totalCost = decimal.Round(unitCost * line.Quantity, 2);

            db.StockMovements.Add(new StockMovement
            {
                // WarehouseStock şirketi depodan miras alır; ayrı alanı yok.
                WarehouseId = warehouseId,
                InventoryItemId = line.InventoryItemId,
                Type = StockMovementType.Issue,
                Quantity = line.Quantity,
                UnitCost = unitCost,
                TotalCost = totalCost,
                ReferenceNumber = line.MovementNumber,
                MovementDate = movementDate,
                Description = line.Description,
                CreatedByUserId = userId
            });

            costs.Add(new StockSaleCost(line.InventoryItemId, unitCost, totalCost));
        }

        return costs;
    }

    public async Task ReturnAsync(
        Guid companyId,
        Guid warehouseId,
        IReadOnlyList<StockSaleLine> lines,
        IReadOnlyDictionary<Guid, decimal> unitCostByInventoryItem,
        DateTime movementDate,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            if (line.Quantity <= 0m) continue;

            await countLock.EnsureNotLockedAsync(
                warehouseId, line.InventoryItemId, cancellationToken);

            var stock = await db.WarehouseStocks
                .Include(x => x.InventoryItem)
                .SingleOrDefaultAsync(
                    x => x.WarehouseId == warehouseId
                        && x.InventoryItemId == line.InventoryItemId,
                    cancellationToken);

            if (stock is null)
            {
                // Kart depodan tamamen tükenmiş olabilir; iade onu
                // yeniden açar. Malın geri gelmesi engellenemez.
                // WarehouseStock şirketi deposundan miras alır;
                // ayrı bir CompanyId alanı yok.
                stock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    InventoryItemId = line.InventoryItemId,
                    Quantity = 0m
                };

                db.WarehouseStocks.Add(stock);

                stock.InventoryItem = await db.InventoryItems
                    .SingleAsync(x => x.Id == line.InventoryItemId, cancellationToken);
            }

            var unitCost = unitCostByInventoryItem.TryGetValue(
                line.InventoryItemId, out var frozen) ? frozen : 0m;

            // ORTALAMA MALİYET GÜNCELLENİR — yalnız miktar artırmak
            // yetmez. Mal, satıldığı gündeki maliyetiyle geri giriyor;
            // arada ortalama değiştiyse iki değer birbirinden ayrılır.
            // Ortalamaya dokunulmasaydı stok DEĞERİ bugünkü ortalamayla
            // artar, muhasebeye ise dondurulmuş maliyet yazılır ve
            // mutabakat raporu her iadede biraz daha sapardı.
            //
            // Depodaki TÜM kartın miktarı esas alınır (tek depo değil):
            // AverageUnitCost kartın alanı, depo bazlı değil.
            var priorQuantity = await db.WarehouseStocks
                .Where(x => x.InventoryItemId == line.InventoryItemId)
                .SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0m;

            stock.InventoryItem.AverageUnitCost = WeightedAverageCostCalculator.Next(
                priorQuantity,
                stock.InventoryItem.AverageUnitCost,
                line.Quantity,
                unitCost);

            stock.Quantity += line.Quantity;
            stock.UpdatedAtUtc = DateTime.UtcNow;

            db.StockMovements.Add(new StockMovement
            {
                // WarehouseStock şirketi depodan miras alır; ayrı alanı yok.
                WarehouseId = warehouseId,
                InventoryItemId = line.InventoryItemId,
                Type = StockMovementType.Receipt,
                Quantity = line.Quantity,
                UnitCost = unitCost,
                TotalCost = decimal.Round(unitCost * line.Quantity, 2),
                ReferenceNumber = line.MovementNumber,
                MovementDate = movementDate,
                Description = line.Description,
                CreatedByUserId = userId
            });
        }
    }
}
