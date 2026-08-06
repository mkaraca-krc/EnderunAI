using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Hizir;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Engineering;

public sealed record PositionSuggestion(
    Guid PositionId,
    string Code,
    string? OfficialCode,
    string Name,
    string Unit,
    string? Institution,
    string? Category,
    double Score,
    IReadOnlyList<string> MatchedTerms,
    decimal? UnitPrice,
    decimal? MaterialPrice,
    decimal? LaborPrice,
    string? PriceExplanation,
    /// <summary>Dil modelinin sırası (1 en uygun); model kullanılmadıysa null.</summary>
    int? AiRank,
    string? AiReason);

public sealed record PositionMatchResult(
    string Query,
    bool AiUsed,
    /// <summary>Model kullanılmadıysa nedeni; kullanıldıysa null.</summary>
    string? AiSkippedReason,
    IReadOnlyList<PositionSuggestion> Suggestions,
    string Explanation,
    /// <summary>
    /// Eşleşme KESİN mi — otomatik seçilebilir mi. Kesin değilse
    /// kullanıcı aday listesinden seçmeli; sistem kendi başına karar
    /// vermez.
    /// </summary>
    bool IsCertain = false,
    string? CertaintyReason = null);

/// <summary>Toplu eşleştirmede tek satırın sonucu.</summary>
public sealed record BulkMatchRow(
    int RowNumber,
    string Query,
    bool IsCertain,
    string? CertaintyReason,
    IReadOnlyList<PositionSuggestion> Suggestions);

