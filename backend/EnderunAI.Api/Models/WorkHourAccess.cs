namespace EnderunAI.Api.Models;

/// <summary>
/// Bir rolün belirli bir haftanın gününde izinli olduğu saat aralığı.
/// Bir gün için satır yoksa o gün o rol için tamamen kapalıdır. Admin ve
/// Genel Müdür rolleri için hiç satır seed edilmez — bu iki rol
/// WorkHourAccessService içinde her zaman istisnasız izinli kabul edilir.
/// </summary>
public sealed class RoleWorkHourWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;

    /// <summary>.NET DayOfWeek değeri (0=Pazar .. 6=Cumartesi).</summary>
    public int DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

public enum AccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Mesai penceresi dışında giriş denenince kullanıcının gerekçeli olarak
/// gönderdiği erişim talebi.
/// </summary>
public sealed class AccessRequest : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public string Reason { get; set; } = string.Empty;
    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;

    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public int? GrantedDurationMinutes { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>
/// Onaylanan bir erişim talebi sonucunda kullanıcıya tanınan, süresi
/// dolunca kendiliğinden geçersiz olan geçici erişim penceresi.
/// </summary>
public sealed class TemporaryAccessGrant : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid? SourceAccessRequestId { get; set; }
    public AccessRequest? SourceAccessRequest { get; set; }

    public Guid GrantedByUserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
