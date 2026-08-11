namespace EnderunAI.Api.Models.Expenses;

/// <summary>
/// Gider kategorisi — "neye harcadık" ekseni.
///
/// PARAMETRİK, ENUM DEĞİL: kategori listesi işletmeye göre değişir
/// (bir şirkette "araç/yakıt" tek kalem, diğerinde ikiye ayrılır).
/// Enum olsaydı her yeni kalem migration isterdi ve kullanıcı kendi
/// listesini kuramazdı.
///
/// KOD DEĞİŞMEZ, AD DEĞİŞİR: otomatik akışlar (satın alma, görev
/// masrafı, işçilik) kategoriyi <see cref="Code"/> ile buluyor. Ad
/// serbestçe düzeltilebilir; kod sistem kategorilerinde kilitli,
/// çünkü değişirse otomatik kalemler kategorisiz kalır.
/// </summary>
public sealed class ExpenseCategory : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Makine tarafı anahtar (küçük harf, Türkçe karaktersiz).
    /// Şirket içinde tekil.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>
    /// Kurulumla gelen kategori. SİLİNEMEZ ve kodu değiştirilemez:
    /// otomatik kaynaklar bu kodlara bağlı. Adı düzeltilebilir.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Yalnızca otomatik kaynaklardan dolan kategori (malzeme,
    /// işçilik, taşeron, yol). Elle gider girişinde SEÇİLEMEZ —
    /// seçilebilseydi aynı gider hem otomatik akar hem elle girilir,
    /// çift sayımın en kolay yolu açılırdı.
    /// </summary>
    public bool IsAutomaticOnly { get; set; }

    // Aktiflik BaseEntity.IsActive üzerinden yürüyor. Burada yeniden
    // tanımlanmıştı; taban özelliği GÖLGELİYORDU: BaseEntity
    // referansıyla yapılan bir atama tabanı yazar, sorgu türetilmişi
    // okurdu. Tek kolon olduğu için veri bozulmadı, tuzak kaldırıldı.
}
