using System.Globalization;
using ClosedXML.Excel;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>Dosyadaki bir sayfanın başlıkları ve örnek satırları.</summary>
public sealed record SpreadsheetInspection(
    IReadOnlyList<string> SheetNames,
    string SheetName,
    int HeaderRowIndex,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    int TotalRowCount);

/// <summary>
/// Hangi sütunun neye karşılık geldiği. Sütun numaraları 1 tabanlı;
/// kullanıcı eşleme ekranında seçer.
/// </summary>
public sealed record PositionImportMapping(
    string? SheetName,
    int HeaderRowIndex,
    int CodeColumn,
    int NameColumn,
    int UnitColumn,
    int PriceColumn,
    int? CategoryColumn = null,
    int? DescriptionColumn = null);

/// <summary>Ayrıştırılmış tek satır. Hatalıysa <see cref="Error"/> dolu.</summary>
public sealed record PositionImportRow(
    int RowNumber,
    string? Code,
    string? Name,
    string? Unit,
    decimal? UnitPrice,
    string? Category,
    string? Description,
    string? Error)
{
    public bool IsValid => Error is null;
}

public sealed record PositionImportParseResult(
    IReadOnlyList<PositionImportRow> Rows,
    IReadOnlyList<string> FileWarnings);

/// <summary>
/// Poz kitabı dosyalarını okur. Saf ve statik: ağ ve veritabanı yok,
/// sabit dosyayla test edilebilir.
///
/// Sütun düzeni VARSAYILMAZ. ÇŞB ve TEDAŞ kitapları farklı düzende ve
/// düzen yıldan yıla değişiyor; kullanıcı hangi sütunun ne olduğunu
/// eşleme ekranında söyler. Böylece dosyayı görmeden çalışan bir
/// aktarım elde edilir.
///
/// Sayı ayrıştırmada iki biçim de denenir: Excel hücresi sayısalsa
/// doğrudan okunur, metinse önce nokta ondalıklı (1234.56) sonra virgül
/// ondalıklı (1.234,56) yorumlanır. Yanlış yorum fiyatı bin kat
/// şişireceği için belirsiz kalan satır hata olarak işaretlenir.
/// </summary>
public static class PositionImportParser
{
    private const int MaxSampleRows = 8;

    /// <summary>
    /// Dosyayı açar, sayfa adlarını ve seçilen sayfanın başlık satırını
    /// döner. Başlık satırı verilmezse en çok dolu hücresi olan ilk
    /// satır başlık kabul edilir.
    /// </summary>
    public static SpreadsheetInspection Inspect(
        Stream stream, string? sheetName = null, int? headerRowIndex = null)
    {
        using var workbook = new XLWorkbook(stream);

        var sheetNames = workbook.Worksheets.Select(x => x.Name).ToList();

        if (sheetNames.Count == 0)
            throw new InvalidOperationException("Dosyada okunabilir sayfa yok.");

        var sheet = sheetName is not null
            ? workbook.Worksheets.FirstOrDefault(x => x.Name == sheetName)
              ?? workbook.Worksheet(1)
            : workbook.Worksheet(1);

        var used = sheet.RangeUsed();

        if (used is null)
        {
            return new SpreadsheetInspection(
                sheetNames, sheet.Name, 1, [], [], 0);
        }

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var firstColumn = used.FirstColumn().ColumnNumber();
        var lastColumn = used.LastColumn().ColumnNumber();

        var header = headerRowIndex ?? DetectHeaderRow(sheet, firstRow, lastRow, firstColumn, lastColumn);

        var headers = new List<string>();
        for (var column = firstColumn; column <= lastColumn; column++)
            headers.Add(sheet.Cell(header, column).GetFormattedString().Trim());

        var samples = new List<IReadOnlyList<string>>();
        for (var row = header + 1; row <= lastRow && samples.Count < MaxSampleRows; row++)
        {
            var values = new List<string>();
            for (var column = firstColumn; column <= lastColumn; column++)
                values.Add(sheet.Cell(row, column).GetFormattedString().Trim());

            if (values.All(string.IsNullOrWhiteSpace))
                continue;

            samples.Add(values);
        }

        return new SpreadsheetInspection(
            sheetNames,
            sheet.Name,
            header,
            headers,
            samples,
            Math.Max(0, lastRow - header));
    }

    public static PositionImportParseResult Parse(Stream stream, PositionImportMapping mapping)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = mapping.SheetName is not null
            ? workbook.Worksheets.FirstOrDefault(x => x.Name == mapping.SheetName)
              ?? workbook.Worksheet(1)
            : workbook.Worksheet(1);

        var used = sheet.RangeUsed();
        var rows = new List<PositionImportRow>();
        var warnings = new List<string>();

        if (used is null)
            return new PositionImportParseResult(rows, ["Sayfada veri yok."]);

