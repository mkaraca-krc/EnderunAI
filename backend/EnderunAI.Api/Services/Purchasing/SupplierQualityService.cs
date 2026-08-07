using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing;

/// <summary>
/// Bir tedarikçinin mal kabul kalitesi.
/// </summary>
/// <param name="SupplierCurrentAccountId">Tedarikçi cari kartı.</param>
/// <param name="SupplierTitle">Tedarikçi unvanı.</param>
/// <param name="ReceiptCount">Dönemde kesinleşmiş mal kabul sayısı.</param>
/// <param name="ProblemReceiptCount">İçinde red/hasar olan mal kabul
/// sayısı.</param>
/// <param name="DeliveredQuantity">Gelen toplam miktar.</param>
/// <param name="AcceptedQuantity">Kabul edilen toplam miktar.</param>
/// <param name="RejectedQuantity">Reddedilen (yanlış/eksik) miktar.</param>
/// <param name="DamagedQuantity">Hasarlı miktar.</param>
/// <param name="RejectionRatePercent">(Red + hasar) / gelen, yüzde.</param>
/// <param name="LastProblemDate">Son sorunlu teslimatın tarihi.</param>
/// <param name="LateOrderCount">Teslim tarihi geçmiş açık sipariş
/// sayısı.</param>
public sealed record SupplierQualityRow(
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    int ReceiptCount,
    int ProblemReceiptCount,
    decimal DeliveredQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal DamagedQuantity,
    decimal RejectionRatePercent,
    DateTime? LastProblemDate,
    int LateOrderCount);

/// <summary>
/// Tedarikçi kalite karnesi.
/// </summary>
/// <param name="Months">Bakılan dönem (ay).</param>
/// <param name="Rows">Tedarikçi bazında satırlar; sorunlu olan başta.</param>
/// <param name="ProblemSupplierCount">Red oranı eşiği aşan tedarikçi
/// sayısı.</param>
public sealed record SupplierQualityReport(
    int Months,
    IReadOnlyList<SupplierQualityRow> Rows,
    int ProblemSupplierCount);

/// <summary>
/// Tedarikçi kalite takibi: hangi tedarikçiden gelen mal sık sık
/// reddediliyor ya da hasarlı geliyor.
///
/// KAYNAK yalnızca KESİNLEŞMİŞ (Posted) mal kabullerdir. Taslak mal
/// kabul henüz bir teslimat değil; depo sorumlusu sayarken girip
/// vazgeçmiş olabilir. Taslağı saymak, düzeltilmiş bir hatayı
/// tedarikçinin karnesine yazmak olurdu.
///
/// RED ORANI miktar üzerinden hesaplanır, teslimat sayısı üzerinden
/// değil: 1.000 adetten 5'i bozuk çıkan tedarikçi ile 10 adetten 5'i
/// bozuk çıkan aynı değildir.
/// </summary>
public sealed class SupplierQualityService(AppDbContext db)
{
    /// <summary>
    /// Bu oranın üstü "sorunlu tedarikçi" sayılır ve brifingde çıkar.
    /// Yüzde 5, tek bir kırık kalemin tedarikçiyi damgalamasını
    /// önleyecek kadar yüksek, sistematik sorunu yakalayacak kadar
    /// düşük.
    /// </summary>
    public const decimal ProblemThresholdPercent = 5m;

    /// <summary>Varsayılan bakış penceresi.</summary>
    public const int DefaultMonths = 12;

    /// <summary>
    /// Tedarikçi kalite karnesi.
    /// </summary>
    /// <param name="companyId">Şirket filtresi; null ise tümü.</param>
    /// <param name="months">Kaç aylık geçmişe bakılacağı.</param>
    public async Task<SupplierQualityReport> GetReportAsync(
        Guid? companyId, int? months, CancellationToken cancellationToken)
    {
        var window = months is > 0 and <= 60 ? months.Value : DefaultMonths;

        var since = DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddMonths(-window), DateTimeKind.Utc);

