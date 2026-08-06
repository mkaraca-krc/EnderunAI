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

    /// <summary>
    /// Belirteçleri önceden çıkarılmış aday. Aynı havuz onlarca satır
    /// için taranırken adayın metnini her satırda yeniden parçalamak
    /// işin neredeyse tamamını oluşturuyordu; toplu eşleştirmede havuz
    /// bir kez hazırlanıp tekrar tekrar kullanılır.
    /// </summary>
    public sealed record PreparedCandidate(
        MatchCandidate Candidate,
        IReadOnlyList<string> Tokens,
        HashSet<string> Haystack);

    public static PreparedCandidate Prepare(MatchCandidate candidate)
    {
        var tokens = Tokenize(
            $"{candidate.Name} {candidate.Category} {candidate.Keywords} {candidate.Unit}");

        var haystack = new HashSet<string>(tokens, StringComparer.Ordinal);

        // "1x40 mm2" tek belirteç olarak "1x40" veriyor; içindeki sayı
        // dizileri ayrıca eklenmezse "40" sorgusu tutmaz. Kesit bilgisi
        // kabloda en ayırt edici veri, kaybedilemez.
        foreach (var token in tokens)
        {
            foreach (var run in System.Text.RegularExpressions.Regex.Matches(token, @"\d+"))
                haystack.Add(run.ToString());
        }

        return new PreparedCandidate(candidate, tokens, haystack);
    }

    public static IReadOnlyList<PreparedCandidate> PrepareAll(
        IEnumerable<MatchCandidate> candidates)
        => candidates.Select(Prepare).ToList();

    /// <summary>
    /// Belirteçten adaylara ters dizin.
    ///
    /// Anahtar, belirtecin ilk dört harfi (kısa belirteçlerde tamamı).
    /// Skorlama yalnızca birebir eşleşmeyi ve ön ek eşleşmesini (ikisi de
    /// en az dört harf) puanladığı için, aynı kovada olmayan bir aday
    /// zaten sıfır alır: dizin GERÇEK aday kaybettirmez, yalnızca hiç
    /// puan alamayacak adayları taramaz.
    ///
    /// Bunsuz 350 satırlık bir icmal 23 binlik kütüphaneye karşı 8
    /// milyon skorlama demek — ölçülen süre bir dakikanın üstünde.
    /// </summary>
    public sealed class CandidateIndex
    {
        private readonly Dictionary<string, List<PreparedCandidate>> buckets;

        /// <summary>
        /// Resmi poz numarasına göre ayrı dizin. Kod, adayın metin
        /// belirteçlerine KATILMIYOR (katılsaydı sorgudaki her sayı
        /// rastgele kodlara puan verirdi); birebir kod eşleşmesi bu
        /// yüzden ayrı tutuluyor.
        /// </summary>
        private readonly Dictionary<string, PreparedCandidate> byOfficialCode;

        public CandidateIndex(IEnumerable<PreparedCandidate> candidates)
        {
            buckets = new Dictionary<string, List<PreparedCandidate>>(StringComparer.Ordinal);
            byOfficialCode = new Dictionary<string, PreparedCandidate>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                var official = candidate.Candidate.OfficialCode?.Trim();

                if (!string.IsNullOrEmpty(official))
                    byOfficialCode.TryAdd(official, candidate);

                foreach (var token in candidate.Haystack)
                {
                    var key = BucketKey(token);

                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = [];
                        buckets[key] = bucket;
                    }

                    bucket.Add(candidate);
                }
            }
        }

        public IReadOnlyList<PreparedCandidate> CandidatesFor(
            IReadOnlyList<string> terms, IReadOnlyList<string>? codeTerms = null)
        {
            var seen = new HashSet<Guid>();
            var result = new List<PreparedCandidate>();

            foreach (var code in codeTerms ?? [])
            {
                if (byOfficialCode.TryGetValue(code.Trim(), out var exact)
                    && seen.Add(exact.Candidate.Id))
                {
                    result.Add(exact);
                }
            }

            foreach (var term in terms)
            {
                // Yalnızca rakam tutan aday zaten elenecek; sayı
                // kovalarını taramak hem gereksiz hem de en pahalısı
                // (bir kütüphanede "2" binlerce pozda geçer).
                if (term.All(char.IsDigit))
                    continue;

                if (!buckets.TryGetValue(BucketKey(term), out var bucket))
                    continue;

                foreach (var candidate in bucket)
                {
                    if (seen.Add(candidate.Candidate.Id))
                        result.Add(candidate);
                }
            }

            return result;
        }

        private static string BucketKey(string token) =>
            token.Length >= 4 ? token[..4] : token;
    }

    public static IReadOnlyList<MatchScore> Rank(
        string query,
        IEnumerable<MatchCandidate> candidates,
        int limit = DefaultLimit)
        => Rank(query, PrepareAll(candidates), limit);

    /// <summary>
    /// Dizin üzerinden sıralama: yalnızca puan alabilecek adaylar
    /// taranır. Sonuç, tüm havuzu taramakla birebir aynıdır.
    /// </summary>
    public static IReadOnlyList<MatchScore> Rank(
        string query,
        CandidateIndex index,
        int limit = DefaultLimit)
    {
        var terms = Tokenize(query);

        if (terms.Count == 0)
            return [];

        return Rank(query, index.CandidatesFor(terms, ExtractCodes(query)), limit);
    }

    public static IReadOnlyList<MatchScore> Rank(
        string query,
        IReadOnlyList<PreparedCandidate> candidates,
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
                results.Add(new MatchScore(candidate.Candidate, score, matched));
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
        PreparedCandidate prepared,
        out List<string> matched)
    {
        matched = [];

        var candidate = prepared.Candidate;
        var official = candidate.OfficialCode?.Trim();

        // Kullanıcı doğrudan poz numarası yazdıysa tartışma yok.
        if (official is not null
            && codeTerms.Any(c => string.Equals(c, official, StringComparison.OrdinalIgnoreCase)))
        {
            matched.Add(official);
            return 1000;
        }

        var haystackTokens = prepared.Tokens;

        if (haystackTokens.Count == 0)
            return 0;

        var haystack = prepared.Haystack;
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

        // YALNIZCA rakam tutan aday elenir. Sayılar tek başına ayırt
        // edici değil: "A Blok / At-2/4/6/8/10 Panosu" satırındaki
        // rakamlar "1 X 150 / 25 mm2" kablo pozuyla da tutuyor ve sayılar
        // yüksek puanlı olduğu için alakasız poz listenin başına çıkıyor.
        // İşin ne olduğunu söyleyen kelimedir; en az bir kelime tutmayan
        // aday öneri değildir.
        if (matched.All(x => x.All(char.IsDigit)))
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
