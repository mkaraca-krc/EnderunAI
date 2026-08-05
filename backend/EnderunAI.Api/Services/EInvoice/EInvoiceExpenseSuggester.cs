using System.Text;

namespace EnderunAI.Api.Services.EInvoice;

/// <summary>Anahtar kelimeden çıkan gider önerisi.</summary>
public sealed record ExpenseSuggestion(
    bool IsExpense,
    string? AccountCode,
    string? Reason);

/// <summary>
/// E-faturanın alış mı gider mi olduğunu ve gider ise hangi hesaba
/// yazılacağını tahmin eder.
///
/// Bu YALNIZCA ÖNERİDİR: elektrik faturası sanılan bir kalem pekâlâ
/// şantiyeye çekilen pano malzemesi olabilir. Ekranda seçili gelir ama
/// kullanıcı değiştirebilir; kaydeden şey her zaman kullanıcının onayı.
///
/// Yalnızca KALEM AÇIKLAMALARINA bakar, tedarikçi unvanına bakmaz:
/// "AY GLOBAL ELEKTRİK MALZEMELERİ" bir malzeme tedarikçisidir ve
/// unvanına bakılsaydı kablo faturası elektrik gideri sanılırdı.
/// Faturanın ne olduğunu kalem yazar.
///
/// Veritabanı bilmez — anahtar kelime eşleşmesi saf metin işidir ve
/// böyle test edilebilir. Kod karşılığı hesap, şirketin hesap planında
/// varsa çağıran tarafından bağlanır.
/// </summary>
public static class EInvoiceExpenseSuggester
{
    /// <summary>
    /// Anahtar kelime → hesap kodu. Sıra önemlidir: ilk eşleşen kazanır,
    /// bu yüzden dar ifadeler ("doğalgaz") geniş olanlardan ("gaz") önce
    /// gelir.
    ///
    /// Ünsüz yumuşamasına uğrayan kelimeler iki biçimiyle de yazılır
    /// ("elektrik" / "elektriği"): eşleşme kelimenin başından yapıldığı
    /// için "elektrik" anahtarı "elektriği" ifadesini yakalayamaz.
    /// </summary>
    private static readonly (string Keyword, string AccountCode)[] Keywords =
    [
        ("dogalgaz", "770.03.12"),
        ("dogal gaz", "770.03.12"),
        ("elektrik", "770.03.10"),
        ("elektrigi", "770.03.10"),
        ("su tuketim", "770.03.10"),
        ("su abone", "770.03.10"),
        ("internet", "770.03.13"),
        ("fiber", "770.03.13"),
        ("adsl", "770.03.13"),
        ("telefon", "770.03.08"),
        ("gsm", "770.03.08"),
        ("mobil hat", "770.03.08"),
        ("iletisim", "770.03.08"),
        ("haberlesme", "770.03.08"),
        ("temizlik", "770.03.14"),
        ("temizligi", "770.03.14"),
        ("osgb", "770.03.15"),
        ("isyeri hekim", "770.03.15"),
        ("is guvenligi", "770.03.15"),
        ("noter", "770.03.01"),
        ("akaryakit", "770.03.02"),
        ("motorin", "770.03.02"),
        ("benzin", "770.03.02"),
        ("kirtasiye", "770.03.03"),
        ("mali musavir", "770.03.05"),
        ("smmm", "770.03.05"),
        ("konaklama", "770.03.09"),
        ("otel", "770.03.09"),
        ("kargo", "770.04.03"),
        ("sigorta", "770.04.10"),
        ("police", "770.04.10"),
        ("kasko", "770.04.10"),
        ("dask", "770.04.10"),
        ("kira", "770.04.13"),
        ("aidat", "770.04.15"),
        ("yemek", "770.04.16"),
        ("yemegi", "770.04.16"),
        ("banka masraf", "770.04.08")
    ];

    public static ExpenseSuggestion Suggest(IEnumerable<string?> lineDescriptions)
    {
        var haystack = Normalize(
            string.Join(" ", lineDescriptions.Where(x => x is not null)));

        if (haystack.Length == 0)
            return new ExpenseSuggestion(false, null, null);

        foreach (var (keyword, accountCode) in Keywords)
        {
            if (!Contains(haystack, keyword))
                continue;

            return new ExpenseSuggestion(
                true,
                accountCode,
                $"Faturada \"{keyword}\" geçtiği için gider olarak önerildi.");
        }

        return new ExpenseSuggestion(false, null, null);
    }

    /// <summary>
    /// Anahtar kelime bir kelimenin BAŞINDA geçmelidir; sonuna ek
    /// gelebilir. Türkçe ekli yazıldığı için ("kirası", "elektriği",
    /// "temizliği") tam kelime eşleşmesi çok şey kaçırırdı; buna karşılık
    /// kelime ortasında eşleşmeye izin verilseydi "su" ifadesi "kusur"
    /// içinde tutar ve fatura yanlış hesaba önerilirdi.
    /// </summary>
    private static bool Contains(string haystack, string keyword)
    {
        var index = 0;

        while ((index = haystack.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
        {
            var startsWord = index == 0 || haystack[index - 1] == ' ';

            if (startsWord)
                return true;

            index += keyword.Length;
        }

        return false;
    }

    /// <summary>
    /// Türkçe karakterleri sadeleştirir, harf/rakam dışını boşluğa
    /// çevirir. Faturalarda unvan bazen büyük harfli ve noktalı yazılır
    /// ("ELEKTRİK DAĞ. A.Ş."); eşleşme buna takılmamalı.
    /// </summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var character in value)
        {
            // Türkçe büyük harfler önce elden geçer: ToLowerInvariant
            // 'İ' harfini olduğu gibi bırakır ve "İNTERNET" ifadesi
            // "internet" anahtarına takılmazdı.
            var mapped = character switch
            {
                'İ' or 'I' or 'Î' => 'i',
                'Ğ' => 'g',
                'Ü' => 'u',
                'Ş' => 's',
                'Ö' => 'o',
                'Ç' => 'c',
                'Â' => 'a',
                var other => char.ToLowerInvariant(other) switch
                {
                    'ı' or 'i' or 'î' => 'i',
                    'ğ' => 'g',
                    'ü' => 'u',
                    'ş' => 's',
                    'ö' => 'o',
                    'ç' => 'c',
                    'â' => 'a',
                    var lowered => lowered
                }
            };

            if (char.IsLetterOrDigit(mapped))
            {
                builder.Append(mapped);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
