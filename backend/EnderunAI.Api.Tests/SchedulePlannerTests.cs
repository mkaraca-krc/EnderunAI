using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kritik yol motoru (G1).
///
/// Referans hafta: 2026-03-02 pazartesi … 2026-03-07 cumartesi,
/// 2026-03-08 pazar tatil, 2026-03-09 pazartesi.
///
/// Bu testlerin koruduğu asıl fikirler:
/// - Bağımlılık tarihi yalnızca İLERİ iter. Elle konmuş geç bir tarih
///   sisteme öne çektirilemez; oradaki boşluk (malzeme bekleme,
///   işveren onayı) bilinçli olabilir.
/// - Hesap TEKRARLIDIR: aynı plan iki kez hesaplandığında tarihler
///   oynamaz. Oynasaydı her açılışta program kendiliğinden kayardı.
/// - Döngü hesaplanmaz, reddedilir.
/// - Kritik yol, bolluğu sıfır veya negatif olan zincirdir; gecikmesi
///   proje bitişini doğrudan öteleyen tek yer orasıdır.
/// </summary>
public sealed class SchedulePlannerTests
{
    private static readonly ScheduleCalendar Calendar = ScheduleCalendar.Default;

    private static readonly DateOnly Mon02 = new(2026, 3, 2);
    private static readonly DateOnly Tue03 = new(2026, 3, 3);
    private static readonly DateOnly Wed04 = new(2026, 3, 4);
    private static readonly DateOnly Thu05 = new(2026, 3, 5);
    private static readonly DateOnly Fri06 = new(2026, 3, 6);
    private static readonly DateOnly Sat07 = new(2026, 3, 7);
    private static readonly DateOnly Mon09 = new(2026, 3, 9);
    private static readonly DateOnly Tue10 = new(2026, 3, 10);

    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static ScheduleActivityInput Activity(
        Guid id, string name, DateOnly start, DateOnly finish) =>
        new(id, name, start, finish);

    private static ScheduledActivity Get(SchedulePlan plan, Guid id) =>
        plan.Activities.Single(x => x.Id == id);

    // ---------- Bitir-Başla ----------

