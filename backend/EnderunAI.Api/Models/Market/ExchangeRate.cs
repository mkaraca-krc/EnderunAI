namespace EnderunAI.Api.Models.Market;

/// <summary>
/// TCMB günlük döviz kuru arşivi.
///
/// Kayıt şirketten bağımsızdır: merkez bankası kuru herkes için aynıdır,
/// şirket başına kopyalamak aynı sayıyı çoğaltmaktan başka işe yaramaz.
///
/// TCMB hafta sonu ve resmi tatillerde bülten yayımlamaz; o günlerin
/// satırı hiç oluşmaz. Bir tarihe kur soranlara
/// <see cref="EnderunAI.Api.Services.Market.IExchangeRateService"/> en yakın
/// ÖNCEKİ yayınlanmış kuru döner ve hangi tarihi kullandığını birlikte
/// söyler — ara değer uydurulmaz.
/// </summary>
public sealed class ExchangeRate : BaseEntity
{
    /// <summary>Bültenin tarihi (gün başlangıcı, UTC).</summary>
    public DateTime RateDate { get; set; }

    /// <summary>ISO kodu — USD, EUR.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>
    /// Kaç birim için kotasyon verildiği. TCMB bazı para birimlerini
    /// 100 birim üzerinden yayımlar (JPY gibi); tüm tutarlar bu değere
    /// bölünerek 1 birime indirgenmiş olarak saklanır.
    /// </summary>
    public int Unit { get; set; } = 1;

    /// <summary>Döviz alış — muhasebe fişlerinde esas alınan kur.</summary>
    public decimal ForexBuying { get; set; }

    public decimal ForexSelling { get; set; }

    /// <summary>Efektif alış. TCMB bazı kurlarda boş bırakır.</summary>
    public decimal? BanknoteBuying { get; set; }

    public decimal? BanknoteSelling { get; set; }

    /// <summary>TCMB bülten numarası — mutabakatta kaynağı gösterir.</summary>
    public string? BulletinNumber { get; set; }

    /// <summary>Kaynak etiketi; bugün yalnızca "TCMB".</summary>
    public string Source { get; set; } = "TCMB";

    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
