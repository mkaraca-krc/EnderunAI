using ClosedXML.Excel;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>Kısım başlığının nasıl tanınacağı.</summary>
public enum ContractSummarySectionRule
{
    /// <summary>Ayrı bir "Kısım" sütunu dolu, poz kodu boş (ENDERUN şablonu).</summary>
    SectionColumn = 0,

    /// <summary>
    /// Kodu olan ama BİRİMİ boş satır başlıktır. Gerçek icmallerde en
    /// güvenilir kural: başlık satırında ara toplamlar sayı sütunlarına
    /// yazılıyor, bu yüzden "sayı var mı" bakmak yanıltıcı.
    /// </summary>
    EmptyUnit = 1,

    /// <summary>Kod noktayla bitiyor ("01." gibi).</summary>
    CodeEndsWithDot = 2
}

/// <summary>
/// Hangi sütunun neye karşılık geldiği. Sütun numaraları 1 tabanlı.
/// </summary>
public sealed record ContractSummaryMapping(
    string? SheetName,
    int HeaderRowIndex,
    int CodeColumn,
    int DescriptionColumn,
    int UnitColumn,
    int QuantityColumn,
    int MaterialColumn,
    int LaborColumn,
    int OverheadColumn,
    /// <summary>Ayrı kısım sütunu — yalnızca SectionColumn kuralında.</summary>
    int? SectionColumn = null,
    /// <summary>
    /// Dosyanın kendi tutar sütunu. VERİ OLARAK KULLANILMAZ; yalnızca
    /// doğrulama toplamı: hesaplanan tutar buna uymuyorsa satır şüpheli
    /// işaretlenir ve belirsiz sayılar bununla çözülür.
    /// </summary>
    int? TotalColumn = null,
    ContractSummarySectionRule SectionRule = ContractSummarySectionRule.EmptyUnit,
    /// <summary>
    /// Özet (icmal) sayfasının adı. Verilirse kısımların özetteki
    /// adları okunup alias olarak taşınır. Boşsa alias okunmaz.
    /// </summary>
    string? AliasSheetName = null,
    /// <summary>Özet sayfasında kısım kodunun bulunduğu sütun.</summary>
    int? AliasCodeColumn = null,
    /// <summary>Özet sayfasında kısım adının bulunduğu sütun.</summary>
    int? AliasNameColumn = null);

/// <summary>
/// Sözleşme icmalini KULLANICININ EŞLEDİĞİ sütunlardan okur.
///
/// Sabit düzenli şablon okuyucusu (<see cref="ContractSummaryExcelParser"/>)
/// duruyor; bu okuyucu her müşterinin kendi icmal düzeniyle gelmesi için
/// var. Gerçek icmallerde sütun sırası, başlık satırı ve kısım gösterimi
/// dosyadan dosyaya değişiyor.
///
/// İki koruma taşır:
/// 1. <b>Belirsiz sayı tahmin edilmez.</b> "3.976" hem üç bin dokuz yüz
///    yetmiş altı hem üç virgül dokuz yüz yetmiş altı olabilir. Dosyanın
///    kendi tutar sütunu eşlendiyse iki okuma denenip TUTARI DOĞRULAYAN
///    seçilir; doğrulanamıyorsa satır hata olur.
/// 2. <b>Tutar tutmuyorsa satır aktarılmaz.</b> Hesaplanan tutar ile
///    dosyanın kendi tutarı belirgin biçimde ayrışıyorsa (miktarın bin
///    katı kaybolması gibi) satır şüpheli işaretlenir. Böyle bir satırı
///    sessizce içeri almak, icmali sessizce eksik yazardı.
/// </summary>
public static class ContractSummaryMappedParser
{
    /// <summary>
    /// Hesaplanan tutarın dosyadaki tutardan bu orandan fazla sapması
    /// şüphelidir. Birim fiyatların kuruş yuvarlaması gerçek dosyalarda
    /// %1,5'e kadar çıkabiliyor; bin kat sapmalar ise %99 seviyesinde.
    /// Aradaki boşluk geniş, eşik oraya konuyor.
    /// </summary>
    public const decimal ChecksumTolerancePercent = 5m;

    /// <summary>Bu tutarın altındaki mutlak sapma zaten önemsiz.</summary>
    public const decimal ChecksumMinimumDeviation = 50m;

