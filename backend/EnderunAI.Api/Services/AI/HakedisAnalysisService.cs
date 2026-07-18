using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EnderunAI.Api.Contracts;
using UglyToad.PdfPig;

namespace EnderunAI.Api.Services.AI;

public sealed class HakedisAnalysisService
    : IHakedisAnalysisService
{
    public Task<HakedisAnalysisResult> AnalyzeAsync(
        string fullPath,
        string originalFileName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Analiz edilecek dosya bulunamadı.",
                fullPath
            );
        }

        var extension =
            Path.GetExtension(fullPath).ToLowerInvariant();

        var extractedText = extension switch
        {
            ".pdf" => ExtractPdfText(fullPath),
            ".xlsx" => ExtractExcelText(fullPath),
            ".xls" => throw new InvalidOperationException(
                "Eski .xls formatı henüz desteklenmiyor. " +
                "Dosyayı .xlsx olarak kaydedin."
            ),
            ".csv" => File.ReadAllText(fullPath),
            _ => throw new InvalidOperationException(
                "Bu dosya türü analiz edilemiyor."
            ),
        };

        var normalizedText =
            NormalizeText(extractedText);

        var result = new HakedisAnalysisResult
        {
            FileName = originalFileName,
            ExtractedText = extractedText,
            RequiresOcr =
                extension == ".pdf" &&
                normalizedText.Length < 50,
        };

        if (result.RequiresOcr)
        {
            result.Status = "ocr_required";
            result.Confidence = 0;
            result.Warnings.Add(
                "PDF içinde okunabilir metin bulunamadı. " +
                "Belge taranmış olabilir ve OCR gerektiriyor."
            );

            return Task.FromResult(result);
        }

        result.Project = FindValue(
            normalizedText,
            [
                @"proje\s*(?:adı)?\s*[:\-]\s*(.+)",
                @"işin\s*adı\s*[:\-]\s*(.+)",
            ]
        );

        result.Employer = FindValue(
            normalizedText,
            [
                @"işveren\s*[:\-]\s*(.+)",
                @"idare\s*[:\-]\s*(.+)",
                @"yüklenici\s*idare\s*[:\-]\s*(.+)",
            ]
        );

        result.ProgressPaymentNo = FindValue(
            normalizedText,
            [
                @"hakediş\s*(?:raporu)?\s*no\s*[:\-]?\s*(\d+)",
                @"hakediş\s*no\s*[:\-]?\s*(\d+)",
                @"(\d+)\s*nolu\s*hakediş",
            ],
            singleLine: true
        );

        result.Period = FindValue(
            normalizedText,
            [
                @"dönem\s*[:\-]\s*(.+)",
                @"hakediş\s*dönemi\s*[:\-]\s*(.+)",
            ]
        );

        result.AmountExcludingVat = FindMoney(
            normalizedText,
            [
                @"kdv\s*hariç\s*(?:hakediş\s*)?(?:bedeli|tutarı)\s*[:\-]?\s*([\d\.,]+)",
                @"hakediş\s*bedeli\s*[:\-]?\s*([\d\.,]+)",
                @"yapılan\s*iş\s*bedeli\s*[:\-]?\s*([\d\.,]+)",
                @"ara\s*toplam\s*[:\-]?\s*([\d\.,]+)",
            ]
        );

        result.VatRate = FindPercentage(
            normalizedText,
            [
                @"kdv\s*oranı\s*[:\-]?\s*%?\s*([\d\.,]+)",
                @"%\s*([\d\.,]+)\s*kdv",
                @"kdv\s*%\s*([\d\.,]+)",
            ]
        );

        result.VatAmount = FindMoney(
            normalizedText,
            [
                @"hesaplanan\s*kdv\s*[:\-]?\s*([\d\.,]+)",
                @"kdv\s*tutarı\s*[:\-]?\s*([\d\.,]+)",
                @"kdv\s*[:\-]?\s*([\d\.,]+)",
            ]
        );

        if (
            result.VatAmount is null &&
            result.AmountExcludingVat is not null &&
            result.VatRate is not null
        )
        {
            result.VatAmount =
                result.AmountExcludingVat.Value *
                result.VatRate.Value /
                100m;
        }

        result.SuggestedWithholding =
            SuggestWithholding(normalizedText);

        BuildWarnings(result);

        var populatedFields = new object?[]
        {
            result.Project,
            result.Employer,
            result.ProgressPaymentNo,
            result.Period,
            result.AmountExcludingVat,
            result.VatRate,
            result.VatAmount,
            result.SuggestedWithholding,
        }.Count(value => value is not null);

        result.Confidence =
            Math.Round(populatedFields / 8d, 2);

        return Task.FromResult(result);
    }

    private static string ExtractPdfText(string fullPath)
    {
        var builder = new StringBuilder();

        using var document = PdfDocument.Open(fullPath);

        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static string ExtractExcelText(string fullPath)
    {
        var builder = new StringBuilder();

        using var workbook = new XLWorkbook(fullPath);

        foreach (var worksheet in workbook.Worksheets)
        {
            builder.AppendLine(
                $"ÇALIŞMA SAYFASI: {worksheet.Name}"
            );

            var range = worksheet.RangeUsed();

            if (range is null)
            {
                continue;
            }

            foreach (var row in range.RowsUsed())
            {
                var values = row
                    .CellsUsed()
                    .Select(cell =>
                        cell.GetFormattedString().Trim()
                    )
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value)
                    );

                builder.AppendLine(
                    string.Join(" | ", values)
                );
            }
        }

        return builder.ToString();
    }

    private static string NormalizeText(string text)
    {
        return Regex.Replace(
            text.Replace("\r", "\n"),
            @"[ \t]+",
            " "
        ).Trim();
    }

    private static string? FindValue(
        string text,
        IEnumerable<string> patterns,
        bool singleLine = false
    )
    {
        var options =
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant;

        if (!singleLine)
        {
            options |= RegexOptions.Multiline;
        }

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                text,
                pattern,
                options
            );

            if (!match.Success)
            {
                continue;
            }

            return match.Groups[1]
                .Value
                .Trim()
                .TrimEnd('|', ';');
        }

        return null;
    }

    private static decimal? FindMoney(
        string text,
        IEnumerable<string> patterns
    )
    {
        var value = FindValue(
            text,
            patterns,
            singleLine: true
        );

        return ParseTurkishDecimal(value);
    }

    private static decimal? FindPercentage(
        string text,
        IEnumerable<string> patterns
    )
    {
        var value = FindValue(
            text,
            patterns,
            singleLine: true
        );

        return ParseTurkishDecimal(value);
    }

    private static decimal? ParseTurkishDecimal(
        string? value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = Regex.Replace(
            value,
            @"[^\d,.\-]",
            ""
        );

        if (
            decimal.TryParse(
                cleaned,
                NumberStyles.Number |
                NumberStyles.AllowLeadingSign,
                CultureInfo.GetCultureInfo("tr-TR"),
                out var turkishValue
            )
        )
        {
            return turkishValue;
        }

        if (
            decimal.TryParse(
                cleaned,
                NumberStyles.Number |
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var invariantValue
            )
        )
        {
            return invariantValue;
        }

        return null;
    }

    private static string SuggestWithholding(string text)
    {
        var lower = text.ToLowerInvariant();

        if (
            lower.Contains("yapım işi") ||
            lower.Contains("elektrik taahhüt") ||
            lower.Contains("inşaat") ||
            lower.Contains("montaj")
        )
        {
            return "4/10";
        }

        if (
            lower.Contains("proje hizmeti") ||
            lower.Contains("danışmanlık") ||
            lower.Contains("etüt") ||
            lower.Contains("müşavirlik")
        )
        {
            return "9/10";
        }

        if (
            lower.Contains("bakım") ||
            lower.Contains("onarım")
        )
        {
            return "7/10";
        }

        return "Manuel kontrol gerekli";
    }

    private static void BuildWarnings(
        HakedisAnalysisResult result
    )
    {
        if (result.Project is null)
        {
            result.Warnings.Add(
                "Proje adı otomatik bulunamadı."
            );
        }

        if (result.Employer is null)
        {
            result.Warnings.Add(
                "İşveren bilgisi otomatik bulunamadı."
            );
        }

        if (result.ProgressPaymentNo is null)
        {
            result.Warnings.Add(
                "Hakediş numarası otomatik bulunamadı."
            );
        }

        if (result.AmountExcludingVat is null)
        {
            result.Warnings.Add(
                "KDV hariç hakediş bedeli bulunamadı."
            );
        }

        if (result.VatRate is null)
        {
            result.Warnings.Add(
                "KDV oranı bulunamadı."
            );
        }

        result.Warnings.Add(
            "Tevkifat önerisi belge içeriğine göre " +
            "üretilmiştir; güncel mevzuat ve sözleşme " +
            "mali müşavir tarafından doğrulanmalıdır."
        );
    }
}