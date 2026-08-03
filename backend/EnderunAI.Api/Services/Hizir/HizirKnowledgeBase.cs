using System.Text;
using System.Text.Json;

namespace EnderunAI.Api.Services.Hizir;

public sealed record KnowledgePage(
    string Title,
    string Path,
    string? Permission,
    string Purpose,
    IReadOnlyList<string> Steps);

public sealed record KnowledgeModule(
    string Module,
    IReadOnlyList<KnowledgePage> Pages);

public interface IHizirKnowledgeBase
{
    /// <summary>
    /// Konuyla eşleşen sayfaları, YALNIZCA kullanıcının izni olanları
    /// döndürür. İzinsiz sayfa hiç görünmez — Hızır kullanıcıya
    /// açamayacağı bir sayfayı tarif etmez.
    /// </summary>
    string Search(string topic, IReadOnlyCollection<string> permissions);

    int PageCount { get; }
}

/// <summary>
/// Sistemin modül/sayfa haritası. Kaynak dosya
/// <c>Data/Seeds/hizir-knowledge-base.json</c>; menü ağacıyla
/// (components/erp/erp-shell.tsx) eşleşecek şekilde tutulur.
///
/// BAKIM: Yeni bir modül/paket tamamlandığında bu dosyaya o modülün
/// sayfaları eklenmelidir; aksi halde Hızır yeni ekranları tarif edemez.
/// </summary>
public sealed class HizirKnowledgeBase : IHizirKnowledgeBase
{
    /// <summary>Bir aramada bağlama girecek en fazla sayfa.</summary>
    private const int MaxResults = 6;

    private readonly IReadOnlyList<KnowledgeModule> _modules;
    private readonly ILogger<HizirKnowledgeBase> _logger;

    public HizirKnowledgeBase(
        IWebHostEnvironment environment,
        ILogger<HizirKnowledgeBase> logger)
    {
        _logger = logger;
        _modules = Load(environment);
    }

    public int PageCount => _modules.Sum(x => x.Pages.Count);

    private IReadOnlyList<KnowledgeModule> Load(IWebHostEnvironment environment)
    {
        var path = Path.Combine(
            environment.ContentRootPath, "Data", "Seeds", "hizir-knowledge-base.json");

        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "Hızır kullanım kılavuzu dosyası bulunamadı: {Path}", path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<List<KnowledgeModule>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Hızır kullanım kılavuzu okunamadı.");
            return [];
        }
    }

    public string Search(string topic, IReadOnlyCollection<string> permissions)
    {
        var visible = _modules
            .Select(module => new
            {
                module.Module,
                Pages = module.Pages
                    .Where(page =>
                        page.Permission is null ||
                        permissions.Contains(page.Permission, StringComparer.OrdinalIgnoreCase))
                    .ToList()
            })
            .Where(x => x.Pages.Count > 0)
            .ToList();

        if (visible.Count == 0)
            return string.Empty;

        var terms = (topic ?? string.Empty)
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3)
            .ToArray();

        var scored = visible
            .SelectMany(module => module.Pages.Select(page => new
            {
                module.Module,
                Page = page,
                Score = Score(module.Module, page, terms)
            }))
            .Where(x => terms.Length == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(MaxResults)
            .ToList();

        if (scored.Count == 0)
            return string.Empty;

        var builder = new StringBuilder(
            "Kullanım kılavuzu (yalnızca bu kullanıcının erişebildiği sayfalar):\n");

        foreach (var item in scored)
        {
            builder.AppendLine(
                $"- [{item.Module}] {item.Page.Title} ({item.Page.Path}): {item.Page.Purpose}");

            foreach (var step in item.Page.Steps)
                builder.AppendLine($"    · {step}");
        }

        return builder.ToString();
    }

    private static int Score(string module, KnowledgePage page, string[] terms)
    {
        if (terms.Length == 0)
            return 1;

        var haystack = string.Join(
            " ",
            module, page.Title, page.Purpose, string.Join(" ", page.Steps))
            .ToLowerInvariant();

        return terms.Count(term => haystack.Contains(term));
    }
}
