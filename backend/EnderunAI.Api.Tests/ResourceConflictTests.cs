using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kaynak çakışması (G1).
///
/// Çakışma bir HATA değil, uyarıdır: bir ustabaşı gerçekten iki işi
/// birden yürütebilir. Bu testlerin koruduğu şey, çakışmanın görünür
/// olması ve İKİSİ DE KRİTİK YOLDAYSA ayrı bir ağırlıkla
/// işaretlenmesi — orada tek kişinin bölünmesi doğrudan proje bitişini
/// öteler.
/// </summary>
public sealed class ResourceConflictTests
{
    private static readonly ScheduleCalendar Calendar = ScheduleCalendar.Default;

    private static readonly Guid Ali = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Veli = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private static readonly Guid Panolar = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid Busbar = Guid.Parse("aaaa2222-0000-0000-0000-000000000002");
    private static readonly Guid Tava = Guid.Parse("aaaa3333-0000-0000-0000-000000000003");

    private static ResourceWindow Window(
        Guid resourceId,
        string resourceName,
        Guid activityId,
        string activityName,
        DateOnly start,
        DateOnly finish,
        bool critical = false,
        ScheduleResourceKind kind = ScheduleResourceKind.Personnel) =>
        new(kind, resourceId, resourceName, activityId, activityName,
            start, finish, critical);

    [Fact]
    public void OverlappingWindows_OfTheSameResource_AreReported()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7)),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12))
        ]);

        var conflict = Assert.Single(conflicts);

        Assert.Equal(Ali, conflict.ResourceId);
        Assert.Equal(new DateOnly(2026, 3, 5), conflict.OverlapStart);
        Assert.Equal(new DateOnly(2026, 3, 7), conflict.OverlapFinish);
        Assert.Equal(3, conflict.OverlapWorkDays);
        Assert.Equal("Uyarı", conflict.Severity);
    }

    [Fact]
    public void BothActivitiesOnTheCriticalPath_RaiseTheSeverity()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7), critical: true),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12), critical: true)
        ]);

        var conflict = Assert.Single(conflicts);

        Assert.True(conflict.BothCritical);
        Assert.Equal("Kritik", conflict.Severity);
    }

    [Fact]
    public void OnlyOneCritical_StaysAWarning()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7), critical: true),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12))
        ]);

        Assert.Equal("Uyarı", Assert.Single(conflicts).Severity);
    }

    [Fact]
    public void SequentialWindows_DoNotConflict()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 7))
        ]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DifferentResources_DoNotConflict()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7)),
            Window(Veli, "Veli ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7))
        ]);

        Assert.Empty(conflicts);
    }

    /// <summary>
    /// Aynı kimlik farklı türde ise aynı kaynak değildir: bir personel
    /// kimliğiyle bir taşeron sözleşmesi kimliği karışmamalı.
    /// </summary>
    [Fact]
    public void SameIdDifferentKind_IsNotTheSameResource()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7)),
            Window(Ali, "X Taşeron", Busbar, "Busbar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 7),
                kind: ScheduleResourceKind.Subcontractor)
        ]);

        Assert.Empty(conflicts);
    }

    /// <summary>
    /// Yalnızca pazara denk gelen örtüşme gerçek bir çakışma değildir —
    /// o gün zaten çalışılmıyor.
    /// </summary>
    [Fact]
    public void OverlapOnANonWorkingDayOnly_IsNotAConflict()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 14))
        ]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ThreeOverlappingWindows_ProduceEveryPair()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 12)),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 12)),
            Window(Ali, "Ali ekibi", Tava, "Kablo tava",
                new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 12))
        ]);

        Assert.Equal(3, conflicts.Count);
    }

    /// <summary>Kritik çakışmalar listenin başında durur.</summary>
    [Fact]
    public void CriticalConflicts_AreListedFirst()
    {
        var conflicts = ResourceConflictDetector.Detect(Calendar,
        [
            Window(Ali, "Ali ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 12)),
            Window(Ali, "Ali ekibi", Busbar, "Busbar",
                new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 12)),

            Window(Veli, "Veli ekibi", Tava, "Kablo tava",
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 12), critical: true),
            Window(Veli, "Veli ekibi", Panolar, "Panolar",
                new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 12), critical: true)
        ]);

        Assert.Equal(2, conflicts.Count);
        Assert.True(conflicts[0].BothCritical);
        Assert.Equal(Veli, conflicts[0].ResourceId);
    }

    [Fact]
    public void NoAssignments_ProduceNoConflicts()
    {
        Assert.Empty(ResourceConflictDetector.Detect(Calendar, []));
    }
}
