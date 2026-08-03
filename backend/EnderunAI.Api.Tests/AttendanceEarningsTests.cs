using EnderunAI.Api.Models;
using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz E3: puantajın ücrete dönüşmesi. Çarpanlar İş Kanunu m.41 (fazla
/// çalışma %50 zamlı) ve m.47 (tatil çalışması) esasına dayanır.
/// </summary>
public sealed class AttendanceEarningsTests
{
    /// <summary>Brüt 33.030 → günlük 1.101, saatlik 146,80.</summary>
    private static SalaryRates Rates(
        decimal overtimeMultiplier = 1.5m,
        decimal sundayMultiplier = 2m,
        decimal publicHolidayMultiplier = 2m) => new(
            MonthlyGross: 33_030m,
            DailyRate: 1_101m,
            HourlyRate: 146.80m,
            OvertimeMultiplier: overtimeMultiplier,
            SundayMultiplier: sundayMultiplier,
            PublicHolidayMultiplier: publicHolidayMultiplier);

    private static AttendanceDay Day(
        AttendanceStatus status,
        decimal overtime = 0m,
        decimal sunday = 0m,
        decimal holiday = 0m) =>
        new(status, overtime, sunday, holiday);

    [Fact]
    public void NoAttendanceRecorded_KeepsFullMonthlySalary()
    {
        var result = AttendanceEarningsCalculator.Calculate(
            Rates(), Array.Empty<AttendanceDay>());

        Assert.Equal(33_030m, result.NormalWorkAmount);
        Assert.Equal(33_030m, result.TotalEarnings);
        Assert.Equal(0m, result.PaidDays);
    }

    [Fact]
    public void FullMonth_EqualsMonthlySalary()
    {
        var days = Enumerable.Range(0, 30)
            .Select(_ => Day(AttendanceStatus.Worked))
            .ToList();

        var result = AttendanceEarningsCalculator.Calculate(Rates(), days);

        Assert.Equal(30m, result.PaidDays);
        Assert.Equal(33_030m, result.NormalWorkAmount);
    }

    /// <summary>
    /// Devamsız günler ödenmez; ücretli izin, hafta tatili ve resmi
    /// tatil ödenir; yarım gün yarım sayılır.
    /// </summary>
    [Fact]
    public void PaidDayFactors_FollowStatusSemantics()
    {
        var days = new[]
        {
            Day(AttendanceStatus.Worked),          // 1
            Day(AttendanceStatus.RemoteWork),      // 1
            Day(AttendanceStatus.PaidLeave),       // 1
            Day(AttendanceStatus.PublicHoliday),   // 1
            Day(AttendanceStatus.WeeklyHoliday),   // 1
            Day(AttendanceStatus.HalfDay),         // 0,5
            Day(AttendanceStatus.Absent),          // 0
            Day(AttendanceStatus.SickReport),      // 0
            Day(AttendanceStatus.UnpaidLeave),     // 0
            Day(AttendanceStatus.ExcusedAbsence)   // 0
        };

        var result = AttendanceEarningsCalculator.Calculate(Rates(), days);

        Assert.Equal(5.5m, result.PaidDays);
        // AbsentDays yalnızca "Devamsız" statüsünü sayar; raporlu,
        // ücretsiz izin ve mazeretli devamsızlık ödenmez ama devamsızlık
        // olarak raporlanmaz.
        Assert.Equal(1, result.AbsentDays);
        Assert.Equal(6_055.50m, result.NormalWorkAmount); // 1.101 × 5,5
    }

    /// <summary>
    /// 10 saat fazla mesai: 146,80 × 1,5 × 10 = 2.202,00 — normal ücretin
    /// üstüne eklenir.
    /// </summary>
    [Fact]
    public void Overtime_UsesOneAndHalfMultiplier()
    {
        var days = new[]
        {
            Day(AttendanceStatus.Worked, overtime: 4m),
            Day(AttendanceStatus.Worked, overtime: 6m)
        };

        var result = AttendanceEarningsCalculator.Calculate(Rates(), days);

        Assert.Equal(2_202.00m, result.OvertimeAmount);
        Assert.Equal(2_202.00m, result.NormalWorkAmount); // 2 gün × 1.101
        Assert.Equal(4_404.00m, result.TotalEarnings);
    }

    /// <summary>
    /// Hafta tatili ve resmi tatil çalışması 2 kat: 8 saat × 146,80 × 2.
    /// </summary>
    [Fact]
    public void HolidayWork_UsesDoubleMultiplier()
    {
        var days = new[]
        {
            Day(AttendanceStatus.WeeklyHoliday, sunday: 8m),
            Day(AttendanceStatus.PublicHoliday, holiday: 8m)
        };

        var result = AttendanceEarningsCalculator.Calculate(Rates(), days);

        Assert.Equal(2_348.80m, result.SundayWorkAmount);
        Assert.Equal(2_348.80m, result.PublicHolidayAmount);

        // Tatil günleri ücrete esas gün olarak da sayılır.
        Assert.Equal(2_202.00m, result.NormalWorkAmount);
        Assert.Equal(6_899.60m, result.TotalEarnings);
    }

    /// <summary>
    /// Çarpan alanına yüzde girilmesi (1,5 yerine 150) canlıda yaşanmış
    /// bir hata. Motor böyle bir değerle ücret hesaplamayı reddeder.
    /// </summary>
    [Theory]
    [InlineData(150, 2, 2)]
    [InlineData(1.5, 200, 2)]
    [InlineData(1.5, 2, 200)]
    public void PercentageEnteredAsMultiplier_IsRejected(
        decimal overtime, decimal sunday, decimal holiday)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AttendanceEarningsCalculator.Calculate(
                Rates(overtime, sunday, holiday),
                new[] { Day(AttendanceStatus.Worked, overtime: 1m) }));

        Assert.Contains("yüzde", exception.Message);
    }

    [Fact]
    public void NegativeMultiplier_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AttendanceEarningsCalculator.Calculate(
                Rates(overtimeMultiplier: -1m),
                new[] { Day(AttendanceStatus.Worked) }));
    }

    /// <summary>
    /// Tek günün maliyeti, proje/şantiye maliyet dağıtımının temeli.
    /// </summary>
    [Fact]
    public void SingleDayCost_CoversNormalAndOvertime()
    {
        var result = AttendanceEarningsCalculator.CalculateDay(
            Rates(), Day(AttendanceStatus.Worked, overtime: 2m));

        Assert.Equal(1_101m, result.NormalWorkAmount);
        Assert.Equal(440.40m, result.OvertimeAmount); // 146,80 × 1,5 × 2
        Assert.Equal(1_541.40m, result.TotalEarnings);
    }

    [Fact]
    public void AbsentDay_CostsNothing()
    {
        var result = AttendanceEarningsCalculator.CalculateDay(
            Rates(), Day(AttendanceStatus.Absent));

        Assert.Equal(0m, result.NormalWorkAmount);
        Assert.Equal(0m, result.TotalEarnings);
    }
}
