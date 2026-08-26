using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

public sealed record StockCountLineInput(
    Guid LineId,
    decimal? CountedQuantity,
    int? VarianceReason,
    string? Note);

public interface IStockCountService
{
    /// <summary>
    /// Oturum açar ve sistem miktarlarını DONDURUR. Aynı depo/bölge
    /// için açık bir oturum varsa ikincisini açmaz.
    /// </summary>
    Task<StockCountSession> StartAsync(
        Guid companyId, Guid warehouseId, Guid? warehouseZoneId,
        string name, DateTime countDate, CancellationToken cancellationToken);

    Task<StockCountSession> SaveCountsAsync(
        Guid sessionId, IReadOnlyCollection<StockCountLineInput> lines,
        CancellationToken cancellationToken);

    /// <summary>Onaya gönderir; farklı satırların gerekçesi eksikse reddeder.</summary>
    Task<StockCountSession> SubmitAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Onaylar: stoğu düzeltir, hareketleri yazar ve TEK muhasebe fişi
    /// keser. Sayılmayan satırlara DOKUNMAZ.
    /// </summary>
    Task<StockCountSession> ApproveAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<StockCountSession> RejectAsync(
        Guid sessionId, string reason, CancellationToken cancellationToken);

    Task<StockCountSession> CancelAsync(
        Guid sessionId, string reason, CancellationToken cancellationToken);
}

