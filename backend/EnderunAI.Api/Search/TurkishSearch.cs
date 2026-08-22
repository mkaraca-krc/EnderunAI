namespace EnderunAI.Api.Search;

/// <summary>
/// TÜRKÇE ARAMA KATLAMASI — SUNUCU TARAFI.
///
/// Arayüzdeki <c>lib/search/fold.ts</c> ile BİREBİR AYNI kuralı
/// uygular: önce yerel bağımsız küçültme, sonra Türkçe harflerin ASCII
/// karşılığına katlanması. "sube" yazan "Şube"yi bulmalı; kullanıcı
/// arama kutusuna Türkçe karakter yazmak için klavye değiştirmez.
///
/// NEDEN AYNI OLMAK ZORUNDA: küçük listeler ekranda, büyük listeler
/// sunucuda süzülüyor. İki katlama ayrışırsa aynı arama bir listede
/// kaydı bulur, diğerinde bulamaz — ve kullanıcı hangisinin doğru
/// olduğunu bilemez. Eşitlik testle sabit (TurkishSearchFoldingTests).
///
/// ToLower() DEĞİL, ToLowerInvariant(): Türkçe kültürde "I" harfi
/// noktasız "ı"ya döner ve "SCHNEIDER" → "schneıder" olur; marka adları
/// aranamaz hale gelir. Bu tuzak canlıda yaşandı (eski arama seçicisi).
///
/// VERİTABANI KARŞILIĞI: accounting_accounts.SearchFold üretilmiş
/// kolonu aynı kuralı SQL'de uyguluyor —
/// translate(lower(...), 'ışğüöçâîû', 'isguocaiu').
/// </summary>
public static class TurkishSearch
{
    /// <summary>Katlanan harfler ve ASCII karşılıkları (fold.ts ile aynı sıra).</summary>
    private static readonly (char From, char To)[] Map =
    [
        ('ı', 'i'),
        // "İ" (U+0130): .NET'te ToLowerInvariant() bunu DEĞİŞTİRMİYOR
        // (ölçüldü: "İSTANBUL" -> "İstanbul"). JS ise "i" + birleşik
        // nokta üretiyor, PostgreSQL ise düz "i". Üçü ayrışıyordu;
        // burada elle katlanıyor.
        ('\u0130', 'i'),
        ('ş', 's'),
        ('ğ', 'g'),
        ('ü', 'u'),
        ('ö', 'o'),
        ('ç', 'c'),
        ('â', 'a'),
        ('î', 'i'),
        ('û', 'u'),
    ];

    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lowered = text.ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            // Birleşik nokta (U+0307): "İ".ToLowerInvariant() bazı
            // ortamlarda "i" + birleşik nokta üretiyor. fold.ts de bunu
            // "i"ye indiriyor; nokta atılmazsa "İSTANBUL" ile "istanbul"
            // eşleşmezdi.
            if (character == '̇') continue;

            var replaced = character;

            foreach (var (from, to) in Map)
            {
                if (character == from)
                {
                    replaced = to;
                    break;
                }
            }

            builder.Append(replaced);
        }

        return builder.ToString();
    }
}
