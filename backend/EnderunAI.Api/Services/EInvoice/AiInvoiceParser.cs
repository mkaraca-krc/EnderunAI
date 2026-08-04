using System.Globalization;
using System.Text.Json;
using EnderunAI.Api.Services.Hizir;

namespace EnderunAI.Api.Services.EInvoice;

public interface IAiInvoiceParser
{
    bool IsAvailable { get; }

    /// <summary>
    /// XML'i dil modeline okutur. Model kullanılamıyorsa veya çıkardığı
    /// değerler doğrulanamazsa null döner — yarım okunmuş fatura
    /// üretmez.
    /// </summary>
    Task<ParsedInvoice?> ParseAsync(string xml, CancellationToken cancellationToken);
}

/// <summary>
/// AI yedek ayrıştırıcı: standart UBL-TR ayrıştırıcı zorunlu alanları
/// çıkaramadığında veya tutarlar tutmadığında devreye girer. Farklı
/// entegratörlerin ürettiği beklenmedik XML biçimlerine karşı
/// dayanıklılık sağlar.
///
/// UYDURMA ENGELİ ÜÇ KATMANLI — asıl güvence ikincisi:
///
///   1. Modele araç VERİLMEZ ve "bulamazsan null yaz, tahmin etme"
///      denir. Kendi başına veri çekemez.
///
///   2. KAYNAK DOĞRULAMA: modelin döndürdüğü her skaler değer, XML
///      metninde gerçekten geçiyor mu diye aranır. Geçmeyen değer
///      kabul edilmez, alan "bulunamadı" olur. Uydurulmuş bir VKN veya
///      tutar bu elemeden geçemez — modelin iyi niyetine değil,
///      belgenin kendisine bakılır.
///
///   3. Tutarlılık kontrolü (UblTrInvoiceValidator) AI çıktısına da
///      uygulanır.
///
/// Bu ayrıştırıcının okuduğu fatura HİÇBİR KOŞULDA otomatik
/// onaylanmaz; her zaman elle kontrol için önizlemeye düşer.
/// </summary>
public sealed class AiInvoiceParser(
    IHizirLlmClient llm,
    ILogger<AiInvoiceParser> logger) : IAiInvoiceParser
{
    /// <summary>
    /// Modele gönderilecek en büyük XML. Aşırı büyük dosyalar hem
    /// maliyetli hem de büyük ihtimalle fatura değil.
    /// </summary>
    private const int MaxXmlLength = 120_000;

    private const string SystemPrompt =
        "Sen bir e-fatura XML okuyucususun. Sana verilen XML'den fatura " +
        "bilgilerini çıkarıp SADECE JSON döndürürsün.\n\n" +
        "MUTLAK KURAL: Bir değeri XML'de bulamazsan o alana null yaz. " +
        "ASLA tahmin etme, ASLA örnek/varsayılan değer üretme, ASLA " +
        "hesaplayarak doldurma. Yanlış bir değer, eksik değerden çok " +
        "daha zararlıdır — çıkardığın her sayı ve metin XML'de aynen " +
        "geçiyor olmalıdır.\n\n" +
        "Tutarları XML'de yazdığı gibi, nokta ondalık ayırıcıyla ver.";

    public bool IsAvailable => llm.IsConfigured;

    public async Task<ParsedInvoice?> ParseAsync(
        string xml, CancellationToken cancellationToken)
    {
        if (!llm.IsConfigured)
            return null;

        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlLength)
            return null;

        try
        {
            var prompt =
                "Aşağıdaki e-fatura XML'inden şu alanları çıkar ve yalnızca " +
                "JSON döndür (açıklama yazma):\n\n" +
                "{\n" +
                "  \"invoiceNumber\": string|null,\n" +
                "  \"issueDate\": \"yyyy-MM-dd\"|null,\n" +
                "  \"currencyCode\": string|null,\n" +
                "  \"supplierTaxNumber\": string|null,\n" +
                "  \"supplierName\": string|null,\n" +
                "  \"customerTaxNumber\": string|null,\n" +
                "  \"customerName\": string|null,\n" +
                "  \"lines\": [{ \"name\": string, \"quantity\": number, " +
                "\"unit\": string|null, \"unitPrice\": number, " +
                "\"lineExtensionAmount\": number, \"vatRate\": number, " +
                "\"vatAmount\": number }],\n" +
                "  \"vatTotal\": number|null,\n" +
                "  \"withholdingAmount\": number|null,\n" +
                "  \"lineExtensionTotal\": number|null,\n" +
                "  \"taxExclusiveAmount\": number|null,\n" +
                "  \"taxInclusiveAmount\": number|null,\n" +
                "  \"payableAmount\": number|null\n" +
                "}\n\n" +
                "XML:\n" + xml;

            // Araç verilmiyor: model yalnızca verilen metinden okuyabilir.
            var response = await llm.CompleteAsync(
                SystemPrompt,
                [new LlmMessage(LlmRole.User, prompt)],
                [],
                cancellationToken);

            var json = ExtractJson(response.Text);

            if (json is null)
            {
                logger.LogWarning("AI fatura okuyucusu JSON döndürmedi.");
                return null;
            }

            return BuildVerified(json.Value, xml);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "AI fatura okuyucusu çalışmadı.");
            return null;
        }
    }

    /// <summary>
    /// Modelin JSON'unu, her değeri XML'de doğrulayarak nesneye çevirir.
    /// Doğrulanamayan alan atılır ve gerekçesi Problems'a yazılır.
    /// </summary>
    private static ParsedInvoice? BuildVerified(JsonElement json, string xml)
    {
        var problems = new List<string>();

        string? VerifiedText(string property)
        {
            var value = Text(json, property);

            if (value is null)
                return null;

            // KAYNAK DOĞRULAMA: değer XML'de geçmiyorsa uydurulmuş
            // sayılır ve alınmaz.
            if (!XmlContains(xml, value))
            {
                problems.Add(
                    $"AI'ın verdiği '{property}' değeri ({value}) XML'de " +
                    "bulunamadı; uydurma olabileceği için alınmadı.");
                return null;
            }

            return value;
        }

        decimal? VerifiedAmount(string property)
        {
            var value = Amount(json, property);

            if (value is null)
                return null;

            if (!XmlContainsAmount(xml, value.Value))
            {
                problems.Add(
                    $"AI'ın verdiği '{property}' tutarı ({value.Value}) XML'de " +
                    "bulunamadı; uydurma olabileceği için alınmadı.");
                return null;
            }

            return value;
        }

        var invoiceNumber = VerifiedText("invoiceNumber");
        var supplierTax = VerifiedText("supplierTaxNumber");
        var customerTax = VerifiedText("customerTaxNumber");

        var issueDateText = Text(json, "issueDate");
        DateTime? issueDate = null;

        if (issueDateText is not null && XmlContains(xml, issueDateText) &&
            DateTime.TryParse(issueDateText, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsedDate))
        {
            issueDate = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
        }
        else if (issueDateText is not null)
        {
            problems.Add(
                $"AI'ın verdiği fatura tarihi ({issueDateText}) XML'de doğrulanamadı.");
        }

        var lines = new List<ParsedInvoiceLine>();

        if (json.TryGetProperty("lines", out var lineArray) &&
            lineArray.ValueKind == JsonValueKind.Array)
        {
            var index = 1;

            foreach (var line in lineArray.EnumerateArray())
            {
                var name = Text(line, "name") ?? "";

                // Kalem adı XML'de geçmiyorsa satırın tamamı şüphelidir.
                if (!string.IsNullOrWhiteSpace(name) && !XmlContains(xml, name))
                {
                    problems.Add(
                        $"AI'ın verdiği {index}. kalem adı ('{name}') XML'de " +
                        "bulunamadı; satır alınmadı.");
                    index++;
                    continue;
                }

                lines.Add(new ParsedInvoiceLine(
                    LineNumber: index++,
                    Name: name,
                    Quantity: Amount(line, "quantity") ?? 0m,
                    Unit: Text(line, "unit") ?? "",
                    UnitPrice: Amount(line, "unitPrice") ?? 0m,
                    LineExtensionAmount: Amount(line, "lineExtensionAmount") ?? 0m,
                    VatRate: Amount(line, "vatRate") ?? 0m,
                    VatAmount: Amount(line, "vatAmount") ?? 0m));
            }
        }

        var result = new ParsedInvoice(
            ProfileId: null,
            InvoiceNumber: invoiceNumber,
            IssueDate: issueDate,
            CurrencyCode: Text(json, "currencyCode") ?? "TRY",
            Supplier: new ParsedParty(supplierTax, VerifiedText("supplierName")),
            Customer: new ParsedParty(customerTax, VerifiedText("customerName")),
            Lines: lines,
            LineExtensionTotal: VerifiedAmount("lineExtensionTotal"),
            TaxExclusiveAmount: VerifiedAmount("taxExclusiveAmount"),
            TaxInclusiveAmount: VerifiedAmount("taxInclusiveAmount"),
            PayableAmount: VerifiedAmount("payableAmount"),
            VatTotal: VerifiedAmount("vatTotal") ?? 0m,
            WithholdingAmount: VerifiedAmount("withholdingAmount") ?? 0m,
            ParseSource: InvoiceParseSource.Ai,
            Problems: problems);

        // Zorunlu alanlar doğrulamadan geçemediyse AI okuması işe
        // yaramaz; yarım fatura döndürmek yerine null dönülür.
        return result.HasRequiredFields ? result : null;
    }

    /// <summary>
    /// Değer XML metninde geçiyor mu. Karşılaştırma boşluklara ve
    /// büyük/küçük harfe duyarsız; XML'de "ENDERUN A.S." yazarken
    /// modelin "Enderun A.S." demesi uydurma değildir.
    /// </summary>
    public static bool XmlContains(string xml, string value)
    {
        var needle = value.Trim();

        if (needle.Length == 0)
            return false;

        if (xml.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;

        // Boşluk farklarını yok sayarak ikinci deneme.
        var normalizedXml = RemoveWhitespace(xml);
        var normalizedNeedle = RemoveWhitespace(needle);

        return normalizedNeedle.Length > 0 &&
               normalizedXml.Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tutar XML'de geçiyor mu. Biçim farklarına tolerans: 57036.00 ile
    /// 57036 aynı sayıdır. Sıfır her zaman kabul edilir çünkü
    /// "yok" anlamına gelir.
    /// </summary>
    public static bool XmlContainsAmount(string xml, decimal value)
    {
        if (value == 0m)
            return true;

        var candidates = new[]
        {
            value.ToString(CultureInfo.InvariantCulture),
            value.ToString("0.##", CultureInfo.InvariantCulture),
            value.ToString("0.00", CultureInfo.InvariantCulture),
            value.ToString("0.0000", CultureInfo.InvariantCulture)
        };

        return candidates.Any(x => xml.Contains(x, StringComparison.Ordinal));
    }

    private static string RemoveWhitespace(string value) =>
        new(value.Where(x => !char.IsWhiteSpace(x)).ToArray());

    /// <summary>
    /// Modelin cevabından JSON gövdesini ayıklar; kod bloğu içinde
    /// veya açıklama arasında gelebilir.
    /// </summary>
    private static JsonElement? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static decimal? Amount(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
