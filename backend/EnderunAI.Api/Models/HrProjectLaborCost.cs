namespace EnderunAI.Api.Models;

public sealed class HrProjectLaborCost : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PersonnelId { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public DateTime WorkDate { get; set; }
    public Guid? AttendanceRecordId { get; set; }
    public string? WorkItemCode { get; set; }
    public string? WorkItemName { get; set; }

    /// <summary>Puantajdan taşınan icmal kısmı; boş olabilir.</summary>
    public Guid? ProjectHakedisSectionId { get; set; }

    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal SundayHours { get; set; }
    public decimal PublicHolidayHours { get; set; }

    public decimal NormalCost { get; set; }
    public decimal OvertimeCost { get; set; }
    public decimal SundayCost { get; set; }
    public decimal PublicHolidayCost { get; set; }
    public decimal MealCost { get; set; }
    public decimal AccommodationCost { get; set; }
    public decimal ShuttleCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal CompensationCost { get; set; }
    public decimal TotalLaborCost { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
}
