using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Bir şirketin eşik durumu ve bekleyen tetiklenmeleri.
/// </summary>
/// <param name="CompanyId">Şirket.</param>
/// <param name="Commodity">Emtia.</param>
/// <param name="BuyBelowUsdPerTon">Alım eşiği.</param>
/// <param name="AlertAboveUsdPerTon">Risk eşiği.</param>
/// <param name="IsEnabled">Eşik açık mı.</param>
/// <param name="LatestPriceUsdPerTon">Son fiyat.</param>
/// <param name="LatestPriceDate">Son fiyat günü.</param>
/// <param name="CurrentState">Şu an hangi bölgedeyiz.</param>
/// <param name="PendingTriggers">Görüldü işaretlenmemiş tetiklenmeler.</param>
public sealed record CommodityAlertStatus(
    Guid CompanyId,
    Commodity Commodity,
    decimal? BuyBelowUsdPerTon,
    decimal? AlertAboveUsdPerTon,
    bool IsEnabled,
    decimal? LatestPriceUsdPerTon,
    DateTime? LatestPriceDate,
    CommodityAlertDirection? CurrentState,
    IReadOnlyList<CommodityAlertTriggerView> PendingTriggers);

/// <summary>Tetiklenmenin ekrana dönen hâli.</summary>
/// <param name="Id">Tetiklenme kimliği.</param>
/// <param name="Direction">Yön.</param>
/// <param name="PriceDate">Geçiş günü.</param>
/// <param name="PriceUsdPerTon">O günkü fiyat.</param>
/// <param name="PriceTryPerTon">TL karşılığı.</param>
/// <param name="ThresholdUsdPerTon">Aşılan eşik.</param>
/// <param name="AcknowledgedAtUtc">Görüldü işaretlendiyse zamanı.</param>
public sealed record CommodityAlertTriggerView(
    Guid Id,
    CommodityAlertDirection Direction,
    DateTime PriceDate,
    decimal PriceUsdPerTon,
    decimal? PriceTryPerTon,
    decimal ThresholdUsdPerTon,
    DateTime? AcknowledgedAtUtc);

