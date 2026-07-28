namespace EnderunAI.Api.Models;

public sealed class DocumentNumberSequence : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string DocumentType { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;

    public int Year { get; set; }
    public long LastNumber { get; set; }

    public int NumberLength { get; set; } = 6;
}
