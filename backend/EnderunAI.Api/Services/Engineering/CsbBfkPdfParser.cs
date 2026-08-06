using System.Text;
using System.Text.RegularExpressions;
using EnderunAI.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>
/// ÇŞB (Çevre, Şehircilik ve İklim Değişikliği Bakanlığı) birim fiyat
/// kitabı PDF ayrıştırıcısı.
///
/// <b>Neden metin değil koordinat.</b> PDF'ten düz metin çıkarıldığında
/// okuma sırası kayıyor: tanım poz numarasının önüne geçiyor, fiyat ile
/// birim birbirine yapışıyor ("30X30X1.5 mm10.200.3609 40,25m").
/// Bu yüzden her kelimenin sayfadaki KONUMU kullanılıyor; kelimeler
/// önce Y'ye göre satırlara, sonra X'e göre kolonlara ayrılıyor.
///
/// <b>Kolonlar sayfadan okunuyor.</b> Kitapta birden çok düzen var:
/// rayiç sayfalarında "Ölçü Birimi / Satın Alma Yeri / Rayiç Fiyatı",
/// elektrik (35.xxx) sayfalarında "Montajlı Birim Fiyat / Montaj
/// Bedeli" ve birim tanım içinde "(Ölçü: Ad.)" olarak geçiyor.
/// Kolon sınırları her sayfanın kendi başlık satırından türetiliyor,
/// sabit X değeri varsayılmıyor.
///
/// <b>Uydurma yok.</b> Poz gibi görünüp fiyatı okunamayan satırlar
/// şüpheli listesine yazılıyor; tahmin edilmiyor.
/// </summary>
public static class CsbBfkPdfParser
{
    public const string ProfileKey = "CSB_BFK_PDF";

    /// <summary>Poz numarası: 10.200.3609 / 35.415.1610 biçimi.</summary>
    private static readonly Regex PositionCode =
        new(@"^\d{2}\.\d{3}\.\d{3,4}$", RegexOptions.Compiled);

    /// <summary>Türkçe ondalıklı para: 1.782,16 — kuruş zorunlu.</summary>
    private static readonly Regex MoneyToken =
        new(@"^\d{1,3}(\.\d{3})*,\d{2}$", RegexOptions.Compiled);

    /// <summary>Tanım içine gömülü birim: "(Ölçü: Ad.)".</summary>
    private static readonly Regex InlineUnit =
        new(@"\(\s*Ölçü\s*:\s*([^)]{1,20}?)\s*\)", RegexOptions.Compiled);

    /// <summary>Poz numarasının bulunduğu sol şerit.</summary>
    private const double CodeBandRight = 105;

    /// <summary>Aynı satır sayılacak dikey tolerans (punto yüksekliğinin altında).</summary>
    private const double RowTolerance = 3.0;

    public static BookParseResult Parse(
        Stream stream,
        string? codePrefixFilter = null,
        int? maxPages = null)
    {
        using var document = PdfDocument.Open(stream);

        var rows = new List<ParsedBookRow>();
        var suspicious = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pageCount = maxPages is { } limit
            ? Math.Min(limit, document.NumberOfPages)
            : document.NumberOfPages;

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            Page page;

            try
            {
                page = document.GetPage(pageNumber);
            }
            catch (Exception ex)
            {
                suspicious.Add($"Sayfa {pageNumber} açılamadı: {ex.GetType().Name}");
                continue;
            }

            ParsePage(page, pageNumber, codePrefixFilter, rows, suspicious, seen);
        }

        if (rows.Count == 0)
            warnings.Add("Hiç poz okunamadı; dosya beklenen kitap düzeninde olmayabilir.");

