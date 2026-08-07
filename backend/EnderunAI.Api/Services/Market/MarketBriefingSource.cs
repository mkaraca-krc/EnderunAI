using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Piyasa brifingi: alım fırsatı / maliyet riski eşiklerinin
/// tetiklenmesi ve fiyat arşivinin bayatlaması.
///
/// Yalnızca GÖRÜLMEMİŞ tetiklenmeler madde üretir; kullanıcı "görüldü"
/// dediğinde brifingden düşer. Aksi hâlde bakır bir kez ucuzladığında
/// aynı madde her sabah tekrarlanır ve brifing okunmaz hâle gelir.
///
/// YETKİ: kaynak yalnızca kullanıcının görebildiği şirketlerin
/// eşiklerine bakar — göstermediğimiz veriyi hesaplamıyoruz da.
/// </summary>
public sealed class MarketBriefingSource(
    AppDbContext db,
    ICommodityPriceService commodityPrices) : IHizirBriefingSource
{
    public string Key => "piyasa_esik";
    public string? RequiredPermission => PermissionCatalog.Keys.FinanceView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var items = new List<BriefingItem>();

        var triggers = db.CommodityAlertTriggers
            .AsNoTracking()
            .Where(x =>
                x.AcknowledgedAtUtc == null &&
                x.CommodityAlertThreshold.IsEnabled);

        if (!context.Scope.HasGlobalAccess)
        {
            var companyIds = context.Scope.VisibleCompanyIds;

            triggers = triggers.Where(x =>
                companyIds.Contains(x.CommodityAlertThreshold.CompanyId));
        }

        var pending = await triggers
            .OrderByDescending(x => x.PriceDate)
            .Select(x => new
            {
                x.Direction,
                x.PriceDate,
                x.PriceUsdPerTon,
                x.ThresholdUsdPerTon,
                CompanyName = x.CommodityAlertThreshold.Company.Name
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var trigger in pending)
        {
            var isBuy = trigger.Direction == CommodityAlertDirection.BuyOpportunity;

            items.Add(new BriefingItem(
                isBuy
                    ? $"Bakır alım eşiğinin altına indi — " +
                      $"{TurkishFormat.Amount(trigger.PriceUsdPerTon)} USD/ton"
                    : $"Bakır risk eşiğini aştı — " +
                      $"{TurkishFormat.Amount(trigger.PriceUsdPerTon)} USD/ton",
                $"{trigger.CompanyName} · eşik " +
                $"{TurkishFormat.Amount(trigger.ThresholdUsdPerTon)} USD/ton · " +
                $"{trigger.PriceDate:dd.MM.yyyy}" +
                (isBuy
                    ? ". Stok alımı için pencere açılmış olabilir."
                    : ". Açık tekliflerdeki bakır maliyetini gözden geçirin."),
                isBuy ? BriefingSeverity.Info : BriefingSeverity.Warning,
                "/finans/piyasa"));
        }

        // Fiyat arşivi bayatladıysa eşikler de körleşir: uyarı
        // gelmemesi "fiyat iyi" demek değil, "veri yok" demek olabilir.
        var summary = await commodityPrices.GetSummaryAsync(
            Commodity.Copper, days: 7, cancellationToken);

        if (summary.IsStale)
        {
            items.Add(new BriefingItem(
                "Bakır fiyat arşivi güncel değil",
                summary.Warning ??
                "Eşik uyarıları bayat veriyle çalışıyor; fiyatları yenileyin.",
                BriefingSeverity.Warning,
                "/finans/piyasa"));
        }

        return items;
    }
}