public interface IPositionMatchService
{
    Task<PositionMatchResult> SuggestAsync(
        Guid companyId,
        string query,
        int? year = null,
        int limit = PositionMatcher.DefaultLimit,
        bool useAi = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çok satırlı eşleştirme. Toplu icmal aktarımında satır başına
    /// ayrı sorgu atmak yüzlerce satırda kabul edilemez; adaylar TEK
    /// seferde çekilip skorlama bellekte yapılır. Dil modeli
    /// kullanılmaz — toplu akışta yüzlerce model çağrısı ne makul ne
    /// gerekli.
    /// </summary>
    Task<IReadOnlyList<BulkMatchRow>> SuggestBulkAsync(
        Guid companyId,
        IReadOnlyList<(int RowNumber, string Query)> rows,
        int? year = null,
        int limit = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Serbest metin iş tanımından poz önerir.
///
/// İki kademe:
/// 1. <b>Deterministik eleme</b> — poz kodu, tanım, kategori ve anahtar
///    kelimeler üzerinde belirteç skorlaması. Adayları YALNIZCA bu üretir.
/// 2. <b>Dil modeli sıralaması</b> — model, birinci kademenin verdiği
///    listeyi sıralar ve gerekçelendirir. Listeye yeni poz EKLEYEMEZ;
///    döndürdüğü her kod aday listesine karşı doğrulanır, tutmayan
///    atılır.
///
/// Bu yapı sayesinde "uydurma poz" yapısal olarak imkânsız: model
/// kendi başına bir poz numarası üretse bile doğrulama onu eler.
/// Aday yoksa hiçbir şey önerilmez — kullanıcı elle girer.
/// </summary>
public sealed class PositionMatchService(
    AppDbContext db,
    IPositionPriceService prices,
    IHizirLlmClient llm,
    ILogger<PositionMatchService> logger) : IPositionMatchService
{
    /// <summary>Skorlamaya girecek en fazla kayıt — toplam ön eleme sınırı.</summary>
    private const int PrefilterLimit = 600;

    /// <summary>Tek bir belirtecin getirebileceği en fazla kayıt.</summary>
    private const int PrefilterPerTermLimit = 200;

    /// <summary>Toplu eşleştirmede ön elemeye giren en fazla belirteç.</summary>
    private const int BulkTermLimit = 40;

    /// <summary>
    /// Tek sorguda aranacak en fazla belirteç. Toplu akışta bu sınır
    /// yetmiyor: onlarca satırın kelimeleri birleşiyor ve ilk sekizde
    /// kalan bir sınır, sonraki satırların pozlarını havuza hiç
    /// almıyordu — o satırlar sessizce eşleşmesiz görünüyordu.
    /// </summary>
    private const int SingleQueryTermLimit = 8;

    public async Task<PositionMatchResult> SuggestAsync(
        Guid companyId,
        string query,
        int? year = null,
        int limit = PositionMatcher.DefaultLimit,
        bool useAi = true,
        CancellationToken cancellationToken = default)
    {
        var terms = PositionMatcher.Tokenize(query);

        if (terms.Count == 0)
        {
            return new PositionMatchResult(
                query, false, "Sorgu boş.", [],
                "Aranacak bir iş tanımı girin.");
        }

        var candidates = await PrefilterAsync(companyId, terms, cancellationToken);

        if (candidates.Count == 0)
        {
            return new PositionMatchResult(
                query, false, null, [],
                "Bu tanıma uyan poz bulunamadı. Poz kütüphanesinde karşılığı yoksa " +
                "özel poz tanımlayabilirsiniz.");
        }

        var ranked = PositionMatcher.Rank(query, candidates, limit);

        if (ranked.Count == 0)
        {
            return new PositionMatchResult(
                query, false, null, [],
                "Yeterince benzer poz bulunamadı. Tanımı biraz daha açık yazmayı " +
                "deneyebilirsiniz; yine çıkmazsa özel poz tanımlayın.");
        }

        var suggestions = await BuildSuggestionsAsync(ranked, year, cancellationToken);
        var (isCertain, certaintyReason) = EvaluateCertainty(ranked);

        if (!useAi)
        {
            return new PositionMatchResult(
                query, false, "Yapay zekâ sıralaması istenmedi.", suggestions,
                $"{suggestions.Count} aday benzerlik skoruna göre sıralandı.",
                isCertain, certaintyReason);
        }

        if (!llm.IsConfigured)
        {
            return new PositionMatchResult(
                query, false, "Dil modeli yapılandırılmamış.", suggestions,
                $"{suggestions.Count} aday benzerlik skoruna göre sıralandı.",
                isCertain, certaintyReason);
        }

        try
        {
            var reranked = await RankWithAiAsync(query, suggestions, cancellationToken);

            return new PositionMatchResult(
                query, true, null, reranked,
                $"{reranked.Count} aday listelendi. Sıralama ve gerekçeler yapay zekâdan; " +
                "adaylar kütüphaneden geldi, kesin eşleşme kararını siz verin.",
                isCertain, certaintyReason);
        }
        catch (Exception ex)
        {
            // Model erişilemezse öneri kaybolmaz, skor sıralaması kalır.
            logger.LogWarning(ex, "Poz eşleştirmede dil modeli kullanılamadı.");

            return new PositionMatchResult(
                query, false, $"Dil modeline ulaşılamadı ({ex.GetType().Name}).",
                suggestions,
                $"{suggestions.Count} aday benzerlik skoruna göre sıralandı.",
                isCertain, certaintyReason);
        }
    }

    /// <summary>
    /// Eşleşmenin KESİN sayılıp otomatik seçilebileceği durumlar.
    ///
    /// İki koşuldan biri: (a) kullanıcı poz numarasını yazmıştır —
    /// tartışma yok; (b) en iyi aday ikinciden belirgin biçimde
    /// öndedir. İkisi de yoksa karar insana bırakılır: yakın iki aday
    /// arasından sistemin seçmesi, yanlış pozla fiyatlanmış bir keşif
    /// üretir ve bunu sonradan fark etmek çok zordur.
    /// </summary>
    private const double CertaintyDominanceFactor = 2.0;
    private const double CertaintyMinimumScore = 40.0;

    private static (bool IsCertain, string? Reason) EvaluateCertainty(
        IReadOnlyList<MatchScore> ranked)
    {
        if (ranked.Count == 0)
            return (false, null);

        var best = ranked[0];

        if (best.Score >= 1000)
            return (true, "Poz numarası birebir eşleşti.");

        if (best.Score < CertaintyMinimumScore)
            return (false, "Benzerlik yeterince yüksek değil; aday listesinden seçin.");

        if (ranked.Count == 1)
            return (true, "Tek aday var ve benzerlik yüksek.");

        var runnerUp = ranked[1].Score;

        if (runnerUp <= 0 || best.Score >= runnerUp * CertaintyDominanceFactor)
            return (true, "En iyi aday diğerlerinden belirgin biçimde önde.");

        return (false, "Birbirine yakın birden çok aday var; seçim size ait.");
    }

    public async Task<IReadOnlyList<BulkMatchRow>> SuggestBulkAsync(
        Guid companyId,
        IReadOnlyList<(int RowNumber, string Query)> rows,
        int? year = null,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return [];

        // Bütün satırların belirteçleri birleştirilip TEK ön eleme
        // yapılır; satır başına sorgu yüzlerce satırda kabul edilemez.
        var allTerms = rows
            .SelectMany(x => PositionMatcher.Tokenize(x.Query))
            .Distinct(StringComparer.Ordinal)
            .Take(BulkTermLimit)
            .ToList();

        var pool = allTerms.Count == 0
            ? []
            : await PrefilterAsync(companyId, allTerms, cancellationToken, BulkTermLimit);

        var results = new List<BulkMatchRow>(rows.Count);

        foreach (var (rowNumber, query) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ranked = PositionMatcher.Rank(query, pool, limit);
            var (isCertain, reason) = EvaluateCertainty(ranked);

            var suggestions = await BuildSuggestionsAsync(ranked, year, cancellationToken);

            results.Add(new BulkMatchRow(
                rowNumber, query, isCertain, reason, suggestions));
        }

        return results;
    }

    /// <summary>
    /// SQL ön elemesi: belirteçlerden herhangi biri tanımda, kategoride,
    /// anahtar kelimelerde ya da resmi kodda geçen pozlar. Skorlama
    /// bellekte yapılıyor; 20 binin üzerindeki kütüphaneyi tamamen
    /// çekmek gereksiz.
    /// </summary>
    private async Task<List<MatchCandidate>> PrefilterAsync(
        Guid companyId,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken,
        int maxTerms = SingleQueryTermLimit)
    {
        // Belirteç başına ayrı sorgu: OR zincirini tek ifadede kurmak
        // EF tarafından SQL'e çevrilemiyor. Belirteç sayısı azdır
        // (sorgu bir iş tanımı, roman değil) ve her sorgu kendi
        // sınırıyla dönüyor.
        var merged = new Dictionary<Guid, MatchCandidate>();

        foreach (var term in terms.Take(maxTerms))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pattern = $"%{term}%";

            var rows = await db.EngineeringPositions
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.Status != EngineeringPositionStatus.Archived
                            && (EF.Functions.ILike(x.Name, pattern)
                                || (x.SearchKeywords != null
                                    && EF.Functions.ILike(x.SearchKeywords, pattern))
                                || (x.OfficialCode != null
                                    && EF.Functions.ILike(x.OfficialCode, pattern))))
                .OrderBy(x => x.Name.Length)
                .Take(PrefilterPerTermLimit)
                .Select(x => new MatchCandidate(
                    x.Id, x.Code, x.OfficialCode, x.Name, x.Unit, x.Category, x.SearchKeywords))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
                merged.TryAdd(row.Id, row);

            if (merged.Count >= PrefilterLimit)
                break;
        }

        return merged.Values.ToList();
    }

    private async Task<List<PositionSuggestion>> BuildSuggestionsAsync(
        IReadOnlyList<MatchScore> ranked, int? year, CancellationToken cancellationToken)
    {
        var ids = ranked.Select(x => x.Candidate.Id).ToList();

        var institutions = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.OfficialInstitution, cancellationToken);

        var suggestions = new List<PositionSuggestion>(ranked.Count);

        foreach (var match in ranked)
        {
            var price = await prices.ResolveAsync(
                match.Candidate.Id, year, cancellationToken: cancellationToken);

            suggestions.Add(new PositionSuggestion(
                match.Candidate.Id,
                match.Candidate.Code,
                match.Candidate.OfficialCode,
                match.Candidate.Name,
                match.Candidate.Unit,
                institutions.GetValueOrDefault(match.Candidate.Id),
                match.Candidate.Category,
                Math.Round(match.Score, 1),
                match.MatchedTerms,
                price.UnitPrice,
                price.MaterialPrice,
                price.LaborPrice,
                price.Explanation,
                null,
                null));
        }

        return suggestions;
    }

