namespace EnderunAI.Api.Contracts.Isg;

public sealed record IsgOsgbExpertRequest(
    int ExpertType,
    string FullName,
    string? CertificateNumber,
    string? ExpertClass,
    string? Phone,
    string? Email,
    DateOnly StartDate,
    DateOnly? EndDate);

public sealed record CreateIsgOsgbContractRequest(
    Guid CompanyId,
    Guid CurrentAccountId,
    string ContractNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    int BillingType,
    decimal MonthlyFee,
    decimal PerPersonFee,
    string CurrencyCode,
    string? Notes,
    IReadOnlyCollection<IsgOsgbExpertRequest> Experts);

public sealed record UpdateIsgOsgbContractRequest(
    Guid CurrentAccountId,
    string ContractNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    int BillingType,
    decimal MonthlyFee,
    decimal PerPersonFee,
    string CurrencyCode,
    string? Notes,
    IReadOnlyCollection<IsgOsgbExpertRequest> Experts);

public sealed record IsgOsgbExpertResponse(
    Guid Id,
    int ExpertType,
    string ExpertTypeName,
    string FullName,
    string? CertificateNumber,
    string? ExpertClass,
    string? Phone,
    string? Email,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrentlyAssigned);

public sealed record IsgOsgbContractListItem(
    Guid Id,
    string ContractNumber,
    Guid CurrentAccountId,
    string OsgbTitle,
    DateOnly StartDate,
    DateOnly? EndDate,
    int BillingType,
    string BillingTypeName,
    decimal MonthlyFee,
    decimal PerPersonFee,
    string CurrencyCode,
    /// <summary>Aktif / Süresi doluyor / Süresi doldu.</summary>
    string StatusName,
    int? DaysUntilExpiry,
    int ExpertCount);

public sealed record IsgOsgbContractDetail(
    Guid Id,
    Guid CompanyId,
    string ContractNumber,
    Guid CurrentAccountId,
    string OsgbTitle,
    string? OsgbTaxNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    int BillingType,
    string BillingTypeName,
    decimal MonthlyFee,
    decimal PerPersonFee,
    string CurrencyCode,
    string? Notes,
    string StatusName,
    int? DaysUntilExpiry,
    IReadOnlyCollection<IsgOsgbExpertResponse> Experts,
    IReadOnlyCollection<IsgOsgbInvoiceResponse> Invoices);

/// <summary>
/// OSGB'nin kestiği tedarikçi faturaları. Ayrı bir tabloda tutulmuyor —
/// OSGB bir cari olduğuna göre faturaları zaten tedarikçi faturası;
/// burada yalnızca o carinin faturaları listeleniyor.
/// </summary>
public sealed record IsgOsgbInvoiceResponse(
    Guid Id,
    string InternalNumber,
    string InvoiceNumber,
    DateTime InvoiceDate,
    decimal GrandTotal,
    string CurrencyCode,
    int Status,
    string StatusName);

/// <summary>
/// Hakedişe önerilen İSG kesintisi. Alan adları
/// <c>ProgressPaymentDeductionRequest</c> ile eşleşir ki hakediş ekranı
/// doğrudan kesinti satırına yerleştirebilsin.
/// </summary>
public sealed record IsgDeductionSuggestionResponse(
    bool HasSuggestion,
    /// <summary>HakedisDeductionType.OhsContribution = 8.</summary>
    int DeductionType,
    string Description,
    decimal ManualAmount,
    int? PersonCount,
    Guid? OsgbContractId,
    string? ContractNumber,
    /// <summary>Öneri üretilemediyse sebebi; üretildiyse null.</summary>
    string? Reason);
