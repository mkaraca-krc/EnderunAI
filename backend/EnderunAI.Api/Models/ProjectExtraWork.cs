namespace EnderunAI.Api.Models;

/// <summary>
/// İlave işin onay durumu. Anahtar teslimde belirleyicidir: yalnızca
/// işveren onaylı ek iş tahsil edilebilir, dolayısıyla yalnızca o kâr
/// erozyonundan düşülür.
/// </summary>
public enum ExtraWorkApprovalStatus
{
    /// <summary>İşveren onayı bekleniyor — tahsil edilebilir değil.</summary>
    Pending = 0,

    /// <summary>İşveren onayladı; onay belgesi iliştirilmiş olmalı.</summary>
    Approved = 1,

    /// <summary>İşveren reddetti — bedeli alınamayacak.</summary>
    Rejected = 2
}

/// <summary>
/// Keşif üstü gerçekleşmenin kayda geçmiş hali.
///
/// İki sözleşme tipinde iki farklı şey ifade eder:
///
///   BİRİM FİYATLI — "ilave iş". Yapılan iş kadar ödendiği için
///   doğrudan hakedişe eklenebilir; ayrı bir işveren onayı şartı yoktur
///   (sözleşmedeki birim fiyat geçerlidir).
///
///   ANAHTAR TESLİM — "işveren onaylı ek iş". Bedel sabit olduğu için
///   ek iş ancak işveren yazılı onay verirse tahsil edilebilir. Onay
///   belgesi Dosya Merkezi'nden iliştirilir. Onaysız ek iş kâr
///   erozyonundan DÜŞÜLMEZ — düşülseydi tahsil edilemeyecek bir tutar
///   kâr gibi görünürdü.
/// </summary>
public sealed class ProjectExtraWork : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>İşin ait olduğu imalat bölümü; zorunlu değil.</summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Miktar × birim fiyat.</summary>
    public decimal Amount { get; set; }

    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    /// <summary>
    /// Onay durumu. Birim fiyatlı projede varsayılan olarak onaylı
    /// kabul edilir (sözleşme birim fiyatı geçerlidir); anahtar teslimde
    /// işveren onayı beklenir.
    /// </summary>
    public ExtraWorkApprovalStatus ApprovalStatus { get; set; }
        = ExtraWorkApprovalStatus.Pending;

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>
    /// İşveren onay belgesi (Dosya Merkezi'nden). Anahtar teslimde
    /// onaylı işaretlemek için belge iliştirilmiş olmalıdır — sözlü
    /// onay tahsilatta işe yaramaz.
    /// </summary>
    public Guid? ApprovalDocumentId { get; set; }
    public ProjectDocument? ApprovalDocument { get; set; }

    /// <summary>
    /// Hakedişe aktarıldıysa hangi hakediş. Aynı ilave iş iki kez
    /// hakedişe girmesin diye tutulur.
    /// </summary>
    public Guid? ProgressPaymentId { get; set; }
    public ProgressPayment? ProgressPayment { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Bu ilave iş tahsil edilebilir mi — kâr erozyonundan düşülüp
    /// düşülmeyeceğini belirler.
    /// </summary>
    public bool IsCollectible(ProjectContractType contractType) =>
        contractType == ProjectContractType.UnitPrice
            ? ApprovalStatus != ExtraWorkApprovalStatus.Rejected
            : ApprovalStatus == ExtraWorkApprovalStatus.Approved;
}