    /// <summary>
    /// Modele adayları verir ve SIRALAMASINI ister. Model yalnızca
    /// verilen kodlar arasından seçebilir; döndürdüğü her kod listeye
    /// karşı doğrulanır.
    /// </summary>
    private async Task<List<PositionSuggestion>> RankWithAiAsync(
        string query,
        IReadOnlyList<PositionSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        var catalog = new StringBuilder();

        foreach (var suggestion in suggestions)
        {
            catalog.AppendLine(
                $"- {suggestion.OfficialCode ?? suggestion.Code} | {suggestion.Name} " +
                $"| birim: {suggestion.Unit}" +
                (suggestion.Institution is null ? "" : $" | kurum: {suggestion.Institution}"));
        }

        const string systemPrompt =
            "Sen bir elektrik taahhüt firmasının keşif mühendisisin. Sana bir iş " +
            "tanımı ve ADAY POZ LİSTESİ verilir. Görevin bu listeyi işe uygunluğa " +
            "göre sıralamak ve her biri için tek cümlelik kısa gerekçe yazmak.\n\n" +
            "KURALLAR:\n" +
            "1. YALNIZCA sana verilen poz numaralarını kullan. Listede olmayan bir " +
            "poz numarası ASLA yazma.\n" +
            "2. Hiçbiri uymuyorsa boş liste döndür.\n" +
            "3. Gerekçede uydurma teknik ayrıntı verme; yalnızca tanımdan çıkanı yaz.\n" +
            "4. Yanıtı SADECE şu JSON biçiminde ver, başka metin ekleme:\n" +
            "{\"sonuclar\":[{\"poz\":\"35.100.1301\",\"gerekce\":\"...\"}]}";

        var userMessage =
            $"İş tanımı: {query}\n\nAday pozlar:\n{catalog}";

        var completion = await llm.CompleteAsync(
            systemPrompt,
            [new LlmMessage(LlmRole.User, userMessage)],
            [],
            cancellationToken);

        var ordering = ParseOrdering(completion.Text);

        if (ordering.Count == 0)
            return suggestions.ToList();

        var byCode = suggestions.ToDictionary(
            x => (x.OfficialCode ?? x.Code).Trim(),
            StringComparer.OrdinalIgnoreCase);

        var result = new List<PositionSuggestion>();
        var rank = 1;

        foreach (var (code, reason) in ordering)
        {
            // DOĞRULAMA: model listede olmayan bir kod döndürdüyse atılır.
            if (!byCode.TryGetValue(code.Trim(), out var suggestion))
            {
                logger.LogWarning(
                    "Poz eşleştirmede model aday listesi dışında kod döndürdü: {Code}", code);

                continue;
            }

            if (result.Any(x => x.PositionId == suggestion.PositionId))
                continue;

            result.Add(suggestion with { AiRank = rank++, AiReason = reason });
        }

        // Modelin sıralamadığı adaylar kaybolmaz, sona eklenir.
        foreach (var suggestion in suggestions)
        {
            if (result.All(x => x.PositionId != suggestion.PositionId))
                result.Add(suggestion);
        }

        return result;
    }

    private static List<(string Code, string Reason)> ParseOrdering(string? text)
    {
        var result = new List<(string, string)>();

        if (string.IsNullOrWhiteSpace(text))
            return result;

        // Model bazen JSON'u kod bloğuna sarar.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
            return result;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);

            if (!document.RootElement.TryGetProperty("sonuclar", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in items.EnumerateArray())
            {
                var code = item.TryGetProperty("poz", out var codeValue)
                    ? codeValue.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var reason = item.TryGetProperty("gerekce", out var reasonValue)
                    ? reasonValue.GetString()
                    : null;

                result.Add((code, reason ?? string.Empty));
            }
        }
        catch (JsonException)
        {
            // Bozuk yanıt sessizce yok sayılır; skor sıralaması geçerli kalır.
        }

        return result;
    }
}
