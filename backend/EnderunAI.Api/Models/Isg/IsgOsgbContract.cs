namespace EnderunAI.Api.Models;

/// <summary>OSGB bedelinin nasıl hesaplandığı.</summary>
public enum OsgbBillingType
{
    /// <summary>Personel sayısından bağımsız sabit aylık bedel.</summary>
    MonthlyFixed = 0,

    /// <summary>Kişi başı birim bedel — tutar çalışan sayısıyla değişir.</summary>
    PerPerson = 1
}

/// <summary>
/// OSGB (Ortak Sağlık Güvenlik Birimi) hizmet sözleşmesi.
///
/// OSGB firması sistemde ayrı bir varlık değil, bir cari: faturaları
/// mevcut tedarikçi faturası akışından geçer. Burada tutulan, o cariyle
/// yapılan hizmet sözleşmesinin şartları ve atanan uzman/hekim bilgisi
/// — denetimde sorulan da budur.
/// </summary>
public sealed class IsgOsgbContract : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>OSGB firmasının cari kartı.</summary>
    public Guid CurrentAccountId { get; set; }
    public CurrentAccount CurrentAccount { get; set; } = null!;

    public string ContractNumber { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Sözleşme bitişi. Boşsa süresiz kabul edilir; bitiş uyarısı
    /// üretilmez.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    public OsgbBillingType BillingType { get; set; }

    /// <summary>Aylık sabit bedelde geçerli tutar.</summary>
    public decimal MonthlyFee { get; set; }

    /// <summary>Kişi başı bedelde bir çalışan için aylık tutar.</summary>
    public decimal PerPersonFee { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    public string? Notes { get; set; }

    public ICollection<IsgOsgbExpert> Experts { get; set; }
        = new List<IsgOsgbExpert>();
}

/// <summary>OSGB'nin bize atadığı görevlinin türü.</summary>
public enum OsgbExpertType
{
    /// <summary>İş güvenliği uzmanı.</summary>
    SafetySpecialist = 0,

    /// <summary>İşyeri hekimi.</summary>
    WorkplacePhysician = 1,

    /// <summary>Diğer sağlık personeli.</summary>
    OtherHealthStaff = 2
}

/// <summary>
/// Sözleşme kapsamında bize atanan iş güvenliği uzmanı / işyeri hekimi.
/// Denetimde "kim atanmış, belge no ve sınıfı ne" diye sorulur.
/// </summary>
public sealed class IsgOsgbExpert : BaseEntity
{
    public Guid IsgOsgbContractId { get; set; }
    public IsgOsgbContract IsgOsgbContract { get; set; } = null!;

    public OsgbExpertType ExpertType { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Uzmanlık/hekimlik belge numarası.</summary>
    public string? CertificateNumber { get; set; }

    /// <summary>İş güvenliği uzmanlığı sınıfı: A, B veya C.</summary>
    public string? ExpertClass { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Görev bitişi; boşsa görev sürüyor demektir.</summary>
    public DateOnly? EndDate { get; set; }
}
