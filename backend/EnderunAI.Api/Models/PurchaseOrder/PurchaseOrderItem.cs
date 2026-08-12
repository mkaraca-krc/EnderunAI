namespace EnderunAI.Api.Models.PurchaseOrder;

public sealed class PurchaseOrderItem : BaseEntity
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid? RfqItemId { get; set; }
    public global::EnderunAI.Api.Models.Rfq.RfqItem? RfqItem { get; set; }

    public Guid? RfqSupplierQuotationItemId { get; set; }
    public global::EnderunAI.Api.Models.Rfq.RfqSupplierQuotationItem? RfqSupplierQuotationItem { get; set; }

    public int LineNumber { get; set; }

    /// <summary>
    /// Sipariş edilen stok kartı. Talepteki seçim buraya, buradan da mal
    /// kabule taşınır; böylece mal kabulde kartı elle eşleştirme adımı
    /// çoğu kalemde gereksizleşir. Opsiyonel — katalog dışı alım
    /// engellenmiyor.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public global::EnderunAI.Api.Models.InventoryItem? InventoryItem { get; set; }

    /// <summary>
    /// Zincirin dayandığı poz — talep kaleminden taşınıyor.
    ///
    /// Ad ve birim kopyası bugünkü gibi devam ediyor; poz kimliği ONUN
    /// YANINDA duruyor ki "bu sipariş hangi imalat kalemine ait"
    /// sorusu metin eşleştirmeden cevaplanabilsin. Opsiyonel: pozsuz
    /// serbest kalem zinciri hâlâ yürüyor.
    /// </summary>
    public Guid? EngineeringPositionId { get; set; }
    public global::EnderunAI.Api.Models.EngineeringPosition? EngineeringPosition { get; set; }

    public string MaterialDescription { get; set; } = string.Empty;
    /// <summary>
    /// Tedarikçinin VERDİĞİ marka (tekliften gelir).
    /// <see cref="RequestedBrand"/> ile karıştırılmaz; ikisi yan yana
    /// durur ki istenen mi geldi, muadil mi görülebilsin.
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>Talep edenin İSTEDİĞİ marka; RFQ üzerinden talepten gelir.</summary>
    public string? RequestedBrand { get; set; }

    /// <summary>Muadil kabul ediliyor muydu; talepten gelir.</summary>
    public bool BrandIrrelevant { get; set; }
    public string? Model { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal NetUnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public int? DeliveryDays { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? Notes { get; set; }

    public ICollection<global::EnderunAI.Api.Models.GoodsReceipt.GoodsReceiptItem> GoodsReceiptItems { get; set; } =
        new List<global::EnderunAI.Api.Models.GoodsReceipt.GoodsReceiptItem>();
}
