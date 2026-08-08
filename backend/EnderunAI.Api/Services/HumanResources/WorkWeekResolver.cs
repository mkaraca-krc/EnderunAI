using EnderunAI.Api.Services.Schedule;

namespace EnderunAI.Api.Services.HumanResources;

/// <param name="Source">Ayarın hangi kademeden geldiği — ekranda
/// yazılır ki kullanıcı neyi değiştirmesi gerektiğini bilsin.</param>
public sealed record WorkWeekResolution(
    WorkWeekDays Days,
    string Source,
    string Description);

/// <summary>
/// Bir personelin çalışma haftası.
///
/// Saf ve veritabanısız.
///
/// ÜÇ KADEME, en dardan en genişe:
///   1. Personele özel tanım (istisna)
///   2. Görev yeri: merkez kadrosu ayrı, şantiye ayrı
///   3. Şirket varsayılanı
///
/// Kademeler personel–DEPARTMAN bağı üzerinden kurulamadı: bu kod
/// tabanında Personnel kaydının departmanı yok (HrDepartment var ama
/// personele bağlanmıyor). Gerçekte var olan ve aranan ayrımı
/// karşılayan eksen görev yeri: ofis/idari kadro merkezde, saha
/// şantiyede. Cumartesi çalışması tam olarak bu çizgide ayrılıyor.
/// </summary>
public static class WorkWeekResolver
{
    /// <summary>Görev yeri: merkez.</summary>
    public const int HeadOffice = 1;

    /// <summary>Şantiyede yaygın olan: pazar hariç her gün.</summary>
    public const WorkWeekDays SiteDefault = WorkWeekDays.MondayToSaturday;

    public static WorkWeekResolution Resolve(
        int? personnelWorkWeek,
        int workLocationType,
        int? headOfficeWorkWeek,
        int? companyWorkWeek)
    {
        if (IsUsable(personnelWorkWeek))
        {
            return new WorkWeekResolution(
                (WorkWeekDays)personnelWorkWeek!.Value,
                "Personel",
                "Bu personel için özel çalışma haftası tanımlı.");
        }

        if (workLocationType == HeadOffice && IsUsable(headOfficeWorkWeek))
        {
            return new WorkWeekResolution(
                (WorkWeekDays)headOfficeWorkWeek!.Value,
                "Merkez kadrosu",
                "Merkez kadrosu için tanımlı çalışma haftası uygulanıyor.");
        }

        if (IsUsable(companyWorkWeek))
        {
            return new WorkWeekResolution(
                (WorkWeekDays)companyWorkWeek!.Value,
                "Şirket",
                "Şirket varsayılan çalışma haftası uygulanıyor.");
        }

        return new WorkWeekResolution(
            SiteDefault,
            "Varsayılan",
            "Hiçbir kademede tanım yok; pazar hariç her gün kabul edildi.");
    }

    /// <summary>
    /// Sıfır ve aralık dışı değerler tanım SAYILMAZ: çalışılan günü
    /// olmayan bir hafta maskesi süre hesabını sonsuz döngüye sokardı,
    /// bu yüzden kademe atlanır ve bir sonrakine bakılır.
    /// </summary>
    private static bool IsUsable(int? value) =>
        value is > 0 and <= 127;

    public static string Describe(WorkWeekDays days) => days switch
    {
        WorkWeekDays.AllDays => "Her gün (takvim günü)",
        WorkWeekDays.MondayToFriday => "Pazartesi–Cuma",
        WorkWeekDays.MondayToSaturday => "Pazartesi–Cumartesi",
        _ => string.Join(", ", DayNames(days))
    };

    private static IEnumerable<string> DayNames(WorkWeekDays days)
    {
        if (days.HasFlag(WorkWeekDays.Monday)) yield return "Pzt";
        if (days.HasFlag(WorkWeekDays.Tuesday)) yield return "Sal";
        if (days.HasFlag(WorkWeekDays.Wednesday)) yield return "Çar";
        if (days.HasFlag(WorkWeekDays.Thursday)) yield return "Per";
        if (days.HasFlag(WorkWeekDays.Friday)) yield return "Cum";
        if (days.HasFlag(WorkWeekDays.Saturday)) yield return "Cmt";
        if (days.HasFlag(WorkWeekDays.Sunday)) yield return "Paz";
    }
}
