namespace EnderunAI.Api.Models;

public sealed class PurchaseRequestItem : BaseEntity
{
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>
    /// Talep edilen stok kartı. OPSİYONEL: katalogda olmayan malzeme de
    /// talep edilebilmeli, aksi halde talep hiç açılamaz ve süreç kartın
    /// tanımlanmasını beklerdi. Seçilirse ad ve birim karttan gelir,
    /// zincir mal kabule kadar kopmadan taşınır.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public string MaterialDescription { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;

    public DateTime? RequestedDeliveryDate { get; set; }
    public string? Notes { get; set; }
}
