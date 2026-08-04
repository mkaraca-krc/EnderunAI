namespace EnderunAI.Api.Models;

public enum IsgSiteDocumentType
{
    /// <summary>Risk değerlendirmesi — sahanın temel İSG belgesi.</summary>
    RiskAssessment = 0,

    /// <summary>Acil durum planı.</summary>
    EmergencyPlan = 1,

    /// <summary>İSG kurul toplantı tutanağı.</summary>
    CommitteeMinutes = 2,

    /// <summary>Saha denetim formu.</summary>
    SiteAudit = 3,

    /// <summary>
    /// KKD zimmet formu (imzalı belge). Kişi bazlı fiili KKD takibi
    /// için sistemde ayrıca HrAssetAssignment zimmet modülü var;
    /// burada tutulan ıslak imzalı formun kendisidir.
    /// </summary>
    PpeHandover = 4,

    Other = 99
}

/// <summary>
/// Şantiye bazlı İSG belgesi.
///
/// Proje dokümanlarından ayrı bir tablo: <c>ProjectDocument</c>'ta
/// geçerlilik tarihi yok ve dosyalar proje klasörü mantığıyla
/// sürümleniyor. İSG belgesinin asıl sorusu "hâlâ geçerli mi" —
/// süresi dolan risk değerlendirmesi denetimde belge yokluğuyla aynı
/// sonucu doğurur.
/// </summary>
public sealed class IsgSiteDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>
    /// Belge tek bir şantiyeye aitse dolu; proje geneli belgelerde boş.
    /// </summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public IsgSiteDocumentType DocumentType { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    /// <summary>
    /// Geçerlilik bitişi. Boşsa süresiz sayılır ve uyarı üretilmez;
    /// risk değerlendirmesi gibi periyodik belgelerde doldurulmalı.
    /// </summary>
    public DateOnly? ValidUntil { get; set; }

    // --- Dosya ---

    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public string? Notes { get; set; }
}
