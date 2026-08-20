namespace EnderunAI.Api.Models;

/// <summary>
/// Perakende satış fişinin durumu.
///
/// Peşin + tavan içi satış <see cref="Draft"/>'tan doğrudan
/// <see cref="Completed"/>'a geçer; onay gerekiyorsa arada
/// <see cref="PendingApproval"/> durur.
/// </summary>
public enum RetailSaleStatus
{
    Draft = 0,
    PendingApproval = 1,
    Completed = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>Fişin ödeme yöntemi. Vade ve çek kayıtlı cari zorunlu kılar.</summary>
public enum RetailPaymentMethod
{
    Cash = 0,
    CreditCard = 1,
    Cheque = 2,
    Term = 3
}

/// <summary>
/// PERAKENDE SATIŞ FİŞİ — AYRI BİR SATIŞ DEFTERİ DEĞİLDİR.
///
/// Bu kayıt bir HIZLI GİRİŞ NOKTASI ve onay kapısıdır. Onaydan geçtiği
/// anda iş mevcut altyapıya akar: <see cref="SalesInvoiceId"/> ile
/// fatura/gelir, <see cref="StockMovement"/> ile stok düşümü,
/// <see cref="CashTransaction"/> ile tahsilat. Ciro, tahsilat ve vade
/// HER ZAMAN o kaynaklardan toplanır.
///
/// ÇİFT SAYIM YASAK: raporlar bu tablodan tutar toplamaz; buradaki
/// tutarlar fişin kendi kaydı ve onay geçmişidir. "Perakende cirosu"
/// sorusunun cevabı, kaynağı bu fiş olan satış faturalarıdır.
///
/// Tamamlanan bir fiş DEĞİŞTİRİLMEZ: düzeltme iptal ya da iade ile
/// yapılır, çünkü fatura ve stok hareketi çoktan oluşmuştur.
/// </summary>
public sealed class RetailSale : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Malın çıktığı merkez depo (WarehouseType.Central).</summary>
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Fiş numarası — DocumentNumberService'ten (RETAIL_SALE / PSF).</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public DateTime SaleDate { get; set; }

    /// <summary>
    /// Kayıtlı müşteri. İSİMSİZ NAKİT SATIŞTA BOŞ olabilir; ama vade ve
    /// çekte zorunludur — alacağın kime ait olduğu bilinmeden vade
    /// takibi yapılamaz.
    /// </summary>
    public Guid? CustomerCurrentAccountId { get; set; }
    public CurrentAccount? CustomerCurrentAccount { get; set; }

    /// <summary>İsimsiz satışta müşteri adı (serbest metin, opsiyonel).</summary>
    public string? WalkInCustomerName { get; set; }

    public RetailPaymentMethod PaymentMethod { get; set; }

    /// <summary>Vadeli satışta ödeme tarihi. Nakit akışa bu tarihle girer.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Fiş geneline uygulanan iskonto oranı (%).</summary>
    public decimal OverallDiscountRate { get; set; }

    /// <summary>Satır iskontoları sonrası, fiş iskontosu öncesi ara toplam.</summary>
    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// KAYITLI tutar — faturaya ve resmî gelire giren kısım.
    /// <see cref="GrandTotal"/> = <see cref="RecordedAmount"/> +
    /// <see cref="CashAmount"/>.
    /// </summary>
    public decimal RecordedAmount { get; set; }

    /// <summary>
    /// ELDEN tutar — AYRI SAYISAL ALAN, metinden parse edilmez.
    ///
    /// Resmî gelire, faturaya ve muhasebe fişine GİRMEZ; yalnız yetkili
    /// iç nakit takibinde görünür. Açıklama alanına yazılsaydı hem
    /// maskelenemez hem de toplanamazdı.
    ///
    /// Görünürlüğü extra_payment.view maskesine bağlıdır: yetkisiz
    /// kullanıcıya null döner, eksik kayıt sayısı hiddenCount ile
    /// bildirilir — tutar sızmaz.
    ///
    /// MAL HER HÂLÜKÂRDA ÇIKAR: elden kısım stoğu aynen düşürür, ayrım
    /// yalnızca paranın kaydındadır.
    /// </summary>
    public decimal CashAmount { get; set; }

    public RetailSaleStatus Status { get; set; } = RetailSaleStatus.Draft;

