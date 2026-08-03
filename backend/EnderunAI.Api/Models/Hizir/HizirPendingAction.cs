namespace EnderunAI.Api.Models;

public enum HizirPendingActionStatus
{
    /// <summary>Hazırlandı, kullanıcı onayı bekleniyor.</summary>
    Pending = 0,
    /// <summary>Kullanıcı onayladı ve eylem yürütüldü.</summary>
    Executed = 1,
    /// <summary>Kullanıcı vazgeçti.</summary>
    Cancelled = 2,
    /// <summary>Süresi doldu, artık yürütülemez.</summary>
    Expired = 3,
    /// <summary>Onaylandı ama yürütme sırasında hata oluştu.</summary>
    Failed = 4
}

/// <summary>
/// Hızır'ın hazırladığı, kullanıcı onayı bekleyen eylem.
///
/// Bu kaydın varlık sebebi güvenlik: Hızır eylemi hazırlarken iş
/// servisini HİÇ çağırmaz, yalnızca bu satırı yazar. Yürütme ayrı bir
/// HTTP ucundan, kullanıcının kendi oturumuyla yapılır. Böylece
/// "kullanıcı onayladı mı" kararı dil modelinden alınmış olur.
///
/// <see cref="ArgumentsJson"/> hazırlama anında dondurulur; gösterilen
/// özet (<see cref="Summary"/>) de bu argümanlardan SUNUCUDA üretilir.
/// Modelin yazdığı serbest metin özet olarak kullanılmaz — aksi halde
/// yönlendirilmiş bir model, gösterdiğinden farklı bir iş yaptırabilirdi.
/// </summary>
public sealed class HizirPendingAction : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Hangi sohbette hazırlandı.</summary>
    public Guid? ConversationId { get; set; }
    public HizirConversation? Conversation { get; set; }

    /// <summary>Yürütülecek eylemin araç adı (ör. "rfq_ac").</summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>Dondurulmuş argümanlar. Onay sonrası değiştirilemez.</summary>
    public string ArgumentsJson { get; set; } = "{}";

    /// <summary>
    /// Kullanıcıya gösterilen özet. Sunucuda argümanlardan üretilir.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Yürütme anında yeniden kontrol edilecek izin anahtarı.</summary>
    public string? RequiredPermission { get; set; }

    public HizirPendingActionStatus Status { get; set; } =
        HizirPendingActionStatus.Pending;

    /// <summary>
    /// Bu andan sonra onaylansa bile yürütülmez. Eski bir onayın
    /// sonradan tetiklenmesini engeller.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);

    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>Yürütme sonucu ya da hata mesajı.</summary>
    public string? ResultMessage { get; set; }

    public bool IsOpen(DateTime now) =>
        Status == HizirPendingActionStatus.Pending && ExpiresAtUtc > now;
}
