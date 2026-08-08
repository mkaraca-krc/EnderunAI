namespace EnderunAI.Api.Models.GoodsReceipt;

public sealed class GoodsReceiptItem : BaseEntity
{
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public Guid PurchaseOrderItemId { get; set; }
    public global::EnderunAI.Api.Models.PurchaseOrder.PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;

    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public int LineNumber { get; set; }
    public string MaterialDescription { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyReceivedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }

    /// <summary>
    /// Red / hasar gerekçesi. Reddedilen ya da hasarlı miktar varsa
    /// ZORUNLU.
    ///
    /// Gerekçesiz red, tedarikçiyle mutabakatta savunulamaz ve
    /// tedarikçi kalite geçmişini "sebebi bilinmeyen redler"le
    /// doldurur. Alış iadesi belgesine de buradan kopyalanır.
    /// </summary>
    public string? RejectionReason { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public string? ShelfLocation { get; set; }
    public string? Notes { get; set; }
}
