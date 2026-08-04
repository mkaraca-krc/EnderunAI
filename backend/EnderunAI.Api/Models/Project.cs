namespace EnderunAI.Api.Models;

/// <summary>
/// Sadeleştirilmiş 4 durumlu proje akışı: Keşif/Teklif → Aktif →
/// Tamamlandı/İptal. Ordinal değerler bilinçli olarak eski
/// (kullanılmayan) 7 durumlu enum'daki karşılıklarıyla aynı tutuldu
/// (Active=2, Completed=4, Cancelled=5) — canlıda tek kullanılan değer
/// Active(2) olduğundan veri migrasyonu gerekmedi.
/// </summary>
/// <summary>
/// Sözleşme tipi. Keşif ile gerçekleşen arasındaki sapmanın anlamı
/// tamamen buna bağlıdır.
/// </summary>
public enum ProjectContractType
{
    /// <summary>Henüz seçilmedi — sapma yorumlanmaz.</summary>
    Undetermined = 0,

    /// <summary>
    /// Anahtar teslim (götürü): bedel sabittir. Keşif üstü gerçekleşme
    /// ek gelir getirmez, doğrudan kâr erozyonudur.
    /// </summary>
    LumpSum = 1,

    /// <summary>
    /// Birim fiyatlı: yapılan iş kadar ödenir. Keşif üstü gerçekleşme
    /// ilave hakediş fırsatıdır.
    /// </summary>
    UnitPrice = 2,

    /// <summary>
    /// Karma: bölümlerin bir kısmı götürü, bir kısmı birim fiyatlı.
    /// Tip bölüm bazında belirlenir.
    /// </summary>
    Mixed = 3
}

public enum ProjectStatus
{
    Kesif = 0,
    Active = 2,
    Completed = 4,
    Cancelled = 5
}

public enum ProjectHealthStatus
{
    Green = 0,
    Yellow = 1,
    Red = 2
}

public sealed class Project : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>
    /// Keşif/Teklif statüsünde henüz kesinleşmemiş olabileceği için
    /// opsiyonel; Aktif'e geçerken zorunlu hale gelir (bkz.
    /// ProjectsController).
    /// </summary>
    public Guid? EmployerCurrentAccountId { get; set; }
    public CurrentAccount? EmployerCurrentAccount { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? ContractNumber { get; set; }
    public DateTime? ContractDate { get; set; }
    public decimal? ContractAmount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal VatRate { get; set; }

    /// <summary>
    /// Sözleşme/proje artış yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal IncreaseRate { get; set; }

    /// <summary>
    /// Nakit teminat kesintisi yüzdesi.
    /// Örnek: yüzde 5 için 5.00.
    /// </summary>
    public decimal CashRetentionRate { get; set; }

    /// <summary>
    /// Stopaj kesintisi yüzdesi.
    /// Örnek: yüzde 3 için 3.00.
    /// </summary>
    public decimal WithholdingTaxRate { get; set; }

    /// <summary>
    /// Malzeme kesintisi yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal MaterialDeductionRate { get; set; }

    /// <summary>
    /// Sözleşme tipi. Keşif–gerçekleşen sapmasının nasıl yorumlanacağını
    /// belirler: birim fiyatlı işte keşif üstü gerçekleşme ilave hakediş
    /// fırsatıdır, anahtar teslimde aynı sapma doğrudan kâr erozyonudur.
    ///
    /// Mevcut projeler <see cref="ProjectContractType.Undetermined"/>
    /// olarak açılır; tip seçilene kadar sapma yorumlanmaz — yanlış
    /// varsayım yanlış renk ve yanlış alarm üretirdi.
    /// </summary>
    public ProjectContractType ContractType { get; set; }
        = ProjectContractType.Undetermined;

    /// <summary>
    /// Anahtar teslimde toplam sapmanın kâr erozyon alarmı üreteceği
    /// eşik (%). Birim fiyatlı projede kullanılmaz.
    /// </summary>
    public decimal DeviationAlertThresholdRate { get; set; } = 5m;

    /// <summary>All-risk inşaat sigortası kesinti oranı (%). Yaygın: 0,5.</summary>
    public decimal AllRiskInsuranceRate { get; set; }

    /// <summary>
    /// Projenin varsayılan barter oranı (%). Şantiyede oran tanımlıysa
    /// o öncelikli; hakedişte de düzeltilebilir.
    /// </summary>
    public decimal BarterRate { get; set; }
    public string? WithholdingRate { get; set; }

    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }

    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Kesif;
    public ProjectHealthStatus HealthStatus { get; set; } = ProjectHealthStatus.Green;
    public string? HealthReason { get; set; }

    public Guid? ProjectManagerUserId { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
