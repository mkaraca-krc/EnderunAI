namespace EnderunAI.Api.Models;

/// <summary>
/// Servis talebinin durumu.
///
/// Akış: şantiyeden talep → merkeze transfer → karar → serviste →
/// döndü ya da hurda.
/// </summary>
public enum ToolServiceStatus
{
    /// <summary>Şantiyeden talep açıldı, alet hâlâ sahada.</summary>
    Requested = 0,

    /// <summary>Alet merkeze ulaştı, karar bekliyor.</summary>
    Transferred = 1,

    /// <summary>Karar verildi, onarım sürüyor.</summary>
    InService = 2,

    /// <summary>Onarıldı ve kullanıma döndü.</summary>
    Completed = 3,

    /// <summary>Onarılamadı, hurdaya ayrıldı.</summary>
    Scrapped = 4,

    Cancelled = 5
}

/// <summary>
/// Arızanın nasıl giderileceği kararı.
/// </summary>
public enum ToolServiceDecision
{
    /// <summary>Henüz karar verilmedi.</summary>
    Pending = 0,

    /// <summary>Dış servise gitti, garanti kapsamında — maliyet sıfır.</summary>
    ExternalWarranty = 1,

    /// <summary>Dış servise gitti, ücretli.</summary>
    ExternalPaid = 2,

    /// <summary>Merkezde yerinde onarıldı.</summary>
    InHouse = 3,

    /// <summary>Onarılamaz, hurda.</summary>
    Scrap = 4
}

public enum ToolServiceUrgency
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Alet servis talebi.
///
/// MALİYET KURALI: ücretli servisin bedeli, talebi AÇAN şantiyenin
/// projesine yazılır — aleti bozan işin maliyetidir. Merkez talebinde
/// proje yoktur ve maliyet genel gider olarak kalır, hiçbir projeye
/// yüklenmez. Garanti kapsamındaki onarımda tutar sıfırdır ve maliyet
/// kaydı hiç oluşmaz.
///
/// Bu yüzden <see cref="ProjectId"/> talep açılırken saklanır ve
/// sonradan aletin yeri değişse bile DEĞİŞMEZ: masrafı doğuran, o
/// dönem aleti kullanan şantiyedir.
/// </summary>
public sealed class ToolServiceRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ToolAssetId { get; set; }
    public ToolAsset ToolAsset { get; set; } = null!;

    public string RequestNumber { get; set; } = string.Empty;

    /// <summary>
    /// Talebi açan şantiyenin projesi. Ücretli servis maliyeti buraya
    /// yazılır. Merkez talebinde boştur.
    /// </summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public DateTime RequestDate { get; set; }

    /// <summary>Arıza tanımı — "şarj tutmuyor", "mandreni kırık".</summary>
    public string FaultDescription { get; set; } = string.Empty;

    public ToolServiceUrgency Urgency { get; set; } = ToolServiceUrgency.Normal;

    public ToolServiceStatus Status { get; set; } = ToolServiceStatus.Requested;

    public ToolServiceDecision Decision { get; set; } = ToolServiceDecision.Pending;

    /// <summary>Karar gerekçesi — denetimde sorulan budur.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Dış servise gittiyse servis firması.</summary>
    public string? ServiceProviderName { get; set; }

    /// <summary>
    /// Onarım bedeli (KDV hariç). Garanti kapsamında sıfırdır.
    /// </summary>
    public decimal ServiceCost { get; set; }

    /// <summary>
    /// Maliyetin yazıldığı proje maliyet kaydı. Dolu ise maliyet
    /// işlenmiştir; mükerrer işlemeyi bu alan engeller.
    /// </summary>
    public Guid? ProjectCostTransactionId { get; set; }

    /// <summary>
    /// Hurdaya ayrıldıysa yerine açılan satın alma talebi.
    /// </summary>
    public Guid? ReplacementPurchaseRequestId { get; set; }

    public DateTime? TransferredAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Guid? RequestedByUserId { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Aletin kullanımdan çıktığı durumlar. Bu durumlarda alet
    /// zimmetli kalmaya devam eder ama kullanılamaz.
    /// </summary>
    public bool IsOpen =>
        Status is ToolServiceStatus.Requested
            or ToolServiceStatus.Transferred
            or ToolServiceStatus.InService;
}
