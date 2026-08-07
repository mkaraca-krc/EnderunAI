namespace EnderunAI.Api.Models;

public sealed class OfferItem : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;

    public int LineNumber { get; set; }

    public string? PositionNumber { get; set; }

    public Guid? EngineeringPositionId { get; set; }
    public EngineeringPosition? EngineeringPosition { get; set; }

    public Guid? EngineeringRecipeId { get; set; }
    public int? RecipeVersion { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? ManufacturerPriceListItemId { get; set; }
    public ManufacturerPriceListItem? ManufacturerPriceListItem { get; set; }

    public string? ManufacturerName { get; set; }
    public string? ProductCode { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }

    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;

    public decimal ListPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal NetPurchasePrice { get; set; }

    public decimal FreightRate { get; set; }
    public decimal WasteRate { get; set; }
    public decimal FinanceRate { get; set; }
    public decimal GeneralExpenseRate { get; set; }
    public decimal ProfitRate { get; set; }

    public decimal UnitCost { get; set; }
    public decimal UnitSalesPrice { get; set; }

    // --- Satış fiyatının bileşenleri ---
    // Malzeme / montaj / genel gider ayrımı. İcmal kaleminde
    // (ProjectBoqItem) kurulan ayrımın aynısı; teklif icmale
    // aktarıldığında bileşenler bire bir taşınabilsin diye.
    //
    // UnitSalesPrice üçünün TOPLAMIdır ve tek gerçek satış fiyatıdır;
    // bileşen girilmemiş kalemde tutarın tamamı malzemeye yazılır ve
    // toplam değişmez. Böylece eski teklifler aynen çalışmaya devam
    // eder.

    public decimal MaterialUnitPrice { get; set; }
    public decimal LaborUnitPrice { get; set; }
    public decimal OverheadUnitPrice { get; set; }

    public decimal CostTotal { get; set; }
    public decimal SalesTotal { get; set; }

    public string? Notes { get; set; }
}
