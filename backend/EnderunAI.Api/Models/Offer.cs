namespace EnderunAI.Api.Models;

/// <summary>
/// Teklifin fırsat hunisindeki yeri.
///
/// Ordinal değerler korundu: 2 numaralı değer eskiden "Onaylandı"
/// adını taşıyordu ama onu yazan hiçbir uç yoktu ve canlıdaki her
/// teklif Taslak'tı, bu yüzden veri taşımadan Beklemede olarak
/// adlandırıldı.
/// </summary>
public enum OfferStatus
{
    /// <summary>Hazırlanıyor — henüz karşı tarafa verilmedi.</summary>
    Draft = 0,

    /// <summary>Verildi — teklif karşı tarafa sunuldu.</summary>
    Submitted = 1,

    /// <summary>Beklemede — sunuldu, cevap bekleniyor.</summary>
    Pending = 2,

    /// <summary>
    /// KULLANILMIYOR. Eski "Reddedildi" değeri; yeni akışta kayıp
    /// nedeni <see cref="OfferLostReason"/> ile Kaybedildi altında
    /// tutuluyor. Geçiş haritası bu değere ne girer ne çıkar.
    /// </summary>
    Rejected = 3,

    /// <summary>Kazanıldı — sözleşme ve proje bu noktada doğar.</summary>
    Won = 4,

    /// <summary>Kaybedildi — neden zorunlu, kayıt arşivde kalır.</summary>
    Lost = 5,

    /// <summary>İptal — iş rafa kalktı ya da teklif geri çekildi.</summary>
    Cancelled = 6
}

/// <summary>
/// Teklifi kime verdiğimiz. Alt yüklenici konumunda aynı işi hem
/// işverene hem ana yükleniciye verebiliyoruz; kazanma oranı bu ayrım
/// olmadan yanıltıcı olur çünkü ikisinin rekabet koşulları farklı.
/// </summary>
public enum OfferCounterpartyRole
{
    Unspecified = 0,
    Employer = 1,
    MainContractor = 2
}

/// <summary>
/// Teklif tipi. Sözleşme tipiyle aynı ayrım (birim fiyatlı / anahtar
/// teslim); kazanıldığında projenin sözleşme tipine bu değer önerilir.
/// </summary>
public enum OfferKind
{
    Unspecified = 0,

    /// <summary>Birim fiyatlı — keşif/poz üzerinden, yapılan iş kadar.</summary>
    UnitPrice = 1,

    /// <summary>Anahtar teslim götürü — bedel sabit.</summary>
    LumpSum = 2
}

/// <summary>
/// Teklifi neden kaybettik. Serbest metin olsaydı sayılamazdı;
/// "fiyatımız mı yüksek yoksa referansımız mı yetmiyor" sorusunun
/// cevabı ancak sayılabilir bir alanla verilebilir.
/// </summary>
public enum OfferLostReason
{
    None = 0,

    /// <summary>Fiyatımız yüksek kaldı.</summary>
    PriceTooHigh = 1,

    /// <summary>Referans/deneyim yetersiz bulundu.</summary>
    InsufficientReference = 2,

    /// <summary>Başka firmaya verildi.</summary>
    CompetitorWon = 3,

    /// <summary>İş iptal edildi / yapılmadı.</summary>
    WorkCancelled = 4,

    Other = 5
}

public sealed class Offer : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// KULLANILMIYOR. Teklif motoru ilk yazıldığında açılmış ham bir
    /// kolon: navigation'ı yok, doğrulanmıyor ve hiçbir ekran ismini
    /// çözmüyordu (canlıdaki iki teklifte de boş). Karşı taraf artık
    /// <see cref="CounterpartyCurrentAccountId"/> ile tutuluyor.
    /// Kolon, olası eski veriyi kaybetmemek için duruyor.
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Teklifi verdiğimiz cari — işveren ya da ana yüklenici.
    ///
    /// Opsiyonel: teklif hazırlanmaya başlarken karşı taraf henüz
    /// belli olmayabilir. Ancak "Verildi" durumuna geçmek için zorunlu
    /// hale gelir; kime verildiği bilinmeyen bir teklif takip
    /// listesinde sayılamaz.
    /// </summary>
    public Guid? CounterpartyCurrentAccountId { get; set; }
    public CurrentAccount? CounterpartyCurrentAccount { get; set; }

    public OfferCounterpartyRole CounterpartyRole { get; set; }
        = OfferCounterpartyRole.Unspecified;

    public OfferKind Kind { get; set; } = OfferKind.Unspecified;

    /// <summary>Kaybedildi durumunda zorunlu.</summary>
    public OfferLostReason LostReason { get; set; } = OfferLostReason.None;

    /// <summary>Kayıp nedeninin serbest açıklaması.</summary>
    public string? LostReasonNote { get; set; }

    /// <summary>Durumun en son ne zaman değiştiği.</summary>
    public DateTime? StatusChangedAtUtc { get; set; }

    /// <summary>Durumu en son kimin değiştirdiği.</summary>
    public Guid? StatusChangedByUserId { get; set; }

    /// <summary>Durum değişikliğinin gerekçesi.</summary>
    public string? StatusNote { get; set; }

    public string OfferNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public DateTime OfferDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? ValidUntil { get; set; }

    public string Currency { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;

    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal CostTotal { get; set; }
    public decimal ProfitTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public ICollection<OfferItem> Items { get; set; }
        = new List<OfferItem>();
}
