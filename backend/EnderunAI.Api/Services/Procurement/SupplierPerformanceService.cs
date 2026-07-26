using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record SupplierPerformanceResult(
    Guid SupplierId,
    decimal DeliveryScore,
    decimal QualityScore,
    decimal PriceScore,
    decimal TechnicalScore,
    decimal FinancialScore,
    decimal CommunicationScore,
    decimal OverallScore,
    SupplierRiskLevel RiskLevel,
    int TotalOrderCount,
    int CompletedOrderCount,
    int LateOrderCount,
    decimal TotalOrderAmountTry,
    decimal OnTimeDeliveryRate,
    decimal ReturnRate,
    IReadOnlyList<string> Warnings);

public interface ISupplierPerformanceService
{
    Task<SupplierPerformanceResult> CalculateAsync(Guid companyId, Guid supplierId, DateTime? periodStartUtc = null, DateTime? periodEndUtc = null, bool saveSnapshot = true, CancellationToken cancellationToken = default);
}

public sealed class SupplierPerformanceService(
    AppDbContext appDb,
    ProcurementDbContext procurementDb,
    SupplierPerformanceDbContext performanceDb) : ISupplierPerformanceService
{
    public async Task<SupplierPerformanceResult> CalculateAsync(
        Guid companyId,
        Guid supplierId,
        DateTime? periodStartUtc = null,
        DateTime? periodEndUtc = null,
        bool saveSnapshot = true,
        CancellationToken cancellationToken = default)
    {
        var end = periodEndUtc ?? DateTime.UtcNow;
        var start = periodStartUtc ?? end.AddMonths(-12);
        if (start >= end)
            throw new InvalidOperationException("Performans dönemi geçersizdir.");

        var supplierExists = await appDb.CurrentAccounts.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == supplierId, cancellationToken);
        if (!supplierExists)
            throw new InvalidOperationException("Tedarikçi cari hesabı bulunamadı.");

        var orders = await appDb.PurchaseOrders.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CompanyId == companyId &&
                        x.SupplierCurrentAccountId == supplierId &&
                        x.OrderDateUtc >= start && x.OrderDateUtc <= end &&
                        x.Status != PurchaseOrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.Id).ToList();
        var receipts = orderIds.Count == 0
            ? new List<GoodsReceipt>()
            : await appDb.GoodsReceipts.AsNoTracking()
                .Where(x => orderIds.Contains(x.PurchaseOrderId) && x.Status == GoodsReceiptStatus.Posted)
                .ToListAsync(cancellationToken);

        var qualityRecords = await performanceDb.QualityRecords.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupplierCurrentAccountId == supplierId &&
                        x.EventDateUtc >= start && x.EventDateUtc <= end)
            .ToListAsync(cancellationToken);

        var manual = await performanceDb.ManualEvaluations.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupplierCurrentAccountId == supplierId &&
                        x.EvaluationDateUtc >= start && x.EvaluationDateUtc <= end)
            .ToListAsync(cancellationToken);

        var offers = await procurementDb.SupplierOffers.AsNoTracking()
            .Where(x => x.SupplierCurrentAccountId == supplierId && x.OfferDateUtc >= start && x.OfferDateUtc <= end)
            .ToListAsync(cancellationToken);

        var totalOrders = orders.Count;
        var completedOrders = orders.Count(x => x.Status == PurchaseOrderStatus.Completed);
        var lateOrders = orders.Count(order =>
        {
            if (!order.DeliveryDateUtc.HasValue)
                return false;
            var lastReceipt = receipts.Where(x => x.PurchaseOrderId == order.Id)
                .Select(x => (DateTime?)x.ReceiptDateUtc).Max();
            return lastReceipt.HasValue
                ? lastReceipt.Value.Date > order.DeliveryDateUtc.Value.Date
                : end.Date > order.DeliveryDateUtc.Value.Date && order.Status != PurchaseOrderStatus.Completed;
        });

        var onTimeRate = totalOrders == 0 ? 100m : Math.Clamp((decimal)(totalOrders - lateOrders) / totalOrders * 100m, 0m, 100m);
        var completionRate = totalOrders == 0 ? 100m : (decimal)completedOrders / totalOrders * 100m;
        var deliveryScore = Math.Clamp(onTimeRate * 0.75m + completionRate * 0.25m, 0m, 100m);

        var negativeQuality = qualityRecords.Where(x => x.EventType is SupplierQualityEventType.Rejected or SupplierQualityEventType.Returned or SupplierQualityEventType.WarrantyIssue).ToList();
        var totalQualityQuantity = qualityRecords.Sum(x => Math.Max(0m, x.Quantity));
        var returnedQuantity = negativeQuality.Sum(x => Math.Max(0m, x.Quantity));
        var returnRate = totalQualityQuantity <= 0 ? 0m : Math.Clamp(returnedQuantity / totalQualityQuantity * 100m, 0m, 100m);
        var automaticQuality = Math.Clamp(100m - returnRate - negativeQuality.Sum(x => Math.Clamp(x.ImpactScore, 0m, 100m)) / Math.Max(1, negativeQuality.Count) * 0.25m, 0m, 100m);

        var manualQuality = manual.Count == 0 ? automaticQuality : manual.Average(x => x.QualityScore);
        var qualityScore = Math.Clamp(automaticQuality * 0.70m + manualQuality * 0.30m, 0m, 100m);
        var technicalScore = manual.Count == 0 ? 75m : Math.Clamp(manual.Average(x => x.TechnicalScore), 0m, 100m);
        var financialScore = manual.Count == 0 ? 75m : Math.Clamp(manual.Average(x => x.FinancialScore), 0m, 100m);
        var communicationScore = manual.Count == 0 ? 75m : Math.Clamp(manual.Average(x => x.CommunicationScore), 0m, 100m);

        var priceScore = offers.Count == 0
            ? 70m
            : Math.Clamp(70m + Math.Min(30m, offers.Count * 2m), 0m, 100m);

        var overall = decimal.Round(
            deliveryScore * 0.25m +
            qualityScore * 0.25m +
            priceScore * 0.15m +
            technicalScore * 0.15m +
            financialScore * 0.10m +
            communicationScore * 0.10m, 2);

        var risk = overall switch
        {
            >= 85m => SupplierRiskLevel.Low,
            >= 70m => SupplierRiskLevel.Medium,
            >= 50m => SupplierRiskLevel.High,
            _ => SupplierRiskLevel.Critical
        };

        var totalAmountTry = orders.Sum(x => x.Items.Sum(i => i.Quantity * i.UnitPrice * (1m - i.DiscountRate / 100m)) * x.ExchangeRate);
        var warnings = new List<string>();
        if (lateOrders > 0) warnings.Add($"{lateOrders} siparişte teslimat gecikmesi var.");
        if (returnRate > 5m) warnings.Add($"İade/uygunsuzluk oranı %{returnRate:0.##}.");
        if (totalOrders == 0) warnings.Add("Seçilen dönemde sipariş geçmişi bulunmuyor.");
        if (manual.Count == 0) warnings.Add("Manuel iletişim, finans ve teknik değerlendirme bulunmuyor.");

        var result = new SupplierPerformanceResult(
            supplierId,
            decimal.Round(deliveryScore, 2),
            decimal.Round(qualityScore, 2),
            decimal.Round(priceScore, 2),
            decimal.Round(technicalScore, 2),
            decimal.Round(financialScore, 2),
            decimal.Round(communicationScore, 2),
            overall,
            risk,
            totalOrders,
            completedOrders,
            lateOrders,
            decimal.Round(totalAmountTry, 2),
            decimal.Round(onTimeRate, 2),
            decimal.Round(returnRate, 2),
            warnings);

        if (saveSnapshot)
        {
            performanceDb.Snapshots.Add(new SupplierPerformanceSnapshot
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = supplierId,
                PeriodStartUtc = start,
                PeriodEndUtc = end,
                DeliveryScore = result.DeliveryScore,
                QualityScore = result.QualityScore,
                PriceScore = result.PriceScore,
                TechnicalScore = result.TechnicalScore,
                FinancialScore = result.FinancialScore,
                CommunicationScore = result.CommunicationScore,
                OverallScore = result.OverallScore,
                RiskLevel = result.RiskLevel,
                TotalOrderCount = result.TotalOrderCount,
                CompletedOrderCount = result.CompletedOrderCount,
                LateOrderCount = result.LateOrderCount,
                TotalOrderAmountTry = result.TotalOrderAmountTry,
                OnTimeDeliveryRate = result.OnTimeDeliveryRate,
                ReturnRate = result.ReturnRate,
                Notes = string.Join(" ", warnings)
            });
            await performanceDb.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
