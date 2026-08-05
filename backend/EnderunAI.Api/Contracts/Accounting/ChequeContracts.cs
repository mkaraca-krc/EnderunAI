namespace EnderunAI.Api.Contracts.Accounting;

/// <summary>
/// Çekin bir payı. Fatura verilirse proje ve masraf merkezi faturadan
/// türetilir; gönderilen değerler yok sayılır (tek kaynak fatura olsun).
/// </summary>
public sealed record ChequeAllocationRequest(
    decimal Amount,
    Guid? ProjectId = null,
    string? CostCenterCode = null,
    Guid? SupplierInvoiceId = null,
    Guid? SalesInvoiceId = null,
    string? Description = null);

public sealed record ChequeAllocationsRequest(
    IReadOnlyCollection<ChequeAllocationRequest> Allocations);

public sealed record ChequeAllocationResponse(
    Guid Id,
    decimal Amount,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CostCenterCode,
    Guid? SupplierInvoiceId,
    string? SupplierInvoiceNumber,
    Guid? SalesInvoiceId,
    string? SalesInvoiceNumber,
    string? Description);

/// <summary>Bir faturayı ödeyen çekin özeti (fatura ekranında görünür).</summary>
public sealed record InvoiceChequePaymentResponse(
    Guid ChequeId,
    string InternalNumber,
    string ChequeNumber,
    DateTime DueDate,
    int Status,
    string StatusName,
    decimal AllocatedAmount);

public sealed record CreateChequeRequest(
    Guid CompanyId,
    int Direction,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description,
    /// <summary>Merkez ofis ya da şantiye kodu; boşsa proje kodu kullanılır.</summary>
    string? CostCenterCode = null,
    /// <summary>
    /// Proje/masraf merkezi dağılımı. Boş bırakılırsa çek tek parça
    /// işlenir (bugünkü davranış).
    /// </summary>
    IReadOnlyCollection<ChequeAllocationRequest>? Allocations = null);

public sealed record UpdateChequeRequest(
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description,
    string? CostCenterCode = null);

/// <summary>
/// Durum geçişi. CashAccountId yalnızca para hareketi doğuran
/// geçişlerde zorunlu (bankaya verme, tahsil, ödeme, rücu).
/// </summary>
public sealed record ChequeStatusChangeRequest(
    int ToStatus,
    DateTime MovementDate,
    Guid? CashAccountId,
    string? Description);

/// <summary>
/// Çek erteleme/değişim. Tutar YENİDEN ALINMAZ: yeni çek eski çekle
/// aynı tutarda olmak zorunda. Vade farkı ayrı bir belgeyle (fatura ya
/// da dekont) kaydedilir; burada otomatik bir gider hesabı uydurulmaz.
/// </summary>
public sealed record ReplaceChequeRequest(
    string ChequeNumber,
    DateTime DueDate,
    DateTime MovementDate,
    string? BankName = null,
    string? BankBranch = null,
    string? Drawer = null,
    DateTime? IssueDate = null,
    string? Description = null);

public sealed record ChequeMovementResponse(
    Guid Id,
    DateTime MovementDate,
    int? FromStatus,
    string? FromStatusName,
    int ToStatus,
    string ToStatusName,
    string Description,
    Guid? CashAccountId,
    string? CashAccountName,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber);

public sealed record ChequeListItemResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? CostCenterCode,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    int DaysToDue,
    bool IsOverdue);

public sealed record ChequeDetailResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CostCenterCode,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    string? ProgressPaymentNumber,
    Guid? SupplierInvoiceId,
    string? SupplierInvoiceNumber,
    Guid? CashAccountId,
    string? CashAccountName,
    string? Description,
    IReadOnlyCollection<int> AllowedNextStatuses,
    IReadOnlyCollection<ChequeMovementResponse> Movements,
    IReadOnlyCollection<ChequeAllocationResponse> Allocations,
    /// <summary>Bu çek ertelendiyse yerine geçen çek.</summary>
    Guid? ReplacedByChequeId,
    string? ReplacedByChequeNumber,
    /// <summary>Bu çek bir ertelemenin sonucuysa yerine geçtiği çek.</summary>
    Guid? ReplacesChequeId,
    string? ReplacesChequeNumber,
    /// <summary>
    /// Zincirde kaç kez ertelendiği. Risk sinyali: sürekli ertelenen
    /// çek tahsilat sorununun habercisidir.
    /// </summary>
    int RenewalCount);

public sealed record ChequeSummaryResponse(
    decimal ReceivedPortfolioAmount,
    decimal ReceivedAtBankAmount,
    decimal ReceivedAtFactoringAmount,
    decimal ReceivedCollectedAmount,
    decimal ReceivedBouncedAmount,
    decimal IssuedOpenAmount,
    decimal IssuedPaidAmount,
    int ReceivedOpenCount,
    int IssuedOpenCount);