        var lastRow = used.LastRow().RowNumber();
        var seenCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = mapping.HeaderRowIndex + 1; rowNumber <= lastRow; rowNumber++)
        {
            var code = Text(sheet, rowNumber, mapping.CodeColumn);
            var name = Text(sheet, rowNumber, mapping.NameColumn);
            var unit = Text(sheet, rowNumber, mapping.UnitColumn);
            var priceText = Text(sheet, rowNumber, mapping.PriceColumn);
            var category = mapping.CategoryColumn is { } c ? Text(sheet, rowNumber, c) : null;
            var description = mapping.DescriptionColumn is { } d ? Text(sheet, rowNumber, d) : null;

            // Tümüyle boş satır: kitapların arasında ayraç olarak sık
            // görülür, hata sayılmaz, sessizce atlanır.
            if (string.IsNullOrWhiteSpace(code)
                && string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(priceText))
            {
                continue;
            }

            string? error = null;
            decimal? price = null;

            if (string.IsNullOrWhiteSpace(code))
            {
                error = "Poz numarası boş.";
            }
            else if (string.IsNullOrWhiteSpace(name))
            {
                error = "Poz tanımı boş.";
            }
            else if (seenCodes.TryGetValue(code.Trim(), out var firstSeen))
            {
                error = $"Poz numarası dosyada tekrar ediyor (ilk görüldüğü satır {firstSeen}).";
            }
            else
            {
                var parsed = ParseNumber(sheet.Cell(rowNumber, mapping.PriceColumn), priceText);

                if (parsed is null)
                {
                    error = string.IsNullOrWhiteSpace(priceText)
                        ? "Birim fiyat boş."
                        : $"Birim fiyat sayıya çevrilemedi: \"{priceText}\".";
                }
                else if (parsed <= 0)
                {
                    error = "Birim fiyat sıfır veya negatif.";
                }
                else
                {
                    price = parsed;
                    seenCodes[code.Trim()] = rowNumber;
                }
            }

            rows.Add(new PositionImportRow(
                rowNumber,
                code?.Trim(),
                name?.Trim(),
                string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
                price,
                string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                error));
        }

        if (rows.Count == 0)
            warnings.Add("Başlık satırından sonra hiç veri satırı bulunamadı.");

        var missingUnits = rows.Count(x => x.IsValid && x.Unit is null);
        if (missingUnits > 0)
        {
            warnings.Add(
                $"{missingUnits} satırda birim boş; bu satırlarda birim \"AD\" olarak yazılacak.");
        }

        return new PositionImportParseResult(rows, warnings);
    }

    /// <summary>
    /// Başlık satırı tahmini: ilk 20 satır içinde en çok dolu hücresi
    /// olan satır. Kullanıcı eşleme ekranında değiştirebilir.
    /// </summary>
    private static int DetectHeaderRow(
        IXLWorksheet sheet, int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        var bestRow = firstRow;
        var bestCount = -1;
        var limit = Math.Min(lastRow, firstRow + 19);

        for (var row = firstRow; row <= limit; row++)
        {
            var filled = 0;

            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (!string.IsNullOrWhiteSpace(sheet.Cell(row, column).GetFormattedString()))
                    filled++;
            }

            if (filled > bestCount)
            {
                bestCount = filled;
                bestRow = row;
            }
        }

        return bestRow;
    }

    private static string? Text(IXLWorksheet sheet, int row, int column)
    {
        if (column <= 0)
            return null;

        var value = sheet.Cell(row, column).GetFormattedString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Sayı okuma. Hücre sayısalsa doğrudan alınır — biçimlendirme
    /// tuzağına hiç girilmez. Metinse iki ondalık geleneği de denenir,
    /// ikisi farklı sonuç veriyorsa belirsizlik hata sayılır: 1.234'ü
    /// bin iki yüz otuz dört mü bir virgül iki üç dört mü diye
    /// tahmin etmek, fiyatı bin kat şişirebilir.
    /// </summary>
    internal static decimal? ParseNumber(IXLCell cell, string? text)
    {
        if (cell.DataType == XLDataType.Number)
            return Convert.ToDecimal(cell.GetDouble());

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return ParseNumericText(text);
    }

    /// <summary>Metinden sayı okur; ayrıştırıcıdan bağımsız test edilebilsin diye ayrı.</summary>
    public static decimal? ParseNumericText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = text.Trim()
            .Replace("₺", string.Empty)
            .Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty);

        if (cleaned.Length == 0)
            return null;

        var hasComma = cleaned.Contains(',');
        var hasDot = cleaned.Contains('.');

        // İkisi de varsa: sonda olan ondalık ayırıcıdır.
        if (hasComma && hasDot)
        {
            var decimalSeparator = cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.') ? ',' : '.';
            var groupSeparator = decimalSeparator == ',' ? '.' : ',';

            cleaned = cleaned.Replace(groupSeparator.ToString(), string.Empty);
            cleaned = cleaned.Replace(decimalSeparator, '.');

            return decimal.TryParse(
                cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var both)
                ? both
                : null;
        }

        // Yalnız virgül: Türkçe ondalık.
        if (hasComma)
        {
            cleaned = cleaned.Replace(',', '.');

            return decimal.TryParse(
                cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var comma)
                ? comma
                : null;
        }

        // Yalnız nokta: ondalık mı binlik mi belirsiz olabilir.
        // "1.234" gibi tam üç haneli bir kuyruk binlik ayırıcı da
        // olabilir; belirsizliği tahminle kapatmıyoruz.
        if (hasDot)
        {
            var parts = cleaned.Split('.');

            if (parts.Length > 2)
                return null;

            if (parts.Length == 2 && parts[1].Length == 3 && parts[0].Length <= 3)
                return null;

            return decimal.TryParse(
                cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var dot)
                ? dot
                : null;
        }

        return decimal.TryParse(
            cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain)
            ? plain
            : null;
    }
}