        var query = db.GoodsReceiptItems
            .AsNoTracking()
            .Where(x =>
                x.GoodsReceipt.Status == GoodsReceiptStatus.Posted &&
                x.GoodsReceipt.ReceiptDate >= since);

        if (companyId.HasValue)
        {
            query = query.Where(x =>
                x.GoodsReceipt.CompanyId == companyId.Value);
        }

        var rows = await query
            .Select(x => new
            {
                SupplierId = x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccountId,
                SupplierTitle =
                    x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccount.Title,
                ReceiptId = x.GoodsReceiptId,
                x.GoodsReceipt.ReceiptDate,
                x.DeliveredQuantity,
                x.AcceptedQuantity,
                x.RejectedQuantity,
                x.DamagedQuantity
            })
            .ToListAsync(cancellationToken);

        // Teslim tarihi geçmiş, hâlâ kapanmamış siparişler.
        var today = DateTime.UtcNow.Date;

        var lateOrdersQuery = db.PurchaseOrders
            .AsNoTracking()
            .Where(x =>
                x.ExpectedDeliveryDate != null &&
                x.ExpectedDeliveryDate < today &&
                (x.Status == PurchaseOrderStatus.Approved ||
                 x.Status == PurchaseOrderStatus.PartiallyReceived));

        if (companyId.HasValue)
            lateOrdersQuery = lateOrdersQuery.Where(x => x.CompanyId == companyId.Value);

        var lateOrders = await lateOrdersQuery
            .GroupBy(x => x.SupplierCurrentAccountId)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lateMap = lateOrders.ToDictionary(x => x.SupplierId, x => x.Count);

        var report = rows
            .GroupBy(x => new { x.SupplierId, x.SupplierTitle })
            .Select(group =>
            {
                var delivered = group.Sum(x => x.DeliveredQuantity);
                var rejected = group.Sum(x => x.RejectedQuantity);
                var damaged = group.Sum(x => x.DamagedQuantity);
                var problem = rejected + damaged;

                // Sorunlu mal kabul = içinde en az bir red/hasar olan.
                var problemReceipts = group
                    .Where(x => x.RejectedQuantity > 0m || x.DamagedQuantity > 0m)
                    .Select(x => x.ReceiptId)
                    .Distinct()
                    .ToList();

                var lastProblem = group
                    .Where(x => x.RejectedQuantity > 0m || x.DamagedQuantity > 0m)
                    .Select(x => (DateTime?)x.ReceiptDate)
                    .DefaultIfEmpty(null)
                    .Max();

                return new SupplierQualityRow(
                    SupplierCurrentAccountId: group.Key.SupplierId,
                    SupplierTitle: group.Key.SupplierTitle,
                    ReceiptCount: group.Select(x => x.ReceiptId).Distinct().Count(),
                    ProblemReceiptCount: problemReceipts.Count,
                    DeliveredQuantity: decimal.Round(delivered, 2),
                    AcceptedQuantity: decimal.Round(group.Sum(x => x.AcceptedQuantity), 2),
                    RejectedQuantity: decimal.Round(rejected, 2),
                    DamagedQuantity: decimal.Round(damaged, 2),
                    // Gelen miktar sıfırsa oran hesaplanamaz; sıfır
                    // yazmak "sorunsuz" izlenimi verirdi.
                    RejectionRatePercent: delivered > 0m
                        ? decimal.Round(problem / delivered * 100m, 2)
                        : 0m,
                    LastProblemDate: lastProblem,
                    LateOrderCount: lateMap.GetValueOrDefault(group.Key.SupplierId, 0));
            })
            // Sorunlu olan başta: kullanıcı önce riskli tedarikçiyi görmeli.
            .OrderByDescending(x => x.RejectionRatePercent)
            .ThenByDescending(x => x.LateOrderCount)
            .ThenBy(x => x.SupplierTitle)
            .ToList();

        return new SupplierQualityReport(
            window,
            report,
            report.Count(x => x.RejectionRatePercent > ProblemThresholdPercent));
    }
}