        // Grup başlığı kavramı ÇŞB'de "fiyatsız tanım pozu" olarak
        // karşımıza çıkıyor; sayısı şüpheli değil, bilgi amaçlı.
        return new BookParseResult(rows, 0, suspicious, warnings);
    }

    private static void ParsePage(
        Page page,
        int pageNumber,
        string? codePrefixFilter,
        List<ParsedBookRow> rows,
        List<string> suspicious,
        HashSet<string> seen)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
            return;

        var layout = DetectLayout(words, page.Width);
        if (layout is null)
            return;

        // Fiyatlar SAĞA DAYALI: sol kenar basamak sayısına göre kayıyor
        // (78,50 → 489, 1.162,50 → 478), sağ kenar ise sabit. Bu yüzden
        // kolonlar sayfadaki sağ kenar kümelerinden çıkarılıyor ve her
        // küme, altında durduğu başlığa göre bileşene bağlanıyor.
        var columnRights = DetectPriceColumnRights(words, layout);

        if (columnRights.Count == 0)
            return;

        // Kelimeleri Y'ye göre satırlara kümele (üstten alta).
        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / RowTolerance))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
            .ToList();

        // Aynı poz ailesi içinde birim, tanım pozundan alt pozlara taşınır.
        string? inheritedUnit = null;
        string? inheritedUnitPrefix = null;

        foreach (var line in lines)
        {
            var codeWord = line.FirstOrDefault(w =>
                w.BoundingBox.Left < CodeBandRight && PositionCode.IsMatch(w.Text));

            if (codeWord is null)
                continue;

            var code = codeWord.Text;

            if (codePrefixFilter is not null
                && !code.StartsWith(codePrefixFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var description = BuildDescription(line, layout, codeWord);

            // Tanım pozları birimi metin içinde taşıyor ve fiyatsız olur.
            var inlineUnit = InlineUnit.Match(description);
            var prefix = PrefixOf(code);

            if (inlineUnit.Success)
            {
                inheritedUnit = inlineUnit.Groups[1].Value.Trim().TrimEnd('.');
                inheritedUnitPrefix = prefix;
            }

            var (prices, ambiguous) = ReadPrices(line, layout, columnRights);

            if (ambiguous)
            {
                suspicious.Add(
                    $"Sayfa {pageNumber}: {code} — aynı fiyat kolonuna birden çok sayı düştü, " +
                    "satır atlandı.");

                continue;
            }

            if (prices.Count == 0)
            {
                // Fiyatsız satır: ya tanım pozu (alt pozları fiyatlı) ya
                // da devam satırı. İkisi de hata değil, sessizce geçilir.
                continue;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                suspicious.Add($"Sayfa {pageNumber}: {code} — tanım okunamadı.");
                continue;
            }

            var unit = ReadUnit(line, layout);
            var inherited = false;

            if (unit is null && inheritedUnit is not null && inheritedUnitPrefix == prefix)
            {
                unit = inheritedUnit;
                inherited = true;
            }

            if (!seen.Add(code))
            {
                // Kitapta aynı poz iki kez basılmış olabilir; ilkini
                // tutup ikincisini bildiriyoruz, sessizce ezmiyoruz.
                suspicious.Add($"Sayfa {pageNumber}: {code} daha önce de görüldü, atlandı.");
                continue;
            }

            rows.Add(new ParsedBookRow(
                pageNumber,
                code,
                CleanDescription(description),
                unit,
                prices,
                layout.SectionTitle,
                null,
                inherited));
        }
    }

    /// <summary>
    /// Sayfanın kendi başlık satırından kolon sınırlarını çıkarır.
    /// Fiyat kolonu başlığın ADINDAN sınıflanır — konumundan değil:
    /// elektrik sayfalarında en sağdaki kolon "Montaj Bedeli"dir ve
    /// keşif birim fiyatı değildir.
    /// </summary>
    private static PageLayout? DetectLayout(IReadOnlyList<Word> words, double pageWidth)
    {
        var unitHeader = words.FirstOrDefault(w =>
            w.Text.Equals("Ölçü", StringComparison.OrdinalIgnoreCase)
            && w.BoundingBox.Left > pageWidth * 0.6);

        var priceColumns = new List<(double Left, PositionPriceComponent Component)>();

        // "Rayiç Fiyatı" / "Birim Fiyat" → toplam
        foreach (var word in words.Where(w => w.BoundingBox.Left > pageWidth * 0.6))
        {
            if (word.Text.StartsWith("Rayiç", StringComparison.OrdinalIgnoreCase))
                priceColumns.Add((word.BoundingBox.Left, PositionPriceComponent.Total));
        }

        // "Montajlı ... Birim Fiyat" → toplam, "Montaj Bedeli" → montaj.
        var montajli = words.FirstOrDefault(w =>
            w.Text.StartsWith("Montajlı", StringComparison.OrdinalIgnoreCase));

        var montajBedeli = words.FirstOrDefault(w =>
            w.Text.Equals("Montaj", StringComparison.OrdinalIgnoreCase)
            && words.Any(o => o.Text.StartsWith("Bedeli", StringComparison.OrdinalIgnoreCase)
                              && Math.Abs(o.BoundingBox.Bottom - w.BoundingBox.Bottom) < RowTolerance
                              && o.BoundingBox.Left > w.BoundingBox.Left));

        if (montajli is not null)
        {
            var birimFiyat = words.FirstOrDefault(w =>
                w.Text.Equals("Birim", StringComparison.OrdinalIgnoreCase)
                && w.BoundingBox.Left > pageWidth * 0.6);

            if (birimFiyat is not null)
                priceColumns.Add((birimFiyat.BoundingBox.Left, PositionPriceComponent.Total));
        }
        else if (priceColumns.Count == 0)
        {
            var birimFiyat = words.FirstOrDefault(w =>
                w.Text.Equals("Birim", StringComparison.OrdinalIgnoreCase)
                && w.BoundingBox.Left > pageWidth * 0.6
                && words.Any(o => o.Text.StartsWith("Fiyat", StringComparison.OrdinalIgnoreCase)
                                  && o.BoundingBox.Left > w.BoundingBox.Left));

            if (birimFiyat is not null)
                priceColumns.Add((birimFiyat.BoundingBox.Left, PositionPriceComponent.Total));
        }

        if (montajBedeli is not null)
            priceColumns.Add((montajBedeli.BoundingBox.Left, PositionPriceComponent.Labor));

        if (priceColumns.Count == 0)
            return null;

        priceColumns = priceColumns.OrderBy(x => x.Left).ToList();

        // Bölüm başlığı: "35.100.-Kuvvetli Akım İç Tesisatı"
        var sectionWord = words.FirstOrDefault(w =>
            w.Text.Contains(".-", StringComparison.Ordinal)
            && w.BoundingBox.Bottom > 760);

        var sectionTitle = sectionWord is null
            ? null
            : string.Join(" ", words
                .Where(w => Math.Abs(w.BoundingBox.Bottom - sectionWord.BoundingBox.Bottom) < RowTolerance)
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => w.Text));

        return new PageLayout(
            unitHeader?.BoundingBox.Left,
            priceColumns,
            sectionTitle);
    }

    /// <summary>
    /// Satırdaki para belirteçlerini fiyat kolonlarına dağıtır.
    ///
    /// Her belirteç EN YAKIN kolona atanır. Kolon şeritlerini ayrı ayrı
    /// test etmek işe yaramıyor: elektrik sayfalarında iki fiyat kolonu
    /// birbirine yakın ve şeritler çakışıyor, o yüzden bir belirteç iki
    /// kolona birden uyabiliyordu ve kolon sessizce boş kalıyordu.
    ///
    /// İki belirteç aynı kolona düşerse satır belirsizdir; tahmin
    /// edilmez, şüpheli olarak bildirilir.
    /// </summary>
    private static (List<ParsedPositionPrice> Prices, bool Ambiguous) ReadPrices(
        IReadOnlyList<Word> line,
        PageLayout layout,
        IReadOnlyList<(double Right, PositionPriceComponent Component)> columnRights)
    {
        var firstColumnLeft = layout.PriceColumns[0].Left;

        var tokens = line
            .Where(w => MoneyToken.IsMatch(w.Text) && w.BoundingBox.Left >= firstColumnLeft - 5)
            .ToList();

        var assigned = new Dictionary<PositionPriceComponent, decimal>();
        var ambiguous = false;

        foreach (var token in tokens)
        {
            var nearest = columnRights
                .Select(c => (c.Component, Distance: Math.Abs(c.Right - token.BoundingBox.Right)))
                .OrderBy(x => x.Distance)
                .First();

            // Hiçbir kolona yakın değilse bu bir fiyat değildir.
            if (nearest.Distance > 12)
                continue;

            var value = PositionImportParser.ParseNumericText(token.Text);

            if (value is not > 0)
                continue;

            if (assigned.ContainsKey(nearest.Component))
            {
                ambiguous = true;
                continue;
            }

            assigned[nearest.Component] = decimal.Round(value.Value, 4);
        }

        var prices = assigned
            .Select(x => new ParsedPositionPrice(x.Key, x.Value))
            .ToList();

        return (prices, ambiguous);
    }

    /// <summary>
    /// Sayfadaki para belirteçlerinin sağ kenarlarını kümeler ve her
    /// kümeyi, solunda kalan en yakın fiyat başlığına bağlar.
    /// </summary>
    private static List<(double Right, PositionPriceComponent Component)> DetectPriceColumnRights(
        IReadOnlyList<Word> words, PageLayout layout)
    {
        var firstColumnLeft = layout.PriceColumns[0].Left;

        var rights = words
            .Where(w => MoneyToken.IsMatch(w.Text) && w.BoundingBox.Left >= firstColumnLeft - 5)
            .Select(w => w.BoundingBox.Right)
            .OrderBy(x => x)
            .ToList();

        var clusters = new List<List<double>>();

        foreach (var right in rights)
        {
            var last = clusters.LastOrDefault();

            if (last is not null && right - last[^1] <= 8)
                last.Add(right);
            else
                clusters.Add([right]);
        }

        var result = new List<(double, PositionPriceComponent)>();

        foreach (var cluster in clusters)
        {
            // Gürültü kümelerini ele: bir kolon sayfada birkaç kez geçer.
            if (cluster.Count < 2)
                continue;

            var center = cluster.Average();

            var header = layout.PriceColumns
                .Where(c => c.Left <= center)
                .OrderByDescending(c => c.Left)
                .FirstOrDefault();

            if (header.Left <= 0)
                continue;

            if (result.All(x => x.Item2 != header.Component))
                result.Add((center, header.Component));
        }

        return result;
    }

    private static string? ReadUnit(IReadOnlyList<Word> line, PageLayout layout)
    {
        if (layout.UnitColumnLeft is not { } unitLeft)
            return null;

        var firstPrice = layout.PriceColumns[0].Left;

        var tokens = line
            .Where(w => w.BoundingBox.Left >= unitLeft - 12
                        && w.BoundingBox.Left < firstPrice - 12
                        && !MoneyToken.IsMatch(w.Text))
            .OrderBy(w => w.BoundingBox.Left)
            .Select(w => w.Text)
            .ToList();

        if (tokens.Count == 0)
            return null;

        var unit = tokens[0].Trim();

        // Birim kısa bir simgedir; uzun bir metin geldiyse bu kolon
        // aslında birim kolonu değildir (satın alma yeri gibi).
        return unit.Length is > 0 and <= 8 ? unit : null;
    }

    private static string BuildDescription(
        IReadOnlyList<Word> line, PageLayout layout, Word codeWord)
    {
        var rightBound = layout.UnitColumnLeft ?? layout.PriceColumns[0].Left;

        var builder = new StringBuilder();

        foreach (var word in line
            .Where(w => w.BoundingBox.Left >= CodeBandRight
                        && w.BoundingBox.Left < rightBound - 12
                        && w != codeWord)
            .OrderBy(w => w.BoundingBox.Left))
        {
            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(word.Text);
        }

        return builder.ToString().Trim();
    }

    private static string CleanDescription(string description)
    {
        var cleaned = InlineUnit.Replace(description, string.Empty).Trim();
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', ':', '-');

        var value = cleaned.Length == 0 ? description.Trim() : cleaned;

        return value.Length > 500 ? value[..500] : value;
    }

    /// <summary>"35.415.1610" → "35.415" — birim mirasında aile kontrolü.</summary>
    private static string PrefixOf(string code)
    {
        var lastDot = code.LastIndexOf('.');

        return lastDot > 0 ? code[..lastDot] : code;
    }

    private sealed record PageLayout(
        double? UnitColumnLeft,
        IReadOnlyList<(double Left, PositionPriceComponent Component)> PriceColumns,
        string? SectionTitle);
}