    /// <summary>
    /// Onay neden gerekti — kullanıcıya ve onaycıya gösterilir.
    /// Boşsa satış onaysız tamamlanmıştır.
    /// </summary>
    public string? ApprovalReason { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Reddetme ya da iptal gerekçesi — ikisinde de zorunlu.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>
    /// Onay sonrası üretilen satış faturası. GELİRİN TEK KAYNAĞI budur;
    /// bu alan dolu değilse ortada gelir yok demektir.
    /// </summary>
    public Guid? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    /// <summary>
    /// Tahsilatın gireceği kasa/banka hesabı.
    ///
    /// Peşinde kasa, kartta banka hesabı seçilir — POS parası bankaya
    /// düşer. Vade ve çekte BOŞ kalır: o an tahsilat yoktur, alacak
    /// açıktır ve nakit akışa vade tarihiyle girer.
    ///
    /// NOT: kart tahsilatı şirketin KENDİ kredi kartı modülüne
    /// bağlanmaz — o modül şirketin ödeme yaptığı kartları izliyor,
    /// müşteriden yapılan POS tahsilatını değil.
    /// </summary>
    public Guid? CashAccountId { get; set; }

    /// <summary>Onay sonrası oluşan tahsilat (peşin/kart). Vadede boş kalır.</summary>
    public Guid? CashTransactionId { get; set; }

    /// <summary>
    /// FATURASIZ satışta kesilen muhasebe fişi — isimsiz nakit satış
    /// ya da tamamı elden satış. Faturalı satışta boş kalır: orada fiş
    /// faturanın kendisine bağlıdır ve <see cref="SalesInvoiceId"/>
    /// üzerinden bulunur.
    ///
    /// İki alan aynı anda dolu OLAMAZ; hangisinin dolu olduğu satışın
    /// hangi yoldan muhasebeleştiğini de söyler.
    /// </summary>
    public Guid? AccountingVoucherId { get; set; }

    /// <summary>
    /// Bu fiş bir İADE fişi mi.
    ///
    /// İade AYRI BİR VARLIK DEĞİL, aynı fiş türünün ters yönlüsü —
    /// böylece onay kapısı, elden maskesi ve durum makinesi tek yerde
    /// kalıyor. İkinci bir onay motoru açmak, aynı kuralların iki kez
    /// yazılması ve zamanla ayrışması demekti.
    /// </summary>
    public bool IsReturn { get; set; }

    /// <summary>İade fişinin kaynağı olan satış.</summary>
    public Guid? OriginalSaleId { get; set; }
    public RetailSale? OriginalSale { get; set; }

    public ICollection<RetailSaleItem> Items { get; set; } = new List<RetailSaleItem>();
}

/// <summary>
/// Fiş satırı. Fiyat ve iskonto tavanı stok kartından KOPYALANIR:
/// kart sonradan değişse bile bu fişin koşulları sabit kalır.
/// </summary>
public sealed class RetailSaleItem : BaseEntity
{
    public Guid RetailSaleId { get; set; }
    public RetailSale RetailSale { get; set; } = null!;

    public int LineNumber { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>Satır anındaki kart bilgileri — sonradan değişse de fiş sabit.</summary>
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    /// <summary>Stok kartındaki satış fiyatı (KDV hariç).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Bu satıra uygulanan iskonto oranı (%).</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>
    /// Satırın hazırlandığı andaki kart tavanı. Onaycı, tavanın ne kadar
    /// aşıldığını sonradan da görebilsin diye saklanıyor — kart tavanı
    /// değiştirilirse geçmiş satışın gerekçesi kaybolmasın.
    /// </summary>
    public decimal MaxDiscountRateAtSale { get; set; }

    public decimal VatRate { get; set; }

    /// <summary>Miktar × birim fiyat × (1 − iskonto), KDV hariç.</summary>
    public decimal LineSubtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Fiş tamamlanırken dondurulan ağırlıklı ortalama birim maliyet.
    ///
    /// Taslakta BOŞTUR: maliyet, malın fiilen çıktığı anda ne ise odur.
    /// Taslak açıldığı gündeki maliyet yazılsaydı, araya giren bir mal
    /// kabulü ortalamayı değiştirdiğinde fişteki maliyet stoktan
    /// düşülenle tutmaz ve 621 ile 153 birbirini kapatmazdı.
    /// </summary>
    public decimal? UnitCostAtSale { get; set; }

    /// <summary>Miktar × <see cref="UnitCostAtSale"/>.</summary>
    public decimal? LineCost { get; set; }
}
