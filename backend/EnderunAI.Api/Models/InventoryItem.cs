namespace EnderunAI.Api.Models;

public enum InventoryItemType
{
    Material = 0,
    Equipment = 1,
    Consumable = 2,
    SparePart = 3
}

public sealed class InventoryItem : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// KATEGORİ BAĞI (S1). Kartın özellik şablonunu, izin verilen
    /// birimlerini ve tipini (STANDART/SERBEST) buradan alır.
    ///
    /// Zorunlu değil çünkü ARŞİVLENMİŞ eski kartlar kategorisiz:
    /// onlar serbest metin <see cref="Category"/> alanıyla açılmıştı
    /// ve geçmiş fatura kalemleri hâlâ onlara bağlı. Yeni kart
    /// açılışında kategori zorunlu hâle gelecek (S2).
    /// </summary>
    public Guid? InventoryCategoryId { get; set; }
    public InventoryCategory? InventoryCategory { get; set; }

    public ICollection<InventoryItemAttributeValue> AttributeValues { get; set; }
        = new List<InventoryItemAttributeValue>();

    /// <summary>
    /// MÜKERRER ENGELİNİN VERİTABANI DAYANAĞI (S2).
    ///
    /// Kategori kodu + seçilen özellik değerlerinden üretilen
    /// deterministik imza: `KABLO_TAVASI|CINS=Perfore|KALINLIK=1.5|...`
    /// Özellikler koda göre SIRALANIR ki seçim sırası imzayı
    /// değiştirmesin.
    ///
    /// NEDEN KOLON, NEDEN SORGUYLA KONTROL DEĞİL: sorgu yarışa açık —
    /// iki kullanıcı aynı anda aynı malzemeyi açarsa ikisi de "yok"
    /// görür. Kolon üzerinde `(CompanyId, AttributeSignature)` tekil
    /// indeksi var; ikinci kayıt veritabanı seviyesinde reddedilir.
    ///
    /// ŞİRKET İÇİ: iki farklı şirket aynı malzemeyi kendi kartıyla
    /// tutabilir. Mükerrerin amacı BİR ŞİRKET İÇİNDE stok bölünmesini
    /// engellemek.
    ///
    /// SERBEST tipte NULL: dekoratif aydınlatma ve özel imalatta her
    /// ürün tekildir, mükerrer engeli uygulanmaz.
    /// </summary>
    public string? AttributeSignature { get; set; }

    /// <summary>
    /// MALZEMENİN KONUMU — tek konum, çok konum takibi yok (karar).
    ///
    /// Bölge zorunlu gibi görünse de nullable: arşivlenmiş eski
    /// kartların konumu yok ve depo bölgeleri tanımlanmadan da kart
    /// açılabilmeli.
    ///
    /// AÇIK bölgede raf ve kat NULL kalır — rafa sığmayan büyük
    /// malzemeden raf/kat istemek olmayan bir ayrıntıyı zorunlu
    /// kılmak olurdu.
    /// </summary>
    public Guid? WarehouseZoneId { get; set; }
    public WarehouseZone? WarehouseZone { get; set; }

    public Guid? WarehouseShelfId { get; set; }
    public WarehouseShelf? WarehouseShelf { get; set; }

    public Guid? WarehouseShelfLevelId { get; set; }
    public WarehouseShelfLevel? WarehouseShelfLevel { get; set; }

    /// <summary>
    /// ESKİ serbest metin kategori. S1'den itibaren yeni kartlarda
    /// kullanılmıyor; arşivlenmiş kartların geçmişini okuyabilmek için
    /// duruyor. Canlıda bir kartın değeri "TURAN" (tedarikçi adı)
    /// yazıyordu — serbest metin kategorinin ne ürettiğinin kanıtı.
    /// </summary>
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal? MaximumStock { get; set; }
    /// <summary>
    /// Stok kartının birimi başına bakır içeriği (kg). İcmal kalemi bu
    /// karta bağlıysa ve kalemde katsayı yoksa buradaki değer kullanılır.
    /// </summary>
    public decimal? CopperKgPerUnit { get; set; }

    public InventoryItemType Type { get; set; } = InventoryItemType.Material;

    /// <summary>
    /// Ağırlıklı ortalama birim maliyet, her zaman TRY. Döviz cinsi mal
    /// kabullerinde PurchaseOrder.ExchangeRate ile TRY'ye çevrilerek
    /// ortalamaya katılır (bkz. GoodsReceiptService.PostAsync).
    /// </summary>
    public decimal AverageUnitCost { get; set; }

    /// <summary>
    /// En son mal kabulde ödenen TRY birim fiyat.
    ///
    /// <see cref="AverageUnitCost"/> stok değerlemesi için doğru olandır
    /// ama "bu malzemeyi en son kaça aldık" sorusunu cevaplamaz —
    /// ortalama, eski ucuz alışları da içinde taşır. Satın alma
    /// pazarlığında bakılan rakam budur.
    /// </summary>
    public decimal? LastPurchasePrice { get; set; }

    /// <summary>Son alışın tarihi; fiyatın ne kadar güncel olduğunu söyler.</summary>
    public DateTime? LastPurchaseDate { get; set; }

    /// <summary>
    /// Tercih edilen tedarikçi. Zorunlu değil ve satın almayı
    /// kısıtlamaz — yalnızca teklif isterken kime sorulacağını hatırlatır.
    /// </summary>
    public Guid? PreferredSupplierCurrentAccountId { get; set; }
    public CurrentAccount? PreferredSupplierCurrentAccount { get; set; }

    /// <summary>Malzemenin tabi olduğu KDV oranı (%).</summary>
    public decimal? VatRate { get; set; }

    /// <summary>Teknik özellik, kullanım notu vb.</summary>
    public string? Description { get; set; }

    /// <summary>Yüklenen görselin dosya yolu (uploads/stok-kartlari).</summary>
    public string? ImagePath { get; set; }

    /// <summary>
    /// Perakende liste fiyatı (KDV hariç, TRY). Satış ekranı birim fiyatı
    /// BURADAN okur — satıcının elle yazdığı fiyat değil.
    ///
    /// MALİYETTEN AYRI ALAN: <see cref="AverageUnitCost"/> stok
    /// değerlemesi için, bu ise satış için. İkisini tek alana
    /// indirgemek maliyeti satıcıya göstermek demek olurdu; perakende
    /// ekranı maliyeti hiç okumaz.
    ///
    /// Boşsa o kalem perakende satışa kapalıdır — sıfır fiyat değil,
    /// "fiyatlandırılmamış" demektir.
    /// </summary>
    public decimal? SalesPrice { get; set; }

    /// <summary>
    /// Satış personelinin uygulayabileceği EN YÜKSEK iskonto oranı (%).
    /// Yönetim belirler; personel bu tavanı aşamaz.
    ///
    /// TAVAN SUNUCUDA ZORLANIR. İstemciden gelen orana güvenilmez:
    /// ekran tavanı gösterip girişi kısıtlasa bile, uç doğrudan
    /// çağrılabiliyor. Aşan satış reddedilmez — FİNANS ONAYINA düşer;
    /// yani tavan bir yasak değil, yetki sınırıdır.
    ///
    /// Varsayılan sıfır: tanımlanmadıysa iskonto yok demektir.
    /// </summary>
    public decimal MaxDiscountRate { get; set; }

    public ICollection<WarehouseStock> WarehouseStocks { get; set; } = new List<WarehouseStock>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
