namespace EnderunAI.Api.Contracts.Accounting;

/// <summary>
/// Bir kalemin tarihinin ne kadar güvenilir olduğu.
///
/// Likidite kararı buna bakılarak verilir: kesin kalemlerden oluşan
/// bir açık finansman gerektirir, tahminlerden oluşan bir açık önce
/// doğrulanır. İkisini aynı renkte göstermek, tahmini bir gecikmeyi
/// kesin bir borç gibi okutur.
/// </summary>
public enum CashFlowCertainty
{
    /// <summary>Çek vadesi, vergi vadesi, sözleşmeli fatura vadesi.</summary>
    Confirmed = 0,

    /// <summary>Vadeden hesaplanan hakediş, bordro, tekrarlayan gider.</summary>
    Estimated = 1,

    /// <summary>
    /// NAKİT DEĞİL — barter alacağı gibi, nakde dönmeyecek kalem.
    ///
    /// Yürüyen bakiyeye GİRMEZ; ayrı gösterilir. Nakit sayılsaydı
    /// tablo, eline hiç geçmeyecek bir parayı likidite gibi okurdu.
    /// Hiç gösterilmeseydi de "hakedişin bu kısmı nereye gitti"
    /// sorusu cevapsız kalırdı.
    /// </summary>
    NonCash = 2
}

public sealed record CashFlowProjectionItem(
    DateTime Date,
    string Kind,
    string KindName,
    string Title,
    string? Reference,
    Guid? ProjectId,
    string? ProjectCode,
    decimal Amount,
    bool IsInflow,
    int Certainty,
    string CertaintyName);

/// <summary>Takvimin tek günü: o günün hareketleri ve gün sonu bakiyesi.</summary>
public sealed record CashFlowProjectionDay(
    DateTime Date,
    decimal Inflow,
    decimal Outflow,
    decimal Net,
    decimal RunningBalance,
    IReadOnlyCollection<CashFlowProjectionItem> Items);

public sealed record CashFlowProjectionMonth(
    int Year,
    int Month,
    string Label,
    decimal Inflow,
    decimal Outflow,
    decimal Net,
    decimal ClosingBalance,
    decimal LowestBalance,
    DateTime? LowestBalanceDate);

/// <summary>
/// Finansman açığı: bakiyenin negatife düştüğü ilk gün ve EN DERİN
/// nokta.
///
/// İkisi ayrı sorudur. İlk gün "ne zaman para bitiyor", en derin nokta
/// "ne kadar bulmam gerekiyor" — kredi ya da erken tahsilat pazarlığı
/// ikincisine göre yapılır.
/// </summary>
public sealed record CashFlowShortfall(
    DateTime FirstNegativeDate,
    decimal FirstNegativeBalance,
    DateTime PeakDate,
    decimal PeakBalance,
    decimal RequiredFinancing);

/// <summary>Hedef tarihe kadar kümülatif tablo.</summary>
public sealed record CashFlowTargetSummary(
    DateTime TargetDate,
    decimal Inflow,
    decimal Outflow,
    decimal ClosingBalance,
    decimal RequiredFinancing);

public sealed record CashFlowProjectionResponse(
    Guid CompanyId,
    DateTime FromDate,
    DateTime ToDate,
    int Months,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyCollection<CashFlowProjectionMonth> MonthlySummary,
    IReadOnlyCollection<CashFlowProjectionDay> Days,
    CashFlowShortfall? Shortfall,
    CashFlowTargetSummary? Target,
    IReadOnlyCollection<string> Notes);
