namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// Haftanın çalışılan günleri. Bayrak olarak tutuluyor çünkü şantiyeden
/// şantiyeye değişir: bir işte cumartesi çalışılır, ötekinde çalışılmaz.
/// </summary>
[Flags]
public enum WorkWeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary>Şantiyede yaygın olan: pazar hariç her gün.</summary>
    MondayToSaturday = MondayToFriday | Saturday,

    /// <summary>Takvim günü — tatil kavramı yok.</summary>
    AllDays = MondayToSaturday | Sunday
}

/// <summary>
/// İş programının takvimi: hangi günler çalışılıyor, süreler nasıl
/// gerçek tarihe dönüyor.
///
/// Saf ve veritabanısız. Tüm süreler ÇALIŞMA GÜNÜ cinsindendir:
/// "10 günlük iş" 10 çalışma günüdür, araya giren pazar ve resmî tatil
/// sayılmaz. Takvim günüyle çalışmak isteyen
/// <see cref="Continuous"/> kullanır — o zaman her gün çalışma günüdür
/// ve iki mod arasındaki tek fark budur; motorun geri kalanı değişmez.
///
/// <see cref="DateOnly"/> kullanılıyor: saat/zaman dilimi bir iş
/// programında anlamsızdır ve UTC kaymaları gün sınırında sessizce
/// bir gün oynatır.
/// </summary>
public sealed class ScheduleCalendar
{
    /// <summary>
    /// Arama üst sınırı. Tatil listesi yanlışlıkla yılları kaplarsa
    /// sonsuz döngü yerine anlaşılır hata verilsin diye.
    /// </summary>
    private const int SearchLimitDays = 3650;

    private readonly WorkWeekDays _workDays;
    private readonly HashSet<DateOnly> _holidays;

    public ScheduleCalendar(
        WorkWeekDays workDays = WorkWeekDays.MondayToSaturday,
        IEnumerable<DateOnly>? holidays = null)
    {
        if (workDays == WorkWeekDays.None)
        {
            throw new ArgumentException(
                "İş takviminde en az bir çalışma günü olmalı.", nameof(workDays));
        }

        _workDays = workDays;
        _holidays = holidays?.ToHashSet() ?? [];
    }

    /// <summary>Pazar hariç çalışılan varsayılan şantiye takvimi.</summary>
    public static ScheduleCalendar Default { get; } = new();

    /// <summary>Her günün çalışma günü sayıldığı takvim.</summary>
    public static ScheduleCalendar Continuous { get; } = new(WorkWeekDays.AllDays);

    public WorkWeekDays WorkDays => _workDays;
    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    public bool IsWorkDay(DateOnly date) =>
        (_workDays & ToFlag(date.DayOfWeek)) != 0 && !_holidays.Contains(date);

    /// <summary>Verilen günden itibaren ilk çalışma günü (gün kendisi dahil).</summary>
    public DateOnly NextWorkDay(DateOnly date) => Seek(date, forward: true);

    /// <summary>Verilen günden geriye ilk çalışma günü (gün kendisi dahil).</summary>
    public DateOnly PreviousWorkDay(DateOnly date) => Seek(date, forward: false);

    /// <summary>
    /// Çalışma günü ekler/çıkarır. Başlangıç günü SAYILMAZ: sıfır
    /// kaydırma günü çalışma gününe yuvarlar, +1 bir sonraki çalışma
    /// gününü verir. Bağımlılık gecikmesi (lag) böyle uygulanıyor.
    /// </summary>
    public DateOnly AddWorkDays(DateOnly date, int offset)
    {
        var forward = offset >= 0;
        var current = Seek(date, forward);
        var remaining = Math.Abs(offset);

        for (var step = 0; step < remaining; step++)
        {
            current = Seek(current.AddDays(forward ? 1 : -1), forward);
        }

        return current;
    }

    /// <summary>
    /// Başlangıç ve süreden bitiş tarihi. Süre, başlangıç gününün
    /// KENDİSİNİ sayar: 1 günlük iş aynı gün biter.
    /// </summary>
    public DateOnly FinishFromStart(DateOnly start, int durationWorkDays)
    {
        if (durationWorkDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationWorkDays), "Süre en az bir çalışma günü olmalı.");
        }

        return AddWorkDays(NextWorkDay(start), durationWorkDays - 1);
    }

    /// <summary>Bitiş ve süreden başlangıç tarihi — geri hesap.</summary>
    public DateOnly StartFromFinish(DateOnly finish, int durationWorkDays)
    {
        if (durationWorkDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationWorkDays), "Süre en az bir çalışma günü olmalı.");
        }

        return AddWorkDays(PreviousWorkDay(finish), -(durationWorkDays - 1));
    }

    /// <summary>
    /// İki tarih arasındaki çalışma günü sayısı, İKİ UÇ DAHİL. Bir
    /// aktivitenin süresi budur.
    /// </summary>
    public int WorkDaysBetween(DateOnly start, DateOnly finish)
    {
        if (finish < start)
            return 0;

        var count = 0;

        for (var day = start; day <= finish; day = day.AddDays(1))
        {
            if (IsWorkDay(day))
                count++;
        }

        return count;
    }

    /// <summary>
    /// <paramref name="from"/> gününden <paramref name="to"/> gününe
    /// kaç çalışma günü ADIM var. Aynı güne sıfır, bir sonraki çalışma
    /// gününe bir; geriye doğru negatif.
    ///
    /// Bolluk (float) ve gecikme gün sayısı bununla ölçülüyor:
    /// "iki uç dahil" sayım burada bir fazla verirdi.
    /// </summary>
    public int WorkDayOffset(DateOnly from, DateOnly to)
    {
        var start = NextWorkDay(from);
        var end = NextWorkDay(to);

        return end >= start
            ? WorkDaysBetween(start, end) - 1
            : -(WorkDaysBetween(end, start) - 1);
    }

    /// <summary>Süre: iki uç dahil çalışma günü, en az 1.</summary>
    public int DurationOf(DateOnly start, DateOnly finish) =>
        Math.Max(1, WorkDaysBetween(NextWorkDay(start), PreviousWorkDay(finish)));

    private DateOnly Seek(DateOnly date, bool forward)
    {
        var current = date;

        for (var step = 0; step <= SearchLimitDays; step++)
        {
            if (IsWorkDay(current))
                return current;

            current = current.AddDays(forward ? 1 : -1);
        }

        throw new InvalidOperationException(
            $"{date:dd.MM.yyyy} tarihinden itibaren {SearchLimitDays} gün " +
            "içinde çalışma günü bulunamadı — tatil listesini kontrol edin.");
    }

    private static WorkWeekDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkWeekDays.Monday,
        DayOfWeek.Tuesday => WorkWeekDays.Tuesday,
        DayOfWeek.Wednesday => WorkWeekDays.Wednesday,
        DayOfWeek.Thursday => WorkWeekDays.Thursday,
        DayOfWeek.Friday => WorkWeekDays.Friday,
        DayOfWeek.Saturday => WorkWeekDays.Saturday,
        _ => WorkWeekDays.Sunday
    };
}
