using EnderunAI.Api.Services.Tax;

namespace EnderunAI.Api.Models;

/// <summary>
/// Ödendi işaretlenen vergi dönemi.
///
/// Nakit akış tahmini vergi çıkışlarını gösteriyor; ödenen dönem
/// işaretlenmezse listede durmaya devam eder ve nakit resmi olduğundan
/// kötü görünür. Kayıt yalnızca "bu dönem ödendi" bilgisini tutar —
/// muhasebe fişi ayrı bir iştir ve kasa/banka modülünden yürür.
/// </summary>
public sealed class TaxPayment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public TaxObligationKind Kind { get; set; }

    /// <summary>Yükümlülüğün ait olduğu dönem yılı.</summary>
    public int PeriodYear { get; set; }

    /// <summary>
    /// Aylık yükümlülüklerde ay (1-12), geçici vergide çeyrek (1-4).
    /// </summary>
    public int PeriodNumber { get; set; }

    /// <summary>Ödenen tutar; tahminden farklı olabilir.</summary>
    public decimal Amount { get; set; }

    public DateTime PaidAtUtc { get; set; }

    public string? Note { get; set; }
}
