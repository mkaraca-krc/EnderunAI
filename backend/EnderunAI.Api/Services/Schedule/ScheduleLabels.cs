using EnderunAI.Api.Models.Schedule;

namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// Ekranda ve uçlarda kullanılan Türkçe adlar. Tek yerde durmalarının
/// nedeni: aynı durumun iki ekranda iki farklı adla çıkması kullanıcıyı
/// bunların farklı şeyler olduğuna inandırır.
/// </summary>
public static class ScheduleLabels
{
    public static string Dependency(ScheduleDependencyType type) => type switch
    {
        ScheduleDependencyType.StartToStart => "Başla-Başla",
        ScheduleDependencyType.FinishToFinish => "Bitir-Bitir",
        ScheduleDependencyType.StartToFinish => "Başla-Bitir",
        _ => "Bitir-Başla"
    };

    /// <summary>Bağın kısa açıklaması — seçim kutusunda yardımcı metin.</summary>
    public static string DependencyHint(ScheduleDependencyType type) => type switch
    {
        ScheduleDependencyType.StartToStart =>
            "Öncül başlamadan ardıl başlamaz.",
        ScheduleDependencyType.FinishToFinish =>
            "Öncül bitmeden ardıl bitemez.",
        ScheduleDependencyType.StartToFinish =>
            "Öncül başlamadan ardıl bitemez.",
        _ => "Öncül bitmeden ardıl başlamaz (en yaygın)."
    };

    public static string Status(ProjectScheduleStatus status) => status switch
    {
        ProjectScheduleStatus.Active => "Yürürlükte",
        ProjectScheduleStatus.Archived => "Arşivlendi",
        _ => "Taslak"
    };

    public static string WorkWeek(WorkWeekDays days) => days switch
    {
        WorkWeekDays.AllDays => "Takvim günü (her gün)",
        WorkWeekDays.MondayToFriday => "Pazartesi–Cuma",
        WorkWeekDays.MondayToSaturday => "Pazartesi–Cumartesi",
        _ => "Özel"
    };

    public static string Resource(ScheduleResourceKind kind) =>
        kind == ScheduleResourceKind.Subcontractor ? "Taşeron" : "Personel";
}
