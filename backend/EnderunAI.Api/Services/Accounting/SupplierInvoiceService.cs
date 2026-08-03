using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public interface ISupplierInvoiceService
{
    Task<IReadOnlyCollection<SupplierInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? supplierId,
        string? search, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> CreateAsync(
        CreateSupplierInvoiceRequest request, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSupplierInvoiceRequest request, CancellationToken cancellationToken);

    Task<SupplierInvoiceActionResponse> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> ApproveAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> RejectAsync(Guid id, string reason, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> CancelAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class SupplierInvoiceService(
    AppDbContext db,
    IDocumentNumberService documentNumberService,
    IAccountingIntegrationService accountingIntegration,
    ICurrentUserService currentUser) : ISupplierInvoiceService
{
    private static readonly string[] GmRoleNames = ["Admin", "Genel Müdür"];

    public async Task<IReadOnlyCollection<SupplierInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? supplierId,
        string? search, CancellationToken cancellationToken)
    {
        var query = db.SupplierInvoices.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (supplierId.HasValue)
            query = query.Where(x => x.SupplierCurrentAccountId == supplierId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.InvoiceNumber, $"%{term}%") ||
                EF.Functions.ILike(x.InternalNumber, $"%{term}%") ||
                EF.Functions.ILike(x.SupplierCurrentAccount.Title, $"%{term}%"));
        }

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new SupplierInvoiceListItemResponse(
                x.Id, x.InternalNumber, x.InvoiceNumber, x.InvoiceDate,
                x.SupplierCurrentAccountId, x.SupplierCurrentAccount.Title,
                x.ProjectId, x.Project.Code, x.Project.Name,
                x.CurrencyCode, x.Subtotal, x.VatTotal, x.GrandTotal,
                (int)x.Status, (int)x.MatchStatus, x.RequiresGmApproval,
                x.PurchaseOrder != null ? x.PurchaseOrder.OrderNumber : null,
                x.AccountingVoucher != null ? x.AccountingVoucher.VoucherNumber : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierInvoiceDetailResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await LoadDetailAsync(id, cancellationToken);
        return invoice is null
            ? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.")
            : MapDetail(invoice);
    }

    public async Task<SupplierInvoiceDetailResponse> CreateAsync(
        CreateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        await ValidateHeaderAsync(
            request.CompanyId, request.SupplierCurrentAccountId, request.ProjectId,
            request.PurchaseOrderId, request.GoodsReceiptId,
            request.InvoiceNumber, request.CurrencyCode, request.ExchangeRate,
            cancellationToken);

        var items = BuildItems(request.Items);

        var internalNumber = await documentNumberService.GenerateAsync(
            request.CompanyId, "SUPPLIER_INVOICE", "SFT", cancellationToken);

        var invoice = new SupplierInvoice
        {
            CompanyId = request.CompanyId,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            ProjectId = request.ProjectId,
            PurchaseOrderId = request.PurchaseOrderId,
            GoodsReceiptId = request.GoodsReceiptId,
            InternalNumber = internalNumber,
            InvoiceNumber = request.InvoiceNumber.Trim(),
            InvoiceDate = AsUtc(request.InvoiceDate),
            DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            Description = Normalize(request.Description),
            Status = SupplierInvoiceStatus.Draft
        };

        ApplyItemsAndTotals(invoice, items);

        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SupplierInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak faturalar güncellenebilir.");

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
            throw new ArgumentException("Tedarikçi fatura numarası zorunludur.");

        var items = BuildItems(request.Items);

        invoice.InvoiceNumber = request.InvoiceNumber.Trim();
        invoice.InvoiceDate = AsUtc(request.InvoiceDate);
        invoice.DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null;
        invoice.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        invoice.ExchangeRate = request.ExchangeRate;
        invoice.Description = Normalize(request.Description);
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        db.SupplierInvoiceItems.RemoveRange(invoice.Items);
        invoice.Items.Clear();
        ApplyItemsAndTotals(invoice, items);

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SupplierInvoiceActionResponse> SubmitAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak faturalar onaya gönderilebilir.");

        var settings = await accountingIntegration.GetOrCreateFinanceSettingsAsync(
            invoice.CompanyId, cancellationToken);

        await RunThreeWayMatchAsync(invoice, settings, cancellationToken);

        var grandTotalTry = decimal.Round(invoice.GrandTotal * invoice.ExchangeRate, 2);
        if (grandTotalTry > settings.GmApprovalThresholdTry)
        {
            invoice.RequiresGmApproval = true;
            invoice.MatchNote = AppendNote(invoice.MatchNote,
                $"Tutar ({grandTotalTry:N2} TL) GM onay eşiğini ({settings.GmApprovalThresholdTry:N2} TL) aşıyor.");
        }

        invoice.Status = SupplierInvoiceStatus.PendingApproval;
        invoice.SubmittedAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status,
            invoice.RequiresGmApproval
                ? "Fatura onaya gönderildi — Genel Müdür onayı gerekiyor."
                : "Fatura onaya gönderildi.");
    }

    public async Task<SupplierInvoiceActionResponse> ApproveAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen faturalar onaylanabilir.");

        if (invoice.RequiresGmApproval &&
            !currentUser.Roles.Any(role => GmRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Bu fatura (tolerans dışı fark veya tutar eşiği nedeniyle) yalnızca Genel Müdür/Admin tarafından onaylanabilir.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var posting = await accountingIntegration.CreateSupplierInvoiceVoucherAsync(
            invoice, cancellationToken);

        invoice.Status = SupplierInvoiceStatus.Approved;
        invoice.AccountingVoucherId = posting.VoucherId;
        invoice.ApprovedByUserId = currentUser.UserId;
        invoice.ApprovedAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        // Mal kabul (depo) üzerinden gelen faturalarda proje maliyeti,
        // stok çıkışında (InventoryController → StockMovement) oluşur;
        // burada da yazılırsa maliyet çift sayılır. Yalnız depoya
        // uğramayan doğrudan hizmet/masraf faturaları projeye işlenir.
        if (invoice.GoodsReceiptId is null)
        {
            var supplierTitle = await db.CurrentAccounts
                .Where(x => x.Id == invoice.SupplierCurrentAccountId)
                .Select(x => x.Title)
                .SingleAsync(cancellationToken);

            db.ProjectCostTransactions.Add(new ProjectCostTransaction
            {
                ProjectId = invoice.ProjectId,
                CostType = invoice.PurchaseOrderId is null
                    ? ProjectCostType.Other
                    : ProjectCostType.Material,
                CostDate = invoice.InvoiceDate,
                Amount = decimal.Round(invoice.Subtotal * invoice.ExchangeRate, 2),
                Description = $"Tedarikçi faturası {invoice.InternalNumber} — {supplierTitle}",
                ReferenceType = "SupplierInvoice",
                ReferenceId = invoice.Id,
                // Muhasebedeki maliyet satırına bağla — proje maliyeti ile
                // gider hesapları arasında iki ayrı "doğru" rakam oluşmasın.
                AccountingVoucherLineId = posting.ExpenseLineId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status,
            "Fatura onaylandı; muhasebe fişi otomatik oluşturuldu ve kesinleştirildi.");
    }

    public async Task<SupplierInvoiceActionResponse> RejectAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen faturalar reddedilebilir.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Ret gerekçesi zorunludur.");

        invoice.Status = SupplierInvoiceStatus.Rejected;
        invoice.RejectedByUserId = currentUser.UserId;
        invoice.RejectedAtUtc = DateTime.UtcNow;
        invoice.RejectionReason = reason.Trim();
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, "Fatura reddedildi.");
    }

    public async Task<SupplierInvoiceActionResponse> CancelAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status is not (SupplierInvoiceStatus.Draft or SupplierInvoiceStatus.PendingApproval))
        {
            throw new InvalidOperationException(
                "Yalnızca taslak veya onay bekleyen faturalar iptal edilebilir. " +
                "Onaylanmış fatura için muhasebe fişini iptal edip düzeltme kaydı oluşturun.");
        }

        invoice.Status = SupplierInvoiceStatus.Cancelled;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, "Fatura iptal edildi.");
    }

    /// <summary>
    /// 3 yönlü kontrol: fatura ara toplamı (KDV hariç) ↔ sipariş tutarı ↔
    /// mal kabul edilen miktar × sipariş birim fiyatı. En büyük mutlak
    /// fark tolerans yüzdesiyle karşılaştırılır.
    /// </summary>
    private async Task RunThreeWayMatchAsync(
        SupplierInvoice invoice,
        CompanyFinanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (invoice.PurchaseOrderId is null)
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.NotApplicable;
            invoice.MatchDifferenceAmount = 0m;
            invoice.MatchNote = null;
            invoice.RequiresGmApproval = false;
            return;
        }

        var poNet = await db.PurchaseOrders
            .Where(x => x.Id == invoice.PurchaseOrderId.Value)
            .Select(x => x.GrandTotal)
            .SingleAsync(cancellationToken);

        decimal? grNet = null;
        if (invoice.GoodsReceiptId is not null)
        {
            grNet = await db.GoodsReceiptItems
                .Where(x => x.GoodsReceiptId == invoice.GoodsReceiptId.Value)
                .SumAsync(x => x.AcceptedQuantity * x.PurchaseOrderItem.NetUnitPrice, cancellationToken);
            grNet = decimal.Round(grNet.Value, 2);
        }

        var invoiceNet = invoice.Subtotal;
        var diffToPo = invoiceNet - poNet;
        var diffToGr = grNet.HasValue ? invoiceNet - grNet.Value : 0m;
        var maxDiff = Math.Abs(diffToPo) >= Math.Abs(diffToGr) ? diffToPo : diffToGr;

        var baseAmount = poNet != 0m ? poNet : invoiceNet;
        var diffPercent = baseAmount != 0m
            ? Math.Abs(maxDiff) / Math.Abs(baseAmount) * 100m
            : 0m;

        invoice.MatchDifferenceAmount = decimal.Round(maxDiff, 2);

        var summary =
            $"Fatura (KDV hariç): {invoiceNet:N2} | Sipariş: {poNet:N2}" +
            (grNet.HasValue ? $" | Mal kabul: {grNet.Value:N2}" : "");

        if (diffPercent > settings.ThreeWayTolerancePercent)
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.ToleranceExceeded;
            invoice.RequiresGmApproval = true;
            invoice.MatchNote =
                $"{summary} — fark %{diffPercent:N2}, tolerans %{settings.ThreeWayTolerancePercent:N2} aşıldı.";
        }
        else
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.Matched;
            invoice.RequiresGmApproval = false;
            invoice.MatchNote = $"{summary} — tolerans içinde (%{diffPercent:N2}).";
        }
    }

    private async Task ValidateHeaderAsync(
        Guid companyId, Guid supplierId, Guid projectId,
        Guid? purchaseOrderId, Guid? goodsReceiptId,
        string invoiceNumber, string currencyCode, decimal exchangeRate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Tedarikçi fatura numarası zorunludur.");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new ArgumentException("Para birimi 3 karakter olmalıdır.");
        if (exchangeRate <= 0)
            throw new ArgumentException("Kur sıfırdan büyük olmalıdır.");

        var supplier = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == supplierId && x.CompanyId == companyId, cancellationToken)
            ?? throw new ArgumentException("Tedarikçi cari kartı bulunamadı.");

        if (supplier.Status != CurrentAccountStatus.Approved)
            throw new ArgumentException("Fatura yalnızca onaylı cari karta kesilebilir.");

        var projectExists = await db.Projects
            .AnyAsync(x => x.Id == projectId && x.CompanyId == companyId, cancellationToken);
        if (!projectExists)
            throw new ArgumentException("Proje bulunamadı.");

        if (purchaseOrderId is not null)
        {
            var poValid = await db.PurchaseOrders.AnyAsync(x =>
                x.Id == purchaseOrderId.Value &&
                x.CompanyId == companyId &&
                x.SupplierCurrentAccountId == supplierId, cancellationToken);
            if (!poValid)
                throw new ArgumentException("Sipariş bulunamadı veya bu tedarikçiye ait değil.");
        }

        if (goodsReceiptId is not null)
        {
            if (purchaseOrderId is null)
                throw new ArgumentException("Mal kabul bağlantısı için sipariş de seçilmelidir.");

            var grValid = await db.GoodsReceipts.AnyAsync(x =>
                x.Id == goodsReceiptId.Value &&
                x.PurchaseOrderId == purchaseOrderId.Value, cancellationToken);
            if (!grValid)
                throw new ArgumentException("Mal kabul bulunamadı veya seçilen siparişe ait değil.");
        }
    }

    private static List<SupplierInvoiceItem> BuildItems(
        IReadOnlyCollection<SupplierInvoiceItemRequest> requests)
    {
        if (requests is null || requests.Count == 0)
            throw new ArgumentException("Faturada en az bir kalem bulunmalıdır.");

        var items = new List<SupplierInvoiceItem>();
        var lineNumber = 1;

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException($"Kalem {lineNumber}: açıklama zorunludur.");
            if (request.Quantity <= 0)
                throw new ArgumentException($"Kalem {lineNumber}: miktar sıfırdan büyük olmalıdır.");
            if (request.UnitPrice < 0)
                throw new ArgumentException($"Kalem {lineNumber}: birim fiyat negatif olamaz.");
            if (request.VatRate is < 0 or > 100)
                throw new ArgumentException($"Kalem {lineNumber}: KDV oranı 0-100 arasında olmalıdır.");

            var lineSubtotal = decimal.Round(request.Quantity * request.UnitPrice, 2);
            var vatAmount = decimal.Round(lineSubtotal * request.VatRate / 100m, 2);

            items.Add(new SupplierInvoiceItem
            {
                LineNumber = lineNumber++,
                Description = request.Description.Trim(),
                Quantity = request.Quantity,
                Unit = string.IsNullOrWhiteSpace(request.Unit) ? "adet" : request.Unit.Trim(),
                UnitPrice = request.UnitPrice,
                VatRate = request.VatRate,
                LineSubtotal = lineSubtotal,
                VatAmount = vatAmount,
                LineTotal = lineSubtotal + vatAmount,
                PurchaseOrderItemId = request.PurchaseOrderItemId
            });
        }

        return items;
    }

    private static void ApplyItemsAndTotals(SupplierInvoice invoice, List<SupplierInvoiceItem> items)
    {
        foreach (var item in items)
            invoice.Items.Add(item);

        invoice.Subtotal = decimal.Round(items.Sum(x => x.LineSubtotal), 2);
        invoice.VatTotal = decimal.Round(items.Sum(x => x.VatAmount), 2);
        invoice.GrandTotal = invoice.Subtotal + invoice.VatTotal;
    }

    private async Task<SupplierInvoice?> LoadDetailAsync(Guid id, CancellationToken cancellationToken) =>
        await db.SupplierInvoices
            .AsNoTracking()
            .Include(x => x.Items.OrderBy(i => i.LineNumber))
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Project)
            .Include(x => x.PurchaseOrder)
            .Include(x => x.GoodsReceipt)
            .Include(x => x.AccountingVoucher)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private static SupplierInvoiceDetailResponse MapDetail(SupplierInvoice invoice) =>
        new(
            invoice.Id, invoice.CompanyId, invoice.InternalNumber, invoice.InvoiceNumber,
            invoice.InvoiceDate, invoice.DueDate,
            invoice.SupplierCurrentAccountId, invoice.SupplierCurrentAccount.Title,
            invoice.ProjectId, invoice.Project.Code, invoice.Project.Name,
            invoice.PurchaseOrderId, invoice.PurchaseOrder?.OrderNumber,
            invoice.GoodsReceiptId, invoice.GoodsReceipt?.ReceiptNumber,
            invoice.CurrencyCode, invoice.ExchangeRate,
            invoice.Subtotal, invoice.VatTotal, invoice.GrandTotal,
            invoice.Description,
            (int)invoice.Status, (int)invoice.MatchStatus,
            invoice.MatchDifferenceAmount, invoice.MatchNote, invoice.RequiresGmApproval,
            invoice.SubmittedAtUtc, invoice.ApprovedAtUtc, invoice.RejectedAtUtc,
            invoice.RejectionReason,
            invoice.AccountingVoucherId, invoice.AccountingVoucher?.VoucherNumber,
            invoice.Items.Select(item => new SupplierInvoiceItemResponse(
                item.Id, item.LineNumber, item.Description, item.Quantity, item.Unit,
                item.UnitPrice, item.VatRate, item.LineSubtotal, item.VatAmount,
                item.LineTotal, item.PurchaseOrderItemId)).ToList());

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? AppendNote(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
