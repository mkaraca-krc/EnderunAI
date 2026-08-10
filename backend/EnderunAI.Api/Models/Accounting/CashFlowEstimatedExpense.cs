namespace EnderunAI.Api.Models;

/// <summary>
/// Nakit akış takvimine elle girilen TEKRARLAYAN TAHMİNİ GİDER.
///
/// NEDEN VAR: gider merkezi modülü henüz yok. Kira, elektrik, sigorta
/// gibi düzenli genel giderler hiçbir kaydın içinden çıkmıyor ve
/// takvimde hiç görünmüyor. Görünmedikleri sürece tablo her zaman
/// olduğundan iyimser çıkar — "ne zaman açığa düşüyoruz" sorusunun
/// cevabı geç bir tarih verir.
///
/// GEÇİCİ OLDUĞU AÇIK: her satır TAHMİNİ işaretiyle taşınıyor ve
/// gider merkezi geldiğinde toplu kapatılabilsin diye ayrı tabloda
/// duruyor. Gerçek gider kaydına karışmıyor — muhasebe fişi
/// üretmiyor, yalnızca projeksiyonda görünüyor.
/// </summary>
public sealed class CashFlowEstimatedExpense : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Belirli bir projeye aitse. Boşsa şirket geneli (merkez gideri).
    /// </summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Aylık tutar (TRY).</summary>
    public decimal Amount { get; set; }

    /// <summary>İlk çıkışın ayı.</summary>
    public int StartYear { get; set; }
    public int StartMonth { get; set; }

    /// <summary>
    /// Kaç ay tekrar edeceği. Süresiz bir gider tanımlanamıyor
    /// bilinçli olarak: ufuk boyunca sonsuza kadar akan bir tahmin,
    /// kimsenin gözden geçirmediği bir varsayıma dönüşürdü.
    /// </summary>
    public int RecurrenceCount { get; set; } = 1;

    /// <summary>
    /// Ayın kaçında ödendiği. Gün ayda yoksa ayın son gününe
    /// çekilir (31'i seçilip şubatı olan bir gider için).
    /// </summary>
    public int PaymentDay { get; set; } = 1;
}
