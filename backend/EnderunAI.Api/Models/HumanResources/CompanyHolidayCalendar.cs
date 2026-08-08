namespace EnderunAI.Api.Models.HumanResources;

/// <summary>
/// Şirketin bir yıla ait resmî tatil takvimi.
///
/// DOĞRULAMA ZORUNLU: takvim doğrulanmadan puantaj cetvelini doldurmakta
/// KULLANILMAZ. Sabit tatiller hesaplanabilir ama dini bayramlar resmî
/// ilana bağlıdır; sistemin tahmin ettiği bir tarihle üretilen puantaj
/// sessizce yanlış bordro demektir. Aynı fail-closed deseni bordro
/// parametrelerinde de var (bkz. CompanyPayrollSettings.VerifiedAtUtc).
/// </summary>
public sealed class CompanyHolidayCalendar : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int Year { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public string? VerificationNote { get; set; }

    public ICollection<CompanyHoliday> Days { get; set; } = new List<CompanyHoliday>();

    /// <summary>Doğrulanmamış takvim otomatik doldurmada kullanılmaz.</summary>
    public bool IsVerified => VerifiedAtUtc is not null;
}

/// <summary>Takvimdeki tek bir tatil günü.</summary>
public sealed class CompanyHoliday : BaseEntity
{
    public Guid CompanyHolidayCalendarId { get; set; }
    public CompanyHolidayCalendar CompanyHolidayCalendar { get; set; } = null!;

    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arife günleri yarım gündür. Puantajda tam gün sayılmaz; ücrete
    /// esas gün hesabı bunu ayırmak zorunda.
    /// </summary>
    public bool IsHalfDay { get; set; }
}
