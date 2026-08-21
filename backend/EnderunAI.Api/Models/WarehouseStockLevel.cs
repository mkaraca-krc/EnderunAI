namespace EnderunAI.Api.Models;

/// <summary>
/// DEPO BAZLI ASGARİ/AZAMİ STOK SEVİYESİ — min/max takibinin tek kaynağı.
///
/// NEDEN KART ÜZERİNDE DEĞİL: seviye şirket geneli bir sayı olamaz.
/// Merkez deposunda 100 metre kablo bulundurmak isteriz, biten bir
/// şantiye deposunda aynı kalem için sıfır doğrudur. Kart üzerinde tek
/// sayı olduğu sürece "hangi depoya alalım" sorusu cevapsız kalıyordu;
/// üstelik iki mevcut uç aynı alanı farklı anlamda kullanıyordu
/// (kart listesi TOPLAM stoğa, uyarı ucu DEPO miktarına bakıyordu).
///
/// NEDEN <see cref="WarehouseStock"/> SATIRINA KOLON DEĞİL: o satır bir
/// BAKİYE, bu bir POLİTİKA. Bakiye satırı yalnızca malzeme o depoya bir
/// kez girdiyse vardır — oysa seviye takibine en çok stok SIFIRKEN
/// ihtiyaç duyulur. Politika ayrı satırda durduğu için bakiyesi hiç
/// olmayan kalem de uyarı üretebilir.
///
/// SATIRIN VARLIĞI TAKİBİN KENDİSİDİR: seviye takip edilmeyecekse satır
/// silinir. Asgarisi sıfır olan bir satır anlamsızdır ve kabul edilmez —
/// "her zaman kritik" demek olurdu.
/// </summary>
public sealed class WarehouseStockLevel : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>
    /// Bu depoda bulundurulacak en az miktar. Mevcut miktar buna EŞİT
    /// ya da altındaysa uyarı doğar (eşitlik dahil: minimuma dokunmuş
    /// stok zaten ikmal edilmeli, bir birim daha çıkması beklenmemeli).
    /// Sıfır ya da negatif olamaz.
    /// </summary>
    public decimal MinimumQuantity { get; set; }

    /// <summary>
    /// İkmalin hedefi. Sipariş önerisi <c>azami − mevcut</c> ile
    /// hesaplanır; bu yüzden azami tanımlı değilse öneri ÜRETİLMEZ.
    /// Uydurma bir katsayı (örneğin asgarinin iki katı) kullanılmıyor:
    /// kaç adet alınacağı işletme kararıdır, tahmin edilmez. Uyarı yine
    /// de çıkar — eksik olan bilgi öneridir, uyarı değil.
    /// </summary>
    public decimal? MaximumQuantity { get; set; }

    public string? Note { get; set; }
}
