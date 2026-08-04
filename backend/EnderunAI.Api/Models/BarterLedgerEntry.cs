namespace EnderunAI.Api.Models;

public enum BarterEntryType
{
    /// <summary>
    /// Hakedişten kesilen barter — işverenden mal/hizmet alacağımız
    /// doğar, bakiye artar.
    /// </summary>
    Deduction = 0,

    /// <summary>
    /// İşverenden teslim alınan mal/hizmet (daire, dükkân vb.) —
    /// bakiye azalır.
    /// </summary>
    Receipt = 1
}

/// <summary>
/// Barter defteri. Barter, hakedişin nakit yerine mal/hizmet olarak
/// ödenecek kısmıdır: kesinti yapıldığında işverenden o tutarda
/// mal/hizmet alacağımız doğar.
///
/// Bakiye = kümülatif kesilen − teslim alınan. Resmi muhasebeye kendi
/// başına fiş yazmaz; hakediş fişindeki barter kesintisi satırı bu
/// alacağı zaten kayda geçirir.
/// </summary>
public sealed class BarterLedgerEntry : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Şantiye bazlı takip için; zorunlu değil.</summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>Kesinti kaydıysa kaynağı olan hakediş.</summary>
    public Guid? ProgressPaymentId { get; set; }
    public ProgressPayment? ProgressPayment { get; set; }

    public BarterEntryType EntryType { get; set; }

    public DateTime EntryDate { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
