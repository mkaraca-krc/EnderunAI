namespace EnderunAI.Api.Models.GoodsReceipt;

/// <summary>
/// Alış iadesi belgesinin durumu.
/// </summary>
public enum PurchaseReturnStatus
{
    /// <summary>
    /// Taslak — mal kabulle birlikte doğar, henüz tedarikçiye
    /// gönderilmedi. Bekleyen iade listesi bu durumdan beslenir.
    /// </summary>
    Draft = 0,

    /// <summary>Tedarikçiye gönderildi / teslim edildi.</summary>
    Sent = 1,

    /// <summary>Tedarikçi iadeyi kabul etti, süreç kapandı.</summary>
    Completed = 2,

    /// <summary>İptal — mal iade edilmedi (yerinde çözüldü).</summary>
    Cancelled = 3
}

/// <summary>
/// Neden iade ediliyor.
/// </summary>
public enum PurchaseReturnReasonKind
{
    /// <summary>Şartnameye uymadığı için reddedildi.</summary>
    Rejected = 0,

    /// <summary>Hasarlı geldi.</summary>
    Damaged = 1
}

/// <summary>
/// Alış iadesi belgesi.
///
/// NEDEN VAR: mal kabulde reddedilen ya da hasarlı gelen miktar
/// stoğa girmiyor ama bir yere de yazılmıyordu. Tedarikçiye neyin,
/// ne kadarının, hangi gerekçeyle iade edildiği belgesiz kalınca
/// ne cari mutabakatı yapılabiliyor ne de tedarikçi kalite geçmişi
/// güvenilir oluyordu.
///
/// Mal kabul kesinleşirken OTOMATİK doğar; elle açma adımı
/// unutulduğunda reddedilen mal kayıtsız kalırdı. Taslak doğar,
/// tedarikçiye gönderim ayrı bir adımdır.
/// </summary>
public sealed class PurchaseReturn : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>İadenin doğduğu mal kabul.</summary>
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public Guid PurchaseOrderId { get; set; }
    public global::EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder PurchaseOrder { get; set; }
        = null!;

    /// <summary>
    /// İade edilen tedarikçi. Sipariş anında kim ise odur; sonradan
    /// sipariş değişse bile belge kime iade edildiğini korumalı.
    /// </summary>
    public Guid SupplierCurrentAccountId { get; set; }
    public CurrentAccount SupplierCurrentAccount { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }

    public PurchaseReturnStatus Status { get; set; } = PurchaseReturnStatus.Draft;

    /// <summary>
    /// Belge para birimi ve kuru siparişten donar: iade bedeli,
    /// malın alındığı fiyat üzerinden konuşulur.
    /// </summary>
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public DateTime? SentAtUtc { get; set; }
    public Guid? SentByUserId { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<PurchaseReturnItem> Items { get; set; }
        = new List<PurchaseReturnItem>();

    /// <summary>Açık iade: henüz tedarikçiyle kapanmamış.</summary>
    public bool IsOpen =>
        Status is PurchaseReturnStatus.Draft or PurchaseReturnStatus.Sent;
}

/// <summary>
/// Alış iadesi kalemi. Red ve hasar AYRI satırlar olarak tutulur:
/// ikisi farklı gerekçelerdir ve tedarikçi kalite analizinde ayrı
/// sayılmaları gerekir.
/// </summary>
public sealed class PurchaseReturnItem : BaseEntity
{
    public Guid PurchaseReturnId { get; set; }
    public PurchaseReturn PurchaseReturn { get; set; } = null!;

    public Guid GoodsReceiptItemId { get; set; }
    public GoodsReceiptItem GoodsReceiptItem { get; set; } = null!;

    public Guid PurchaseOrderItemId { get; set; }

    public int LineNumber { get; set; }

    public string MaterialDescription { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    /// <summary>Siparişteki net birim fiyat (belge para biriminde).</summary>
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public PurchaseReturnReasonKind ReasonKind { get; set; }

    /// <summary>
    /// Mal kabulde girilen gerekçe. Belgeye kopyalanır: mal kabul
    /// sonradan düzeltilse bile iade belgesi neyin neden iade
    /// edildiğini olduğu gibi taşımalı.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
