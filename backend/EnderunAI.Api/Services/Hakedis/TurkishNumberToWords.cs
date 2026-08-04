using System.Text;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>
/// Tutarı Türkçe yazıya çevirir — hakediş ve fatura çıktılarında
/// "yazı ile" satırı için.
///
/// Türkçe okumanın iki özel kuralı var ve ikisi de burada: "bir yüz" ya
/// da "bir bin" denmez (yüz, bin); ancak "bir milyon" denir. Kuruş
/// kısmı iki hane olarak okunur.
/// </summary>
public static class TurkishNumberToWords
{
    private static readonly string[] Ones =
        ["", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz"];

    private static readonly string[] Tens =
        ["", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan"];

    /// <summary>Basamak grupları: 10^3, 10^6, 10^9, 10^12.</summary>
    private static readonly string[] Groups =
        ["", "bin", "milyon", "milyar", "trilyon"];

    /// <summary>
    /// Örnek: 33.058,43 → "otuzüçbinellisekiz TL kırküç Kr".
    /// </summary>
    public static string Convert(decimal amount, string currencyName = "TL")
    {
        var negative = amount < 0m;
        amount = Math.Abs(decimal.Round(amount, 2, MidpointRounding.AwayFromZero));

        var whole = (long)decimal.Truncate(amount);
        var cents = (int)decimal.Round((amount - whole) * 100m, 0, MidpointRounding.AwayFromZero);

        var builder = new StringBuilder();

        if (negative)
            builder.Append("eksi ");

        builder.Append(whole == 0 ? "sıfır" : ConvertWhole(whole));
        builder.Append(' ').Append(currencyName);

        if (cents > 0)
            builder.Append(' ').Append(ConvertWhole(cents)).Append(" Kr");

        return builder.ToString();
    }

    private static string ConvertWhole(long value)
    {
        if (value == 0)
            return string.Empty;

        // Sayıyı üçlü gruplara ayır: en düşük grup başta.
        var groups = new List<int>();

        while (value > 0)
        {
            groups.Add((int)(value % 1000));
            value /= 1000;
        }

        var builder = new StringBuilder();

        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];

            if (group == 0)
                continue;

            // "birbin" değil "bin"; ama "birmilyon" doğru.
            if (index == 1 && group == 1)
            {
                builder.Append(Groups[index]);
                continue;
            }

            builder.Append(ConvertGroup(group)).Append(Groups[index]);
        }

        return builder.ToString();
    }

    private static string ConvertGroup(int value)
    {
        var builder = new StringBuilder();

        var hundreds = value / 100;
        var remainder = value % 100;

        if (hundreds > 0)
        {
            // "biryüz" değil "yüz".
            if (hundreds > 1)
                builder.Append(Ones[hundreds]);

            builder.Append("yüz");
        }

        builder.Append(Tens[remainder / 10]);
        builder.Append(Ones[remainder % 10]);

        return builder.ToString();
    }
}