    [Fact]
    public void FinishToStart_PushesSuccessorToTheNextWorkDay()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Kablo tavası", Mon02, Wed04),
                Activity(B, "Kablo çekimi", Mon02, Wed04)
            ],
            [new ScheduleDependencyInput(A, B)]);

        var b = Get(plan, B);

        Assert.Equal(Thu05, b.Start);
        Assert.Equal(Sat07, b.Finish);
        Assert.Equal(3, b.DurationWorkDays);
        Assert.Equal(3, b.ShiftedWorkDays);
        Assert.Equal(Sat07, plan.ProjectFinish);
    }

    [Fact]
    public void FinishToStart_LagAddsWorkDays()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Beton", Mon02, Wed04),
                Activity(B, "Montaj", Mon02, Mon02)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.FinishToStart, 2)]);

        // Çar bitiş + 2 gün bekleme → Per(1), Cum(2) beklenir, Cmt başlar.
        Assert.Equal(Sat07, Get(plan, B).Start);
    }

    /// <summary>
    /// Negatif gecikme = örtüşme: "duvar bitmeden bir gün önce
    /// kablo çekimine başlanır".
    /// </summary>
    [Fact]
    public void FinishToStart_NegativeLagOverlapsTheActivities()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Duvar", Mon02, Thu05),
                Activity(B, "Kablo", Mon02, Mon02)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.FinishToStart, -1)]);

        Assert.Equal(Thu05, Get(plan, B).Start);
    }

    // ---------- Başla-Başla ----------

    [Fact]
    public void StartToStart_AlignsTheStarts()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Pano montajı", Wed04, Fri06),
                Activity(B, "Pano kablolama", Mon02, Tue03)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.StartToStart)]);

        var b = Get(plan, B);

        Assert.Equal(Wed04, b.Start);
        Assert.Equal(Thu05, b.Finish);
    }

    [Fact]
    public void StartToStart_LagDelaysTheSuccessorStart()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Kazı", Mon02, Sat07),
                Activity(B, "Dolgu", Mon02, Mon02)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.StartToStart, 2)]);

        Assert.Equal(Wed04, Get(plan, B).Start);
    }

    // ---------- Bitir-Bitir ----------

    /// <summary>
    /// Bitişi zorlayan bağ başlangıcı GERİ hesaplar; süre korunur.
    /// Süre kısaltılsaydı iş kendiliğinden hızlanmış görünürdü.
    /// </summary>
    [Fact]
    public void FinishToFinish_PullsTheSuccessorFinishAndKeepsDuration()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Test", Mon02, Wed04),
                Activity(B, "Devreye alma", Mon02, Tue03)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.FinishToFinish)]);

        var b = Get(plan, B);

        Assert.Equal(Wed04, b.Finish);
        Assert.Equal(Tue03, b.Start);
        Assert.Equal(2, b.DurationWorkDays);
    }

    // ---------- Başla-Bitir ----------

    [Fact]
    public void StartToFinish_SuccessorCannotFinishBeforePredecessorStarts()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Yeni sistem devrede", Wed04, Fri06),
                Activity(B, "Eski sistem sökümü", Mon02, Tue03)
            ],
            [new ScheduleDependencyInput(A, B, ScheduleDependencyType.StartToFinish)]);

        var b = Get(plan, B);

        Assert.Equal(Wed04, b.Finish);
        Assert.Equal(Tue03, b.Start);
    }

    // ---------- Tek yönlü kaydırma ----------

    /// <summary>
    /// Kullanıcı ardılı zaten daha geç bir tarihe koymuşsa bağ onu öne
    /// ÇEKMEZ.
    /// </summary>
    [Fact]
    public void Dependency_NeverPullsAnActivityEarlier()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Keşif", Mon02, Mon02),
                Activity(B, "İmalat", Tue10, Tue10)
            ],
            [new ScheduleDependencyInput(A, B)]);

        var b = Get(plan, B);

        Assert.Equal(Tue10, b.Start);
        Assert.Equal(0, b.ShiftedWorkDays);
    }

    /// <summary>
    /// Hesabın çıktısı tekrar girdi yapıldığında tarihler oynamamalı.
    /// </summary>
    [Fact]
    public void Plan_IsStableWhenRecalculated()
    {
        ScheduleActivityInput[] activities =
        [
            Activity(A, "A", Mon02, Wed04),
            Activity(B, "B", Mon02, Wed04),
            Activity(C, "C", Mon02, Mon02)
        ];

        ScheduleDependencyInput[] links =
        [
            new(A, B),
            new(B, C, ScheduleDependencyType.StartToStart, 1)
        ];

        var first = SchedulePlanner.Build(Calendar, activities, links);

        var recomputed = SchedulePlanner.Build(
            Calendar,
            first.Activities
                .Select(x => new ScheduleActivityInput(x.Id, x.Name, x.Start, x.Finish))
                .ToList(),
            links);

        foreach (var activity in first.Activities)
        {
            var again = Get(recomputed, activity.Id);

            Assert.Equal(activity.Start, again.Start);
            Assert.Equal(activity.Finish, again.Finish);
        }

        Assert.Equal(first.ProjectFinish, recomputed.ProjectFinish);
        Assert.All(recomputed.Activities, x => Assert.Equal(0, x.ShiftedWorkDays));
    }

    // ---------- Döngü ----------

    [Fact]
    public void DirectCycle_IsRejected()
    {
        ScheduleActivityInput[] activities =
        [
            Activity(A, "Pano", Mon02, Mon02),
            Activity(B, "Kablo", Mon02, Mon02)
        ];

        ScheduleDependencyInput[] links = [new(A, B), new(B, A)];

        var message = SchedulePlanner.FindCycle(activities, links);

        Assert.NotNull(message);
        Assert.Contains("Döngüsel bağımlılık", message!);
        Assert.Contains("Pano", message!);
        Assert.Contains("Kablo", message!);

        Assert.Throws<ScheduleCycleException>(
            () => SchedulePlanner.Build(Calendar, activities, links));
    }

    [Fact]
    public void IndirectCycle_IsRejected()
    {
        ScheduleActivityInput[] activities =
        [
            Activity(A, "A", Mon02, Mon02),
            Activity(B, "B", Mon02, Mon02),
            Activity(C, "C", Mon02, Mon02)
        ];

        ScheduleDependencyInput[] links = [new(A, B), new(B, C), new(C, A)];

        Assert.NotNull(SchedulePlanner.FindCycle(activities, links));
    }

    [Fact]
    public void AcyclicGraph_HasNoCycleReported()
    {
        ScheduleActivityInput[] activities =
        [
            Activity(A, "A", Mon02, Mon02),
            Activity(B, "B", Mon02, Mon02),
            Activity(C, "C", Mon02, Mon02)
        ];

        Assert.Null(SchedulePlanner.FindCycle(
            activities, [new(A, B), new(A, C), new(B, C)]));
    }

    /// <summary>Kendine bağ hesabı bozmaz, uyarıya düşer.</summary>
    [Fact]
    public void SelfDependency_IsIgnoredWithWarning()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [Activity(A, "Pano", Mon02, Wed04)],
            [new ScheduleDependencyInput(A, A)]);

        Assert.Equal(Mon02, Get(plan, A).Start);
        Assert.Contains(plan.Warnings, x => x.Contains("kendisine bağlanmış"));
    }

    [Fact]
    public void UnknownActivityReference_IsIgnoredWithWarning()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [Activity(A, "Pano", Mon02, Wed04)],
            [new ScheduleDependencyInput(A, D)]);

        Assert.Single(plan.Activities);
        Assert.Contains(plan.Warnings, x => x.Contains("bulunmayan"));
    }

    [Fact]
    public void EmptySchedule_DoesNotThrow()
    {
        var plan = SchedulePlanner.Build(Calendar, [], []);

        Assert.Empty(plan.Activities);
        Assert.Contains(plan.Warnings, x => x.Contains("aktivite yok"));
    }

    // ---------- Kritik yol ve bolluk ----------

    /// <summary>
    /// Elle hesaplanmış referans ağ:
    ///
    ///   S (Pzt, 1g) ─┬─ A (Sal–Cmt, 5g) ─┐
    ///                └─ B (Sal–Çar, 2g) ─┴─ E (1g)
    ///
    /// A uzun kol; kritik yol S→A→E. B'nin bolluğu 3 iş günü.
    /// </summary>
    [Fact]
    public void CriticalPath_IsTheLongestChain_AndSlackIsOnTheShortOne()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "Kolon kablo", Tue03, Sat07),
                Activity(B, "Kablo tava", Tue03, Wed04),
                Activity(C, "Saha teslim", Mon02, Mon02),
                Activity(D, "Test", Tue03, Tue03)
            ],
            [
                new ScheduleDependencyInput(C, A),
                new ScheduleDependencyInput(C, B),
                new ScheduleDependencyInput(A, D),
                new ScheduleDependencyInput(B, D)
            ]);

        Assert.Equal(Mon09, plan.ProjectFinish);

        Assert.Equal(0, Get(plan, C).TotalFloatWorkDays);
        Assert.Equal(0, Get(plan, A).TotalFloatWorkDays);
        Assert.Equal(0, Get(plan, D).TotalFloatWorkDays);
        Assert.Equal(3, Get(plan, B).TotalFloatWorkDays);

        Assert.True(Get(plan, A).IsCritical);
        Assert.False(Get(plan, B).IsCritical);

        Assert.Equal(new[] { C, A, D }, plan.CriticalActivityIds);
    }

    [Fact]
    public void ParallelIndependentActivities_AreBothCriticalWhenEqualLength()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [
                Activity(A, "A", Mon02, Wed04),
                Activity(B, "B", Mon02, Wed04)
            ],
            []);

        Assert.True(Get(plan, A).IsCritical);
        Assert.True(Get(plan, B).IsCritical);
    }

    // ---------- Termin ----------

    /// <summary>
    /// Termin plan bitişinden önceyse bolluklar negatife düşer: plan bu
    /// haliyle bile terminde bitmiyor demektir ve bu, henüz hiç gecikme
    /// yaşanmadan görünmeli.
    /// </summary>
    [Fact]
    public void DeadlineBeforePlannedFinish_ProducesNegativeFloatAndWarning()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [Activity(A, "Busbar", Mon02, Sat07)],
            [],
            deadline: Wed04);

        Assert.Equal(-3, plan.DeadlineFloatWorkDays);
        Assert.Equal(-3, Get(plan, A).TotalFloatWorkDays);
        Assert.True(Get(plan, A).IsCritical);
        Assert.Contains(plan.Warnings, x => x.Contains("termini"));
    }

    /// <summary>
    /// Bol terminde kritik yol KAYBOLMAMALI: geri geçişin çıpası plan
    /// bitişinde kalır, termin bolluğu ayrıca raporlanır.
    /// </summary>
    [Fact]
    public void DeadlineAfterPlannedFinish_KeepsTheCriticalPathVisible()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [Activity(A, "Busbar", Mon02, Sat07)],
            [],
            deadline: new DateOnly(2026, 3, 14));

        Assert.Equal(6, plan.DeadlineFloatWorkDays);
        Assert.Equal(0, Get(plan, A).TotalFloatWorkDays);
        Assert.True(Get(plan, A).IsCritical);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void NoDeadline_LeavesDeadlineFloatUnknown()
    {
        var plan = SchedulePlanner.Build(
            Calendar, [Activity(A, "Busbar", Mon02, Sat07)], []);

        Assert.Null(plan.DeadlineFloatWorkDays);
        Assert.Null(plan.Deadline);
    }

    // ---------- Bozuk veri ----------

    [Fact]
    public void FinishBeforeStart_IsTreatedAsOneDayWithWarning()
    {
        var plan = SchedulePlanner.Build(
            Calendar, [Activity(A, "Ters kayıt", Sat07, Mon02)], []);

        Assert.Equal(1, Get(plan, A).DurationWorkDays);
        Assert.Contains(plan.Warnings, x => x.Contains("bitişi başlangıcından önce"));
    }

    /// <summary>Pazar başlangıcı ilk çalışma gününe yuvarlanır.</summary>
    [Fact]
    public void StartOnSunday_IsMovedToTheNextWorkDay()
    {
        var plan = SchedulePlanner.Build(
            Calendar,
            [Activity(A, "Pano", new DateOnly(2026, 3, 8), Tue10)],
            []);

        Assert.Equal(Mon09, Get(plan, A).Start);
    }
}
