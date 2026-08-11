namespace EnderunAI.Api.Models.FinancialInstruments;

/// <summary>Kartın sahibi — çift sayımın ve maskenin ayrım noktası.</summary>
public enum CreditCardOwnership
{
    /// <summary>
    /// Şirket kartı. Ekstre ödemesi şirket hesabından çıkar; nakit
    /// akışta ÇIKIŞ üretir.
    /// </summary>
    Company = 0,

    /// <summary>
    /// ŞAHIS KARTI ile yapılan şirket harcaması. Ekstreyi şahıs
    /// öder; şirketin nakdi çıkmaz. Harcama şahsın carisine BORÇ
    /// olarak yazılır (şirket şahsa borçlanır) ve nakit akışta çıkış
    /// ÜRETMEZ — üretseydi şirket ödemediği bir parayı ödemiş
    /// görünürdü.
    /// </summary>
    Personal = 1
}

/// <summary>
/// Kredi kartı.
///
/// İKİ TARİH BURADA EN GÖRÜNÜR: harcama günü gider doğar (tahakkuk),
/// ekstrenin son ödeme günü para çıkar (nakit). Aynı harcamayı iki
/// kez saymamanın yolu bu ayrım: gider merkezi harcama tarihinden,
/// nakit akış ekstre tarihinden okur.
/// </summary>
public sealed class CreditCard : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? BankName { get; set; }

    /// <summary>Kartın son dört hanesi; tam numara TUTULMAZ.</summary>
    public string? LastFourDigits { get; set; }

    public CreditCardOwnership Ownership { get; set; } = CreditCardOwnership.Company;

    /// <summary>
    /// Şahıs kartıysa hangi kişinin carisine yazılacağı. Şahıs
    /// kartında ZORUNLU: sahibi belli olmayan bir harcama hiçbir
    /// bakiyeye düşmez.
    /// </summary>
    public Guid? PartnerAccountId { get; set; }
    public Expenses.PartnerAccount? PartnerAccount { get; set; }

    /// <summary>Ekstrenin kesildiği gün (ayın kaçı).</summary>
    public int StatementDay { get; set; } = 1;

    /// <summary>
    /// Son ödeme günü (ayın kaçı). Kesimden sonraki aya taşıyorsa
    /// (kesim 25, ödeme 5) ekstre dönemi hesabı bunu kendisi çözer.
    /// </summary>
    public int DueDay { get; set; } = 10;

    /// <summary>Ödemenin çıkacağı hesap.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    public bool IsActive { get; set; } = true;
}
