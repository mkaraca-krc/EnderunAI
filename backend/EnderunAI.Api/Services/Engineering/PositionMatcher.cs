using System.Text;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>Eşleştirmeye giren aday poz — veritabanından bağımsız.</summary>
public sealed record MatchCandidate(
    Guid Id,
    string Code,
    string? OfficialCode,
    string Name,
    string Unit,
    string? Category,
    string? Keywords);

public sealed record MatchScore(
    MatchCandidate Candidate,
    double Score,
    IReadOnlyList<string> MatchedTerms);

/// <summary>
/// Serbest metinden poz adaylarını bulan deterministik eleyici.
///
/// Saf ve statik: ağ, veritabanı ve dil modeli yok. Dil modeli bu
/// listeyi yalnızca SIRALAR; aday üretmez. Böylece modelin var olmayan
/// bir poz numarası uydurması yapısal olarak imkânsız.
///
/// Türkçe küçültme ayrıca ele alınıyor: <c>ToLowerInvariant</c> "İ"yi
/// olduğu gibi bırakır, "I"yı da "i"ye çevirir. Poz tanımlarında
/// "İLETKEN", "KABLO", "IZGARA" gibi kelimeler geçtiği için bu sessizce
/// eşleşme kaybettirirdi.
/// </summary>
public static class PositionMatcher
{
    /// <summary>
    /// Tek başına ayırt edici olmayan kelimeler. Poz tanımlarının
    /// neredeyse tamamında geçtikleri için skoru bozarlar.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "ile", "veya", "her", "nevi", "dahil", "adet", "olan", "için",
        "bir", "bu", "da", "de", "tip", "tipi", "isin", "işin", "cinsi",
        "yapilmasi", "yapılması", "temini", "montaji", "montajı"
    };

    /// <summary>Aday sayısı: modele verilecek liste bundan uzun olmamalı.</summary>
    public const int DefaultLimit = 8;

    /// <summary>
    /// Bu skorun altındaki adaylar gösterilmez. Rastgele bir ortak
    /// kelime yüzünden alakasız poz önermek, hiç önermemekten kötüdür.
    /// </summary>
    public const double MinimumScore = 12.0;

    public static IReadOnlyList<MatchScore> Rank(
        string query,
        IEnumerable<MatchCandidate> candidates,
        int limit = DefaultLimit)
    {
        var terms = Tokenize(query);

        if (terms.Count == 0)
            return [];

        // Poz numarası noktalı yazıldığı için belirteçlere bölününce
        // parçalanıyor ("35.415.1610" → 35, 415, 1610). Ham sorguda
        // ayrıca aranıyor; kullanıcı kodu yazdıysa tartışma bitsin.
        var codeTerms = ExtractCodes(query);

        var results = new List<MatchScore>();

        foreach (var candidate in candidates)
        {
            var score = Score(terms, codeTerms, candidate, out var matched);

            if (score >= MinimumScore)
                results.Add(new MatchScore(candidate, score, matched));
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.OfficialCode ?? x.Candidate.Code)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Serbest metni aranabilir belirteçlere böler. Kesme işareti
    /// ayırıcıdır ("40'lık" → "40", "lık"); iki karakterden kısa
    /// sözcükler atılır ama SAYILAR korunur — kesit ve ölçü bilgisi
    /// kabloda en ayırt edici veridir.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var normalized = Normalize(text);
        var tokens = new List<string>();
        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            Flush(builder, tokens);
        }

        Flush(builder, tokens);

        return tokens
            .Where(x => !StopWords.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void Flush(StringBuilder builder, List<string> tokens)
    {
        if (builder.Length == 0)
            return;

        var token = builder.ToString();
        builder.Clear();

        // Sayılar tek haneli bile olsa anlamlı (kesit, kutup sayısı).
        if (token.Length >= 3 || token.All(char.IsDigit))
            tokens.Add(token);
    }

    /// <summary>Sorgudaki poz numarası biçimindeki ifadeler.</summary>
    public static IReadOnlyList<string> ExtractCodes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return System.Text.RegularExpressions.Regex
            .Matches(text, @"\d{2}\.\d{3}\.\d{3,4}")
            .Select(x => x.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static double Score(
        IReadOnlyList<string> terms,
        IReadOnlyList<string> codeTerms,
        MatchCandidate candidate,
        out List<string> matched)
    {
        matched = [];

        var official = candidate.OfficialCode?.Trim();

        // Kullanıcı doğrudan poz numarası yazdıysa tartışma yok.
        if (official is not null
            && codeTerms.Any(c => string.Equals(c, official, StringComparison.OrdinalIgnoreCase)))
        {
            matched.Add(official);
            return 1000;
        }

        var haystackTokens = Tokenize(
            $"{candidate.Name} {candidate.Category} {candidate.Keywords} {candidate.Unit}");

        if (haystackTokens.Count == 0)
            return 0;

        // "1x40 mm2" tek belirteç olarak "1x40" veriyor; içindeki sayı
        // dizileri ayrıca eklenmezse "40" sorgusu tutmaz. Kesit bilgisi
        // kabloda en ayırt edici veri, kaybedilemez.
        var haystack = new HashSet<string>(haystackTokens, StringComparer.Ordinal);

        foreach (var token in haystackTokens)
        {
            foreach (var run in System.Text.RegularExpressions.Regex.Matches(token, @"\d+"))
                haystack.Add(run.ToString()!);
        }
        var score = 0.0;

        foreach (var term in terms)
        {
            if (haystack.Contains(term))
            {
                // Sayılar (kesit, ölçü) kelimelerden daha ayırt edici.
                score += term.All(char.IsDigit) ? 18 : 12;
                matched.Add(term);
                continue;
            }

            // Kısmi eşleşme: "kablolar" ile "kablo" aynı işi anlatıyor.
            var partial = haystackTokens.FirstOrDefault(x =>
                x.Length >= 4 && term.Length >= 4
                && (x.StartsWith(term, StringComparison.Ordinal)
                    || term.StartsWith(x, StringComparison.Ordinal)));

            if (partial is not null)
            {
                score += 6;
                matched.Add(term);
            }
        }

        if (matched.Count == 0)
            return 0;

        // Sorgunun ne kadarının karşılandığı: iki kelimelik sorgunun
        // ikisini de tutan aday, on kelimelik tanımda tek kelime tutan
        // adaydan iyidir.
        var coverage = (double)matched.Count / terms.Count;
        score *= 0.6 + (0.8 * coverage);

        // Aynı skorda kısa tanım tercih edilir: uzun teknik şartname
        // metinleri rastgele kelime tutturmaya meyilli.
        if (haystackTokens.Count > 40)
            score *= 0.85;

        return score;
    }

    /// <summary>
    /// Türkçe duyarlı küçültme. "İ" → "i", "I" → "ı" eşlemesi elle
    /// yapılıyor; kültüre bırakılırsa sunucu kültürüne göre değişir.
    /// </summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            builder.Append(character switch
            {
                'İ' => 'i',
                'I' => 'ı',
                'Ş' => 'ş',
                'Ğ' => 'ğ',
                'Ü' => 'ü',
                'Ö' => 'ö',
                'Ç' => 'ç',
                _ => char.ToLowerInvariant(character)
            });
        }

        return builder.ToString();
    }
}
