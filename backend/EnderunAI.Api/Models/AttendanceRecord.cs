namespace EnderunAI.Api.Models;

/// <summary>
/// Puantajda bir günün çalışma durumu.
///
/// Bu numaralandırma sistemin tek doğru kaynağıdır. Daha önce backend
/// ile arayüz farklı kodlar kullanıyordu (arayüz 0'ı "Çalıştı", backend
/// 0'ı "Devamsız" sayıyordu); arayüzde çalıştı işaretlenen gün özette
/// devamsızlık olarak sayılıyordu. Kodlar backend'in mevcut anlamlarına
/// göre sabitlendi — canlıdaki tek puantaj kaydı (8 saat, Status=1) da
/// bu anlamla uyumlu.
/// </summary>
public enum AttendanceStatus
{
    /// <summary>Devamsız (mazeretsiz) — ücrete esas gün sayılmaz.</summary>
    Absent = 0,
    /// <summary>Çalıştı — tam gün.</summary>
    Worked = 1,
    /// <summary>Ücretli izin (yıllık izin) — ücrete esas gün sayılır.</summary>
    PaidLeave = 2,
    /// <summary>
    /// Raporlu. İş göremezlik ödeneğini SGK ödediği için işveren
    /// bordrosunda ücrete esas gün sayılmaz.
    /// </summary>
    SickReport = 3,
    /// <summary>Resmi tatil — ücrete esas gün sayılır.</summary>
    PublicHoliday = 4,
    /// <summary>Hafta tatili — ücrete esas gün sayılır.</summary>
    WeeklyHoliday = 5,
    /// <summary>Ücretsiz izin — ücrete esas gün sayılmaz.</summary>
    UnpaidLeave = 6,
    /// <summary>Mazeretli devamsızlık — ücrete esas gün sayılmaz.</summary>
    ExcusedAbsence = 7,
    /// <summary>Yarım gün çalıştı — yarım gün ücrete esas.</summary>
    HalfDay = 8,
    /// <summary>Uzaktan çalışma — tam gün sayılır.</summary>
    RemoteWork = 9
}

public sealed class AttendanceRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Günün geçtiği şantiye. İşçilik maliyetinin projeye ve şantiyeye
    /// dağıtılabilmesi için tutulur.
    /// </summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public Guid PersonnelId { get; set; }

    public DateTime WorkDate { get; set; }
    public int Status { get; set; }

    public TimeSpan? CheckInTime { get; set; }
    public TimeSpan? CheckOutTime { get; set; }

    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal SundayHours { get; set; }
    public decimal PublicHolidayHours { get; set; }
    public decimal TotalHours { get; set; }

    public string? TeamName { get; set; }
    public string? RoleName { get; set; }
    public string? WorkItemCode { get; set; }
    public string? WorkItemName { get; set; }

    /// <summary>
    /// Ekibin o gün çalıştığı icmal kısmı ("bugün aydınlatmada").
    /// OPSİYONEL: serbest metin WorkItemName alanının yerini almaz,
    /// onu kısma bağlayarak maliyet analizinde toplanabilir kılar.
    /// </summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }
    public string? LocationName { get; set; }

    public bool IsApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public string? Description { get; set; }
}
