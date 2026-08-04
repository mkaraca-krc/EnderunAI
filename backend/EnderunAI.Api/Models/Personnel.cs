namespace EnderunAI.Api.Models;

public enum PersonnelStatus
{
    Candidate = 0,
    Active = 1,
    OnLeave = 2,
    Suspended = 3,
    Terminated = 4
}

/// <summary>Personelin fiilen nerede çalıştığı.</summary>
public enum WorkLocationType
{
    /// <summary>
    /// Henüz görev yeri belirlenmedi. Varsayılan: mevcut kayıtların
    /// tamamı buraya düşer ve "atama bekliyor" olarak işaretlenir.
    /// </summary>
    Unassigned = 0,

    /// <summary>Merkez ofis.</summary>
    HeadOffice = 1,

    /// <summary>
    /// Şantiye. Fiili atama ProjectSiteAssignment ile yürür; bu tür
    /// seçili ama aktif ataması yoksa personel yine "atama bekliyor"
    /// sayılır.
    /// </summary>
    ProjectSite = 2
}

public sealed class Personnel : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string? IdentityNumber { get; set; }
    public DateTime? BirthDate { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public string? JobTitle { get; set; }
    public string? Profession { get; set; }
    public string? SgkRegistrationNumber { get; set; }

    public DateTime? EmploymentStartDate { get; set; }
    public DateTime? EmploymentEndDate { get; set; }

    public decimal? MonthlySalary { get; set; }

    /// <summary>
    /// Personelin görev yeri: merkez mi, şantiye mi, yoksa henüz
    /// atanmadı mı.
    ///
    /// Şantiyeye atandıysa fiili atama <see cref="SiteAssignments"/>
    /// üzerinden yürür; bu alan yalnızca "hangi tür" sorusunu
    /// cevaplar. Ayrı bir alan olmasının sebebi: aktif şantiye
    /// ataması yokluğundan "merkezde" sonucunu çıkarmak, hiç
    /// atanmamış personeli de merkez göstermek olurdu.
    /// </summary>
    public WorkLocationType WorkLocationType { get; set; } = WorkLocationType.Unassigned;
    public PersonnelStatus Status { get; set; } = PersonnelStatus.Active;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<PersonnelAssignment> Assignments { get; set; }
        = new List<PersonnelAssignment>();

    public ICollection<ProjectSiteAssignment> SiteAssignments { get; set; }
        = new List<ProjectSiteAssignment>();
}
