using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
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
[Route("api/procurement/dashboard")]
public sealed class ProcurementDashboardController(
    AppDbContext db,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<ActionResult<ProcurementDashboardResponse>> Get(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        var requestQuery = db.PurchaseRequests
            .AsNoTracking()
            .ApplyScope(scope);
        var rfqQuery = db.Rfqs
            .AsNoTracking()
            .ApplyScope(scope);
        var orderQuery = db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope);
        var receiptQuery = db.GoodsReceipts
            .AsNoTracking()
            .ApplyScope(scope);

        if (companyId.HasValue)
        {
            requestQuery = requestQuery.Where(x => x.CompanyId == companyId.Value);
            rfqQuery = rfqQuery.Where(x => x.CompanyId == companyId.Value);
            orderQuery = orderQuery.Where(x => x.CompanyId == companyId.Value);
            receiptQuery = receiptQuery.Where(x => x.CompanyId == companyId.Value);
        }

        if (projectId.HasValue)
        {
            requestQuery = requestQuery.Where(x => x.ProjectId == projectId.Value);
            rfqQuery = rfqQuery.Where(x => x.PurchaseRequest.ProjectId == projectId.Value);
            orderQuery = orderQuery.Where(x => x.ProjectId == projectId.Value);
            receiptQuery = receiptQuery.Where(x => x.PurchaseOrder.ProjectId == projectId.Value);
        }

        var utcNow = DateTime.UtcNow;
        var today = utcNow.Date;

        var purchaseRequests = await requestQuery
            .GroupBy(_ => 1)
            .Select(group => new PurchaseRequestDashboardSummary(
                group.Count(),
                group.Count(x => x.Status == PurchaseRequestStatus.Draft),
                group.Count(x => x.Status == PurchaseRequestStatus.Submitted),
                group.Count(x => x.Status == PurchaseRequestStatus.Approved),
                group.Count(x => x.Status == PurchaseRequestStatus.Quotation),
                group.Count(x => x.Status == PurchaseRequestStatus.Ordered),
                group.Count(x => x.Status == PurchaseRequestStatus.Completed),
                group.Count(x => x.Status == PurchaseRequestStatus.Cancelled),
                group.Count(x => x.Status == PurchaseRequestStatus.Rejected),
                group.Count(x =>
                    x.Status != PurchaseRequestStatus.Completed &&
                    x.Status != PurchaseRequestStatus.Cancelled &&
                    x.Status != PurchaseRequestStatus.Rejected),
                group.Count(x =>
                    x.Priority == PurchaseRequestPriority.Critical &&
                    x.Status != PurchaseRequestStatus.Completed &&
                    x.Status != PurchaseRequestStatus.Cancelled &&
                    x.Status != PurchaseRequestStatus.Rejected)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new PurchaseRequestDashboardSummary(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var rfqs = await rfqQuery
            .GroupBy(_ => 1)
            .Select(group => new RfqDashboardSummary(
                group.Count(),
                group.Count(x => x.Status == RfqStatus.Draft),
                group.Count(x => x.Status == RfqStatus.Sent),
                group.Count(x => x.Status == RfqStatus.ResponsesReceived),
                group.Count(x => x.Status == RfqStatus.Awarded),
                group.Count(x => x.Status == RfqStatus.Closed),
                group.Count(x => x.Status == RfqStatus.Cancelled),
                group.Count(x =>
                    x.ResponseDeadline.HasValue &&
                    x.ResponseDeadline.Value < utcNow &&
                    x.Status != RfqStatus.Awarded &&
                    x.Status != RfqStatus.Closed &&
                    x.Status != RfqStatus.Cancelled)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new RfqDashboardSummary(0, 0, 0, 0, 0, 0, 0, 0);

        var purchaseOrders = await orderQuery
            .GroupBy(_ => 1)
            .Select(group => new PurchaseOrderDashboardSummary(
                group.Count(),
                group.Count(x => x.Status == PurchaseOrderStatus.Draft),
                group.Count(x => x.Status == PurchaseOrderStatus.PendingApproval),
                group.Count(x => x.Status == PurchaseOrderStatus.Approved),
                group.Count(x => x.Status == PurchaseOrderStatus.PartiallyReceived),
                group.Count(x => x.Status == PurchaseOrderStatus.Completed),
                group.Count(x => x.Status == PurchaseOrderStatus.Cancelled),
                group.Count(x => x.Status == PurchaseOrderStatus.Rejected),
                group.Count(x =>
                    x.Status == PurchaseOrderStatus.Draft ||
                    x.Status == PurchaseOrderStatus.PendingApproval ||
                    x.Status == PurchaseOrderStatus.Approved ||
                    x.Status == PurchaseOrderStatus.PartiallyReceived),
                group.Count(x =>
                    x.ExpectedDeliveryDate.HasValue &&
                    x.ExpectedDeliveryDate.Value < today &&
                    (x.Status == PurchaseOrderStatus.Approved ||
                     x.Status == PurchaseOrderStatus.PartiallyReceived))))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new PurchaseOrderDashboardSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var orderCurrencyRows = await orderQuery
            .Select(x => new { x.Currency, x.GrandTotal, x.Status })
            .ToListAsync(cancellationToken);

        var orderValues = orderCurrencyRows
            .GroupBy(x => x.Currency)
            .Select(group => new PurchaseOrderCurrencySummary(
                group.Key,
                group.Sum(x => x.GrandTotal),
                group
                    .Where(x =>
                        x.Status == PurchaseOrderStatus.Draft ||
                        x.Status == PurchaseOrderStatus.PendingApproval ||
                        x.Status == PurchaseOrderStatus.Approved ||
                        x.Status == PurchaseOrderStatus.PartiallyReceived)
                    .Sum(x => x.GrandTotal),
                group
                    .Where(x => x.Status == PurchaseOrderStatus.Completed)
                    .Sum(x => x.GrandTotal)))
            .OrderBy(x => x.Currency)
            .ToList();

        var receiptStatuses = await receiptQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Draft = group.Count(x => x.Status == GoodsReceiptStatus.Draft),
                Posted = group.Count(x => x.Status == GoodsReceiptStatus.Posted),
                Cancelled = group.Count(x => x.Status == GoodsReceiptStatus.Cancelled)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var exceptionLineCount = await receiptQuery
            .SelectMany(x => x.Items)
            .CountAsync(x =>
                x.RejectedQuantity > 0m ||
                x.DamagedQuantity > 0m,
                cancellationToken);

        var goodsReceipts = receiptStatuses is null
            ? new GoodsReceiptDashboardSummary(0, 0, 0, 0, exceptionLineCount)
            : new GoodsReceiptDashboardSummary(
                receiptStatuses.Total,
                receiptStatuses.Draft,
                receiptStatuses.Posted,
                receiptStatuses.Cancelled,
                exceptionLineCount);

        var receiptItemRows = await receiptQuery
            .Where(x => x.Status == GoodsReceiptStatus.Posted)
            .SelectMany(x => x.Items)
            .Select(x => new
            {
                x.Unit,
                x.AcceptedQuantity,
                x.RejectedQuantity,
                x.DamagedQuantity
            })
            .ToListAsync(cancellationToken);

        var receiptQuantities = receiptItemRows
            .GroupBy(x => x.Unit)
            .Select(group => new GoodsReceiptUnitSummary(
                group.Key,
                group.Sum(x => x.AcceptedQuantity),
                group.Sum(x => x.RejectedQuantity),
                group.Sum(x => x.DamagedQuantity),
                group.Count(x =>
                    x.RejectedQuantity > 0m || x.DamagedQuantity > 0m)))
            .OrderBy(x => x.Unit)
            .ToList();

        var recentPurchaseOrders = await orderQuery
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new RecentPurchaseOrderDashboardItem(
                x.Id,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.OrderNumber,
                x.OrderDate,
                x.ExpectedDeliveryDate,
                (int)x.Status,
                x.SupplierCurrentAccount.Title,
                x.Currency,
                x.GrandTotal,
                x.Items.Count))
            .Take(10)
            .ToListAsync(cancellationToken);

        var recentGoodsReceipts = await receiptQuery
            .OrderByDescending(x => x.ReceiptDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new RecentGoodsReceiptDashboardItem(
                x.Id,
                x.PurchaseOrder.ProjectId,
                x.PurchaseOrder.Project.Code,
                x.PurchaseOrder.Project.Name,
                x.ReceiptNumber,
                x.ReceiptDate,
                (int)x.Status,
                x.PurchaseOrder.OrderNumber,
                x.PurchaseOrder.SupplierCurrentAccount.Title,
                x.Warehouse.Name,
                x.Items.Count,
                x.Items.Count(item =>
                    item.RejectedQuantity > 0m ||
                    item.DamagedQuantity > 0m)))
            .Take(10)
            .ToListAsync(cancellationToken);

        var alerts = BuildAlerts(
            purchaseRequests,
            rfqs,
            purchaseOrders,
            goodsReceipts);

        return Ok(new ProcurementDashboardResponse(
            companyId,
            projectId,
            utcNow,
            purchaseRequests,
            rfqs,
            purchaseOrders,
            goodsReceipts,
            orderValues,
            receiptQuantities,
            recentPurchaseOrders,
            recentGoodsReceipts,
            alerts));
    }

    private static IReadOnlyList<ProcurementDashboardAlert> BuildAlerts(
        PurchaseRequestDashboardSummary requests,
        RfqDashboardSummary rfqs,
        PurchaseOrderDashboardSummary orders,
        GoodsReceiptDashboardSummary receipts)
    {
        var alerts = new List<ProcurementDashboardAlert>();

        AddAlert(
            alerts,
            requests.Submitted,
            "warning",
            "purchase-requests-pending",
            "Onay bekleyen satın alma talepleri",
            "Onaya gönderilmiş talepler işlem bekliyor.",
            "/satin-alma");

        AddAlert(
            alerts,
            requests.CriticalOpen,
            "danger",
            "critical-purchase-requests",
            "Kritik açık talepler",
            "Kritik öncelikli satın alma talepleri henüz kapanmadı.",
            "/satin-alma");

        AddAlert(
            alerts,
            rfqs.ResponseOverdue,
            "danger",
            "rfq-response-overdue",
            "Yanıt süresi geçen RFQ kayıtları",
            "Tedarikçi yanıt süresi geçen RFQ süreçleri bulunuyor.",
            "/satin-alma/rfq");

        AddAlert(
            alerts,
            orders.PendingApproval,
            "warning",
            "purchase-orders-pending",
            "Onay bekleyen siparişler",
            "Satın alma siparişleri onay işlemi bekliyor.",
            "/satin-alma/siparis");

        AddAlert(
            alerts,
            orders.OverdueDelivery,
            "danger",
            "purchase-orders-overdue",
            "Teslim tarihi geçen siparişler",
            "Onaylı veya kısmi teslim siparişlerin teslim tarihi geçti.",
            "/satin-alma/siparis");

        AddAlert(
            alerts,
            receipts.Draft,
            "warning",
            "goods-receipts-draft",
            "Stok kaydı bekleyen mal kabuller",
            "Taslak mal kabul kayıtları henüz stoklara işlenmedi.",
            "/depo-stok/mal-kabul");

        AddAlert(
            alerts,
            receipts.ExceptionLineCount,
            "danger",
            "goods-receipt-exceptions",
            "Red veya hasar kaydı bulunan kalemler",
            "Mal kabul kalemlerinde red ya da hasar miktarı bulunuyor.",
            "/depo-stok/mal-kabul");

        return alerts;
    }

    private static void AddAlert(
        ICollection<ProcurementDashboardAlert> alerts,
        int count,
        string severity,
        string code,
        string title,
        string message,
        string href)
    {
        if (count <= 0)
            return;

        alerts.Add(new ProcurementDashboardAlert(
            severity,
            code,
            title,
            message,
            count,
            href));
    }
}
