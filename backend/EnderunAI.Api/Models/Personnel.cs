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

    /// <summary>
    /// GÖREV YERİ DEĞİL, ORGANİZASYON BİRİMİ.
    ///
    /// Mesajlaşma kanallarının üyeliği buradan türeyecek. Rolden
    /// TÜRETİLMEDİ: rol bir yetki kavramı, departman bir organizasyon
    /// kavramı ve bugün de ayrışıyorlar — beş rolü olan bir kullanıcı
    /// var, tek departmanı olacak. Rolden türetseydik o kişi beş
    /// kanala birden düşer ve kimse sebebini anlamazdı.
    ///
    /// DEPARTMANI BOŞ PERSONEL HATA DEĞİL: kanal üyeliği almaz,
    /// o kadar. Bugün 81 personelin hiçbirinde dolu değil.
    ///
    /// DEĞİŞİKLİK TARİHÇESİ AYRI TABLODA
    /// (`personnel_department_history`): bu alan yalnız BUGÜNÜ
    /// söyler, "ayrıldığı tarihe kadarki geçmişi görür" kuralı ise
    /// dünkü cevabı gerektirir.
    /// </summary>
    public Guid? DepartmentId { get; set; }
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

    // --- Fazla mesai muvafakati ---
    //
    // Fazla çalışma için işçiden YILDA BİR yazılı onay alınması
    // gerekiyor (İş Kanunu m.41). Belgenin kendisi özlük arşivinde
    // durur; burada yalnızca hangi yıl için alındığı ve tarihi tutulur
    // ki bordro ön kontrolü "mesai ödemesi var ama muvafakati yok"
    // durumunu yakalayabilsin.

    /// <summary>Muvafakatin geçerli olduğu yıl. Boşsa alınmamış.</summary>
    public int? OvertimeConsentYear { get; set; }

    /// <summary>Muvafakatin alındığı tarih.</summary>
    public DateTime? OvertimeConsentDate { get; set; }

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

    /// <summary>
    /// Bu personele özel çalışma haftası (gün bayrağı). Boşsa görev
    /// yerine, o da yoksa şirket varsayılanına düşer — bkz.
    /// <see cref="Services.HumanResources.WorkWeekResolver"/>.
    ///
    /// İSTİSNA içindir: yarı zamanlı çalışan ya da cumartesi gelmeyen
    /// tek bir kişi için bütün şirketi değiştirmek gerekmesin.
    /// </summary>
    public int? WorkWeek { get; set; }

    /// <summary>
    /// Personel bir TAŞERON EKİBİNİN üyesiyse o taşeron sözleşmesi.
    ///
    /// Yalnızca sözleşmede SGK yükümlülüğü BİZDE olduğunda doldurulur:
    /// işçi taşeronun ama bordro bizde. Bu durumda işçinin bordro
    /// maliyeti taşeron hakedişinde "SGK/işçilik kesintisi" olarak
    /// birikir — yoksa aynı işçiliği hem kendi maliyetimizde hem
    /// taşerona ödediğimiz hakedişte iki kez saymış oluruz.
    ///
    /// Boş olması "bizim personelimiz" demektir; maliyet analizinde
    /// taşeron ekibi kendi satırında toplanır.
    /// </summary>
    public Guid? SubcontractorContractId { get; set; }
    public SubcontractorContract? SubcontractorContract { get; set; }

    public PersonnelStatus Status { get; set; } = PersonnelStatus.Active;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<PersonnelAssignment> Assignments { get; set; }
        = new List<PersonnelAssignment>();

    public ICollection<ProjectSiteAssignment> SiteAssignments { get; set; }
        = new List<ProjectSiteAssignment>();
}
