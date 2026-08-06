namespace EnderunAI.Api.Models;

/// <summary>
/// Birim fiyatı yayımlayan kurum. Aynı poz numarası farklı kurumların
/// kitabında farklı fiyatla geçebildiği için fiyat kaydı kurumdan
/// bağımsız düşünülemez.
/// </summary>
public enum PositionPriceInstitution
{
    /// <summary>Çevre, Şehircilik ve İklim Değişikliği Bakanlığı.</summary>
    Csb = 0,

    /// <summary>TEDAŞ.</summary>
    Tedas = 1,

    /// <summary>Şirketin kendi belirlediği fiyat (özel pozlar ve iç kullanım).</summary>
    Company = 2,

    /// <summary>Diğer kurum/kaynak; adı <see cref="PositionUnitPrice.SourceNote"/> alanında.</summary>
    Other = 3
}

/// <summary>
/// Fiyatın hangi bileşeni olduğu.
///
/// Kitaplar tek bir rakam vermiyor: TEDAŞ malzeme, montaj, demontaj ve
/// demontajdan montajı ayrı kolonlarda yayımlıyor; ÇŞB elektrik
/// bölümünde "montajlı birim fiyat" ve "montaj bedeli" yan yana
/// duruyor. Bunları tek sayıya indirmek bilgi kaybıdır ve keşifte
/// malzeme/montaj ayrımını imkânsız kılar.
/// </summary>
public enum PositionPriceComponent
{
    /// <summary>Rayiç / birim fiyat / montajlı birim fiyat — işin tamamı.</summary>
    Total = 0,

    /// <summary>Yalnız malzeme bedeli.</summary>
    Material = 1,

    /// <summary>Yalnız montaj (işçilik) bedeli.</summary>
    Labor = 2,

    /// <summary>Sökme bedeli.</summary>
    Dismantle = 3,

    /// <summary>Sökülenin yeniden montaj bedeli.</summary>
    RemountFromDismantled = 4
}

/// <summary>
/// Bir pozun belirli bir yıla, kuruma ve bileşene ait yayımlanmış
/// birim fiyatı.
///
/// Fiyat poz kaydının üstüne YAZILMAZ, ayrı satır olarak eklenir:
/// ÇŞB 2024, ÇŞB 2025 ve TEDAŞ 2025 yan yana durur. Geçmiş fiyatın
/// korunması şart — eski bir hakediş ya da teklif hangi yılın kitabıyla
/// hesaplandıysa o rakamla açıklanabilmeli.
///
/// Bu tablo mevcut reçete/analiz motorunun yerini almaz; ikisi birlikte
/// çalışır ve keşifte hangisinin kullanıldığı kalem bazında seçilir.
/// </summary>
public sealed class PositionUnitPrice : BaseEntity
{
    public Guid EngineeringPositionId { get; set; }
    public EngineeringPosition EngineeringPosition { get; set; } = null!;

    /// <summary>Fiyat kitabının yılı (2024, 2025...).</summary>
    public int Year { get; set; }

    public PositionPriceInstitution Institution { get; set; }

    /// <summary>
    /// Fiyatın hangi bileşeni olduğu. Varsayılan Toplam; tek fiyatlı
    /// kitaplarda ve elle girişte bu kullanılır.
    /// </summary>
    public PositionPriceComponent Component { get; set; } = PositionPriceComponent.Total;

    public decimal UnitPrice { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>
    /// Yıl içinde yayımlanan ara/ek fiyat kitapları için yürürlük
    /// tarihi. Boşsa yılın tamamı için geçerli sayılır.
    /// </summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>Kitap adı, bülten numarası veya içe aktarılan dosya adı.</summary>
    public string? SourceNote { get; set; }
}
