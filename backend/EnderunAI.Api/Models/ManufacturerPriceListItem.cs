namespace EnderunAI.Api.Models;

public sealed class ManufacturerPriceListItem : BaseEntity
{
    public Guid ManufacturerPriceListId { get; set; }
    public ManufacturerPriceList ManufacturerPriceList { get; set; } = null!;

    public string ProductCode { get; set; } = string.Empty;
    public string ProductDescription { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ListPrice { get; set; }

    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
}
