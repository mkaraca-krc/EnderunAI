namespace EnderunAI.Api.Models;

/// <summary>
/// Dönem sonu kur değerlemesi turu.
///
/// Dövizli cari bakiyeleri, hareketlerin kendi günündeki kurla TL'ye
/// çevrilmiş DEFTER değeriyle durur. Dönem sonunda bu değerin o günkü
/// kurla karşılığına çekilmesi ve aradaki farkın 646/656'ya yazılması
/// gerekir (VUK değerleme). Bu kayıt hangi tarihte, hangi rakamla ve
/// hangi fişle değerleme yapıldığını tutar.
///
/// KÜMÜLATİF MANTIK: değerleme satırları TL olarak kesilir, dövizin
/// kendi bakiyesini değiştirmez. Bu yüzden bir sonraki değerleme aynı
/// farkı yeniden hesaplar; çift kayıt olmasın diye o turda YALNIZCA
/// daha önce yazılmış düzeltmelerin ÜSTÜNDEKİ fark defterlenir.
/// </summary>
public sealed class CurrencyValuationRun : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Değerleme tarihi (gün başlangıcı, UTC).</summary>
    public DateTime ValuationDate { get; set; }

    /// <summary>Bu turda kesilen (Posted) muhasebe fişi.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    /// <summary>Bu turda deftere yazılan net fark (TL).</summary>
    public decimal PostedDifference { get; set; }

    /// <summary>
    /// Ters kayıtla iptal edildiyse dolu. İptal edilen tur kümülatif
    /// toplama girmez.
    /// </summary>
    public Guid? ReversalVoucherId { get; set; }
    public DateTime? ReversedAtUtc { get; set; }

    public bool IsReversed => ReversedAtUtc is not null;

    public Guid? CreatedByUserId { get; set; }

    public ICollection<CurrencyValuationRunLine> Lines { get; set; } =
        new List<CurrencyValuationRunLine>();
}

/// <summary>
/// Değerleme turunun tek bir cari + para birimi satırı.
/// </summary>
public sealed class CurrencyValuationRunLine : BaseEntity
{
    public Guid CurrencyValuationRunId { get; set; }
    public CurrencyValuationRun CurrencyValuationRun { get; set; } = null!;

    public Guid CurrentAccountId { get; set; }
    public CurrentAccount CurrentAccount { get; set; } = null!;

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Döviz bakiyesi (borç − alacak).</summary>
    public decimal Balance { get; set; }

    /// <summary>Defterdeki TL karşılığı (değerleme düzeltmeleri hariç).</summary>
    public decimal BookValueLocal { get; set; }

    /// <summary>Değerleme kuru.</summary>
    public decimal ValuationRate { get; set; }

    /// <summary>Değerleme kuruyla TL karşılığı.</summary>
    public decimal ValuedLocal { get; set; }

    /// <summary>
    /// Toplam fark (ValuedLocal − BookValueLocal). Bunun tamamı bu
    /// turda defterlenmez; daha önce yazılmış düzeltmeler düşülür.
    /// </summary>
    public decimal TotalDifference { get; set; }

    /// <summary>Bu turda deftere yazılan kısım.</summary>
    public decimal PostedDifference { get; set; }
}
