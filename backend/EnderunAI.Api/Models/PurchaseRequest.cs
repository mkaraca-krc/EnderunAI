namespace EnderunAI.Api.Models;

public enum PurchaseRequestPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum PurchaseRequestStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Quotation = 3,
    Ordered = 4,
    Completed = 5,
    Cancelled = 6,

    /// <summary>
    /// Reddedildi — talep uygun bulunmadı, NİHAİdir. Gerekçesi
    /// <see cref="PurchaseRequest.RejectionReason"/> alanında zorunlu.
    /// </summary>
    Rejected = 7,

    /// <summary>
    /// Düzeltmeye iade edildi — talep sahibine geri döndü.
    ///
    /// Redden farkı: red kapıyı kapatır, iade "şunu düzelt ve yeniden
    /// gönder" der. İkisini tek duruma sıkıştırmak talep sahibinin
    /// düzeltip yeniden gönderebileceği işleri de kalıcı olarak
    /// öldürürdü.
    /// </summary>
    ReturnedForRevision = 8
}

public sealed class PurchaseRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? NeededByDate { get; set; }

    public string RequestedByName { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }

    public string? Description { get; set; }

    public PurchaseRequestPriority Priority { get; set; }
        = PurchaseRequestPriority.Normal;

    public PurchaseRequestStatus Status { get; set; }
        = PurchaseRequestStatus.Draft;

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Red gerekçesi. Reddedildi durumunda zorunlu.</summary>
    public string? RejectionReason { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }

    /// <summary>
    /// Düzeltmeye iade gerekçesi — talep sahibinin neyi düzelteceği.
    /// Gerekçesiz iade, talep sahibini ne yapacağını bilmeden bekletir.
    /// </summary>
    public string? ReturnReason { get; set; }
    public Guid? ReturnedByUserId { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }

    /// <summary>
    /// Kaç kez düzeltmeye iade edilip yeniden gönderildi. Sürekli
    /// gidip gelen talep, formu ya da süreci sorgulamak için sinyal.
    /// </summary>
    public int RevisionCount { get; set; }

    public ICollection<PurchaseRequestItem> Items { get; set; }
        = new List<PurchaseRequestItem>();
}
