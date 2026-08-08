using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Gerçekleşme yüzdesinin kaynağı (G3).
///
/// Korunan fikir: ÖLÇÜLMÜŞ veri elle girilene her zaman üstün gelir ve
/// yüzdeyle birlikte KAYNAĞI da taşınır. Kullanıcı bir orana bakarken
/// onun saha raporundan mı geldiğini yoksa birinin yazdığı bir sayı mı
/// olduğunu bilmeli — ikisi aynı görünürse ikisine de güvenilmez.
/// </summary>
public sealed class ScheduleProgressResolverTests
{
    private static readonly Guid Parent = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ChildA = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid ChildB = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid Section = Guid.Parse("44444444-0000-0000-0000-000000000004");
    private static readonly Guid BoqItem = Guid.Parse("55555555-0000-0000-0000-000000000005");

    private static readonly Dictionary<Guid, decimal> Empty = [];

    private static IReadOnlyDictionary<Guid, ScheduleProgressResult> Resolve(
        IReadOnlyCollection<ScheduleProgressInput> activities,
        IReadOnlyDictionary<Guid, decimal>? sectionField = null,
        IReadOnlyDictionary<Guid, decimal>? sectionEmployer = null,
        IReadOnlyDictionary<Guid, decimal>? itemField = null,
        IReadOnlyDictionary<Guid, decimal>? itemEmployer = null) =>
        ScheduleProgressResolver.Resolve(
            activities,
            sectionField ?? Empty,
            sectionEmployer ?? Empty,
            itemField ?? Empty,
            itemEmployer ?? Empty);

    // ---------- Kaynak sırası ----------

