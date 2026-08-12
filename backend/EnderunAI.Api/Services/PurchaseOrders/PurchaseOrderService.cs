using EnderunAI.Api.Contracts.PurchaseOrders;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.Procurement;
using Microsoft.EntityFrameworkCore;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Services.PurchaseOrders;

public sealed class PurchaseOrderService(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser) : IPurchaseOrderService
{
    private IProcurementApprovalService ApprovalService() =>
        new ProcurementApprovalService(db, dataScope, currentUser);

    public async Task<IReadOnlyList<PurchaseOrderListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? projectId,
        int? status,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var query = db.PurchaseOrders.AsNoTracking().ApplyScope(scope);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(PurchaseOrderStatus), status.Value))
                throw new ProcurementValidationException("Geçersiz satın alma siparişi durumu.");

            query = query.Where(x => x.Status == (PurchaseOrderStatus)status.Value);
        }

        return await query
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new PurchaseOrderListItemResponse(
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.RfqId,
                x.Rfq.RfqNumber,
                x.SupplierCurrentAccountId,
                x.SupplierCurrentAccount.Code,
                x.SupplierCurrentAccount.Title,
                x.OrderNumber,
                x.OrderDate,
                x.ExpectedDeliveryDate,
                (int)x.Status,
                x.Currency,
                x.GrandTotal,
                x.Items.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseOrderDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        EnsureAnyPermission(
            PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.FinanceView);
        var scope = await GetScopeAsync(cancellationToken);
        var result = await db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Id == id)
            .Select(x => new PurchaseOrderDetailResponse(
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.RfqId,
                x.Rfq.RfqNumber,
                x.Rfq.PurchaseRequestId,
                x.Rfq.PurchaseRequest.RequestNumber,
                x.SupplierCurrentAccountId,
                x.SupplierCurrentAccount.Code,
                x.SupplierCurrentAccount.Title,
                x.SupplierCurrentAccount.AuthorizedPerson,
                x.SupplierCurrentAccount.Phone,
                x.SupplierCurrentAccount.Email,
                x.SupplierCurrentAccount.Address,
                x.OrderNumber,
                x.OrderDate,
                x.ExpectedDeliveryDate,
                (int)x.Status,
                x.Currency,
                x.ExchangeRate,
                x.PaymentTerm,
                x.DeliveryAddress,
                x.Description,
                x.Notes,
                x.Subtotal,
                x.DiscountTotal,
                x.GrandTotal,
                x.ApprovedAtUtc,
                x.CancelledAtUtc,
                x.CancellationReason,
                x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new PurchaseOrderItemResponse(
                        i.Id,
                        i.RfqItemId,
                        i.RfqSupplierQuotationItemId,
                        i.LineNumber,
                        i.MaterialDescription,
                        i.Brand,
                        i.Model,
                        i.Quantity,
                        i.ReceivedQuantity,
                        i.Unit,
                        i.UnitPrice,
                        i.DiscountRate,
                        i.NetUnitPrice,
                        i.TotalPrice,
                        i.DeliveryDays,
                        i.ExpectedDeliveryDate,
                        i.Notes))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return result ??
            throw new ProcurementNotFoundException("Satın alma siparişi bulunamadı.");
    }

    public async Task<CreatePurchaseOrderFromRfqResponse> CreateFromRfqAsync(
        Guid rfqId,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var rfq = await db.Rfqs
            .ApplyScope(scope)
            .Include(x => x.PurchaseRequest)
                .ThenInclude(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == rfqId, cancellationToken);

        if (rfq is null)
            throw new ProcurementNotFoundException("RFQ bulunamadı.");

        if (rfq.Status != RfqStatus.Awarded)
            throw new ProcurementValidationException("Sipariş yalnız sonuçlandırılmış RFQ'dan oluşturulabilir.");

        if (await db.PurchaseOrders.AnyAsync(x => x.RfqId == rfqId, cancellationToken))
            throw new ProcurementValidationException("Bu RFQ için satın alma siparişi zaten oluşturulmuş.");

        var supplier = await db.RfqSuppliers
            .Where(x => x.RfqId == rfqId && x.Status == RfqSupplierStatus.Awarded)
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Quotations)
                .ThenInclude(x => x.Items)
                    .ThenInclude(x => x.RfqItem)
                        .ThenInclude(x => x.PurchaseRequestItem)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        var quotation = supplier?.Quotations
            .OrderByDescending(x => x.QuotationDate)
            .FirstOrDefault();

        if (supplier is null || quotation is null || quotation.Items.Count == 0)
            throw new ProcurementValidationException("Kazanan tedarikçinin siparişe dönüştürülebilir teklifi yok.");

        var orderNumber = await documentNumbers.GenerateAsync(
            rfq.CompanyId,
            "PURCHASE_ORDER",
            "PO",
            cancellationToken);

        var defaultVatRate = await db.CompanyFinanceSettings
            .Where(x => x.CompanyId == rfq.CompanyId)
            .Select(x => (decimal?)x.DefaultVatRate)
            .SingleOrDefaultAsync(cancellationToken) ?? 20m;

        var expectedDeliveryDate = quotation.DeliveryDays.HasValue
            ? DateTime.UtcNow.Date.AddDays(quotation.DeliveryDays.Value)
            : (DateTime?)null;

        var entity = new PurchaseOrderEntity
        {
            CompanyId = rfq.CompanyId,
            ProjectId = rfq.PurchaseRequest.ProjectId,
            RfqId = rfq.Id,
            SupplierCurrentAccountId = supplier.SupplierCurrentAccountId,
            OrderNumber = orderNumber,
            OrderDate = DateTime.UtcNow,
            ExpectedDeliveryDate = expectedDeliveryDate,
            Status = PurchaseOrderStatus.Draft,
            Currency = ProcurementServiceSupport.CurrencyOrTry(quotation.Currency, 10),
            ExchangeRate = quotation.ExchangeRate > 0 ? quotation.ExchangeRate : 1m,
            PaymentTerm = quotation.PaymentTerm,
            DeliveryAddress = rfq.PurchaseRequest.Project.Address,
            Description = rfq.Description,
            Notes = quotation.Notes,
            Subtotal = quotation.Subtotal,
            DiscountTotal = quotation.DiscountTotal,
            GrandTotal = quotation.GrandTotal,
            // RFQ zinciri KDV taşımıyor; sipariş, şirket finans
            // ayarlarındaki varsayılan oranla KDV hesaplar.
            VatRate = defaultVatRate,
            VatAmount = decimal.Round(quotation.GrandTotal * defaultVatRate / 100m, 2),
            Items = quotation.Items
                .OrderBy(x => x.RfqItem.LineNumber)
                .Select(x => new PurchaseOrderItem
                {
                    RfqItemId = x.RfqItemId,
                    RfqSupplierQuotationItemId = x.Id,
                    LineNumber = x.RfqItem.LineNumber,
                    // Stok kartı bağı talepten taşınır: talep → RFQ →
                    // sipariş → mal kabul zinciri kopmasın diye. Talepte
                    // kart seçilmemişse null kalır, akış aynen çalışır.
                    InventoryItemId = x.RfqItem.PurchaseRequestItem != null
                        ? x.RfqItem.PurchaseRequestItem.InventoryItemId
                        : null,
                    EngineeringPositionId = x.RfqItem.EngineeringPositionId,
                    MaterialDescription = x.RfqItem.MaterialDescription,
                    // İKİ MARKA YAN YANA: Brand tedarikçinin VERDİĞİ
                    // (tekliften), RequestedBrand talep edenin
                    // İSTEDİĞİ (RFQ üzerinden talepten). Tek alanda
                    // birleştirilseydi "istenen mi geldi, muadil mi"
                    // sorusu bir daha cevaplanamazdı.
                    Brand = x.Brand,
                    RequestedBrand = x.RfqItem.RequestedBrand,
                    BrandIrrelevant = x.RfqItem.BrandIrrelevant,
                    Model = x.Model,
                    Quantity = x.Quantity,
                    ReceivedQuantity = 0m,
                    Unit = x.RfqItem.Unit,
                    UnitPrice = x.UnitPrice,
                    DiscountRate = x.DiscountRate,
                    NetUnitPrice = x.NetUnitPrice,
                    TotalPrice = x.TotalPrice,
                    DeliveryDays = x.DeliveryDays,
                    ExpectedDeliveryDate = x.DeliveryDays.HasValue
                        ? DateTime.UtcNow.Date.AddDays(x.DeliveryDays.Value)
                        : expectedDeliveryDate,
                    Notes = x.Notes
                })
                .ToList()
        };

        rfq.PurchaseRequest.Status = PurchaseRequestStatus.Ordered;
        db.PurchaseOrders.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseOrderFromRfqResponse(
            entity.Id,
            entity.OrderNumber,
            entity.RfqId,
            entity.SupplierCurrentAccountId,
            supplier.SupplierCurrentAccount.Title,
            entity.GrandTotal,
            entity.Currency);
    }

    public async Task<PurchaseOrderActionResponse> SubmitAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await ApprovalService().SubmitOrderAsync(id, cancellationToken);

    public async Task<PurchaseOrderActionResponse> ApproveAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await ApprovalService().ApproveOrderAsync(id, cancellationToken);

    public async Task<PurchaseOrderActionResponse> RejectAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken) =>
        await ApprovalService().RejectOrderAsync(id, reason, cancellationToken);

    public async Task<PurchaseOrderActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ProcurementValidationException("İptal nedeni zorunludur.");

        var entity = await GetTrackedAsync(id, cancellationToken);
        if (entity.Status is
            PurchaseOrderStatus.PartiallyReceived or
            PurchaseOrderStatus.Completed or
            PurchaseOrderStatus.Cancelled)
        {
            throw new ProcurementValidationException(
                "Teslim alınmış, tamamlanmış veya iptal edilmiş sipariş iptal edilemez.");
        }

        entity.Status = PurchaseOrderStatus.Cancelled;
        entity.CancelledByUserId = currentUser.UserId;
        entity.CancelledAtUtc = DateTime.UtcNow;
        entity.CancellationReason = reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Action(entity, "Satın alma siparişi iptal edildi.");
    }

    private async Task<PurchaseOrderEntity> GetTrackedAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        return await db.PurchaseOrders
            .ApplyScope(scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ??
            throw new ProcurementNotFoundException("Satın alma siparişi bulunamadı.");
    }

    private static PurchaseOrderActionResponse Action(
        PurchaseOrderEntity entity,
        string message) =>
        new(entity.Id, entity.OrderNumber, (int)entity.Status, message);

    private void EnsureAnyPermission(params string[] permissions)
    {
        if (!permissions.Any(currentUser.HasPermission))
            throw new UnauthorizedAccessException(
                "Bu satın alma işlemi için gerekli yetkiniz bulunmuyor.");
    }

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");
}
