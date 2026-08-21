namespace EnderunAI.Api.Models;

/// <summary>
/// TEDARİK TİPİ — malzemenin depoya nasıl geldiği.
///
/// ÜÇÜ BİRBİRİNİ DIŞLAR, bu yüzden tek alan: bir kart hem "stokta
/// bulundurulan" hem "siparişe göre üretilen" olamaz. İki ayrı işaret
/// kutusu olsaydı "ikisi de işaretli" diye cevapsız bir durum doğardı.
/// </summary>
public enum InventorySupplyKind
{
    /// <summary>
    /// STOKLU: depoda bulundurulur, tükenince ikmal edilir. Asgari/azami
    /// seviye takibi (S8) yalnız bunda anlamlıdır.
    /// </summary>
    Stocked = 0,

    /// <summary>
    /// ÖZEL İMALAT: bize özel üretilir, her biri tekildir. Katalog
    /// karşılığı yoktur; mükerrer engeli de bu yüzden uygulanmaz.
    /// </summary>
    CustomManufacture = 1,

    /// <summary>
    /// SİPARİŞ ÜZERİNE: katalog ürünüdür ama stokta tutulmaz, iş
    /// çıkınca sipariş edilir. Asgari seviye tanımlamak anlamsızdır —
    /// "hiç bulundurmamak" bilinçli karardır.
    /// </summary>
    MadeToOrder = 2
}

/// <summary>
/// STOK KARTI GÖRSELİ.
///
/// AYRI TABLO, KART ÜZERİNDE KOLON DEĞİL: dekoratif bir armatürde
/// montaj öncesi/sonrası, detay ve ölçü krokisi AYRI görsellerdir. Tek
/// `ImagePath` alanı (S9 öncesi) bunları anlatamıyordu ve ikinci bir
/// açı gerektiğinde kullanıcıyı eskisini silmeye zorlardı.
///
/// DOSYA DİSKTE, KAYIT BURADA: içerik `IUploadService` ile
/// "stok-kartlari" kategorisine yazılıyor — tip ve boyut doğrulaması
/// orada, tek yerde. Baytları veritabanına koymak yedeği şişirir ve
/// her sorguyu ağırlaştırırdı.
/// </summary>
public sealed class InventoryItemPhoto : BaseEntity
{
    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>Diskteki ad — `IUploadService` üretir, çakışmaz.</summary>
    public string StoredName { get; set; } = string.Empty;

    /// <summary>Kullanıcının yüklediği dosyanın adı; indirmede bu görünür.</summary>
    public string OriginalName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }

    /// <summary>
    /// KAPAK GÖRSELİ. Listede, seçicilerde ve etikette bu görünür.
    ///
    /// Kart başına EN FAZLA BİR kapak olur ve galeri boş değilse
    /// MUTLAKA bir kapak vardır: ilk yüklenen kendiliğinden kapak olur,
    /// kapak silinince sıradaki devralır. Aksi halde liste, kullanıcının
    /// hiç seçmediği rastgele bir görseli gösterirdi.
    /// </summary>
    public bool IsCover { get; set; }

    public string? Caption { get; set; }
}
