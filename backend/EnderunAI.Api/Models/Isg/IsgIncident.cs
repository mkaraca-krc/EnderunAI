namespace EnderunAI.Api.Models;

public enum IsgIncidentType
{
    /// <summary>İş kazası — yaralanma veya maddi hasar oluştu.</summary>
    Accident = 0,

    /// <summary>
    /// Ramak kala — zarar oluşmadı ama oluşabilirdi. Kayıt altına
    /// alınması yasal zorunluluk; asıl önleyici değer buradadır.
    /// </summary>
    NearMiss = 1,

    /// <summary>Meslek hastalığı.</summary>
    OccupationalIllness = 2
}

/// <summary>
/// Olayın ağırlığı. Sıra bilinçli: büyüdükçe ağırlaşır, panel ve
/// brifing eşikleri buna göre çalışır.
/// </summary>
public enum IsgIncidentSeverity
{
    /// <summary>Zarar yok (ramak kala).</summary>
    NoInjury = 0,

    /// <summary>İlk yardımla giderildi.</summary>
    FirstAid = 1,

    /// <summary>Tıbbi tedavi gerekti, iş günü kaybı yok.</summary>
    MedicalTreatment = 2,

    /// <summary>İş günü kaybı oluştu.</summary>
    LostWorkday = 3,

    /// <summary>Sürekli iş göremezlik.</summary>
    PermanentDisability = 4,

    /// <summary>Ölümlü.</summary>
    Fatality = 5
}

public enum IsgIncidentStatus
{
    Open = 0,
    UnderInvestigation = 1,
    Closed = 2
}

/// <summary>
/// Kaza ve ramak kala kaydı.
///
/// Yasal zorunluluk: iş kazaları SGK'ya üç iş günü içinde bildirilir,
/// ramak kalalar da kayıt altına alınır. Bu kayıt denetimde istenen
/// belgedir; bu yüzden SGK bildirim durumu ayrı alanlarda tutulur ve
/// bildirilmemiş kayıtlar panelde kritik olarak görünür.
///
/// GİZLİLİK: kayıt defteri <c>isg.incident.view</c> ile korunur —
/// İSG kaydı girebilen herkesin tüm kaza geçmişini görmesi gerekmez.
/// </summary>
public sealed class IsgIncident : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>
    /// Etkilenen personel. Ramak kalada kimse yaralanmadığı için boş
    /// olabilir — zorunlu tutulsaydı ramak kala kaydı hiç girilmezdi.
    /// </summary>
    public Guid? PersonnelId { get; set; }
    public Personnel? Personnel { get; set; }

    public DateTime IncidentDateTime { get; set; }

    public IsgIncidentType IncidentType { get; set; }
    public IsgIncidentSeverity Severity { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Kök neden analizi.</summary>
    public string? RootCause { get; set; }

    /// <summary>Alınan düzeltici/önleyici önlem.</summary>
    public string? ActionTaken { get; set; }

    /// <summary>Kaybedilen iş günü sayısı.</summary>
    public int LostWorkDays { get; set; }

    // --- SGK bildirimi ---

    public bool SgkNotified { get; set; }
    public DateTime? SgkNotificationDate { get; set; }
    public string? SgkNotificationNumber { get; set; }

    public IsgIncidentStatus Status { get; set; } = IsgIncidentStatus.Open;

    public Guid? ReportedByUserId { get; set; }

    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosureNote { get; set; }
}
