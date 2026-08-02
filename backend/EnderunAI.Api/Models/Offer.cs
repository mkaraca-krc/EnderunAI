namespace EnderunAI.Api.Models;

public enum OfferStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Won = 4,
    Lost = 5,
    Cancelled = 6
}

public sealed class Offer : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? CustomerId { get; set; }

    public string OfferNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public DateTime OfferDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? ValidUntil { get; set; }

    public string Currency { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;

    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal CostTotal { get; set; }
    public decimal ProfitTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public ICollection<OfferItem> Items { get; set; }
        = new List<OfferItem>();
}
