namespace EnderunAI.Api.Services.Hizir;

/// <summary>Modele verilen bir aracın tanımı.</summary>
public sealed record LlmToolDefinition(
    string Name,
    string Description,
    /// <summary>JSON Schema (object) — aracın parametreleri.</summary>
    object InputSchema);

/// <summary>Modelin yapmak istediği araç çağrısı.</summary>
public sealed record LlmToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, object?> Arguments);

/// <summary>Bir aracın çalıştırılması sonucu modele geri verilen içerik.</summary>
public sealed record LlmToolResult(
    string ToolCallId,
    string Content,
    bool IsError = false);

public enum LlmRole
{
    User = 0,
    Assistant = 1
}

/// <summary>
/// Sohbet turu. Araç çağrıları ve sonuçları da tur olarak taşınır ki
/// model kendi önceki araç kullanımını görebilsin.
/// </summary>
public sealed record LlmMessage(
    LlmRole Role,
    string? Text,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    IReadOnlyList<LlmToolResult>? ToolResults = null);

public sealed record LlmCompletion(
    string? Text,
    IReadOnlyList<LlmToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// LLM sağlayıcı soyutlaması. Sağlayıcı değişirse yalnızca bu arayüzün
/// uygulaması değişir; sohbet ve araç mantığı sağlayıcıdan bağımsızdır.
/// </summary>
public interface IHizirLlmClient
{
    /// <summary>Sağlayıcı yapılandırılmış mı (API anahtarı var mı).</summary>
    bool IsConfigured { get; }

    /// <summary>Kullanılan model kimliği — kayıt ve maliyet takibi için.</summary>
    string ModelId { get; }

    Task<LlmCompletion> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
