using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing;

/// <summary>
/// Satın alma brifingi: bekleyen talep, geciken açık sipariş ve sorunlu
/// tedarikçi.
///
/// Üçü de "kimsenin bakmadığı sürece sessizce büyüyen" iş: onay
/// bekleyen talep şantiyeyi malzemesiz bırakır, geciken sipariş
/// programı kaydırır, sorunlu tedarikçi her seferinde biraz daha
/// zarar yazdırır.
///
/// YETKİ: kaynak satın alma görme izni ister ve sorgular kullanıcının
/// görebildiği şirketlerle sınırlanır — göstermediğimiz veriyi
/// hesaplamıyoruz da.
/// </summary>
public sealed class PurchasingBriefingSource(
    AppDbContext db,
    SupplierQualityService supplierQuality) : IHizirBriefingSource
{
    public string Key => "satin_alma";
    public string? RequiredPermission => PermissionCatalog.Keys.PurchasingView;

    /// <summary>
    /// Bu kadar gündür onay bekleyen talep "bekliyor" sayılır. Bir
    /// günlük bekleme normaldir; bir haftalık bekleme unutulmuş
    /// demektir.
    /// </summary>
    private const int StaleRequestDays = 3;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var items = new List<BriefingItem>();
        var today = DateTime.UtcNow.Date;

        var companyIds = context.Scope.HasGlobalAccess
            ? null
            : context.Scope.VisibleCompanyIds;

        // --- Onay bekleyen talepler ---
        var requestQuery = db.PurchaseRequests
            .AsNoTracking()
            .Where(x => x.Status == PurchaseRequestStatus.Submitted);

        if (companyIds is not null)
            requestQuery = requestQuery.Where(x => companyIds.Contains(x.CompanyId));

        var staleBefore = today.AddDays(-StaleRequestDays);

        var pendingRequests = await requestQuery.CountAsync(cancellationToken);

        var staleRequests = await requestQuery
            .CountAsync(x => x.CreatedAtUtc < staleBefore, cancellationToken);

        if (pendingRequests > 0)
        {
            items.Add(new BriefingItem(
                $"{pendingRequests} satın alma talebi onay bekliyor",
                staleRequests > 0
                    ? $"{staleRequests} tanesi {StaleRequestDays} günden eski; " +
                      "bekleyen talep şantiyeyi malzemesiz bırakır."
                    : "Onaylandıkça teklif toplamaya geçilebilir.",
                staleRequests > 0 ? BriefingSeverity.Warning : BriefingSeverity.Info,
                "/satin-alma"));
        }

        // --- Teslim tarihi geçmiş açık siparişler ---
        var lateQuery = db.PurchaseOrders
            .AsNoTracking()
            .Where(x =>
                x.ExpectedDeliveryDate != null &&
                x.ExpectedDeliveryDate < today &&
                (x.Status == PurchaseOrderStatus.Approved ||
                 x.Status == PurchaseOrderStatus.PartiallyReceived));

        if (companyIds is not null)
            lateQuery = lateQuery.Where(x => companyIds.Contains(x.CompanyId));

        var lateOrders = await lateQuery
            .Select(x => new
            {
                x.OrderNumber,
                x.ExpectedDeliveryDate,
                Supplier = x.SupplierCurrentAccount.Title
            })
            .OrderBy(x => x.ExpectedDeliveryDate)
            .Take(5)
            .ToListAsync(cancellationToken);

        var lateCount = await lateQuery.CountAsync(cancellationToken);

        if (lateCount > 0)
        {
            var oldest = lateOrders.FirstOrDefault();

            var detail = oldest is null
                ? null
                : $"En eskisi {oldest.OrderNumber} ({oldest.Supplier}), " +
                  $"beklenen teslim {oldest.ExpectedDeliveryDate:dd.MM.yyyy}.";

            items.Add(new BriefingItem(
                $"{lateCount} siparişin teslim tarihi geçti",
                detail,
                BriefingSeverity.Warning,
                "/satin-alma/siparis"));
        }

        // --- Sorunlu tedarikçiler ---
        var quality = await supplierQuality.GetReportAsync(
            companyId: null, months: 6, cancellationToken);

        var problemSuppliers = quality.Rows
            .Where(x =>
                x.RejectionRatePercent > SupplierQualityService.ProblemThresholdPercent)
            .Take(3)
            .ToList();

        foreach (var supplier in problemSuppliers)
        {
            items.Add(new BriefingItem(
                $"{supplier.SupplierTitle}: red/hasar oranı " +
                $"%{supplier.RejectionRatePercent:0.#}",
                $"Son 6 ayda {supplier.ProblemReceiptCount}/{supplier.ReceiptCount} " +
                "teslimatta sorun çıktı. Sipariş vermeden önce kaliteyi " +
                "gözden geçirin.",
                BriefingSeverity.Warning,
                "/satin-alma/raporlar"));
        }

        return items;
    }
}
