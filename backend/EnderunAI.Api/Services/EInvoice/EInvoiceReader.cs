namespace EnderunAI.Api.Services.EInvoice;

/// <summary>
/// Bir dosyanın okunma sonucu.
/// </summary>
/// <param name="RequiresManualReview">AI ile okunduysa veya tutarlar
/// tutmuyorsa true — bu fatura otomatik onaylanamaz.</param>
public sealed record InvoiceReadResult(
    bool Success,
    ParsedInvoice? Invoice,
    InvoiceParseSource Source,
    bool RequiresManualReview,
    IReadOnlyList<string> Problems);

public interface IEInvoiceReader
{
    Task<InvoiceReadResult> ReadAsync(string xml, CancellationToken cancellationToken);
}

/// <summary>
/// İki katmanlı okuma:
///
///   Katman 1 — standart UBL-TR ayrıştırıcı. Hızlı, ücretsiz, tanıdık
///   faturaların neredeyse tamamını okur.
///
///   Katman 2 — AI yedek. YALNIZCA katman 1 zorunlu alanları
///   çıkaramazsa veya tutarlar tutmazsa çağrılır. Token maliyeti bu
///   yüzden sınırlı kalır; başarılı okumada model hiç çağrılmaz.
///
/// AI ile okunan fatura her zaman elle kontrol işaretiyle döner.
/// </summary>
public sealed class EInvoiceReader(
    IAiInvoiceParser aiParser,
    ILogger<EInvoiceReader> logger) : IEInvoiceReader
{
    public async Task<InvoiceReadResult> ReadAsync(
        string xml, CancellationToken cancellationToken)
    {
        var standard = UblTrInvoiceParser.Parse(xml);
        var standardValidation = UblTrInvoiceValidator.Validate(standard);

        var standardUsable = standard.HasRequiredFields && standardValidation.IsConsistent;

        if (standardUsable)
        {
            return new InvoiceReadResult(
                Success: true,
                Invoice: standard,
                Source: InvoiceParseSource.Standard,
                RequiresManualReview: false,
                Problems: standard.Problems);
        }

        // Standart okuma yetersiz — AI yedeğini dene.
        var reasons = standard.Problems.Concat(standardValidation.Issues).ToList();

        if (!aiParser.IsAvailable)
        {
            return Failed(standard, reasons,
                "AI yedek okuyucu yapılandırılmamış; fatura okunamadı.");
        }

        logger.LogInformation(
            "Standart ayrıştırıcı yetersiz kaldı, AI yedeği deneniyor. Sebep: {Sebep}",
            string.Join(" | ", reasons));

        var ai = await aiParser.ParseAsync(xml, cancellationToken);

        if (ai is null)
        {
            return Failed(standard, reasons,
                "AI yedek okuyucu da faturayı okuyamadı.");
        }

        var aiValidation = UblTrInvoiceValidator.Validate(ai);

        var problems = new List<string>(ai.Problems);

        if (!aiValidation.IsConsistent)
        {
            problems.Add(
                "ŞÜPHELİ OKUMA: tutarlar kendi içinde tutmuyor — " +
                string.Join(" ", aiValidation.Issues));
        }

        // AI ile okunan fatura HER ZAMAN elle kontrole düşer.
        return new InvoiceReadResult(
            Success: true,
            Invoice: ai,
            Source: InvoiceParseSource.Ai,
            RequiresManualReview: true,
            Problems: problems);
    }

    private static InvoiceReadResult Failed(
        ParsedInvoice standard, List<string> reasons, string summary)
    {
        var problems = new List<string> { summary };
        problems.AddRange(reasons);

        return new InvoiceReadResult(
            Success: false,
            Invoice: standard.HasRequiredFields ? standard : null,
            Source: InvoiceParseSource.Standard,
            RequiresManualReview: true,
            Problems: problems);
    }
}
