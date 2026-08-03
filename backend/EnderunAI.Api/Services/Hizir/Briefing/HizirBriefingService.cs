using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EnderunAI.Api.Services.Hizir.Briefing;

public sealed record BriefingItemView(
    string Title,
    string? Detail,
    int Severity,
    string? TargetPath);

public sealed record HizirBriefingView(
    string Greeting,
    string Headline,
    DateTime GeneratedAtUtc,
    IReadOnlyList<BriefingItemView> Items);

public interface IHizirBriefingService
{
    Task<HizirBriefingView> BuildAsync(CancellationToken cancellationToken);

    /// <summary>Brifingi kullanıcının KENDİ kayıtlı adresine gönderir.</summary>
    Task<string> EmailToSelfAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Günlük brifing. Kalemler tamamen veriden üretilir; dil modeli
/// yalnızca tek cümlelik başlığı yazar ve o da günde bir kez.
///
/// MALİYET: Kalem üretimi LLM'siz olduğu için brifingin token maliyeti
/// yoktur. Başlık için kullanıcı başına GÜNDE EN FAZLA BİR çağrı yapılır
/// ve gün sonuna kadar önbellekte tutulur. Gösterilecek kalem yoksa
/// çağrı hiç yapılmaz.
///
/// YETKİ: Her kaynak, kullanıcının izni yoksa hiç çalıştırılmaz. Bir
/// kullanıcının brifinginde yetkisi dışındaki hiçbir sayı belirmez.
/// </summary>
public sealed class HizirBriefingService(
    AppDbContext db,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    ICurrentDataScopeService dataScope,
    IEnumerable<IHizirBriefingSource> sources,
    IHizirLlmClient llm,
    IMemoryCache cache,
    IEmailService emailService,
    ILogger<HizirBriefingService> logger) : IHizirBriefingService
{
    private const string NoNews = "Bugün öne çıkan bir şey yok.";

    public async Task<HizirBriefingView> BuildAsync(CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken);

        var items = new List<BriefingItem>();

        foreach (var source in sources)
        {
            // Yetki sınırı: izni olmayan kaynak hiç çalışmaz.
            if (source.RequiredPermission is not null &&
                !context.Has(source.RequiredPermission))
            {
                continue;
            }

            try
            {
                items.AddRange(await source.BuildAsync(context, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Tek kaynağın hatası tüm brifingi düşürmemeli.
                logger.LogWarning(exception,
                    "Brifing kaynağı çalışmadı: {Kaynak}", source.Key);
            }
        }

        var ordered = items
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Title, StringComparer.CurrentCulture)
            .Select(x => new BriefingItemView(
                x.Title, x.Detail, (int)x.Severity, x.TargetPath))
            .ToList();

        var greeting = BuildGreeting(context);

        var headline = ordered.Count == 0
            ? NoNews
            : await GetHeadlineAsync(context, ordered, cancellationToken);

        return new HizirBriefingView(
            greeting, headline, DateTime.UtcNow, ordered);
    }

    public async Task<string> EmailToSelfAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new InvalidOperationException("Oturum bulunamadı.");

        var recipient = await db.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.Email, x.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
        {
            throw new InvalidOperationException(
                "Kullanıcı kaydınızda e-posta adresi yok. Brifing " +
                "gönderilebilmesi için profilinize adres eklenmeli.");
        }

        var briefing = await BuildAsync(cancellationToken);

        var body = new StringBuilder();
        body.Append("<p>").Append(Encode(briefing.Greeting)).Append("</p>");
        body.Append("<p>").Append(Encode(briefing.Headline)).Append("</p>");

        if (briefing.Items.Count > 0)
        {
            body.Append("<ul>");

            foreach (var item in briefing.Items)
            {
                body.Append("<li><strong>").Append(Encode(item.Title)).Append("</strong>");

                if (!string.IsNullOrWhiteSpace(item.Detail))
                    body.Append("<br/>").Append(Encode(item.Detail));

                body.Append("</li>");
            }

            body.Append("</ul>");
        }

        body.Append("<p style=\"color:#64748b;font-size:12px\">")
            .Append("Bu özet Hızır tarafından, yalnızca sizin yetkinizdeki ")
            .Append("verilerden hazırlandı.</p>");

        await emailService.SendAsync(
            recipient.Email!, recipient.FullName,
            $"Hızır günlük özeti - {DateTime.UtcNow:dd.MM.yyyy}",
            body.ToString(), cancellationToken);

        return $"Günlük özet {recipient.Email} adresine gönderildi.";
    }

    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    private async Task<HizirToolContext> BuildContextAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new InvalidOperationException("Oturum bulunamadı.");