    public static ContractSummaryParseResult Parse(
        Stream stream, ContractSummaryMapping mapping)
    {
        var lines = new List<ContractSummaryParsedLine>();
        var errors = new List<ContractSummaryParseError>();

        using var workbook = new XLWorkbook(stream);

        var sheet = mapping.SheetName is not null
            ? workbook.Worksheets.FirstOrDefault(x => x.Name == mapping.SheetName)
              ?? workbook.Worksheet(1)
            : workbook.Worksheet(1);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        string? currentSection = null;
        string? currentGroup = null;

        for (var row = mapping.HeaderRowIndex + 1; row <= lastRow; row++)
        {
            var code = Text(sheet, row, mapping.CodeColumn);
            var description = Text(sheet, row, mapping.DescriptionColumn);
            var unit = Text(sheet, row, mapping.UnitColumn);

            var sectionCell = mapping.SectionColumn is int sectionColumn
                ? Text(sheet, row, sectionColumn)
                : string.Empty;

            // Tamamen boş satır ya da imza/toplam bloğu: kodu ve tanımı
            // olmayan satır poz olamaz.
            if (code.Length == 0 && description.Length == 0 && sectionCell.Length == 0)
                continue;

            // "Elektrik İşleri Toplamı" gibi genel toplam satırları:
            // kodu da birimi de yok, yalnızca bir etiket ve tutar var.
            // Bunları hata listesine yazmak, gerçek hataları görünmez
            // eden bir gürültü üretirdi.
            if (mapping.SectionRule != ContractSummarySectionRule.SectionColumn
                && code.Length == 0 && unit.Length == 0)
            {
                continue;
            }

            // --- Başlık mı? ---
            var isHeader = mapping.SectionRule switch
            {
                ContractSummarySectionRule.SectionColumn =>
                    sectionCell.Length > 0 && code.Length == 0,
                ContractSummarySectionRule.EmptyUnit =>
                    code.Length > 0 && unit.Length == 0,
                ContractSummarySectionRule.CodeEndsWithDot =>
                    code.EndsWith('.'),
                _ => false
            };

            if (isHeader)
            {
                var name = mapping.SectionRule == ContractSummarySectionRule.SectionColumn
                    ? sectionCell
                    : (description.Length > 0 ? description : code);

                if (name.Length == 0)
                {
                    errors.Add(new ContractSummaryParseError(
                        row, "Başlık satırının adı boş."));
                    continue;
                }

                // Kod derinliği seviyeyi verir: "01." ana kısım,
                // "12.06" onun altındaki grup. Alt grup ayrı bir kısım
                // açmaz — hakediş 12 kısım üzerinden düzenleniyor —
                // ama adı kaybolmasın diye satırın kategorisine yazılır.
                if (SegmentCount(code) <= 1
                    || mapping.SectionRule == ContractSummarySectionRule.SectionColumn)
                {
                    currentSection = name;
                    currentGroup = null;

                    lines.Add(new ContractSummaryParsedLine(
                        row, true, name, string.Empty, string.Empty, string.Empty,
                        0m, 0m, 0m, 0m));
                }
                else
                {
                    currentGroup = name;
                }

                continue;
            }

            // --- Poz satırı ---
            if (description.Length == 0)
            {
                errors.Add(new ContractSummaryParseError(row, "Tanım boş."));
                continue;
            }

            if (unit.Length == 0)
            {
                errors.Add(new ContractSummaryParseError(row, "Birim boş."));
                continue;
            }

            var fileTotal = mapping.TotalColumn is int totalColumn
                ? Number(sheet, row, totalColumn)
                : null;

            if (!TryComponent(sheet, row, mapping.MaterialColumn, out var material))
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Malzeme birim fiyatı okunamadı."));
                continue;
            }

            if (!TryComponent(sheet, row, mapping.LaborColumn, out var labor))
            {
                errors.Add(new ContractSummaryParseError(
                    row, "İşçilik birim fiyatı okunamadı."));
                continue;
            }

            if (!TryComponent(sheet, row, mapping.OverheadColumn, out var overhead))
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Genel gider / kâr birim fiyatı okunamadı."));
                continue;
            }

            var unitPrice = material + labor + overhead;

            var (quantity, quantityError) = ResolveQuantity(
                sheet, row, mapping.QuantityColumn, unitPrice, fileTotal);

            if (quantityError is not null)
            {
                errors.Add(new ContractSummaryParseError(row, quantityError));
                continue;
            }

            if (quantity < 0m || material < 0m || labor < 0m || overhead < 0m)
            {
                errors.Add(new ContractSummaryParseError(
                    row, "Miktar ya da birim fiyat negatif olamaz."));
                continue;
            }

            var computed = decimal.Round(quantity!.Value * unitPrice, 2);

