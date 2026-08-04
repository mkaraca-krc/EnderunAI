namespace EnderunAI.Api.Models;

public enum IsgHealthReportType
{
    /// <summary>İşe giriş muayenesi.</summary>
    PreEmployment = 0,

    /// <summary>Periyodik muayene.</summary>
    Periodic = 1,

    /// <summary>İşe dönüş muayenesi (uzun raporlu ayrılık sonrası).</summary>
    ReturnToWork = 2,

    /// <summary>İş değişikliği veya özel durum muayenesi.</summary>
    Special = 3
}

public enum IsgHealthResult
{
    /// <summary>Çalışabilir.</summary>
    Fit = 0,

    /// <summary>Şartlı çalışabilir — kısıtlama notu vardır.</summary>
    FitWithRestrictions = 1,

    /// <summary>Çalışamaz.</summary>
    Unfit = 2
}

/// <summary>
/// OSGB işyeri hekiminin verdiği sağlık raporu.
///
/// GİZLİLİK: Rapor tarihi ve geçerlilik bitişi İSG yetkisi olan herkese
/// görünür — süre takibi bunsuz çalışmaz. Buna karşılık
/// <see cref="Restrictions"/>, <see cref="DoctorNotes"/> ve
/// <see cref="DocumentPath"/> tıbbi veridir ve yalnızca
/// <c>isg.health.view</c> izniyle görünür. Maskeleme sorgu
/// projeksiyonunda yapılır, arayüzde değil.
/// </summary>
public sealed class IsgHealthReport : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    /// <summary>Raporu veren OSGB sözleşmesi; elle girildiyse boş.</summary>
    public Guid? IsgOsgbContractId { get; set; }
    public IsgOsgbContract? IsgOsgbContract { get; set; }

    public IsgHealthReportType ReportType { get; set; }

    public DateOnly ExamDate { get; set; }

    /// <summary>
    /// Geçerlilik bitişi. Boşsa süresiz sayılır ve uyarı üretilmez —
    /// mevzuatta çoğu muayene periyodiktir, boş bırakmak istisnadır.
    /// </summary>
    public DateOnly? ValidUntil { get; set; }

    public IsgHealthResult Result { get; set; }

    /// <summary>Raporu veren hekim.</summary>
    public string? DoctorName { get; set; }

    // --- Tıbbi detay: isg.health.view olmadan dönmez ---

    /// <summary>Çalışma kısıtlaması (ör. "yüksekte çalışamaz").</summary>
    public string? Restrictions { get; set; }

    /// <summary>Hekim notu / teşhis.</summary>
    public string? DoctorNotes { get; set; }

    /// <summary>Taranmış rapor dosyası.</summary>
    public string? DocumentPath { get; set; }
}

public enum IsgTrainingType
{
    /// <summary>Temel İSG eğitimi.</summary>
    Basic = 0,

    /// <summary>İşbaşı eğitimi.</summary>
    OnTheJob = 1,

    /// <summary>Yenileme eğitimi.</summary>
    Refresher = 2,

    /// <summary>Özel/konu bazlı eğitim.</summary>
    Special = 3
}

/// <summary>
/// OSGB'nin verdiği İSG eğitimi. Denetimde eğitim tarihi, süresi ve
/// geçerliliği sorulur.
/// </summary>
public sealed class IsgTraining : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public Guid? IsgOsgbContractId { get; set; }
    public IsgOsgbContract? IsgOsgbContract { get; set; }

    public IsgTrainingType TrainingType { get; set; }

    public string Topic { get; set; } = string.Empty;

    public DateOnly TrainingDate { get; set; }

    /// <summary>Eğitim süresi (saat).</summary>
    public decimal DurationHours { get; set; }

    /// <summary>Geçerlilik bitişi; boşsa süresiz.</summary>
    public DateOnly? ValidUntil { get; set; }

    public string? TrainerName { get; set; }

    public string? DocumentPath { get; set; }

    public string? Notes { get; set; }
}

public enum IsgCertificateType
{
    /// <summary>Yüksekte çalışma yetki belgesi.</summary>
    WorkingAtHeight = 0,

    /// <summary>Elektrik yetki belgesi.</summary>
    ElectricalAuthorization = 1,

    /// <summary>İlk yardımcı sertifikası.</summary>
    FirstAid = 2,

    /// <summary>Yangın güvenliği / söndürme eğitimi belgesi.</summary>
    FireSafety = 3,

    /// <summary>İş makinesi / forklift operatör belgesi.</summary>
    MachineOperator = 4,

    Other = 99
}

/// <summary>
/// Personelin yetki/sertifika belgesi. Süresi dolan belgeyle çalışma
/// yaptırmak yasal sorumluluk doğurur; bu yüzden geçerlilik takibi
/// eğitim ve sağlık raporuyla aynı motordan geçer.
/// </summary>
public sealed class IsgCertificate : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public IsgCertificateType CertificateType { get; set; }

    /// <summary>Diğer türünde belge adı serbest yazılır.</summary>
    public string? CustomTypeName { get; set; }

    public string? CertificateNumber { get; set; }

    /// <summary>Belgeyi veren kurum.</summary>
    public string? IssuedBy { get; set; }

    public DateOnly IssueDate { get; set; }

    /// <summary>Geçerlilik bitişi; boşsa süresiz (ör. bazı ustalık belgeleri).</summary>
    public DateOnly? ExpiryDate { get; set; }

    public string? DocumentPath { get; set; }

    public string? Notes { get; set; }
}
