using System.Globalization;
using System.Xml.Linq;
using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

/// <summary>TCMB bülteninden okunan tek satır.</summary>
public sealed record TcmbRateRow(
    string CurrencyCode,
    int Unit,
    decimal ForexBuying,
    decimal ForexSelling,
    decimal? BanknoteBuying,
    decimal? BanknoteSelling);

public sealed record TcmbBulletin(
    DateTime RateDate,
    string? BulletinNumber,
    IReadOnlyList<TcmbRateRow> Rows);

/// <summary>
/// TCMB kur bülteni XML'ini ayrıştırır. Saf ve statik: ağ, veritabanı ve
/// saat bağımlılığı yok, dolayısıyla sabit örnek XML ile test edilebilir.
///
/// İki tuzak bilinçli olarak ele alınmıştır:
/// 1. TCMB ondalık ayırıcı olarak NOKTA kullanır ("47.4881"). Sunucu
///    kültürü Türkçe olduğunda <c>decimal.Parse</c> bunu 474881 diye
///    okur — tüm ayrıştırma InvariantCulture ile yapılır.
/// 2. Bazı para birimleri 100 birim üzerinden kote edilir. Tutarlar
///    saklanmadan önce birime bölünür; böylece tüketici tarafta
///    "acaba bu 1 birim mi" sorusu hiç doğmaz.
/// </summary>
public static class TcmbRateParser
{
    /// <summary>
    /// Bülteni ayrıştırır. XML bozuksa veya tarih okunamıyorsa null
    /// döner — yarım veri kur arşivine girmez.
    /// </summary>
    public static TcmbBulletin? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Tarih_Date")
            return null;

        var rateDate = ParseBulletinDate(root.Attribute("Tarih")?.Value);
        if (rateDate is null)
            return null;

        var rows = new List<TcmbRateRow>();

        foreach (var element in root.Elements("Currency"))
        {
            var code = (element.Attribute("CurrencyCode")?.Value
                        ?? element.Attribute("Kod")?.Value)?.Trim();

            if (string.IsNullOrWhiteSpace(code))
                continue;

            var unit = ParseInt(element.Element("Unit")?.Value) ?? 1;
            if (unit <= 0)
                unit = 1;

            var forexBuying = ParseDecimal(element.Element("ForexBuying")?.Value);
            var forexSelling = ParseDecimal(element.Element("ForexSelling")?.Value);

            // Döviz alış muhasebenin esas kuru; onsuz satır işe yaramaz.
            if (forexBuying is null or <= 0)
                continue;

            rows.Add(new TcmbRateRow(
                code.ToUpperInvariant(),
                unit,
                PerUnit(forexBuying.Value, unit),
                PerUnit(forexSelling ?? forexBuying.Value, unit),
                Nullable(ParseDecimal(element.Element("BanknoteBuying")?.Value), unit),
                Nullable(ParseDecimal(element.Element("BanknoteSelling")?.Value), unit)));
        }

        if (rows.Count == 0)
            return null;

        return new TcmbBulletin(
            rateDate.Value,
            root.Attribute("Bulten_No")?.Value?.Trim(),
            rows);
    }

    /// <summary>Geçmiş tarih bülteninin yolu: /kurlar/202608/05082026.xml</summary>
    public static string BuildHistoricalPath(DateTime date) =>
        $"kurlar/{date:yyyyMM}/{date:ddMMyyyy}.xml";

    private static decimal PerUnit(decimal value, int unit) =>
        unit == 1 ? value : decimal.Round(value / unit, 6, MidpointRounding.AwayFromZero);

    private static decimal? Nullable(decimal? value, int unit) =>
        value is null or <= 0 ? null : PerUnit(value.Value, unit);

    /// <summary>Bülten tarihi gg.aa.yyyy biçiminde gelir.</summary>
    private static DateTime? ParseBulletinDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParseExact(
            value.Trim(),
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return int.TryParse(
            value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
