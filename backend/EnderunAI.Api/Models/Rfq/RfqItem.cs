namespace EnderunAI.Api.Models.Rfq;

public sealed class RfqItem : BaseEntity
{
    public Guid RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;

    public Guid? PurchaseRequestItemId { get; set; }
    public PurchaseRequestItem? PurchaseRequestItem { get; set; }

    public int LineNumber { get; set; }

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
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime? RequestedDeliveryDate { get; set; }
    public string? Notes { get; set; }

    public ICollection<RfqSupplierQuotationItem> QuotationItems { get; set; } =
        new List<RfqSupplierQuotationItem>();
}
