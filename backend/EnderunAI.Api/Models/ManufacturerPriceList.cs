namespace EnderunAI.Api.Models;

public sealed class ManufacturerPriceList : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string ManufacturerName { get; set; } = string.Empty;
    public string ListName { get; set; } = string.Empty;
    public DateTime ListDate { get; set; }
    public DateTime? ValidUntil { get; set; }

    public string Currency { get; set; } = "TRY";

    public ICollection<ManufacturerPriceListItem> Items { get; set; }
        = new List<ManufacturerPriceListItem>();
}