/// <summary>
/// Emtia alım/risk eşiklerinin yönetimi ve tetiklenme takibi.
///
/// Tetiklenmeler fiyat arşivinden YENİDEN ÜRETİLEBİLİR (seri
/// üzerindeki geçişler), bu yüzden değerlendirme idempotenttir: aynı
/// eşik + tarih + yön ikinci kez yazılmaz. Gecelik iş birden fazla
/// kez koşsa da uyarı çoğalmaz.
///
/// Eşik tanımlı değilse ya da kapalıysa hiçbir şey üretilmez; sinyal
/// üretmek için varsayılan bir eşik UYDURULMAZ — "bizim için ucuz"un
/// ne olduğunu yalnızca şirket bilir.
/// </summary>
public sealed class CommodityAlertService(
    AppDbContext db,
    ICommodityPriceService commodityPrices)
{
    /// <summary>
    /// Geçiş taramasının baktığı pencere. Gecelik iş her gün koştuğu
    /// için birkaç günlük kesinti bile kaçırılmasın diye geniş tutuldu;
    /// idempotent olduğu için tekrar taramanın maliyeti yok.
    /// </summary>
    public const int LookbackDays = 90;

    /// <summary>
    /// Şirketin eşiğini getirir; yoksa null (kayıt oluşturulmaz).
    /// </summary>
    public Task<CommodityAlertThreshold?> GetThresholdAsync(
        Guid companyId, Commodity commodity, CancellationToken cancellationToken) =>
        db.CommodityAlertThresholds
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Commodity == commodity,
                cancellationToken);

    /// <summary>
    /// Eşiği kaydeder (yoksa oluşturur).
    /// </summary>
    public async Task<CommodityAlertThreshold> SaveThresholdAsync(
        Guid companyId,
        Commodity commodity,
        decimal? buyBelow,
        decimal? alertAbove,
        bool isEnabled,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (buyBelow is <= 0m)
            throw new InvalidOperationException("Alım eşiği sıfırdan büyük olmalı.");

        if (alertAbove is <= 0m)
            throw new InvalidOperationException("Risk eşiği sıfırdan büyük olmalı.");

        if (buyBelow is not null && alertAbove is not null &&
            buyBelow.Value >= alertAbove.Value)
        {
            throw new InvalidOperationException(
                "Alım eşiği risk eşiğinden küçük olmalı; aksi hâlde iki uyarı " +
                "aynı anda tetiklenir ve hiçbiri anlam taşımaz.");
        }

        var threshold = await GetThresholdAsync(companyId, commodity, cancellationToken);

        if (threshold is null)
        {
            threshold = new CommodityAlertThreshold
            {
                CompanyId = companyId,
                Commodity = commodity
            };

            db.CommodityAlertThresholds.Add(threshold);
        }

        threshold.BuyBelowUsdPerTon = buyBelow;
        threshold.AlertAboveUsdPerTon = alertAbove;
        threshold.IsEnabled = isEnabled;
        threshold.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return threshold;
    }

    /// <summary>
    /// Fiyat arşivini tarar ve yeni eşik geçişlerini kaydeder.
    /// </summary>
    /// <returns>Bu çağrıda yazılan yeni tetiklenme sayısı.</returns>
    public async Task<int> EvaluateAsync(
        Guid companyId, Commodity commodity, CancellationToken cancellationToken)
    {
        var threshold = await GetThresholdAsync(companyId, commodity, cancellationToken);

        if (threshold is null || !threshold.IsEnabled)
            return 0;

        if (threshold.BuyBelowUsdPerTon is null && threshold.AlertAboveUsdPerTon is null)
            return 0;

        var summary = await commodityPrices.GetSummaryAsync(
            commodity, LookbackDays, cancellationToken);

        var crossings = CommodityThresholdCrossingDetector.Detect(
            summary.Trend,
            threshold.BuyBelowUsdPerTon,
            threshold.AlertAboveUsdPerTon);

        if (crossings.Count == 0)
            return 0;

        // Zaten yazılmış geçişler: idempotenslik buradan geliyor.
        var existing = await db.CommodityAlertTriggers
            .AsNoTracking()
            .Where(x => x.CommodityAlertThresholdId == threshold.Id)
            .Select(x => new { x.PriceDate, x.Direction })
            .ToListAsync(cancellationToken);

        var existingKeys = existing
            .Select(x => (x.PriceDate.Date, x.Direction))
            .ToHashSet();

        var added = 0;

        foreach (var crossing in crossings)
        {
            var key = (crossing.PriceDate.Date, crossing.Direction);

            if (!existingKeys.Add(key))
                continue;

            db.CommodityAlertTriggers.Add(new CommodityAlertTrigger
            {
                CommodityAlertThresholdId = threshold.Id,
                Direction = crossing.Direction,
                PriceDate = DateTime.SpecifyKind(
                    crossing.PriceDate.Date, DateTimeKind.Utc),
                PriceUsdPerTon = crossing.PriceUsdPerTon,
                PriceTryPerTon = crossing.PriceTryPerTon,
                ThresholdUsdPerTon = crossing.ThresholdUsdPerTon
            });

            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        return added;
    }

    /// <summary>
    /// Tanımlı ve açık tüm eşikleri değerlendirir. Gecelik iş bunu
    /// çağırır.
    /// </summary>
    /// <returns>Yazılan toplam yeni tetiklenme sayısı.</returns>
    public async Task<int> EvaluateAllAsync(CancellationToken cancellationToken)
    {
        var targets = await db.CommodityAlertThresholds
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .Select(x => new { x.CompanyId, x.Commodity })
            .ToListAsync(cancellationToken);

        var total = 0;

        foreach (var target in targets)
        {
            total += await EvaluateAsync(
                target.CompanyId, target.Commodity, cancellationToken);
        }

        return total;
    }

    /// <summary>
    /// Şirketin eşik durumu ve bekleyen tetiklenmeleri.
    /// </summary>
    public async Task<CommodityAlertStatus> GetStatusAsync(
        Guid companyId, Commodity commodity, CancellationToken cancellationToken)
    {
        var threshold = await GetThresholdAsync(companyId, commodity, cancellationToken);

        var summary = await commodityPrices.GetSummaryAsync(
            commodity, LookbackDays, cancellationToken);

        if (threshold is null)
        {
            return new CommodityAlertStatus(
                companyId, commodity,
                BuyBelowUsdPerTon: null,
                AlertAboveUsdPerTon: null,
                IsEnabled: false,
                LatestPriceUsdPerTon: summary.LatestUsdPerTon,
                LatestPriceDate: summary.LatestDate,
                CurrentState: null,
                PendingTriggers: []);
        }

        var pending = await db.CommodityAlertTriggers
            .AsNoTracking()
            .Where(x =>
                x.CommodityAlertThresholdId == threshold.Id &&
                x.AcknowledgedAtUtc == null)
            .OrderByDescending(x => x.PriceDate)
            .Select(x => new CommodityAlertTriggerView(
                x.Id, x.Direction, x.PriceDate, x.PriceUsdPerTon,
                x.PriceTryPerTon, x.ThresholdUsdPerTon, x.AcknowledgedAtUtc))
            .ToListAsync(cancellationToken);

        return new CommodityAlertStatus(
            companyId,
            commodity,
            threshold.BuyBelowUsdPerTon,
            threshold.AlertAboveUsdPerTon,
            threshold.IsEnabled,
            summary.LatestUsdPerTon,
            summary.LatestDate,
            threshold.IsEnabled
                ? CommodityThresholdCrossingDetector.CurrentState(
                    summary.Trend,
                    threshold.BuyBelowUsdPerTon,
                    threshold.AlertAboveUsdPerTon)
                : null,
            pending);
    }

    /// <summary>
    /// Tetiklenmeyi görüldü olarak işaretler; brifingden ve karttan
    /// düşer.
    /// </summary>
    public async Task<bool> AcknowledgeAsync(
        Guid triggerId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var trigger = await db.CommodityAlertTriggers
            .SingleOrDefaultAsync(x => x.Id == triggerId, cancellationToken);

        if (trigger is null)
            return false;

        if (trigger.AcknowledgedAtUtc is not null)
            return true;

        trigger.AcknowledgedAtUtc = DateTime.UtcNow;
        trigger.AcknowledgedByUserId = actorUserId;

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
