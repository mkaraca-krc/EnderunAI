using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir;

public sealed record HizirChatRequest(
    Guid? ConversationId,
    string Message,
    string? PagePath);

public sealed record HizirChatResponse(
    Guid ConversationId,
    string Answer,
    IReadOnlyList<string> UsedTools,
    IReadOnlyList<string> DeniedTools);

public sealed record HizirConversationSummary(
    Guid Id,
    string Title,
    string? StartedOnPath,
    DateTime LastMessageAtUtc,
    int MessageCount);

public sealed record HizirMessageView(
    Guid Id,
    int Role,
    string Content,
    string? PagePath,
    DateTime CreatedAtUtc);

public interface IHizirChatService
{
    bool IsConfigured { get; }

    Task<HizirChatResponse> AskAsync(
        HizirChatRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<HizirConversationSummary>> GetConversationsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HizirMessageView>> GetMessagesAsync(
        Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// Hızır sohbet akışı: sistem talimatını kurar, kullanıcının izin
/// verdiği araçlarla modeli çağırır, araç çağrılarını yetki kontrolünden
/// geçirerek yürütür ve cevabı kaydeder.
/// </summary>
public sealed class HizirChatService(
    AppDbContext db,
    IHizirLlmClient llm,
    IHizirToolRegistry tools,
    IHizirKnowledgeBase knowledgeBase,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    ICurrentDataScopeService dataScope) : IHizirChatService
{
    /// <summary>
    /// Bağlama alınacak en fazla geçmiş mesaj. Sohbet uzadıkça token
    /// maliyeti büyümesin diye kırpılır.
    /// </summary>
    private const int HistoryLimit = 12;

    /// <summary>
    /// Model kaç kez üst üste araç çağırabilir. Sonsuz döngüyü ve
    /// beklenmedik maliyeti engeller.
    /// </summary>
    private const int MaxToolRounds = 4;

    public bool IsConfigured => llm.IsConfigured;

    public async Task<HizirChatResponse> AskAsync(
        HizirChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Mesaj boş olamaz.");

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

        var context = new HizirToolContext(
            userId,
            currentUser.FullName ?? currentUser.Username ?? "Kullanıcı",
            honorific,
            snapshot.RoleNames,
            snapshot.Permissions,
            scope);

        // Sohbet kaydı cevap alındıktan sonra oluşturulur; başarısız bir
        // istek boş sohbet kaydı bırakmasın.
        var conversation = request.ConversationId is Guid existingId
            ? await db.HizirConversations.SingleOrDefaultAsync(
                x => x.Id == existingId && x.UserId == userId, cancellationToken)
            : null;

        var history = conversation is null
            ? []
            : await LoadHistoryAsync(conversation.Id, cancellationToken);

        var availableTools = tools.AvailableFor(context);

        var messages = new List<LlmMessage>(history)
        {
            new(LlmRole.User, request.Message.Trim())
        };

        var usedTools = new List<string>();
        var deniedTools = new List<string>();
        var inputTokens = 0;
        var outputTokens = 0;
        string? answer = null;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var completion = await llm.CompleteAsync(
                BuildSystemPrompt(context, request.PagePath),
                messages,
                availableTools
                    .Select(x => new LlmToolDefinition(x.Name, x.Description, x.InputSchema))
                    .ToList(),
                cancellationToken);

            inputTokens += completion.InputTokens;
            outputTokens += completion.OutputTokens;

            if (completion.ToolCalls.Count == 0)
            {
                answer = completion.Text;
                break;
            }

            messages.Add(new LlmMessage(
                LlmRole.Assistant, completion.Text, completion.ToolCalls));

            var results = new List<LlmToolResult>();

            foreach (var call in completion.ToolCalls)
            {
                var outcome = await ExecuteToolAsync(call, context, cancellationToken);

                if (outcome.Denied)
                    deniedTools.Add(call.Name);
                else if (!outcome.IsError)
                    usedTools.Add(call.Name);

                results.Add(new LlmToolResult(call.Id, outcome.Content, outcome.IsError));
            }

            messages.Add(new LlmMessage(LlmRole.User, null, null, results));
        }

        answer ??= "Şu anda bu soruya cevap üretemedim. Sorunuzu biraz " +
                   "daha somut yazarsanız yardımcı olabilirim.";

        conversation ??= await CreateConversationAsync(userId, request, cancellationToken);

        await PersistAsync(
            conversation, request, answer, usedTools, deniedTools,
            inputTokens, outputTokens, cancellationToken);

        return new HizirChatResponse(
            conversation.Id,
            answer,
            usedTools.Distinct().ToList(),
            deniedTools.Distinct().ToList());
    }

    /// <summary>
    /// Araç yürütme. İzin kontrolü burada ikinci kez yapılır: model
    /// tanıtılmamış bir aracı uydurup çağırsa bile veri dönmez.
    /// </summary>
    private async Task<HizirToolOutcome> ExecuteToolAsync(
        LlmToolCall call,
        HizirToolContext context,
        CancellationToken cancellationToken)
    {
        var tool = tools.Find(call.Name);

        if (tool is null)
        {
            return new HizirToolOutcome(
                $"HATA: '{call.Name}' diye bir araç yok.", IsError: true);
        }

        if (tool.RequiredPermission is not null && !context.Has(tool.RequiredPermission))
        {
            return new HizirToolOutcome(
                "YETKİSİZ: Bu kullanıcının bu veriye erişim izni yok. " +
                "Kullanıcıya bu bilgiyi göremeyeceğini kibarca söyle, " +
                "veriyi başka yoldan tahmin etmeye veya örnek üretmeye çalışma.",
                Denied: true);
        }

        try
        {
            return await tool.ExecuteAsync(context, call.Arguments, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HizirToolOutcome(
                "HATA: Veri okunurken bir sorun oluştu.", IsError: true);
        }
    }

    private string BuildSystemPrompt(HizirToolContext context, string? pagePath)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Sen Hızır'sın: Enderun Enerji'nin ERP sistemi içinde çalışan " +
            "Türkçe konuşan kurumsal asistansın.");
        builder.AppendLine();

        builder.AppendLine("KULLANICI:");
        builder.AppendLine($"- Ad: {context.FullName}");

        if (!string.IsNullOrWhiteSpace(context.Honorific))
            builder.AppendLine($"- Hitap: {context.Honorific}");
        builder.AppendLine(
            $"- Rol: {(context.RoleNames.Count > 0 ? string.Join(", ", context.RoleNames) : "tanımsız")}");

        if (!string.IsNullOrWhiteSpace(pagePath))
            builder.AppendLine($"- Şu an baktığı sayfa: {pagePath}");

        builder.AppendLine();
        builder.AppendLine("HİTAP:");
        builder.AppendLine(
            "- Kullanıcıya YUKARIDA VERİLEN adıyla hitap et. Başka bir ad " +
            "kullanma, ad uydurma.");
        builder.AppendLine(
            "- Hitap alanı verilmişse adın ardından onu kullan " +
            "(ör. ad \"Ahmet Yılmaz\" ve hitap \"Bey\" ise \"Ahmet Bey\").");
        builder.AppendLine(
            "- Hitap alanı verilmemişse cinsiyet tahmin etme; " +
            "\"Sayın {ad soyad}\" biçimini kullan.");
        builder.AppendLine(
            "- Cevabı kullanıcının rolüne göre uyarla: sahadaki bir kullanıcıya " +
            "muhasebe jargonu kullanma, yöneticiye gereksiz ayrıntı verme.");
        builder.AppendLine();

        builder.AppendLine("VERİ KULLANIMI (en önemli kural):");
        builder.AppendLine(
            "- Şirket verisiyle ilgili her cevabın araçlardan gelen gerçek " +
            "veriye dayanmalı. Aracı çağırmadan rakam söyleme.");
        builder.AppendLine(
            "- Bir araç \"KAYIT YOK\" derse veri olmadığını söyle. Örnek, " +
            "tahmini ya da temsili rakam ASLA üretme.");
        builder.AppendLine(
            "- Bir araç \"YETKİSİZ\" derse, kullanıcının o bilgiyi görme " +
            "yetkisi olmadığını kibarca söyle ve konuyu kapat. Aynı bilgiyi " +
            "başka bir araçla dolaylı yoldan elde etmeye ÇALIŞMA.");
        builder.AppendLine(
            "- Sana tanıtılmayan araçlar bu kullanıcının yetkisi dışındadır; " +
            "onların verisine erişemezsin.");
        builder.AppendLine();

        builder.AppendLine("KULLANIM KILAVUZU:");
        builder.AppendLine(
            "- Kullanıcı \"nereden yaparım\", \"bulamıyorum\", \"nasıl girerim\" " +
            "gibi bir şey sorarsa kilavuz_ara aracını kullan ve adım adım yol tarif et.");
        builder.AppendLine(
            "- Yalnızca kılavuzdan dönen sayfaları tarif et; kullanıcının " +
            "açamayacağı bir sayfayı önerme.");
        builder.AppendLine();

        builder.AppendLine("ÜSLUP:");
        builder.AppendLine("- Kısa ve net yaz. Gereksiz giriş cümlesi kurma.");
        builder.AppendLine("- Rakamları Türk Lirası ve binlik ayraçla yaz.");
        builder.AppendLine("- Emoji kullanma.");

        return builder.ToString();
    }

    /// <summary>
    /// Yeni sohbet kaydı. Yalnızca cevap üretildikten sonra çağrılır;
    /// başarısız istek boş sohbet bırakmaz. Var olan sohbetler
    /// kullanıcıya özeldir, başkasınınki açılamaz.
    /// </summary>
    private async Task<HizirConversation> CreateConversationAsync(
        Guid userId, HizirChatRequest request, CancellationToken cancellationToken)
    {
        var title = request.Message.Trim();
        if (title.Length > 80)
            title = title[..80] + "...";

        var conversation = new HizirConversation
        {
            UserId = userId,
            Title = title,
            StartedOnPath = request.PagePath,
            CreatedByUserId = userId
        };

        db.HizirConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        return conversation;
    }

    private async Task<List<LlmMessage>> LoadHistoryAsync(
        Guid conversationId, CancellationToken cancellationToken)
    {
        var rows = await db.HizirMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(HistoryLimit)
            .Select(x => new { x.Role, x.Content, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new LlmMessage(
                x.Role == HizirMessageRole.User ? LlmRole.User : LlmRole.Assistant,
                x.Content))
            .ToList();
    }

    private async Task PersistAsync(
        HizirConversation conversation,
        HizirChatRequest request,
        string answer,
        IReadOnlyCollection<string> usedTools,
        IReadOnlyCollection<string> deniedTools,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken)
    {
        db.HizirMessages.Add(new HizirMessage
        {
            ConversationId = conversation.Id,
            Role = HizirMessageRole.User,
            Content = request.Message.Trim(),
            PagePath = request.PagePath,
            CreatedByUserId = conversation.UserId
        });

        db.HizirMessages.Add(new HizirMessage
        {
            ConversationId = conversation.Id,
            Role = HizirMessageRole.Assistant,
            Content = answer,
            PagePath = request.PagePath,
            UsedTools = Join(usedTools),
            DeniedTools = Join(deniedTools),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CreatedByUserId = conversation.UserId
        });

        conversation.LastMessageAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(",", values.Distinct());

    public async Task<IReadOnlyList<HizirConversationSummary>> GetConversationsAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return [];

        return await db.HizirConversations
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Take(30)
            .Select(x => new HizirConversationSummary(
                x.Id,
                x.Title,
                x.StartedOnPath,
                x.LastMessageAtUtc,
                x.Messages.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HizirMessageView>> GetMessagesAsync(
        Guid conversationId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return [];

        var owns = await db.HizirConversations
            .AnyAsync(x => x.Id == conversationId && x.UserId == userId, cancellationToken);

        if (!owns)
            throw new KeyNotFoundException("Sohbet bulunamadı.");

        return await db.HizirMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new HizirMessageView(
                x.Id, (int)x.Role, x.Content, x.PagePath, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
