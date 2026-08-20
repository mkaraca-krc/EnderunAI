using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// SAYIM KİLİDİ — sayılan bölgeye hareket girmesini engeller.
///
/// Sayım sırasında mal girer ya da çıkarsa fark "sayım anındaki"
/// gerçeği yansıtmaz: sayan kişi 40 saymışken araya 5 çıkış girerse
/// sistem 35 gösterir ve 5 adet fire gibi görünür. Fark listesi
/// gerçekte var olmayan bir kayıp üretir ve onaylayan kişi bunu
/// bilemez.
///
/// TEK KAPI OLMASI ŞART: stok değiştiren her yol buradan geçmeli.
/// Bir yol atlanırsa kilit "çoğu zaman" çalışır — en kötü güvence
/// türü, çünkü kimse hangi yolun atladığını bilmez.
///
/// KİLİT SERT: uyarı değil, engel. Kullanıcı "sayım anında hareket
/// girmesin" dedi; uyarı geçilebilir bir şeydir.
/// </summary>
public interface IStockCountLockService
{
    /// <summary>
    /// Bu depo + kart için açık bir sayım varsa hata fırlatır.
    ///
    /// Kart kimliği gerekiyor çünkü kilit BÖLGE bazlı olabiliyor ve
    /// kartın hangi bölgede durduğu ancak karttan bilinir.
    /// </summary>
    Task EnsureNotLockedAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken);
}

public sealed class StockCountLockService(AppDbContext db) : IStockCountLockService
{
    /// <summary>
    /// AKTİF sayılan durumlar. Onay bekleyen oturumda da kilit sürüyor:
    /// sayılan miktarlar henüz stoğa işlenmedi, araya giren hareket
    /// onay anında uygulanacak farkı yanlış yapardı.
    /// </summary>
    private static readonly StockCountStatus[] ActiveStatuses =
        [StockCountStatus.Counting, StockCountStatus.PendingApproval];

    public async Task EnsureNotLockedAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken)
    {
        var open = await db.StockCountSessions
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && ActiveStatuses.Contains(x.Status))
            .Select(x => new
            {
                x.DocumentNumber,
                x.Name,
                x.WarehouseZoneId,
                ZoneName = x.WarehouseZone != null ? x.WarehouseZone.Name : null
            })
            .ToListAsync(cancellationToken);

        if (open.Count == 0) return;

        // Bölgesiz oturum = TÜM DEPO sayılıyor; kart nerede olursa olsun
        // kilitli.
        var whole = open.FirstOrDefault(x => x.WarehouseZoneId is null);

        if (whole is not null)
        {
            throw new InvalidOperationException(
                $"Bu depoda açık sayım var ({whole.DocumentNumber} — {whole.Name}); "
                + "sayım onaylanana ya da iptal edilene kadar stok hareketi "
                + "girilemez.");
        }

        var zoned = open.Where(x => x.WarehouseZoneId is not null).ToList();
        if (zoned.Count == 0) return;

        var cardZoneId = await db.InventoryItems
            .Where(x => x.Id == inventoryItemId)
            .Select(x => x.WarehouseZoneId)
            .SingleOrDefaultAsync(cancellationToken);

        // Kartın bölgesi yoksa bölgesel sayım onu kapsamıyor demektir:
        // o kart zaten sayım listesine girmedi, hareketini engellemek
        // gereksiz yere işi durdururdu.
        if (cardZoneId is null) return;

        var blocking = zoned.FirstOrDefault(x => x.WarehouseZoneId == cardZoneId);

        if (blocking is not null)
        {
            throw new InvalidOperationException(
                $"'{blocking.ZoneName}' bölgesinde açık sayım var "
                + $"({blocking.DocumentNumber} — {blocking.Name}); bu bölgedeki "
                + "malzemelere sayım bitene kadar hareket girilemez.");
        }
    }
}
