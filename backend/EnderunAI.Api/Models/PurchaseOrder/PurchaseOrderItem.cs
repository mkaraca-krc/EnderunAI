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

    public string MaterialDescription { get; set; } = string.Empty;
    public string? Brand { get; set; }
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
