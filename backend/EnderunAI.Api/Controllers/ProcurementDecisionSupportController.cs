using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/procurement/decision-support")]
public sealed class ProcurementDecisionSupportController(
    AppDbContext db,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<ActionResult<ProcurementDecisionSupportResponse>> Get(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int periodDays = 365,
        CancellationToken cancellationToken = default)
    {
        if (periodDays is < 30 or > 3650)
            return BadRequest(new { message = "Rapor dönemi 30-3650 gün arasında olmalıdır." });

        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        var generatedAtUtc = DateTime.UtcNow;
        var periodStartUtc = generatedAtUtc.Date.AddDays(-periodDays);

        var rfqQuery = db.Rfqs
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.IssueDate >= periodStartUtc);
        var orderQuery = db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.OrderDate >= periodStartUtc);
        var receiptQuery = db.GoodsReceipts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.ReceiptDate >= periodStartUtc);

        if (companyId.HasValue)
        {
            rfqQuery = rfqQuery.Where(x => x.CompanyId == companyId.Value);
            orderQuery = orderQuery.Where(x => x.CompanyId == companyId.Value);
            receiptQuery = receiptQuery.Where(x => x.CompanyId == companyId.Value);
        }

        if (projectId.HasValue)
        {
            rfqQuery = rfqQuery.Where(x => x.PurchaseRequest.ProjectId == projectId.Value);
            orderQuery = orderQuery.Where(x => x.ProjectId == projectId.Value);
            receiptQuery = receiptQuery.Where(x => x.PurchaseOrder.ProjectId == projectId.Value);
        }

        var invitationStats = await rfqQuery
            .SelectMany(x => x.Suppliers)
            .GroupBy(x => new
            {
                x.SupplierCurrentAccountId,
                x.SupplierCurrentAccount.CompanyId,
                x.SupplierCurrentAccount.Code,
                x.SupplierCurrentAccount.Title
            })
            .Select(group => new
            {
                group.Key.SupplierCurrentAccountId,
                group.Key.CompanyId,
                SupplierCode = group.Key.Code,
                SupplierTitle = group.Key.Title,
                InvitationCount = group.Count(),
                ResponseCount = group.Count(x => x.Quotations.Any()),
                AwardCount = group.Count(x => x.Status == RfqSupplierStatus.Awarded)
            })
            .ToListAsync(cancellationToken);

        var quotationRows = await rfqQuery
            .SelectMany(x => x.Suppliers)
            .SelectMany(x => x.Quotations)
            .Select(x => new
            {
                RfqId = x.RfqSupplier.RfqId,
                x.RfqSupplierId,
                SupplierCurrentAccountId = x.RfqSupplier.SupplierCurrentAccountId,
                SupplierCompanyId = x.RfqSupplier.SupplierCurrentAccount.CompanyId,
                SupplierCode = x.RfqSupplier.SupplierCurrentAccount.Code,
                SupplierTitle = x.RfqSupplier.SupplierCurrentAccount.Title,
                ProjectId = x.RfqSupplier.Rfq.PurchaseRequest.ProjectId,
                ProjectCode = x.RfqSupplier.Rfq.PurchaseRequest.Project.Code,
                ProjectName = x.RfqSupplier.Rfq.PurchaseRequest.Project.Name,
                x.RfqSupplier.Rfq.RfqNumber,
                x.RfqSupplier.Rfq.IssueDate,
                RfqStatus = (int)x.RfqSupplier.Rfq.Status,
                SupplierStatus = (int)x.RfqSupplier.Status,
                x.QuotationDate,
                x.CreatedAtUtc,
                x.Currency,
                x.ExchangeRate,
                x.GrandTotal,
                x.DeliveryDays
            })
            .ToListAsync(cancellationToken);

        var orderStats = await orderQuery
            .GroupBy(x => new
            {
                x.SupplierCurrentAccountId,
                x.SupplierCurrentAccount.CompanyId,
                x.SupplierCurrentAccount.Code,
                x.SupplierCurrentAccount.Title
            })
            .Select(group => new
            {
                group.Key.SupplierCurrentAccountId,
                group.Key.CompanyId,
                SupplierCode = group.Key.Code,
                SupplierTitle = group.Key.Title,
                TotalOrderCount = group.Count(),
                CompletedOrderCount = group.Count(x => x.Status == PurchaseOrderStatus.Completed),
                ActiveOrderCount = group.Count(x =>
                    x.Status == PurchaseOrderStatus.Draft ||
                    x.Status == PurchaseOrderStatus.PendingApproval ||
                    x.Status == PurchaseOrderStatus.Approved ||
                    x.Status == PurchaseOrderStatus.PartiallyReceived),
                OverdueOpenOrderCount = group.Count(x =>
                    x.ExpectedDeliveryDate.HasValue &&
                    x.ExpectedDeliveryDate.Value < generatedAtUtc.Date &&
                    (x.Status == PurchaseOrderStatus.Approved ||
                     x.Status == PurchaseOrderStatus.PartiallyReceived)),
                DeliveryMeasuredOrderCount = group.Count(x =>
                    x.Status == PurchaseOrderStatus.Completed &&
                    x.ExpectedDeliveryDate.HasValue &&
                    x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted)),
                OnTimeDeliveryOrderCount = group.Count(x =>
                    x.Status == PurchaseOrderStatus.Completed &&
                    x.ExpectedDeliveryDate.HasValue &&
                    x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted) &&
                    !x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted &&
                        receipt.ReceiptDate > x.ExpectedDeliveryDate.Value)),
                LastOrderDate = group.Max(x => (DateTime?)x.OrderDate)
            })
            .ToListAsync(cancellationToken);

        var spendRows = await orderQuery
            .GroupBy(x => new
            {
                x.SupplierCurrentAccountId,
                x.Currency
            })
            .Select(group => new
            {
                group.Key.SupplierCurrentAccountId,
                Currency = group.Key.Currency,
                OrderTotal = group.Sum(x => x.GrandTotal)
            })
            .OrderBy(x => x.SupplierCurrentAccountId)
            .ThenBy(x => x.Currency)
            .ToListAsync(cancellationToken);

        var qualityStats = await receiptQuery
            .Where(x => x.Status == GoodsReceiptStatus.Posted)
            .SelectMany(x => x.Items)
            .GroupBy(x => new
            {
                x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccountId,
                x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccount.CompanyId,
                x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccount.Code,
                x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccount.Title
            })
            .Select(group => new
            {
                group.Key.SupplierCurrentAccountId,
                group.Key.CompanyId,
                SupplierCode = group.Key.Code,
                SupplierTitle = group.Key.Title,
                ReceiptLineCount = group.Count(),
                ExceptionLineCount = group.Count(x =>
                    x.RejectedQuantity > 0m ||
                    x.DamagedQuantity > 0m)
            })
            .ToListAsync(cancellationToken);

        var accumulators = new Dictionary<Guid, SupplierAccumulator>();

        SupplierAccumulator GetSupplier(
            Guid supplierId,
            Guid supplierCompanyId,
            string supplierCode,
            string supplierTitle)
        {
            if (accumulators.TryGetValue(supplierId, out var current))
                return current;

            current = new SupplierAccumulator(
                supplierId,
                supplierCompanyId,
                supplierCode,
                supplierTitle);
            accumulators.Add(supplierId, current);
            return current;
        }

        foreach (var row in invitationStats)
        {
            var supplier = GetSupplier(
                row.SupplierCurrentAccountId,
                row.CompanyId,
                row.SupplierCode,
                row.SupplierTitle);
            supplier.InvitationCount = row.InvitationCount;
            supplier.ResponseCount = row.ResponseCount;
            supplier.AwardCount = row.AwardCount;
        }

        foreach (var row in orderStats)
        {
            var supplier = GetSupplier(
                row.SupplierCurrentAccountId,
                row.CompanyId,
                row.SupplierCode,
                row.SupplierTitle);
            supplier.TotalOrderCount = row.TotalOrderCount;
            supplier.CompletedOrderCount = row.CompletedOrderCount;
            supplier.ActiveOrderCount = row.ActiveOrderCount;
            supplier.OverdueOpenOrderCount = row.OverdueOpenOrderCount;
            supplier.DeliveryMeasuredOrderCount = row.DeliveryMeasuredOrderCount;
            supplier.OnTimeDeliveryOrderCount = row.OnTimeDeliveryOrderCount;
            supplier.LastOrderDate = row.LastOrderDate;
        }

        foreach (var row in qualityStats)
        {
            var supplier = GetSupplier(
                row.SupplierCurrentAccountId,
                row.CompanyId,
                row.SupplierCode,
                row.SupplierTitle);
            supplier.ReceiptLineCount = row.ReceiptLineCount;
            supplier.ExceptionLineCount = row.ExceptionLineCount;
        }

        var latestQuotationRows = quotationRows
            .GroupBy(x => new { x.RfqId, x.RfqSupplierId })
            .Select(group => group
                .OrderByDescending(x => x.QuotationDate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .First())
            .ToList();

        foreach (var row in latestQuotationRows)
        {
            GetSupplier(
                row.SupplierCurrentAccountId,
                row.SupplierCompanyId,
                row.SupplierCode,
                row.SupplierTitle);
        }

        foreach (var group in latestQuotationRows.GroupBy(x => x.RfqId))
        {
            var normalizedRows = group
                .Select(row => new
                {
                    Row = row,
                    Total = ProcurementDecisionScoring.Normalize(
                        row.GrandTotal,
                        row.ExchangeRate)
                })
                .Where(x => x.Total > 0m)
                .ToList();

            if (normalizedRows.Count == 0)
                continue;

            var lowest = normalizedRows.Min(x => x.Total);
            foreach (var row in normalizedRows)
            {
                accumulators[row.Row.SupplierCurrentAccountId].PriceScores.Add(
                    ProcurementDecisionScoring.PriceScore(row.Total, lowest));
            }
        }

        var spendBySupplier = spendRows
            .GroupBy(x => x.SupplierCurrentAccountId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SupplierSpendCurrencyResponse>)group
                    .Select(x => new SupplierSpendCurrencyResponse(
                        x.Currency,
                        x.OrderTotal))
                    .ToList());

        var suppliers = accumulators.Values
            .Select(supplier =>
            {
                var history = supplier.History;
                var priceScore = supplier.PriceScores.Count == 0
                    ? 50m
                    : ProcurementDecisionScoring.RoundScore(
                        supplier.PriceScores.Average());

                return new SupplierPerformanceResponse(
                    supplier.SupplierCurrentAccountId,
                    supplier.CompanyId,
                    supplier.SupplierCode,
                    supplier.SupplierTitle,
                    supplier.InvitationCount,
                    supplier.ResponseCount,
                    history.ResponseRate,
                    supplier.AwardCount,
                    supplier.TotalOrderCount,
                    supplier.CompletedOrderCount,
                    supplier.ActiveOrderCount,
                    supplier.OverdueOpenOrderCount,
                    supplier.DeliveryMeasuredOrderCount,
                    supplier.OnTimeDeliveryOrderCount,
                    history.OnTimeDeliveryRate,
                    supplier.ReceiptLineCount,
                    supplier.ExceptionLineCount,
                    history.QualityRate,
                    supplier.PriceScores.Count,
                    priceScore,
                    ProcurementDecisionScoring.SupplierPerformanceScore(
                        priceScore,
                        history.HistoryScore),
                    history.Confidence,
                    supplier.LastOrderDate,
                    spendBySupplier.GetValueOrDefault(
                        supplier.SupplierCurrentAccountId,
                        Array.Empty<SupplierSpendCurrencyResponse>()));
            })
            .OrderByDescending(x => x.PerformanceScore)
            .ThenByDescending(x => x.TotalOrderCount)
            .ThenBy(x => x.SupplierTitle)
            .ToList();

        var supplierById = suppliers.ToDictionary(
            x => x.SupplierCurrentAccountId);

        var recentRfqComparisons = latestQuotationRows
            .GroupBy(x => x.RfqId)
            .Select(group =>
            {
                var shortestDeliveryDays = group
                    .Where(x => x.DeliveryDays.HasValue)
                    .Select(x => x.DeliveryDays)
                    .Min();
                var normalizedRows = group
                    .Select(row =>
                    {
                        var normalizedTotal = ProcurementDecisionScoring.Normalize(
                            row.GrandTotal,
                            row.ExchangeRate);
                        return new
                        {
                            Row = row,
                            NormalizedTotal = normalizedTotal
                        };
                    })
                    .Where(x => x.NormalizedTotal > 0m)
                    .ToList();

                if (normalizedRows.Count < 2)
                    return null;

                var lowest = normalizedRows.Min(x => x.NormalizedTotal);
                var highest = normalizedRows.Max(x => x.NormalizedTotal);
                var candidates = normalizedRows
                    .Select(x =>
                    {
                        var historyScore = supplierById.TryGetValue(
                            x.Row.SupplierCurrentAccountId,
                            out var performance)
                            ? performance.PerformanceScore
                            : 50m;
                        var decisionScore = ProcurementDecisionScoring.DecisionScore(
                            ProcurementDecisionScoring.PriceScore(
                                x.NormalizedTotal,
                                lowest),
                            ProcurementDecisionScoring.DeliveryTermScore(
                                x.Row.DeliveryDays,
                                shortestDeliveryDays),
                            historyScore);
                        return new
                        {
                            x.Row,
                            x.NormalizedTotal,
                            DecisionScore = decisionScore
                        };
                    })
                    .OrderByDescending(x => x.DecisionScore)
                    .ThenBy(x => x.NormalizedTotal)
                    .ToList();

                var recommended = candidates[0];
                var awarded = candidates.FirstOrDefault(x =>
                    x.Row.SupplierStatus == (int)RfqSupplierStatus.Awarded);
                var first = recommended.Row;

                return new RfqDecisionSupportResponse(
                    first.RfqId,
                    first.ProjectId,
                    first.ProjectCode,
                    first.ProjectName,
                    first.RfqNumber,
                    first.IssueDate,
                    first.RfqStatus,
                    candidates.Count,
                    ProcurementDecisionScoring.ComparisonCurrency,
                    lowest,
                    highest,
                    decimal.Round(
                        candidates.Average(x => x.NormalizedTotal),
                        2,
                        MidpointRounding.AwayFromZero),
                    highest - lowest,
                    recommended.Row.SupplierCurrentAccountId,
                    recommended.Row.SupplierTitle,
                    recommended.NormalizedTotal,
                    recommended.DecisionScore,
                    awarded?.Row.SupplierCurrentAccountId,
                    awarded?.Row.SupplierTitle,
                    awarded?.NormalizedTotal);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.IssueDate)
            .Take(20)
            .ToList();

        var invitationCount = suppliers.Sum(x => x.InvitationCount);
        var responseCount = suppliers.Sum(x => x.ResponseCount);
        var deliveryMeasuredCount = suppliers.Sum(x => x.DeliveryMeasuredOrderCount);
        var onTimeCount = suppliers.Sum(x => x.OnTimeDeliveryOrderCount);
        var receiptLineCount = suppliers.Sum(x => x.ReceiptLineCount);
        var exceptionLineCount = suppliers.Sum(x => x.ExceptionLineCount);

        var summary = new ProcurementDecisionSupportSummary(
            suppliers.Count,
            recentRfqComparisons.Count,
            suppliers.Count == 0
                ? 0m
                : ProcurementDecisionScoring.RoundScore(
                    suppliers.Average(x => x.PerformanceScore)),
            ProcurementDecisionScoring.Rate(
                responseCount,
                invitationCount,
                0m),
            ProcurementDecisionScoring.Rate(
                onTimeCount,
                deliveryMeasuredCount,
                0m),
            receiptLineCount == 0
                ? 0m
                : ProcurementDecisionScoring.Rate(
                    receiptLineCount - exceptionLineCount,
                    receiptLineCount,
                    0m),
            decimal.Round(
                recentRfqComparisons.Sum(x => x.OfferSpread),
                2,
                MidpointRounding.AwayFromZero));

        var alerts = BuildAlerts(suppliers, recentRfqComparisons);

        return Ok(new ProcurementDecisionSupportResponse(
            companyId,
            projectId,
            periodDays,
            periodStartUtc,
            generatedAtUtc,
            summary,
            suppliers,
            recentRfqComparisons,
            alerts));
    }

    private static IReadOnlyList<ProcurementDecisionAlert> BuildAlerts(
        IReadOnlyList<SupplierPerformanceResponse> suppliers,
        IReadOnlyList<RfqDecisionSupportResponse> comparisons)
    {
        var alerts = new List<ProcurementDecisionAlert>();
        var lowPerformanceCount = suppliers.Count(x =>
            x.TotalOrderCount >= 2 &&
            x.PerformanceScore < 60m);
        var overdueOrderCount = suppliers.Sum(x => x.OverdueOpenOrderCount);
        var highSpreadCount = comparisons.Count(x =>
            x.LowestNormalizedTotal > 0m &&
            x.OfferSpread / x.LowestNormalizedTotal >= 0.10m);

        AddAlert(
            alerts,
            lowPerformanceCount,
            "danger",
            "low-supplier-performance",
            "Düşük performanslı tedarikçiler",
            "En az iki sipariş geçmişi olan ve puanı 60'ın altında kalan tedarikçiler bulunuyor.",
            "/satin-alma/karar-destek");
        AddAlert(
            alerts,
            overdueOrderCount,
            "danger",
            "supplier-overdue-orders",
            "Geciken açık siparişler",
            "Tedarikçi performansını etkileyen teslim tarihi geçmiş siparişler bulunuyor.",
            "/satin-alma/siparis");
        AddAlert(
            alerts,
            highSpreadCount,
            "warning",
            "high-rfq-price-spread",
            "Yüksek teklif farkı bulunan RFQ'lar",
            "En düşük teklife göre yüzde 10'dan fazla fiyat farkı bulunan karşılaştırmalar var.",
            "/satin-alma/karar-destek");

        return alerts;
    }

    private static void AddAlert(
        ICollection<ProcurementDecisionAlert> alerts,
        int count,
        string severity,
        string code,
        string title,
        string message,
        string href)
    {
        if (count <= 0)
            return;

        alerts.Add(new ProcurementDecisionAlert(
            severity,
            code,
            title,
            message,
            count,
            href));
    }

    private sealed class SupplierAccumulator(
        Guid supplierCurrentAccountId,
        Guid companyId,
        string supplierCode,
        string supplierTitle)
    {
        public Guid SupplierCurrentAccountId { get; } = supplierCurrentAccountId;
        public Guid CompanyId { get; } = companyId;
        public string SupplierCode { get; } = supplierCode;
        public string SupplierTitle { get; } = supplierTitle;
        public int InvitationCount { get; set; }
        public int ResponseCount { get; set; }
        public int AwardCount { get; set; }
        public int TotalOrderCount { get; set; }
        public int CompletedOrderCount { get; set; }
        public int ActiveOrderCount { get; set; }
        public int OverdueOpenOrderCount { get; set; }
        public int DeliveryMeasuredOrderCount { get; set; }
        public int OnTimeDeliveryOrderCount { get; set; }
        public int ReceiptLineCount { get; set; }
        public int ExceptionLineCount { get; set; }
        public DateTime? LastOrderDate { get; set; }
        public List<decimal> PriceScores { get; } = new();

        public SupplierHistoryMetrics History => new(
            InvitationCount,
            ResponseCount,
            DeliveryMeasuredOrderCount,
            OnTimeDeliveryOrderCount,
            ReceiptLineCount,
            ExceptionLineCount);
    }
}
