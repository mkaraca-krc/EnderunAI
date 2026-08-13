using EnderunAI.Api.Services.Fleet;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DÖNEMSEL ARAÇ MASRAFININ GÜN ORANIYLA BÖLÜŞTÜRÜLMESİ — saf hesap.
///
/// Asıl güvence: TOPLAM DAİMA %100 KAPANIR. Kuruş farkı sessizce
/// düşseydi gider merkezi raporu ile ödenen tutar birbirini tutmaz ve
/// fark her dönem büyürdü.
/// </summary>
public sealed class VehicleCostAllocationTests
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DateTime D(int day) => new(2026, 4, day);

    [Fact]
    public void TekProje_TekSatir()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), null)], D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 30_000m);

        var line = Assert.Single(lines);

        Assert.Equal(ProjectA, line.ProjectId);
        Assert.Equal(30, line.Days);
        Assert.Equal(30_000m, line.Amount);
        Assert.Equal(100m, line.SharePercent);
    }

    [Fact]
    public void IkiProje_GunOraninaGoreBolusur()
    {
        // 1-14 Nisan A (14 gün), 15-30 Nisan B (16 gün).
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), D(15)), (ProjectB, D(15), null)], D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 30_000m);

        Assert.Equal(2, lines.Count);
        Assert.Equal(30, lines.Sum(x => x.Days));
        Assert.Equal(30_000m, lines.Sum(x => x.Amount));

        var a = lines.Single(x => x.ProjectId == ProjectA);
        var b = lines.Single(x => x.ProjectId == ProjectB);

        Assert.Equal(14, a.Days);
        Assert.Equal(16, b.Days);
        Assert.Equal(14_000m, a.Amount);
        Assert.Equal(16_000m, b.Amount);
    }

    /// <summary>
    /// KURUŞ FARKI KAYBOLMAZ: üçe bölünmeyen tutarda paylar yuvarlanır
    /// ama toplam yine tutarın kendisidir; fark en büyük paya yazılır.
    /// </summary>
    [Fact]
    public void KurusFarki_BirSatiraYazilir_ToplamKapanir()
    {
        // 10 gün A, 10 gün B, 10 gün merkez → üç eşit pay.
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), D(11)), (ProjectB, D(11), D(21))], D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 1_000m);

        Assert.Equal(3, lines.Count);
        Assert.Equal(1_000m, lines.Sum(x => x.Amount));

        // 1000/3 = 333,33 → üçü 999,99 eder; 1 kuruş bir satıra gider.
        Assert.Contains(lines, x => x.Amount == 333.34m);
        Assert.Equal(2, lines.Count(x => x.Amount == 333.33m));
    }

    /// <summary>
    /// ATAMA BOŞLUĞU: aracın hiçbir projeye atanmadığı günler MERKEZE
    /// yazılır. Atlanılsaydı gün toplamı dönemi kapatmaz ve tutarın bir
    /// kısmı hiçbir merkeze düşmezdi.
    /// </summary>
    [Fact]
    public void AtamaBosluğu_MerkezePayYazilir()
    {
        // 1-9 merkez (atama yok), 10-30 A.
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(10), null)], D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 3_000m);

        Assert.Equal(2, lines.Count);
        Assert.Equal(30, lines.Sum(x => x.Days));
        Assert.Equal(3_000m, lines.Sum(x => x.Amount));

        var center = lines.Single(x => x.ProjectId is null);
        Assert.Equal(9, center.Days);
    }

    /// <summary>
    /// ÇAKIŞAN ATAMA: veritabanı tek AÇIK atamaya izin veriyor ama
    /// geçmişte kapanmış atamalar tarih olarak üst üste binebilir.
    /// Böyle bir günde EN SON BAŞLAYAN atama geçerlidir — gün iki kez
    /// sayılmaz, toplam yine dönemi kapatır.
    /// </summary>
    [Fact]
    public void CakisanAtama_GunIkiKezSayilmaz()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), D(20)), (ProjectB, D(10), D(25))], D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 3_000m);

        Assert.Equal(30, lines.Sum(x => x.Days));
        Assert.Equal(3_000m, lines.Sum(x => x.Amount));

        // 1-9 A (9 gün), 10-24 B (15 gün), 25-30 merkez (6 gün).
        Assert.Equal(9, lines.Single(x => x.ProjectId == ProjectA).Days);
        Assert.Equal(15, lines.Single(x => x.ProjectId == ProjectB).Days);
        Assert.Equal(6, lines.Single(x => x.ProjectId is null).Days);
    }

    [Fact]
    public void TekGunlukDonem_GecerliVeTamKapanir()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), null)], D(5), D(5));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 100m);

        Assert.Equal(1, Assert.Single(lines).Days);
        Assert.Equal(100m, lines.Sum(x => x.Amount));
    }

    /// <summary>Hiçbir satır sıfır ya da negatif gün taşımaz.</summary>
    [Fact]
    public void HicbirSatir_SifirVeyaNegatifGunTasimaz()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), D(2)), (ProjectB, D(2), D(3))], D(1), D(10));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 1_000m);

        Assert.All(lines, x => Assert.True(x.Days > 0));
        Assert.Equal(10, lines.Sum(x => x.Days));
        Assert.Equal(1_000m, lines.Sum(x => x.Amount));
    }

    [Fact]
    public void TersDonem_Reddedilir()
    {
        Assert.Throws<ArgumentException>(() =>
            VehicleCostAllocationCalculator.BuildSegments([], D(10), D(1)));
    }

    [Fact]
    public void SifirTutar_Reddedilir()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), null)], D(1), D(30));

        Assert.Throws<ArgumentException>(() =>
            VehicleCostAllocationCalculator.Allocate(segments, 0m));
    }

    /// <summary>
    /// Aracın aynı projeye dönem içinde iki kez uğraması TEK satır
    /// üretir: iki ayrı pay, aynı merkezi raporda ikiye bölerdi.
    /// </summary>
    [Fact]
    public void AyniProjeyeIkiKezUgrama_TekSatirdaToplanir()
    {
        var segments = VehicleCostAllocationCalculator.BuildSegments(
            [(ProjectA, D(1), D(11)), (ProjectB, D(11), D(21)), (ProjectA, D(21), null)],
            D(1), D(30));

        var lines = VehicleCostAllocationCalculator.Allocate(segments, 3_000m);

        Assert.Equal(2, lines.Count);
        Assert.Equal(20, lines.Single(x => x.ProjectId == ProjectA).Days);
        Assert.Equal(3_000m, lines.Sum(x => x.Amount));
    }
}
