using EnderunAI.Api.Models;

namespace EnderunAI.Api.Models.HumanResources;

/// <summary>Ücret kartının hangi tutar üzerinden anlaşıldığı.</summary>
public enum SalaryBasis
{
    /// <summary>
    /// Brüt esaslı — kartta girilen brüt sabittir, net ondan çıkar.
    /// Mevcut kayıtların tamamı budur; varsayılan olması bilinçli.
    /// </summary>
    Gross = 0,

    /// <summary>
    /// Net esaslı — kartta girilen NET sabittir. Her bordroda o ayın
    /// kümülatif matrahıyla brüt yeniden hesaplanır; yıl içinde vergi
    /// dilimi yükselse de personel aynı neti alır, artan yükü şirket
    /// üstlenir.
    /// </summary>
    Net = 1
}

public sealed class HrSalaryDefinition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }

    /// <summary>
    /// Kartın esası. Net ise <see cref="TargetNetSalary"/> anlaşılan
    /// tutardır ve <see cref="GrossSalary"/> ondan türetilmiş referans
    /// değerdir (ocak esaslı, ekranda bilgi amaçlı).
    /// </summary>
    public SalaryBasis SalaryBasis { get; set; } = SalaryBasis.Gross;

    /// <summary>
    /// Net esaslı kartta anlaşılan aylık resmi net. Brüt esaslı kartta
    /// kullanılmaz.
    /// </summary>
    public decimal TargetNetSalary { get; set; }

    public decimal GrossSalary { get; set; }
    public decimal NetSalary { get; set; }
    public decimal DailyRate { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal OvertimeMultiplier { get; set; } = 1.5m;
    public decimal SundayMultiplier { get; set; } = 2m;
    public decimal PublicHolidayMultiplier { get; set; } = 2m;
    public string CurrencyCode { get; set; } = "TRY";
    public string? Description { get; set; }
}

public sealed class HrDepartment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentDepartmentId { get; set; }
    public Guid? ManagerPersonnelId { get; set; }
}

public sealed class HrPosition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid DepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Level { get; set; }
    public bool IsManagerial { get; set; }
}
