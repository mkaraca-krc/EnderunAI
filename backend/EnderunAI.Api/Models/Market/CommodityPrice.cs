namespace EnderunAI.Api.Models.Market;

public enum Commodity
{
    Copper = 0
}

/// <summary>
/// Emtia fiyatının kaynağı. Etiket ekranda daima görünür: COMEX ile LME
/// aynı şey değildir ve aradaki fark (özellikle 2025 ABD gümrük
/// vergilerinden sonra) kalıcıdır. Türkiye'deki kablo alımları LME'ye
/// endeksli olduğundan COMEX rakamı yön olarak doğru, seviye olarak
/// sapmalıdır — bu gizlenmez.
/// </summary>
public enum CommodityPriceSourceKind
{
    /// <summary>COMEX bakır vadeli (HG=F). Ücretsiz, anahtar gerektirmez.</summary>
    Comex = 0,

    /// <summary>LME resmî fiyatı; yalnızca API anahtarı tanımlıysa.</summary>
    Lme = 1
}

/// <summary>
/// Günlük emtia fiyatı arşivi.
///
/// TL karşılığı, fiyatın kendi tarihindeki TCMB döviz alışıyla
/// hesaplanır ve <see cref="UsdRate"/> alanında saklanır. Kur o güne
/// bulunamazsa TL karşılığı yazılmaz (null kalır) — bugünkü kurla
/// geçmiş bir fiyatı çarpmak, ne fiyat ne kur değişimini doğru
/// gösteren üçüncü bir sayı üretir.
/// </summary>
public sealed class CommodityPrice : BaseEntity
{
    public DateTime PriceDate { get; set; }

    public Commodity Commodity { get; set; } = Commodity.Copper;

    public CommodityPriceSourceKind SourceKind { get; set; }

    /// <summary>Kaynaktaki sembol — HG=F, LME-XCU gibi. Mutabakat için.</summary>
    public string SourceSymbol { get; set; } = string.Empty;

    public decimal PriceUsdPerTon { get; set; }

    /// <summary>Fiyat gününün TCMB USD döviz alışı; kur yoksa null.</summary>
    public decimal? UsdRate { get; set; }

    public decimal? PriceTryPerTon { get; set; }

    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
