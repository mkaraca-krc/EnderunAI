namespace EnderunAI.Api.Contracts;

public sealed class UpdateRoleWorkHourWindowsRequest
{
    public List<RoleWorkHourWindowItem> Windows { get; set; } = [];
}

public sealed class RoleWorkHourWindowItem
{
    /// <summary>.NET DayOfWeek değeri (0=Pazar .. 6=Cumartesi).</summary>
    public int DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
