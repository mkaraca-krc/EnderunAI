using ClosedXML.Excel;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>Okunan bir icmal satırı — kısım başlığı ya da poz.</summary>
/// <param name="RowNumber">Excel'deki satır numarası; hatayı kullanıcı
/// kendi dosyasında bulabilsin diye taşınıyor.</param>
public sealed record ContractSummaryParsedLine(
    int RowNumber,
    bool IsSectionHeader,
    string? SectionName,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal MaterialUnitPrice,
    decimal LaborUnitPrice,
    decimal OverheadUnitPrice,
    /// <summary>
    /// Kısmın altındaki alt grup adı (örn. "SOSYAL TESİS"). Kısım
    /// hiyerarşisi tek seviyeli olduğu için alt grup ayrı kısım açmaz;
    /// adı burada saklanır ki bilgi kaybolmasın.
    /// </summary>
    string? Category = null)
{
    public decimal UnitPrice =>
        MaterialUnitPrice + LaborUnitPrice + OverheadUnitPrice;

    public decimal TotalAmount =>
        decimal.Round(ContractQuantity * UnitPrice, 2);
}

/// <summary>Okunamayan satır ve nedeni.</summary>
public sealed record ContractSummaryParseError(int RowNumber, string Message);

public sealed record ContractSummaryParseResult(
    IReadOnlyList<ContractSummaryParsedLine> Lines,
    IReadOnlyList<ContractSummaryParseError> Errors)
{
    public int SectionCount => Lines.Count(x => x.IsSectionHeader);
    public int ItemCount => Lines.Count(x => !x.IsSectionHeader);
    public decimal TotalAmount => Lines.Where(x => !x.IsSectionHeader).Sum(x => x.TotalAmount);
}

/// <summary>
/// Sözleşme icmali Excel şablonunun okuyucusu.
///
/// Saf ve veritabanısız: dosyayı satırlara çevirir, hiçbir şey yazmaz.
/// Yazma kararı önizlemeden sonra kullanıcının.
///
/// KISIM BAŞLIĞI iki şekilde tanınır:
///   1. "Kısım" kolonu dolu, poz no boş → başlık satırı
///   2. Poz no "#" ile başlıyor → başlık satırı (elle yazanlar için)
/// Bir başlıktan sonraki poz satırları o kısma bağlanır.
///
/// HATA İZOLASYONU: bozuk satır tüm dosyayı reddettirmez; o satır
/// hataya yazılır, kalanlar okunmaya devam eder. Yarım bir icmali
/// sessizce yazmamak için karar yine kullanıcıya bırakılır.
/// </summary>
public static class ContractSummaryExcelParser
{
    // Şablon kolon sırası. Şablonu üreten ve okuyan tek yer burası
    // olsun diye sabitler burada duruyor.
    public const int ColSection = 1;
    public const int ColPositionCode = 2;
    public const int ColDescription = 3;
    public const int ColUnit = 4;
    public const int ColQuantity = 5;
    public const int ColMaterial = 6;
    public const int ColLabor = 7;
    public const int ColOverhead = 8;

    /// <summary>Başlık satırı sayısı — veri bundan sonra başlar.</summary>
    public const int HeaderRowCount = 1;

    public static ContractSummaryParseResult Parse(Stream stream)
    {
        var lines = new List<ContractSummaryParsedLine>();
        var errors = new List<ContractSummaryParseError>();

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        string? currentSection = null;

        for (var row = HeaderRowCount + 1; row <= lastRow; row++)
        {
            var sectionCell = Text(sheet, row, ColSection);
            var positionCode = Text(sheet, row, ColPositionCode);
            var description = Text(sheet, row, ColDescription);

            // Tamamen boş satır: şablonda ayraç olarak kullanılabilir.
            if (sectionCell.Length == 0 && positionCode.Length == 0 &&
                description.Length == 0)
            {
                continue;
            }

            // --- Kısım başlığı ---
            var isHeaderByColumn = sectionCell.Length > 0 && positionCode.Length == 0;
            var isHeaderByMark = positionCode.StartsWith('#');

            if (isHeaderByColumn || isHeaderByMark)
            {
                var name = isHeaderByColumn
                    ? sectionCell
                    : (description.Length > 0
                        ? description
                        : positionCode.TrimStart('#').Trim());

                if (name.Length == 0)
                {
                    errors.Add(new ContractSummaryParseError(
                        row, "Kısım başlığının adı boş."));
                    continue;
                }

                currentSection = name;

                lines.Add(new ContractSummaryParsedLine(
                    row, true, name, string.Empty, string.Empty, string.Empty,
                    0m, 0m, 0m, 0m));

                continue;
            }

            // --- Poz satırı ---
            if (description.Length == 0)
            {
                errors.Add(new ContractSummaryParseError(row, "Tanım boş."));
                continue;
            }

            var unit = Text(sheet, row, ColUnit);
            if (unit.Length == 0)
            {
                errors.Add(new ContractSummaryParseError(row, "Birim boş."));
                continue;
            }

            if (!TryNumber(sheet, row, ColQuantity, out var quantity))
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Sözleşme miktarı sayı değil."));
                continue;
            }

            if (quantity < 0m)
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Sözleşme miktarı negatif olamaz."));
                continue;
            }

