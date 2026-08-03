namespace EnderunAI.Api.Models;

/// <summary>
/// Bir kullanıcının Hızır ile sohbeti. Sohbetler kullanıcıya özeldir;
/// başka bir kullanıcının sohbeti hiçbir koşulda okunamaz.
/// </summary>
public sealed class HizirConversation : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>İlk kullanıcı mesajından türetilen kısa başlık.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Sohbetin başladığı sayfa (bağlam olarak da kullanılır).</summary>
    public string? StartedOnPath { get; set; }

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<HizirMessage> Messages { get; set; }
        = new List<HizirMessage>();
}

public enum HizirMessageRole
{
    User = 0,
    Assistant = 1
}

public sealed class HizirMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public HizirConversation Conversation { get; set; } = null!;

    public HizirMessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Kullanıcının mesajı gönderdiği sayfa.</summary>
    public string? PagePath { get; set; }

    /// <summary>
    /// Bu cevapta kullanılan araçların adları (virgülle ayrık).
    /// Hangi cevabın hangi veriye dayandığı denetlenebilsin diye tutulur.
    /// </summary>
    public string? UsedTools { get; set; }

    /// <summary>
    /// İzin yetersizliği nedeniyle reddedilen araçlar. Yetki sızması
    /// incelemesinde ilk bakılacak alan.
    /// </summary>
    public string? DeniedTools { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
