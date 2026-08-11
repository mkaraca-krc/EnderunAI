namespace EnderunAI.Api.Models.Expenses;

/// <summary>
/// AYLIK TEKRAR EDEN GİDER ŞABLONU — kira, internet, elektrik.
///
/// TAHMİNİ → GERÇEKLEŞEN: şablon her ay için bir TAHMİNİ tutar
/// taşır. Ay gelince gerçek tutar girilip onaylanır ve o ayın kaydı
/// GERÇEKLEŞEN olarak açılır. Değişken tutarlı giderin (fatura) tek
/// yolu bu: sabit tutar yazılsaydı elektrik faturası her ay yanlış
/// olurdu.
///
/// R5 ÇİFT SAYIM: bir dönem için tahmini ve gerçekleşen AYNI ANDA
/// sayılmaz. Gerçekleşen kayıt açıldığı anda o dönemin tahmini
/// kalemi düşer — kayıt kendi dönemini (PeriodYear/PeriodMonth)
/// taşıdığı için hangi ayın kapandığı ayrı bir bayrağa değil
/// verinin kendisine bakılarak bilinir.
/// </summary>
public sealed class RecurringExpenseTemplate : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ExpenseCenterType CenterType { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public Guid ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Aylık TAHMİNİ tutar. Gerçekleşen girilene kadar raporda ve
    /// nakit akışta bu görünür, "tahmini" işaretiyle.
    /// </summary>
    public decimal EstimatedAmount { get; set; }

    public ExpensePaymentMethod PaymentMethod { get; set; }

    public Guid? SupplierCurrentAccountId { get; set; }
    public CurrentAccount? SupplierCurrentAccount { get; set; }

    /// <summary>İlk dönem.</summary>
    public int StartYear { get; set; }
    public int StartMonth { get; set; }

    /// <summary>
    /// Son dönem. BOŞ BIRAKILABİLİR: gider merkezinde şablon, nakit
    /// akıştaki tahmini gider gibi ufka sonsuza kadar akmıyor —
    /// yalnızca dönemi geldikçe gerçekleşen bekliyor. Yine de
    /// bitiş verilebilir (12 aylık kira sözleşmesi gibi).
    /// </summary>
    public int? EndYear { get; set; }
    public int? EndMonth { get; set; }

    /// <summary>Ayın kaçında ödendiği; nakit akış tarihi bundan çıkar.</summary>
    public int PaymentDay { get; set; } = 1;

    /// <summary>
    /// Şablon durduruldu. Silmek yerine durdurma: geçmiş dönemlerde
    /// bu şablondan doğmuş gerçekleşen kayıtlar kaynaklarını
    /// kaybetmesin.
    /// </summary>
    public bool IsStopped { get; set; }
}
