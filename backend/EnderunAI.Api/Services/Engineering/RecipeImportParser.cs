using ClosedXML.Excel;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>
/// Reçete dosyasında hangi sütunun ne olduğu. Sütun numaraları 1
/// tabanlı; kullanıcı eşleme ekranında seçer.
///
/// Sütun düzeni VARSAYILMAZ — poz kitabı aktarımındaki gerekçenin
/// aynısı: her firmanın reçete tablosu farklı düzende ve düzen yıldan
/// yıla değişiyor.
/// </summary>
public sealed record RecipeImportMapping(
    string? SheetName,
    int HeaderRowIndex,
    int PositionCodeColumn,
    int MaterialNameColumn,
    int QuantityColumn,
    int UnitColumn,
    int? MaterialCodeColumn = null,
    int? WastePercentColumn = null,
    int? NotesColumn = null);

/// <summary>Ayrıştırılmış tek satır. Hatalıysa <see cref="Error"/> dolu.</summary>
public sealed record RecipeImportRow(
    int RowNumber,
    string? PositionCode,
    string? MaterialCode,
    string? MaterialName,
    decimal? Quantity,
    string? Unit,
    decimal WastePercent,
    string? Notes,
    string? Error,
    /// <summary>
    /// Poz kodu bu satırda YAZMIYORDU, üstteki satırdan devralındı.
    /// Önizlemede ayrıca gösterilir: devralma sessiz kalırsa, araya
    /// giren boş bir blok yüzünden malzeme yanlış poza yazılabilir.
    /// </summary>
    bool PositionCodeInherited)
{
    public bool IsValid => Error is null;
}

public sealed record RecipeImportParseResult(
    IReadOnlyList<RecipeImportRow> Rows,
    IReadOnlyList<string> FileWarnings);

/// <summary>
/// Reçete dosyalarını okur. Saf ve statik: ağ ve veritabanı yok, sabit
/// dosyayla test edilebilir.
///
/// Dosyayı İNCELEME işi burada TEKRAR YAZILMADI: sayfa adları, başlık
/// satırı tahmini ve örnek satırlar için <see cref="PositionImportParser.Inspect"/>
/// kullanılır — aynı iş iki yerde durursa biri düzeltilip diğeri
/// unutulur. Sayı ayrıştırma da aynı gerekçeyle
/// <see cref="PositionImportParser.ParseNumericText"/> üzerinden gider;
/// virgül/nokta ondalık kuralı tek yerde.
/// </summary>
public static class RecipeImportParser
{
    public static RecipeImportParseResult Parse(
        Stream stream, RecipeImportMapping mapping)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = mapping.SheetName is not null
            ? workbook.Worksheets.FirstOrDefault(x => x.Name == mapping.SheetName)
              ?? workbook.Worksheet(1)
            : workbook.Worksheet(1);

        var used = sheet.RangeUsed();
        var warnings = new List<string>();
        var rows = new List<RecipeImportRow>();

        if (used is null)
        {
            warnings.Add("Seçilen sayfada okunabilir satır yok.");
            return new RecipeImportParseResult(rows, warnings);
        }

        var lastRow = used.LastRow().RowNumber();

        // Poz kodu çoğu reçete tablosunda yalnız bloğun ilk satırında
        // yazar, altındaki malzeme satırları boş bırakılır. Devralma
        // olmasaydı bu dosyaların neredeyse tamamı "poz kodu yok" diye
        // reddedilirdi.
        string? currentPositionCode = null;

        for (var rowNumber = mapping.HeaderRowIndex + 1; rowNumber <= lastRow; rowNumber++)
        {
            var positionCode = Text(sheet, rowNumber, mapping.PositionCodeColumn);
            var materialName = Text(sheet, rowNumber, mapping.MaterialNameColumn);
            var materialCode = Text(sheet, rowNumber, mapping.MaterialCodeColumn);
            var unit = Text(sheet, rowNumber, mapping.UnitColumn);
            var quantityText = Text(sheet, rowNumber, mapping.QuantityColumn);
            var wasteText = Text(sheet, rowNumber, mapping.WastePercentColumn);
            var notes = Text(sheet, rowNumber, mapping.NotesColumn);

            var inherited = false;

            if (!string.IsNullOrWhiteSpace(positionCode))
            {
                currentPositionCode = positionCode.Trim();
            }
            else if (currentPositionCode is not null)
            {
                positionCode = currentPositionCode;
                inherited = true;
            }

            // Tamamen boş satır: blok ayracı. Devralınan poz kodu da
            // burada düşürülür — boşluktan sonra gelen malzeme satırı
            // bir öncekinin devamı sayılmamalı.
            if (string.IsNullOrWhiteSpace(materialName) &&
                string.IsNullOrWhiteSpace(materialCode) &&
                string.IsNullOrWhiteSpace(quantityText) &&
                string.IsNullOrWhiteSpace(unit))
            {
                currentPositionCode = string.IsNullOrWhiteSpace(positionCode) || inherited
                    ? null
                    : currentPositionCode;

                continue;
            }

            var quantity = PositionImportParser.ParseNumericText(quantityText);
            var waste = PositionImportParser.ParseNumericText(wasteText) ?? 0m;

            var error = Validate(positionCode, materialName, unit, quantity, waste);

            rows.Add(new RecipeImportRow(
                rowNumber,
                string.IsNullOrWhiteSpace(positionCode) ? null : positionCode.Trim(),
                string.IsNullOrWhiteSpace(materialCode) ? null : materialCode.Trim(),
                string.IsNullOrWhiteSpace(materialName) ? null : materialName.Trim(),
                quantity,
                string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
                waste,
                string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                error,
                inherited));
        }

        if (rows.Count == 0)
            warnings.Add("Başlık satırının altında okunabilir satır bulunamadı.");

        return new RecipeImportParseResult(rows, warnings);
    }

    private static string? Validate(
        string? positionCode,
        string? materialName,
        string? unit,
        decimal? quantity,
        decimal wastePercent)
    {
        if (string.IsNullOrWhiteSpace(positionCode))
            return "Poz kodu yok.";

        if (string.IsNullOrWhiteSpace(materialName))
            return "Malzeme adı yok.";

        if (string.IsNullOrWhiteSpace(unit))
            return "Birim yok.";

        if (quantity is null)
            return "Miktar okunamadı.";

        if (quantity <= 0)
            return "Miktar sıfır veya negatif.";

        // Fire yüzdesi ihtiyacı doğrudan çarpar; %1000 gibi bir yazım
        // hatası malzeme ihtiyacını on bir katına çıkarır ve fark
        // edilmeden satın almaya gider.
        if (wastePercent < 0 || wastePercent > 100)
            return "Fire yüzdesi 0-100 aralığında olmalı.";

        return null;
    }

    private static string? Text(IXLWorksheet sheet, int row, int? column) =>
        column is > 0
            ? sheet.Cell(row, column.Value).GetFormattedString().Trim()
            : null;
}