        var snapshot = await authorization.GetAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Kullanıcı yetkileri okunamadı.");

        var scope = await dataScope.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Veri kapsamı okunamadı.");

        var honorific = await db.Users
            .Where(x => x.Id == userId)
            .Select(x => x.Honorific)
            .SingleOrDefaultAsync(cancellationToken);

        return new HizirToolContext(
            userId,
            currentUser.FullName ?? currentUser.Username ?? "Kullanıcı",
            honorific,
            snapshot.RoleNames,
            snapshot.Permissions,
            scope);
    }

    /// <summary>Saate ve hitaba göre karşılama; sohbetteki üslupla aynı.</summary>
    private static string BuildGreeting(HizirToolContext context)
    {
        // Sunucu UTC çalışıyor; kullanıcı Türkiye saatinde.
        var localHour = DateTime.UtcNow.AddHours(3).Hour;

        var timeOfDay = localHour switch
        {
            >= 5 and < 12 => "Günaydın",
            >= 12 and < 18 => "İyi günler",
            _ => "İyi akşamlar"
        };

        var firstName = context.FullName.Split(' ')[0];

        return string.IsNullOrWhiteSpace(context.Honorific)
            ? $"{timeOfDay} {firstName}."
            : $"{timeOfDay} {firstName} {context.Honorific}.";
    }

    /// <summary>
    /// Tek cümlelik doğal dil başlığı. Kullanıcı başına günde bir kez
    /// üretilir; LLM erişilemezse veriden kurulan yedek cümle kullanılır,
    /// brifing yine de çalışır.
    /// </summary>
    private async Task<string> GetHeadlineAsync(
        HizirToolContext context,
        IReadOnlyList<BriefingItemView> items,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackHeadline(items);

        if (!llm.IsConfigured)
            return fallback;

        var today = DateTime.UtcNow.Date;
        var key = $"hizir-brifing-baslik:{context.UserId}:{today:yyyyMMdd}";

        // Kalemler gün içinde değişebilir; başlığın kalemlerle tutarsız
        // kalmaması için önbellek anahtarına kalem imzası da girer.
        var signature = string.Join("|", items.Select(x => x.Title)).GetHashCode();
        key += $":{signature}";

        if (cache.TryGetValue(key, out string? cached) && cached is not null)
            return cached;

        try
        {
            var prompt =
                "Aşağıdaki maddeler bir inşaat şirketi yöneticisinin günlük " +
                "özetidir. Bunları TEK cümlede, Türkçe, sakin ve iş odaklı " +
                "bir dille özetle. Yeni bilgi, yeni sayı veya tahmin EKLEME; " +
                "yalnızca verilenleri özetle. Selamlama yazma.\n\n" +
                string.Join("\n", items.Select(x =>
                    $"- {x.Title}{(string.IsNullOrWhiteSpace(x.Detail) ? "" : $" ({x.Detail})")}"));

            // Araç verilmiyor: başlık yalnızca yukarıdaki maddelerden
            // yazılır, model kendi başına veri çekemez.
            var response = await llm.CompleteAsync(
                "Kısa ve doğru yaz. Uydurma.",
                [new LlmMessage(LlmRole.User, prompt)],
                [],
                cancellationToken);

            var text = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            cache.Set(key, text, today.AddDays(1) - DateTime.UtcNow);

            return text;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Brifing başlığı üretilemedi.");
            return fallback;
        }
    }

    private static string BuildFallbackHeadline(IReadOnlyList<BriefingItemView> items)
    {
        var critical = items.Count(x => x.Severity == (int)BriefingSeverity.Critical);

        return critical > 0
            ? $"Bugün {items.Count} başlık var; {critical} tanesi acil."
            : $"Bugün dikkatinizi bekleyen {items.Count} başlık var.";
    }
}
