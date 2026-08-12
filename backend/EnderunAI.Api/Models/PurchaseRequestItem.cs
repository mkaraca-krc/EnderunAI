namespace EnderunAI.Api.Models;

public sealed class PurchaseRequestItem : BaseEntity
{
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>
    /// TALEP EDENİN istediği marka. Tedarikçinin teklif ettiği
    /// markadan (<c>RfqSupplierQuotation.Brand</c>) AYRIDIR ve onunla
    /// karıştırılmaz: biri "ne istendi", diğeri "ne verildi". İkisi
    /// siparişte yan yana durur ki istenen mi geldi, muadil mi
    /// karşılaştırılabilsin.
    ///
    /// <see cref="BrandIrrelevant"/> ile birlikte ÜÇ durum anlatır:
    ///   - marka dolu + bayrak false → ZORUNLU marka
    ///   - marka dolu + bayrak true  → TERCİH, muadil kabul
    ///   - marka boş  + bayrak true  → farketmez
    /// Marka boş + bayrak false geçersizdir; talep kaydedilmez.
    /// </summary>
    public string? RequestedBrand { get; set; }

    /// <summary>
    /// Muadil kabul ediliyor mu. İşaretliyse tedarikçi serbesttir;
    /// <see cref="RequestedBrand"/> doluysa bu bir TERCİHTİR, şart
    /// değil.
    ///
    /// MEVCUT KAYITLARDA VARSAYILAN TRUE: marka alanı eklenmeden önce
    /// açılmış talepler zorunluluk ihlali sayılmamalı. Geçmişe dönük
    /// bir kural, kimsenin bilmediği bir kararı geriye yüklerdi.
    /// </summary>
    public bool BrandIrrelevant { get; set; }

    /// <summary>
    /// Talep edilen stok kartı. OPSİYONEL: katalogda olmayan malzeme de
    /// talep edilebilmeli, aksi halde talep hiç açılamaz ve süreç kartın
    /// tanımlanmasını beklerdi. Seçilirse ad ve birim karttan gelir,
    /// zincir mal kabule kadar kopmadan taşınır.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>
    /// Talebin dayandığı POZ (keşif kütüphanesi). Stok kartından AYRI
    /// bir eksen: stok kartı "depoda hangi ürün", poz "hangi imalat
    /// kalemi". Şirketin 23 binin üzerinde pozu var, stok kartı ise
    /// avuç içi kadar; talebin gerçek karşılığı çoğu zaman pozdur.
    ///
    /// OPSİYONEL: pozsuz serbest metin talebi hâlâ açılabilir — acil
    /// bir ihtiyaç, poz tanımlanana kadar bekleyemez.
    ///
    /// Ad ve birim seçim anında KOPYALANIR (aşağıdaki alanlara): poz
    /// sonradan revize edilirse geçmiş talep oynamasın.
    /// </summary>
    public Guid? EngineeringPositionId { get; set; }
    public EngineeringPosition? EngineeringPosition { get; set; }

    public string MaterialDescription { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;

    public DateTime? RequestedDeliveryDate { get; set; }
    public string? Notes { get; set; }
}
