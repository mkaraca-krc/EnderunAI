using ClosedXML.Excel;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>Bir pozun tek bir bileşen fiyatı.</summary>
public sealed record ParsedPositionPrice(
    PositionPriceComponent Component,
    decimal UnitPrice);

/// <summary>
/// Profil ayrıştırıcılarının ürettiği satır. Çok bileşenli fiyatı
/// olduğu için genel <see cref="PositionImportRow"/>'dan ayrı.
/// </summary>
public sealed record ParsedBookRow(
    int SourceLine,
    string Code,
    string Name,
    string? Unit,
    IReadOnlyList<ParsedPositionPrice> Prices,
    string? Category,
    string? Note,
    /// <summary>Birim satırda yazmıyordu, üst tanım pozundan alındı.</summary>
    bool UnitInherited = false);

public sealed record BookParseResult(
    IReadOnlyList<ParsedBookRow> Rows,
    /// <summary>Poz değil, kategori başlığı olan satırlar.</summary>
    int GroupHeaderCount,
    /// <summary>Poz gibi görünen ama fiyatı okunamayan satırlar.</summary>
    IReadOnlyList<string> SuspiciousLines,
    IReadOnlyList<string> Warnings);

/// <summary>
/// TEDAŞ Birim Fiyat Kitabı (Excel) ayrıştırıcısı.
///
/// Kitabın yapısı sabit ve belgelenmiş:
/// A = eski poz no, B/C/D = yeni poz no'nun üç parçası (TEDAŞ/Ana/Alt),
/// E = malzeme veya işin cinsi, F = ölçü, G = malzeme, H = montaj,
/// I = demontaj, J = demontajdan montaj, K = açıklama.
///
/// Poz numarası B.C.D birleştirilerek üretilir: 85 + 105 + 1201 →
/// "85.105.1201". Hücreler Excel'de ondalıklı sayı olarak duruyor
/// (85.0 gibi), o yüzden tam sayıya indirgeniyor.
///
/// GRUP BAŞLIKLARI poz değildir: alt numarası yüzün katı olan ve dört
/// fiyat kolonu da boş olan satırlar kategori başlığıdır (DİREKLER,
/// AĞAÇ DİREKLER...). Bunlar atlanır ama sayılır; ayrıca sonraki
/// pozlara kategori olarak taşınır.
/// </summary>
public static class TedasBfkParser
{
    public const string ProfileKey = "TEDAS_BFK_EXCEL";

    private const int ColumnOldCode = 1;
    private const int ColumnTedas = 2;
    private const int ColumnMain = 3;
    private const int ColumnSub = 4;
    private const int ColumnName = 5;
    private const int ColumnUnit = 6;
    private const int ColumnMaterial = 7;
    private const int ColumnLabor = 8;
    private const int ColumnDismantle = 9;
    private const int ColumnRemount = 10;
    private const int ColumnNote = 11;

    /// <summary>Veri satırlarının başladığı ilk satır (1-3 arası başlık).</summary>
    private const int FirstDataRow = 4;

