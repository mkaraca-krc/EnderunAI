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
/// Bir pozun belirli bir yıla ve kuruma ait yayımlanmış birim fiyatı.
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
