namespace EnderunAI.Api.Contracts.Accounting;

public sealed record SupplierInvoiceItemRequest(
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    Guid? PurchaseOrderItemId,
    /// <summary>ALIŞ faturasında zorunlu — kalemin stok kartı.</summary>
    Guid? InventoryItemId = null,
    /// <summary>Kalemin deposu; boşsa faturanın deposu kullanılır.</summary>
    Guid? WarehouseId = null,
    /// <summary>GİDER faturasında zorunlu — kalemin gider hesabı.</summary>
    Guid? ExpenseAccountId = null,
    /// <summary>Kalemin masraf merkezi; boşsa faturanınki kullanılır.</summary>
    string? CostCenterCode = null);

public sealed record CreateSupplierInvoiceRequest(
    Guid CompanyId,
    Guid SupplierCurrentAccountId,
    /// <summary>
    /// Merkez giderinde boş bırakılır. Şantiye gideri ve alış
    /// faturasında dolu olmalıdır.
    /// </summary>
    Guid? ProjectId,
    Guid? PurchaseOrderId,
    Guid? GoodsReceiptId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    IReadOnlyCollection<SupplierInvoiceItemRequest> Items,
    /// <summary>0 = Alış (stok), 1 = Gider. Varsayılan alış.</summary>
    int InvoiceType = 0,
    /// <summary>ALIŞ faturasının varsayılan deposu.</summary>
    Guid? WarehouseId = null,
    /// <summary>Faturanın varsayılan masraf merkezi.</summary>
    string? CostCenterCode = null);

public sealed record UpdateSupplierInvoiceRequest(
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    IReadOnlyCollection<SupplierInvoiceItemRequest> Items,
    int InvoiceType = 0,
    Guid? ProjectId = null,
    Guid? WarehouseId = null,
    string? CostCenterCode = null);

public sealed record RejectSupplierInvoiceRequest(string Reason);

public sealed record SupplierInvoiceItemResponse(
    Guid Id,
    int LineNumber,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    decimal LineSubtotal,
    decimal VatAmount,
    decimal LineTotal,
    Guid? PurchaseOrderItemId,
    Guid? InventoryItemId,
    string? InventoryItemCode,
    string? InventoryItemName,
    Guid? WarehouseId,
    string? WarehouseName,
    Guid? ExpenseAccountId,
    string? ExpenseAccountCode,
    string? ExpenseAccountName,
    string? CostCenterCode);

public sealed record SupplierInvoiceListItemResponse(
    Guid Id,
    string InternalNumber,
    string InvoiceNumber,
    DateTime InvoiceDate,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    int InvoiceType,
    string InvoiceTypeName,
    string? CostCenterCode,
    string CurrencyCode,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    int Status,
    int MatchStatus,
    bool RequiresGmApproval,
    string? PurchaseOrderNumber,
    string? AccountingVoucherNumber);

public sealed record SupplierInvoiceDetailResponse(
    Guid Id,
    Guid CompanyId,
    string InternalNumber,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    int InvoiceType,
    string InvoiceTypeName,
    string? CostCenterCode,
    Guid? WarehouseId,
    string? WarehouseName,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    Guid? GoodsReceiptId,
    string? GoodsReceiptNumber,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string? Description,
    int Status,
    int MatchStatus,
    decimal MatchDifferenceAmount,
    string? MatchNote,
    bool RequiresGmApproval,
    DateTime? SubmittedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber,
    IReadOnlyCollection<SupplierInvoiceItemResponse> Items,
    /// <summary>Bu faturaya bağlanmış çek payları (dağılımdan gelir).</summary>
    IReadOnlyCollection<InvoiceChequePaymentResponse> ChequePayments,
    /// <summary>Çeklerle karşılanan toplam.</summary>
    decimal ChequeAllocatedAmount,
    /// <summary>Fatura tutarından çek payları düşüldükten sonra kalan.</summary>
    decimal ChequeRemainingAmount);

public sealed record SupplierInvoiceActionResponse(
    Guid Id,
    string InternalNumber,
    int Status,
    string Message);

public sealed record CompanyFinanceSettingsResponse(
    Guid CompanyId,
    decimal GmApprovalThresholdTry,
    decimal ThreeWayTolerancePercent,
    decimal DefaultVatRate,
    Guid? VatInAccountId,
    Guid? VatOutAccountId,
    Guid? SalesAccountId,
    Guid? ExpenseAccountId,
    /// <summary>Stok hesabı (153/150); alış faturası buraya yazılır.</summary>
    Guid? InventoryAccountId,
    Guid? PayablesAccountId,
    Guid? ReceivablesAccountId,
    Guid? FactoringExpenseAccountId,
    Guid? DeductionAccountId);

public sealed record UpdateCompanyFinanceSettingsRequest(
    decimal GmApprovalThresholdTry,
    decimal ThreeWayTolerancePercent,
    decimal DefaultVatRate,
    Guid? VatInAccountId,
    Guid? VatOutAccountId,
    Guid? SalesAccountId,
    Guid? ExpenseAccountId,
    /// <summary>Stok hesabı (153/150); alış faturası buraya yazılır.</summary>
    Guid? InventoryAccountId,
    Guid? PayablesAccountId,
    Guid? ReceivablesAccountId,
    Guid? FactoringExpenseAccountId,
    Guid? DeductionAccountId);