    [Fact]
    public void SectionLinkedBar_TakesTheSectionFieldRate()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, Section, null, null, 10)],
            sectionField: new Dictionary<Guid, decimal> { [Section] = 42.5m });

        Assert.Equal(42.5m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.Section, result[Parent].Source);
        Assert.Equal("Saha raporu (icmal kısmı)", result[Parent].SourceName);
    }

    [Fact]
    public void BoqItemLinkedBar_TakesTheItemFieldRate()
    {
        var result = Resolve(
            [new ScheduleProgressInput(ChildA, Parent, null, BoqItem, null, 5)],
            itemField: new Dictionary<Guid, decimal> { [BoqItem] = 80m });

        Assert.Equal(80m, result[ChildA].Rate);
        Assert.Equal(ScheduleProgressSource.BoqItem, result[ChildA].Source);
    }

    /// <summary>
    /// İcmal satırı bağı kısım bağından önce gelir: satır daha
    /// spesifiktir.
    /// </summary>
    [Fact]
    public void BoqItemWins_OverSection()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, Section, BoqItem, null, 5)],
            sectionField: new Dictionary<Guid, decimal> { [Section] = 20m },
            itemField: new Dictionary<Guid, decimal> { [BoqItem] = 90m });

        Assert.Equal(90m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.BoqItem, result[Parent].Source);
    }

    [Fact]
    public void UnlinkedBar_UsesTheManualRate()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, null, null, 35m, 5)]);

        Assert.Equal(35m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.Manual, result[Parent].Source);
        Assert.Equal("Elle girildi", result[Parent].SourceName);
    }

    /// <summary>
    /// Hiçbir kaynağa bağlı olmayan çubuk sıfır gösterir ama kaynağı
    /// "ölçülemiyor" der: sıfır ilerleme ile ölçülemeyen ilerleme aynı
    /// şey değil.
    /// </summary>
    [Fact]
    public void BarWithNoSource_IsReportedAsUnmeasurable()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, null, null, null, 5)]);

        Assert.Equal(0m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.None, result[Parent].Source);
        Assert.Equal("Ölçülemiyor", result[Parent].SourceName);
    }

    /// <summary>
    /// Kısma bağlı ama o kısımda icmal kalemi yoksa oran ölçülemez;
    /// sessizce sıfır yazmak yerine kaynağı "ölçülemiyor" olur.
    /// </summary>
    [Fact]
    public void SectionWithoutBoqItems_IsUnmeasurable()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, Section, null, null, 5)]);

        Assert.Equal(ScheduleProgressSource.None, result[Parent].Source);
    }

    // ---------- Alt aktivitelerden toplama ----------

    /// <summary>
    /// Ağırlık SÜREdir: iki günlük bir işle iki aylık bir iş eşit
    /// sayılırsa yüzde gerçeği yansıtmaz.
    /// </summary>
    [Fact]
    public void ParentWithoutOwnSource_AveragesChildrenWeightedByDuration()
    {
        var result = Resolve(
        [
            new ScheduleProgressInput(Parent, null, null, null, null, 30),
            new ScheduleProgressInput(ChildA, Parent, null, null, 100m, 10),
            new ScheduleProgressInput(ChildB, Parent, null, null, 0m, 30)
        ]);

        // (100×10 + 0×30) / 40 = 25
        Assert.Equal(25m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.Children, result[Parent].Source);
    }

    /// <summary>
    /// Kısma bağlı ana çubuk, alt aktivitelerinin ortalamasını DEĞİL
    /// kısmın kendi saha oranını kullanır: kısım oranı sözleşme
    /// tutarıyla ağırlıklı gerçek veridir.
    /// </summary>
    [Fact]
    public void SectionLinkedParent_IgnoresItsChildren()
    {
        var result = Resolve(
        [
            new ScheduleProgressInput(Parent, null, Section, null, null, 30),
            new ScheduleProgressInput(ChildA, Parent, null, null, 100m, 10)
        ],
        sectionField: new Dictionary<Guid, decimal> { [Section] = 12m });

        Assert.Equal(12m, result[Parent].Rate);
        Assert.Equal(ScheduleProgressSource.Section, result[Parent].Source);
    }

    /// <summary>
    /// Kaynağı olmayan alt aktivite sıfır sayılır — ama ana çubuktan
    /// ÖNCE çözülmeli, yoksa sıralamaya göre farklı sonuç çıkardı.
    /// </summary>
    [Fact]
    public void ParentListedBeforeItsChildren_StillAveragesCorrectly()
    {
        var result = Resolve(
        [
            new ScheduleProgressInput(Parent, null, null, null, null, 20),
            new ScheduleProgressInput(ChildA, Parent, null, BoqItem, null, 10),
            new ScheduleProgressInput(ChildB, Parent, null, null, null, 10)
        ],
        itemField: new Dictionary<Guid, decimal> { [BoqItem] = 60m });

        // (60×10 + 0×10) / 20 = 30
        Assert.Equal(30m, result[Parent].Rate);
    }

    // ---------- İşveren kabulü ----------

    /// <summary>
    /// Saha ile işveren kabulü ayrı taşınır; farkı devreden iştir ve
    /// karıştırılırsa görünmez olur.
    /// </summary>
    [Fact]
    public void EmployerRate_IsCarriedSeparatelyFromField()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, Section, null, null, 10)],
            sectionField: new Dictionary<Guid, decimal> { [Section] = 70m },
            sectionEmployer: new Dictionary<Guid, decimal> { [Section] = 55m });

        Assert.Equal(70m, result[Parent].Rate);
        Assert.Equal(55m, result[Parent].EmployerRate);
    }

    [Fact]
    public void ManualBar_HasNoEmployerRate()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, null, null, 40m, 10)]);

        Assert.Null(result[Parent].EmployerRate);
    }

    // ---------- Sınırlama ----------

    /// <summary>
    /// Kalem bazında oran 100'ü aşabilir (sözleşme üstü imalat) ama
    /// "işin %130'u bitti" anlamsız bir cümledir.
    /// </summary>
    [Fact]
    public void RateAboveHundred_IsClamped()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, Section, null, null, 10)],
            sectionField: new Dictionary<Guid, decimal> { [Section] = 130m });

        Assert.Equal(100m, result[Parent].Rate);
    }

    [Fact]
    public void NegativeRate_IsClampedToZero()
    {
        var result = Resolve(
            [new ScheduleProgressInput(Parent, null, null, null, -5m, 10)]);

        Assert.Equal(0m, result[Parent].Rate);
    }

    [Fact]
    public void EmptySchedule_ResolvesToNothing()
    {
        Assert.Empty(Resolve([]));
    }
}