    public static BookParseResult Parse(Stream stream, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = sheetName is not null
            ? workbook.Worksheets.FirstOrDefault(x => x.Name == sheetName) ?? workbook.Worksheet(1)
            : workbook.Worksheets.FirstOrDefault(x => x.Name.Contains("BF")) ?? workbook.Worksheet(1);

        var used = sheet.RangeUsed();
        var rows = new List<ParsedBookRow>();
        var suspicious = new List<string>();
        var warnings = new List<string>();
        var groupHeaders = 0;

        if (used is null)
            return new BookParseResult(rows, 0, suspicious, ["Sayfada veri yok."]);

        var lastRow = used.LastRow().RowNumber();
        string? currentCategory = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = FirstDataRow; rowNumber <= lastRow; rowNumber++)
        {
            var tedas = ReadInteger(sheet, rowNumber, ColumnTedas);
            var main = ReadInteger(sheet, rowNumber, ColumnMain);
            var sub = ReadInteger(sheet, rowNumber, ColumnSub);
            var name = ReadText(sheet, rowNumber, ColumnName);

            if (tedas is null || main is null || sub is null)
            {
                // Poz numarası yoksa satır ya boş ya da serbest metin.
                if (!string.IsNullOrWhiteSpace(name))
                    suspicious.Add($"Satır {rowNumber}: poz numarası eksik — \"{Shorten(name)}\"");

                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                suspicious.Add($"Satır {rowNumber}: {tedas}.{main}.{sub} için tanım boş.");
                continue;
            }

            var prices = new List<ParsedPositionPrice>();
            AddPrice(prices, PositionPriceComponent.Material, sheet, rowNumber, ColumnMaterial);
            AddPrice(prices, PositionPriceComponent.Labor, sheet, rowNumber, ColumnLabor);
            AddPrice(prices, PositionPriceComponent.Dismantle, sheet, rowNumber, ColumnDismantle);
            AddPrice(
                prices, PositionPriceComponent.RemountFromDismantled,
                sheet, rowNumber, ColumnRemount);

            if (prices.Count == 0)
            {
                // Fiyatsız satır. Alt numarası yüzün katıysa kategori
                // başlığıdır; değilse okunamamış bir poz olabilir.
                if (sub % 100 == 0)
                {
                    currentCategory = name.Trim();
                    groupHeaders++;
                }
                else
                {
                    suspicious.Add(
                        $"Satır {rowNumber}: {tedas}.{main}.{sub} — hiçbir fiyat kolonu dolu değil " +
                        $"(\"{Shorten(name)}\")");
                }

                continue;
            }

            var code = $"{tedas}.{main}.{sub}";

            if (!seen.Add(code))
            {
                suspicious.Add($"Satır {rowNumber}: {code} dosyada tekrar ediyor.");
                continue;
            }

            rows.Add(new ParsedBookRow(
                rowNumber,
                code,
                name.Trim(),
                ReadText(sheet, rowNumber, ColumnUnit)?.Trim(),
                prices,
                currentCategory,
                BuildNote(
                    ReadText(sheet, rowNumber, ColumnOldCode),
                    ReadText(sheet, rowNumber, ColumnNote))));
        }

        if (rows.Count == 0)
            warnings.Add("Hiç poz satırı okunamadı; sayfa veya sürüm beklenenden farklı olabilir.");

        return new BookParseResult(rows, groupHeaders, suspicious, warnings);
    }

    private static void AddPrice(
        List<ParsedPositionPrice> target,
        PositionPriceComponent component,
        IXLWorksheet sheet,
        int row,
        int column)
    {
        var cell = sheet.Cell(row, column);

        var value = cell.DataType == XLDataType.Number
            ? Convert.ToDecimal(cell.GetDouble())
            : PositionImportParser.ParseNumericText(cell.GetFormattedString());

        if (value is > 0)
            target.Add(new ParsedPositionPrice(component, decimal.Round(value.Value, 4)));
    }

    /// <summary>
    /// Poz parçaları Excel'de ondalıklı geliyor (85.0). Tam sayıya
    /// indirgenirken kesirli bir değer çıkarsa satır güvenilmezdir.
    /// </summary>
    private static int? ReadInteger(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);

        if (cell.DataType == XLDataType.Number)
        {
            var raw = cell.GetDouble();

            return Math.Abs(raw - Math.Round(raw)) < 0.0001
                ? (int)Math.Round(raw)
                : null;
        }

        var text = cell.GetFormattedString().Trim();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parsed = PositionImportParser.ParseNumericText(text);

        return parsed is not null && parsed == decimal.Truncate(parsed.Value)
            ? (int)parsed.Value
            : null;
    }

    private static string? ReadText(IXLWorksheet sheet, int row, int column)
    {
        var value = sheet.Cell(row, column).GetFormattedString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? BuildNote(string? oldCode, string? note)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(oldCode))
            parts.Add($"Eski poz: {oldCode.Trim()}");

        if (!string.IsNullOrWhiteSpace(note))
            parts.Add(note.Trim());

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Shorten(string value) =>
        value.Length <= 60 ? value.Trim() : value.Trim()[..60] + "...";
}