public sealed class StockCountService(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentUserService currentUser,
    IStockCountVoucherPoster voucherPoster,
    IStokSatirKilidi stokKilidi) : IStockCountService
{
    private static readonly StockCountStatus[] ActiveStatuses =
        [StockCountStatus.Counting, StockCountStatus.PendingApproval];

    public async Task<StockCountSession> StartAsync(
        Guid companyId, Guid warehouseId, Guid? warehouseZoneId,
        string name, DateTime countDate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Sayım dönemi adı zorunludur.");

        var warehouse = await db.Warehouses
            .SingleOrDefaultAsync(x => x.Id == warehouseId, cancellationToken)
            ?? throw new InvalidOperationException("Depo bulunamadı.");

        if (warehouse.CompanyId != companyId)
            throw new InvalidOperationException("Depo bu şirkete ait değil.");

        if (warehouseZoneId is Guid zoneId)
        {
            var zoneBelongs = await db.WarehouseZones.AnyAsync(
                x => x.Id == zoneId && x.WarehouseId == warehouseId, cancellationToken);

            if (!zoneBelongs)
                throw new InvalidOperationException("Seçilen bölge bu depoya ait değil.");
        }

        // İKİNCİ OTURUM AÇILMAZ. Aynı bölgede iki sayım, iki farklı
        // dondurulmuş sistem miktarı demektir; ikisi de onaylanırsa
        // aynı fark stoğa iki kez uygulanırdı.
        var conflicting = await db.StockCountSessions
            .Where(x => x.WarehouseId == warehouseId && ActiveStatuses.Contains(x.Status))
            .Select(x => new { x.DocumentNumber, x.WarehouseZoneId })
            .ToListAsync(cancellationToken);

        var clash = conflicting.FirstOrDefault(x =>
            x.WarehouseZoneId is null
            || warehouseZoneId is null
            || x.WarehouseZoneId == warehouseZoneId);

        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"Bu depoda/bölgede zaten açık bir sayım var ({clash.DocumentNumber}). "
                + "Önce onu sonuçlandırın.");
        }

        var documentNumber = await documentNumbers.GenerateAsync(
            companyId, "STOCK_COUNT", "SAYIM", cancellationToken);

        var session = new StockCountSession
        {
            CompanyId = companyId,
            WarehouseId = warehouseId,
            WarehouseZoneId = warehouseZoneId,
            DocumentNumber = documentNumber,
            Name = name.Trim(),
            CountDate = AsUtc(countDate),
            Status = StockCountStatus.Counting,
            StartedByUserId = currentUser.UserId
        };

        // SİSTEM MİKTARLARI BURADA DONDURULUYOR.
        var stockQuery = db.WarehouseStocks
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId);

        if (warehouseZoneId is Guid zone)
        {
            // Konum KARTTA duruyor, stok satırında değil.
            stockQuery = stockQuery.Where(x => x.InventoryItem.WarehouseZoneId == zone);
        }

        var rows = await stockQuery
            .Select(x => new
            {
                x.InventoryItemId,
                x.Quantity,
                x.InventoryItem.AverageUnitCost,
                x.InventoryItem.Name
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "Sayılacak stok satırı yok. Depoda (ya da seçilen bölgede) "
                + "kayıtlı malzeme bulunmuyor.");
        }

        foreach (var row in rows)
        {
            session.Lines.Add(new StockCountLine
            {
                InventoryItemId = row.InventoryItemId,
                SystemQuantity = row.Quantity,
                UnitCostAtCount = row.AverageUnitCost
            });
        }

        db.StockCountSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task<StockCountSession> SaveCountsAsync(
        Guid sessionId, IReadOnlyCollection<StockCountLineInput> lines,
        CancellationToken cancellationToken)
    {
        var session = await LoadAsync(sessionId, cancellationToken);

        if (session.Status != StockCountStatus.Counting)
        {
            throw new InvalidOperationException(
                "Yalnızca sayım aşamasındaki oturumda miktar girilebilir.");
        }

        var byId = session.Lines.ToDictionary(x => x.Id);

        foreach (var input in lines)
        {
            if (!byId.TryGetValue(input.LineId, out var line))
                throw new InvalidOperationException("Sayım satırı bulunamadı.");

            if (input.CountedQuantity is decimal counted && counted < 0m)
                throw new InvalidOperationException("Sayılan miktar negatif olamaz.");

            line.CountedQuantity = input.CountedQuantity;
            line.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            line.VarianceReason = input.VarianceReason is int reason
                ? Enum.IsDefined(typeof(StockCountVarianceReason), reason)
                    ? (StockCountVarianceReason)reason
                    : throw new InvalidOperationException("Geçersiz fark gerekçesi.")
                : null;

            line.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task<StockCountSession> SubmitAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await LoadAsync(sessionId, cancellationToken);

        if (session.Status != StockCountStatus.Counting)
            throw new InvalidOperationException("Yalnızca sayım aşamasındaki oturum onaya gönderilebilir.");

        if (!session.Lines.Any(x => x.CountedQuantity is not null))
        {
            throw new InvalidOperationException(
                "Hiçbir satır sayılmamış; onaya gönderilecek bir şey yok.");
        }

        // GEREKÇE ZORUNLU. Gerekçesiz fark, "bir şey oldu ama ne
        // olduğunu kimse yazmadı" demektir; fire oranı ölçülemez ve
        // tekrar eden kayıplar fark edilmez.
        var missing = session.Lines
            .Where(x => x.Difference is decimal d && d != 0m && x.VarianceReason is null)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{missing.Count} satırda fark var ama gerekçe girilmemiş. "
                + "Fark gerekçesi (fire/kayıp/sayım hatası/kırılma) zorunludur.");
        }

        session.Status = StockCountStatus.PendingApproval;
        session.SubmittedAtUtc = DateTime.UtcNow;
        session.SubmittedByUserId = currentUser.UserId;
        session.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task<StockCountSession> ApproveAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await LoadAsync(sessionId, cancellationToken);

        if (session.Status != StockCountStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen sayım onaylanabilir.");

        // SAYILMAYAN SATIR ATLANIR (kullanıcı kararı): fiziki miktarı
        // girilmemiş satırın stoğuna dokunulmaz. Sıfır sayılsaydı
        // unutulan bir satır o malzemenin tüm stoğunu silerdi.
        var adjusted = session.Lines
            .Where(x => x.Difference is decimal d && d != 0m)
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();

        // Belge numaraları transaction DIŞINDA üretiliyor:
        // DocumentNumberService kendi transaction'ını açıyor.
        var movementNumbers = new Dictionary<Guid, string>();

        foreach (var line in adjusted)
        {
            movementNumbers[line.Id] = await documentNumbers.GenerateAsync(
                session.CompanyId, "STOCK_ADJUSTMENT", "SAYIM", cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var line in adjusted)
        {
            var difference = line.Difference!.Value;

            // SATIR KİLİDİ — SAYIM MUTLAK YAZAR, EN TEHLİKELİ BİÇİM.
            //
            // Burada tehlike negatif stok değil, KAYIP GÜNCELLEME:
            // sayım "40 yaz" der, araya giren çıkış 5 düşer, sayım 40
            // yazar ve çıkış yok olur. Sayım kilidi açık oturum varken
            // hareketi engelliyor ama oturum AÇILMADAN ÖNCE başlamış
            // bir işlemi engelleyemez; kalan pencereyi bu kilit kapar.
            await stokKilidi.KilitleAsync(
                session.WarehouseId, line.InventoryItemId, cancellationToken);

            var stock = await db.WarehouseStocks.SingleOrDefaultAsync(
                x => x.WarehouseId == session.WarehouseId
                     && x.InventoryItemId == line.InventoryItemId,
                cancellationToken);

            if (stock is null)
            {
                // Oturum açıldığında vardı, şimdi yok: kart silinmiş
                // olabilir. Sessizce geçmek yerine durup söylüyoruz.
                throw new InvalidOperationException(
                    "Sayım satırının stok kaydı bulunamadı; oturum açıldıktan "
                    + "sonra silinmiş olabilir. Sayımı iptal edip yenileyin.");
            }

            stock.Quantity = line.CountedQuantity!.Value;
            stock.UpdatedAtUtc = DateTime.UtcNow;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = session.CompanyId,
                WarehouseId = session.WarehouseId,
                InventoryItemId = line.InventoryItemId,
                Type = StockMovementType.Adjustment,
                Quantity = difference,
                UnitCost = line.UnitCostAtCount,
                TotalCost = line.UnitCostAtCount * difference,
                ReferenceNumber = movementNumbers[line.Id],
                MovementDate = session.CountDate,
                Description =
                    $"Sayım {session.DocumentNumber} — {ReasonLabel(line.VarianceReason)}",
                CreatedByUserId = currentUser.UserId
            });
        }

        // TEK FİŞ, oturum başına. Sayım tek bir olaydır; satır başına
        // fiş kesilseydi mizan anlamsız bir yığına dönerdi.
        session.AccountingVoucherId = await voucherPoster.PostAsync(
            session, adjusted, cancellationToken);

        session.Status = StockCountStatus.Approved;
        session.DecidedAtUtc = DateTime.UtcNow;
        session.DecidedByUserId = currentUser.UserId;
        session.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return session;
    }

    public Task<StockCountSession> RejectAsync(
        Guid sessionId, string reason, CancellationToken cancellationToken) =>
        DecideAsync(sessionId, reason, StockCountStatus.Rejected,
            StockCountStatus.PendingApproval,
            "Yalnızca onay bekleyen sayım reddedilebilir.", cancellationToken);

    public Task<StockCountSession> CancelAsync(
        Guid sessionId, string reason, CancellationToken cancellationToken) =>
        DecideAsync(sessionId, reason, StockCountStatus.Cancelled, null,
            "Sonuçlanmış sayım iptal edilemez.", cancellationToken);

    private async Task<StockCountSession> DecideAsync(
        Guid sessionId, string reason, StockCountStatus target,
        StockCountStatus? requiredStatus, string error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Gerekçe zorunludur.");

        var session = await LoadAsync(sessionId, cancellationToken);

        var allowed = requiredStatus is StockCountStatus required
            ? session.Status == required
            : ActiveStatuses.Contains(session.Status);

        if (!allowed) throw new InvalidOperationException(error);

        session.Status = target;
        session.DecisionReason = reason.Trim();
        session.DecidedAtUtc = DateTime.UtcNow;
        session.DecidedByUserId = currentUser.UserId;
        session.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return session;
    }

    private async Task<StockCountSession> LoadAsync(
        Guid sessionId, CancellationToken cancellationToken) =>
        await db.StockCountSessions
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Sayım oturumu bulunamadı.");

    public static string ReasonLabel(StockCountVarianceReason? reason) => reason switch
    {
        StockCountVarianceReason.Wastage => "fire",
        StockCountVarianceReason.Loss => "kayıp",
        StockCountVarianceReason.CountingError => "sayım hatası",
        StockCountVarianceReason.Breakage => "kırılma",
        _ => "gerekçesiz"
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
