namespace EnderunAI.Api.Models.Market;

/// <summary>
/// Eşiğin hangi yönde aşıldığı.
/// </summary>
public enum CommodityAlertDirection
{
    /// <summary>Fiyat alım eşiğinin ALTINA indi — alım fırsatı.</summary>
    BuyOpportunity = 0,

    /// <summary>Fiyat uyarı eşiğinin ÜSTÜNE çıktı — maliyet riski.</summary>
    CostRisk = 1
}

/// <summary>
/// Şirketin emtia alım/uyarı eşiği.
///
/// Eşik ŞİRKET BAZLIDIR: aynı bakır fiyatı, stok politikası ve nakit
/// durumu farklı iki şirket için farklı anlama gelir. Tek bir global
/// eşik, birine erken diğerine geç sinyal verirdi.
///
/// Eşikler USD/ton üzerinden tutulur. TL eşiği tutmak, kur hareketini
/// emtia hareketiyle karıştırıp "bakır mı pahalandı, lira mı değer
/// kaybetti" sorusunu eşiğin içine gömerdi; TL karşılığı ekranda
/// ayrıca gösterilir.
/// </summary>
public sealed class CommodityAlertThreshold : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Commodity Commodity { get; set; } = Commodity.Copper;

    /// <summary>
    /// Alım fırsatı eşiği (USD/ton). Fiyat bunun ALTINA inerse sinyal
    /// üretilir. Null ise alım sinyali kapalıdır.
    /// </summary>
    public decimal? BuyBelowUsdPerTon { get; set; }

    /// <summary>
    /// Maliyet riski eşiği (USD/ton). Fiyat bunun ÜSTÜNE çıkarsa
    /// uyarı üretilir. Null ise risk uyarısı kapalıdır.
    /// </summary>
    public decimal? AlertAboveUsdPerTon { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string? Notes { get; set; }

    public ICollection<CommodityAlertTrigger> Triggers { get; set; } =
        new List<CommodityAlertTrigger>();
}

/// <summary>
/// Eşiğin tetiklendiği an.
///
/// Tetiklenme bir GEÇİŞtir, bir durum değil: fiyatın eşiğin altında
/// kaldığı her gün yeni bir kayıt üretilmez, yalnızca eşiği geçtiği
/// gün üretilir. Aksi hâlde bakır iki hafta ucuz kalsa on dört kez
/// "alım fırsatı" denir ve uyarı anlamını yitirir.
///
/// Kayıt arşivden yeniden üretilebilir olduğu için (fiyat serisindeki
/// geçişler) idempotenttir: aynı eşik + tarih + yön ikinci kez
/// yazılmaz.
/// </summary>
public sealed class CommodityAlertTrigger : BaseEntity
{
    public Guid CommodityAlertThresholdId { get; set; }
    public CommodityAlertThreshold CommodityAlertThreshold { get; set; } = null!;

    public CommodityAlertDirection Direction { get; set; }

    /// <summary>Geçişin gerçekleştiği fiyat günü.</summary>
    public DateTime PriceDate { get; set; }

    public decimal PriceUsdPerTon { get; set; }

    /// <summary>Fiyat gününün TL karşılığı; kur yoksa null.</summary>
    public decimal? PriceTryPerTon { get; set; }

    /// <summary>Tetiklenme anında geçerli olan eşik değeri.</summary>
    public decimal ThresholdUsdPerTon { get; set; }

    /// <summary>
    /// Görüldü olarak işaretlendiyse dolu. İşaretlenmemiş tetiklenmeler
    /// brifingde ve dashboard kartında görünür.
    /// </summary>
    public DateTime? AcknowledgedAtUtc { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }

    public bool IsAcknowledged => AcknowledgedAtUtc is not null;
}
