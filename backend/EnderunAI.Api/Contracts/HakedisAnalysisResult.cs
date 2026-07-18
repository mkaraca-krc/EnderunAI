namespace EnderunAI.Api.Contracts;

public sealed class HakedisAnalysisResult
{
    public string Status { get; set; } = "completed";

    public string? FileName { get; set; }

    public string? Project { get; set; }

    public string? Employer { get; set; }

    public string? ProgressPaymentNo { get; set; }

    public string? Period { get; set; }

    public decimal? AmountExcludingVat { get; set; }

    public decimal? VatRate { get; set; }

    public decimal? VatAmount { get; set; }

    public string? SuggestedWithholding { get; set; }

    public double Confidence { get; set; }

    public bool RequiresOcr { get; set; }

    public string ExtractedText { get; set; } = "";

    public List<string> Warnings { get; set; } = [];
}