            if (fileTotal is decimal expected && IsOffChecksum(computed, expected))
            {
                errors.Add(new ContractSummaryParseError(
                    row,
                    $"Dosyadaki tutar {TurkishFormat.Amount(expected)} ile miktar × birim fiyattan " +
                    $"hesaplanan {TurkishFormat.Amount(computed)} uyuşmuyor. Satır aktarılmadı; " +
                    "kaynak dosyadaki değeri kontrol edin.",
                    ContractSummaryErrorKind.Checksum,
                    code,
                    description,
                    expected,
                    computed));

                continue;
            }

            lines.Add(new ContractSummaryParsedLine(
                row, false, currentSection,
                code, description, unit,
                quantity.Value, material, labor, overhead,
                currentGroup));
        }

        var aliasNote = ApplyAliases(workbook, mapping, lines);

        return new ContractSummaryParseResult(lines, errors, aliasNote);
    }

    /// <summary>
    /// Özet (icmal) sayfasındaki kısım adlarını okuyup detay
    /// kısımlarına alias olarak bağlar.
    ///
    /// EŞLEŞTİRME SIRAYA GÖRE, çünkü adlar zaten tutmuyor — tutsalardı
    /// alias'a gerek olmazdı. Sıra dışında ortak bir anahtar yok.
    ///
    /// SAYILAR TUTMUYORSA HİÇ EŞLEŞTİRİLMEZ. Sıra eşleştirmesi ancak
    /// iki listenin aynı şeyi aynı sırada anlattığı varsayımıyla
    /// doğrudur; sayı farklıysa varsayım çökmüştür ve kaydırılmış bir
    /// eşleştirme, kısımları birbirine karıştırıp hakedişi yanlış
    /// satıra yazardı. Sebebi not olarak dönüyor.
    /// </summary>
    private static string? ApplyAliases(
        XLWorkbook workbook,
        ContractSummaryMapping mapping,
        List<ContractSummaryParsedLine> lines)
    {
        if (mapping.AliasSheetName is null ||
            mapping.AliasCodeColumn is not int codeColumn ||
            mapping.AliasNameColumn is not int nameColumn)
        {
            return null;
        }

        var sheet = workbook.Worksheets
            .FirstOrDefault(x => x.Name == mapping.AliasSheetName);

        if (sheet is null)
            return $"Özet sayfası \"{mapping.AliasSheetName}\" dosyada bulunamadı.";

        var aliases = new List<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = 1; row <= lastRow; row++)
        {
            var code = Text(sheet, row, codeColumn);
            var name = Text(sheet, row, nameColumn);

            // Kısım satırının işareti: kodu RAKAMLA başlıyor. Başlık
            // satırı ("Poz"), genel toplam ve imza blokları böylece
            // kendiliğinden eleniyor; ayrı bir "ilk satır" ayarı
            // istemeye gerek kalmıyor.
            if (name.Length == 0 || code.Length == 0 || !char.IsDigit(code[0]))
                continue;

            aliases.Add(name);
        }

        var sections = lines.Where(x => x.IsSectionHeader).ToList();

        if (aliases.Count == 0)
            return $"Özet sayfasında kısım satırı bulunamadı.";

        if (aliases.Count != sections.Count)
        {
            return $"Özet sayfasında {aliases.Count} kısım var, detayda " +
                   $"{sections.Count}. Sayılar tutmadığı için adlar " +
                   "eşleştirilmedi.";
        }

        for (var index = 0; index < sections.Count; index++)
        {
            var position = lines.IndexOf(sections[index]);
            lines[position] = sections[index] with { AliasName = aliases[index] };
        }

        return null;
    }

    /// <summary>
    /// İki kısım adının aynı şeyi anlatıp anlatmadığı.
    ///
    /// Boşluk ve "&" farkları gerçek fark değil ("PANOLAR &TABLOLAR"
    /// ile "PANOLAR & TABLOLAR" aynı kısım). Bunları eşit saymazsak
    /// kullanıcı, hiçbir bilgi taşımayan onaylarla boğulur ve gerçek
    /// farkı gözden kaçırır. NATURA icmalinde ölçüldü: normalize
    /// edilmeden 7, edilerek 5 çift farklı çıkıyor.
    /// </summary>
    public static bool NamesMatch(string? left, string? right)
    {
        static string Normalize(string? value) =>
            System.Text.RegularExpressions.Regex.Replace(
                (value ?? string.Empty)
                    .ToUpperInvariant()
                    .Replace("&", " VE ")
                    .Replace("(", " ")
                    .Replace(")", " "),
                @"\s+",
                " ").Trim();

        return Normalize(left) == Normalize(right);
    }

    /// <summary>
    /// Miktar okuma. Nokta içeren metinlerde iki okuma da mümkün olduğu
    /// için (binlik mi ondalık mı) dosyanın kendi tutarıyla doğrulanır.
    /// Doğrulanamıyorsa tahmin edilmez, hata döner.
    /// </summary>
    private static (decimal? Quantity, string? Error) ResolveQuantity(
        IXLWorksheet sheet, int row, int column, decimal unitPrice, decimal? fileTotal)
    {
        var cell = sheet.Cell(row, column);

        if (cell.DataType == XLDataType.Number)
            return (Convert.ToDecimal(cell.GetDouble()), null);

        var text = cell.GetFormattedString().Trim();

        if (text.Length == 0)
            return (null, "Miktar boş.");

        var candidates = QuantityCandidates(text);

        if (candidates.Count == 1)
            return (candidates[0], null);

        if (candidates.Count == 0)
            return (null, $"Miktar sayı olarak okunamadı: \"{text}\".");

        // Birden fazla okuma mümkün: dosyanın kendi tutarı hakem.
        if (fileTotal is not decimal expected || expected == 0m || unitPrice == 0m)
        {
            return (null,
                $"Miktar belirsiz: \"{text}\" hem {TurkishFormat.Quantity(candidates[0])} hem " +
                $"{TurkishFormat.Quantity(candidates[1])} okunabilir. Doğrulanacak tutar sütunu " +
                "eşlenmediği için tahmin edilmedi.");
        }

        var fitting = candidates
            .Where(x => !IsOffChecksum(decimal.Round(x * unitPrice, 2), expected))
            .ToList();

        if (fitting.Count == 1)
            return (fitting[0], null);

        return (null,
            $"Miktar belirsiz: \"{text}\" okumalarının hiçbiri dosyadaki " +
            $"{TurkishFormat.Amount(expected)} tutarını doğrulamıyor.");
    }

    /// <summary>
    /// Nokta içeren metnin olası okumaları: ondalık ve binlik. Nokta
    /// yoksa tek okuma vardır.
    /// </summary>
    private static List<decimal> QuantityCandidates(string text)
    {
        var direct = PositionImportParser.ParseNumericText(text);

        var cleaned = text
            .Replace("₺", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        // Yalnız nokta içeren metin: "3.976" ve "5.6854" gibi. İkisi de
        // Türkçe binlik ayırıcı olabilir; ondalık okuma da mümkün.
        if (cleaned.Contains('.') && !cleaned.Contains(','))
        {
            var withoutDots = PositionImportParser.ParseNumericText(
                cleaned.Replace(".", string.Empty));

            var asDecimal = decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                    ? parsed
                    : (decimal?)null;

            var options = new List<decimal>();

            if (asDecimal.HasValue) options.Add(asDecimal.Value);
            if (withoutDots.HasValue && withoutDots != asDecimal) options.Add(withoutDots.Value);

            return options;
        }

        return direct.HasValue ? [direct.Value] : [];
    }

    /// <summary>
    /// Birim fiyat bileşeni. Boş hücre SIFIR sayılır: gerçek icmallerde
    /// işçiliği olmayan kalemin hücresi boş bırakılıyor ve dosyanın
    /// kendi toplamı da bunu sıfır sayıyor. Dolu ama okunamayan hücre
    /// sıfıra çevrilmez — satır hata olur.
    /// </summary>
    private static bool TryComponent(
        IXLWorksheet sheet, int row, int column, out decimal value)
    {
        var cell = sheet.Cell(row, column);

        if (cell.DataType == XLDataType.Number)
        {
            value = Convert.ToDecimal(cell.GetDouble());
            return true;
        }

        var text = cell.GetFormattedString().Trim();

        if (text.Length == 0)
        {
            value = 0m;
            return true;
        }

        var parsed = PositionImportParser.ParseNumericText(text);
        value = parsed ?? 0m;

        return parsed.HasValue;
    }

    private static bool IsOffChecksum(decimal computed, decimal expected)
    {
        if (expected == 0m)
            return false;

        var deviation = Math.Abs(computed - expected);

        return deviation > ChecksumMinimumDeviation
               && deviation / Math.Abs(expected) * 100m > ChecksumTolerancePercent;
    }

    private static decimal? Number(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);

        return cell.DataType == XLDataType.Number
            ? Convert.ToDecimal(cell.GetDouble())
            : PositionImportParser.ParseNumericText(cell.GetFormattedString());
    }

    /// <summary>"12.06" → 2, "01." → 1. Kod derinliği kısım seviyesini verir.</summary>
    private static int SegmentCount(string code) =>
        code.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        column <= 0 ? string.Empty : sheet.Cell(row, column).GetFormattedString().Trim();
}
