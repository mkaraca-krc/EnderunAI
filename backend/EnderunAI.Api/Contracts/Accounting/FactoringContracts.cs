namespace EnderunAI.Api.Contracts.Accounting;

/// <summary>
/// Çek kırdırma isteği. Komisyon oran ya da tutar olarak verilebilir;
/// oran verilirse tutar nominalden hesaplanır. BSMV komisyon üzerinden
/// hesaplanır (varsayılan %5).
/// </summary>
public sealed record CreateFactoringTransactionRequest(
    Guid ChequeId,
    Guid CashAccountId,
    Guid? FactoringCurrentAccountId,
    Guid? ProjectId,
    DateTime TransactionDate,
    decimal? CommissionRate,
    decimal? CommissionAmount,
    decimal? BsmvRate,
    decimal ExpenseAmount,
    string? Description,
    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU. Kırdırma da çekin
    /// durumunu değiştiriyor; koruma tek uçta eksikse yok demektir.
    /// </summary>
    DateTime? RowVersion = null);

/// <summary>Kaydetmeden önce kesinti matematiğini önizleme isteği.</summary>
public sealed record FactoringPreviewRequest(
    decimal ChequeAmount,
    decimal? CommissionRate,
    decimal? CommissionAmount,
    decimal? BsmvRate,
    decimal ExpenseAmount);

public sealed record FactoringCalculationResponse(
    decimal ChequeAmount,
    decimal CommissionRate,
    decimal CommissionAmount,
    decimal BsmvRate,
    decimal BsmvAmount,
    decimal ExpenseAmount,
    decimal TotalDeductionAmount,
    decimal NetAmount);

public sealed record FactoringTransactionResponse(
    Guid Id,
    Guid CompanyId,
    string InternalNumber,
    Guid ChequeId,
    string ChequeNumber,
    string ChequeBankName,
    DateTime ChequeDueDate,
    Guid? FactoringCurrentAccountId,
    string? FactoringCurrentAccountTitle,
    Guid CashAccountId,
    string CashAccountName,
    Guid? ProjectId,
    string? ProjectCode,
    DateTime TransactionDate,
    string CurrencyCode,
    decimal ChequeAmount,
    decimal CommissionRate,
    decimal CommissionAmount,
    decimal BsmvRate,
    decimal BsmvAmount,
    decimal ExpenseAmount,
    decimal TotalDeductionAmount,
    decimal NetAmount,
    string? Description,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber);
