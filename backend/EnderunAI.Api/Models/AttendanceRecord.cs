namespace EnderunAI.Api.Models;

public sealed class AttendanceRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid PersonnelId { get; set; }

    public DateTime WorkDate { get; set; }
    public int Status { get; set; }

    public TimeSpan? CheckInTime { get; set; }
    public TimeSpan? CheckOutTime { get; set; }

    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal NightShiftHours { get; set; }
    public decimal SundayHours { get; set; }
    public decimal PublicHolidayHours { get; set; }
    public decimal TotalHours { get; set; }

    public string? TeamName { get; set; }
    public string? RoleName { get; set; }
    public string? WorkItemCode { get; set; }
    public string? WorkItemName { get; set; }
    public string? LocationName { get; set; }

    public bool IsApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public string? Description { get; set; }
}