            if (!TryNumber(sheet, row, ColMaterial, out var material) ||
                !TryNumber(sheet, row, ColLabor, out var labor) ||
                !TryNumber(sheet, row, ColOverhead, out var overhead))
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Birim fiyat kolonlarından biri sayı değil."));
                continue;
            }

            if (material < 0m || labor < 0m || overhead < 0m)
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Birim fiyat negatif olamaz."));
                continue;
            }

            lines.Add(new ContractSummaryParsedLine(
                row, false, currentSection,
                positionCode, description, unit,
                quantity, material, labor, overhead));
        }

        return new ContractSummaryParseResult(lines, errors);
    }

    /// <summary>Doldurulacak boş şablon; bir örnek satırla birlikte.</summary>
    public static byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sözleşme İcmali");

        var headers = new[]
        {
            "Kısım", "Poz No", "Tanım", "Birim", "Sözleşme Miktarı",
            "Malzeme B.F.", "Montaj B.F.", "GG&K B.F."
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#18797C");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Örnek: bir kısım başlığı ve altında iki poz.
        sheet.Cell(2, ColSection).Value = "Panolar / Tablolar";

        sheet.Cell(3, ColPositionCode).Value = "P.01";
        sheet.Cell(3, ColDescription).Value = "Ana dağıtım panosu montajı";
        sheet.Cell(3, ColUnit).Value = "Adet";
        sheet.Cell(3, ColQuantity).Value = 4;
        sheet.Cell(3, ColMaterial).Value = 18500;
        sheet.Cell(3, ColLabor).Value = 4200;
        sheet.Cell(3, ColOverhead).Value = 2300;

        sheet.Cell(4, ColPositionCode).Value = "P.02";
        sheet.Cell(4, ColDescription).Value = "Kat panosu montajı";
        sheet.Cell(4, ColUnit).Value = "Adet";
        sheet.Cell(4, ColQuantity).Value = 12;
        sheet.Cell(4, ColMaterial).Value = 6400;
        sheet.Cell(4, ColLabor).Value = 1500;
        sheet.Cell(4, ColOverhead).Value = 800;

        sheet.Cell(6, ColSection).Value = "Kablo Tava";

        sheet.Cell(7, ColPositionCode).Value = "KT.01";
        sheet.Cell(7, ColDescription).Value = "200 mm galvaniz kablo tavası";
        sheet.Cell(7, ColUnit).Value = "Metre";
        sheet.Cell(7, ColQuantity).Value = 850;
        sheet.Cell(7, ColMaterial).Value = 320;
        sheet.Cell(7, ColLabor).Value = 95;
        sheet.Cell(7, ColOverhead).Value = 45;

        sheet.Columns(1, headers.Length).AdjustToContents();

        // Açıklama AYRI SAYFADA: okuyucu yalnızca ilk sayfayı okuyor ve
        // "Kısım" kolonundaki her dolu hücreyi kısım başlığı sayıyor.
        // Notu veri sayfasına yazmak, onu sahte bir kısım olarak
        // içe aktarırdı.
        var help = workbook.Worksheets.Add("Açıklama");

        var notes = new[]
        {
            "SÖZLEŞME İCMALİ ŞABLONU",
            "",
            "Kısım başlığı: 'Kısım' kolonunu doldurun, 'Poz No' hücresini boş bırakın.",
            "Alternatif olarak Poz No hücresine '# Kısım Adı' yazabilirsiniz.",
            "Bir başlıktan sonraki poz satırları o kısma bağlanır.",
            "",
            "Tek birim fiyatla çalışıyorsanız tutarı 'Malzeme B.F.' kolonuna yazıp",
            "diğer iki kolonu boş bırakın; toplam değişmez.",
            "",
            "Sayı biçimi: hem 1.234,56 hem 1234.56 okunur.",
            "Boş fiyat hücresi sıfır kabul edilir.",
            "",
            "Bu sayfa okunmaz; yalnızca ilk sayfadaki veriler içe aktarılır."
        };

        for (var index = 0; index < notes.Length; index++)
            help.Cell(index + 1, 1).Value = notes[index];

        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).AdjustToContents();

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetString().Trim();

    /// <summary>
    /// Hücreyi sayıya çevirir. Boş hücre 0 sayılır — fiyat kolonlarında
    /// boş bırakmak yaygın ve "sıfır" demektir. Metin hücrede hem nokta
    /// hem virgül ondalık ayracı kabul edilir; Excel'den kopyalanan
    /// veride ikisi de görülür.
    /// </summary>
    private static bool TryNumber(
        IXLWorksheet sheet, int row, int column, out decimal value)
    {
        value = 0m;

        var cell = sheet.Cell(row, column);

        if (cell.IsEmpty())
            return true;

        if (cell.DataType == XLDataType.Number)
        {
            value = (decimal)cell.GetDouble();
            return true;
        }

        var text = cell.GetString().Trim();

        if (text.Length == 0)
            return true;

        text = text.Replace(" ", string.Empty);

        // Virgül varsa Türkçe biçim kabul edilir: nokta binlik ayracıdır,
        // virgül ondalıktır ("1.234,56"). Virgül yoksa nokta ondalık
        // sayılır ("1234.56"). Noktayı koşulsuz silmek "1234.56" değerini
        // 123456 yapardı.
        text = text.Contains(',')
            ? text.Replace(".", string.Empty).Replace(',', '.')
            : text;

        return decimal.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
