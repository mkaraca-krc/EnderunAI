using EnderunAI.Api.Models;
using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tazminat hesap motoru. Beklenen değerler mevzuat hesabıyla elle
/// çıkarıldı; motor veritabanına bağlı olmadığı için saf birim testi.
/// </summary>
public sealed class SeveranceCalculationTests
{
    private const decimal SeveranceCeiling = 53_919.68m;

    /// <summary>2026 resmi parametreleri.</summary>
    private static PayrollParameters Parameters() => new(
        MinimumWageGross: 33_030m,
        SgkBaseFloor: 33_030m,
        SgkBaseCeiling: 297_270m,
        SgkEmployeeRate: 14m,
        UnemploymentEmployeeRate: 1m,
        SgkEmployerRate: 20.75m,
        UnemploymentEmployerRate: 2m,
        SgkEmployerDiscountEnabled: true,
        SgkEmployerDiscountPoints: 2m,
        StampTaxPerMille: 7.59m,
        MinimumWageIncomeTaxExemptionEnabled: true,
        MinimumWageStampTaxExemptionEnabled: true,
        TaxBrackets: new List<PayrollTaxBracketInput>
        {
            new(0m, 190_000m, 15m),
            new(190_000m, 400_000m, 20m),
            new(400_000m, 1_500_000m, 27m),
            new(1_500_000m, 5_300_000m, 35m),
            new(5_300_000m, null, 40m)
        });

    private static SeveranceInput Input(
        int serviceDays,
        decimal monthlyGross = 40_000m,
        decimal unusedLeaveDays = 0m,
        decimal? ceiling = SeveranceCeiling,
        decimal fringe = 0m)
    {
        var end = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        return new SeveranceInput(
            EmploymentStartDate: end.AddDays(-serviceDays),
            TerminationDate: end,
            MonthlyGross: monthlyGross,
            MonthlyFringeBenefits: fringe,
            UnusedLeaveDays: unusedLeaveDays,
            SeveranceCeiling: ceiling,
            Parameters: Parameters());
    }

    // ---------- Ayrılış türü matrisi ----------

    /// <summary>
    /// Hak matrisi tek kaynaktan gelir ve elle işaretlenemez. Tablo
    /// bozulursa bu test kırılır.
    /// </summary>
    [Theory]
    [InlineData(TerminationReason.EmployerTermination, true, true)]
    [InlineData(TerminationReason.Resignation, false, false)]
    [InlineData(TerminationReason.ResignationWithJustCause, true, false)]
    [InlineData(TerminationReason.Retirement, true, false)]
    [InlineData(TerminationReason.MilitaryService, true, false)]
    [InlineData(TerminationReason.Marriage, true, false)]
    [InlineData(TerminationReason.EmployerTerminationWithJustCause, false, false)]
    [InlineData(TerminationReason.FixedTermContractEnd, false, false)]
    [InlineData(TerminationReason.Death, true, false)]
    public void RightsMatrix_MatchesLaw(
        TerminationReason reason, bool severance, bool notice)
    {
        var rights = TerminationRightsMatrix.For(reason);

        Assert.Equal(severance, rights.Severance);
        Assert.Equal(notice, rights.Notice);

        // Kullanılmayan izin ücreti her durumda ödenir (İş Kanunu 59).
        Assert.True(rights.UnusedLeave);
    }

    [Fact]
    public void RightsMatrix_CoversEveryReason()
    {
        foreach (TerminationReason reason in Enum.GetValues<TerminationReason>())
            Assert.NotNull(TerminationRightsMatrix.For(reason));
    }

    /// <summary>
    /// Tazminat hakkı olmayan türde kıdem ve ihbar sıfır çıkar ama
    /// kullanılmayan izin yine ödenir.
    /// </summary>
    [Fact]
    public void Resignation_PaysOnlyUnusedLeave()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_500, unusedLeaveDays: 10m),
            TerminationRightsMatrix.For(TerminationReason.Resignation));

        Assert.Equal(0m, result.Severance.Gross);
        Assert.Equal(0m, result.Notice.Gross);
        Assert.True(result.UnusedLeave.Gross > 0m);
    }

    // ---------- Kıdem ----------

    /// <summary>
    /// 3 yıl (1.095 gün), 40.000 TL brüt. Günlük 1.333,33 → yıllık kıdem
    /// 39.999,90; tavanın altında, tavan kesmiyor.
    /// 39.999,90 × 1.095 / 365 = 119.999,70
    /// Damga: 119.999,70 × ‰7,59 = 910,80
    /// </summary>
    [Fact]
    public void Severance_ThreeYears_IsThirtyDaysPerYear()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095),
            TerminationRightsMatrix.For(TerminationReason.Retirement));

        Assert.Equal(3, result.FullServiceYears);
        Assert.False(result.CeilingApplied);
        Assert.Equal(119_999.70m, result.Severance.Gross);
        Assert.Equal(910.80m, result.Severance.StampTax);

        // Kıdem gelir vergisinden ve SGK'dan istisnadır.
        Assert.Equal(0m, result.Severance.IncomeTax);
        Assert.Equal(0m, result.Severance.SgkAmount);

        Assert.Equal(119_088.90m, result.Severance.Net);
    }

    /// <summary>
    /// Artan süre oranlı ödenir (1475/14): 2 yıl 7 ay ≈ 945 gün.
    /// 39.999,90 × 945 / 365 = 103.561,38
    /// </summary>
    [Fact]
    public void Severance_PartialYear_IsProrated()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 945),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        Assert.Equal(2, result.FullServiceYears);
        Assert.Equal(103_561.38m, result.Severance.Gross);

        // Sadece tam yıl sayılsaydı 79.999,80 çıkardı; aradaki fark
        // işçinin hakkı.
        Assert.True(result.Severance.Gross > 79_999.80m);
    }

    /// <summary>Bir yılı doldurmayan işçi kıdem tazminatına hak kazanmaz.</summary>
    [Fact]
    public void Severance_UnderOneYear_IsNotEarned()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 300),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        Assert.Equal(0m, result.Severance.Gross);
    }

    /// <summary>
    /// Yüksek maaşta tavan devreye girer: 100.000 TL brüt → yıllık kıdem
    /// 100.000 olurdu, tavan 53.919,68'e çeker.
    /// 53.919,68 × 1.095 / 365 = 161.759,04
    /// </summary>
    [Fact]
    public void Severance_AboveCeiling_IsCapped()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095, monthlyGross: 100_000m),
            TerminationRightsMatrix.For(TerminationReason.Retirement));

        Assert.True(result.CeilingApplied);
        Assert.Equal(161_759.04m, result.Severance.Gross);
    }

    /// <summary>
    /// GERÇEK hesapta tavan uygulanmaz — fiilen ödenecek tutar budur.
    /// Aynı maaşla tavansız: günlük 3.333,33 → yıllık 99.999,90;
    /// × 1.095 / 365 = 299.999,70
    /// </summary>
    [Fact]
    public void Severance_WithoutCeiling_IsNotCapped()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095, monthlyGross: 100_000m, ceiling: null),
            TerminationRightsMatrix.For(TerminationReason.Retirement));

        Assert.False(result.CeilingApplied);
        Assert.Equal(299_999.70m, result.Severance.Gross);
    }

    /// <summary>
    /// Giydirilmiş ücret yalnızca kıdeme girer. Yol/yemek eklendiğinde
    /// kıdem büyür, ihbar ve izin değişmez.
    /// </summary>
    [Fact]
    public void FringeBenefits_AffectSeveranceOnly()
    {
        var withoutFringe = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095, unusedLeaveDays: 10m),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        var withFringe = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095, unusedLeaveDays: 10m, fringe: 3_000m),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        Assert.True(withFringe.Severance.Gross > withoutFringe.Severance.Gross);
        Assert.Equal(withoutFringe.Notice.Gross, withFringe.Notice.Gross);
        Assert.Equal(withoutFringe.UnusedLeave.Gross, withFringe.UnusedLeave.Gross);
    }

    // ---------- İhbar ----------

    /// <summary>İhbar süresi kademelerinin sınırları (İş Kanunu 17).</summary>
    [Theory]
    [InlineData(150, 2)]    // 6 aydan az
    [InlineData(179, 2)]
    [InlineData(180, 4)]    // 6 ay
    [InlineData(546, 4)]
    [InlineData(547, 6)]    // 1,5 yıl
    [InlineData(1094, 6)]
    [InlineData(1095, 8)]   // 3 yıl
    [InlineData(4000, 8)]
    public void NoticeWeeks_FollowSeniorityTiers(int serviceDays, int expectedWeeks)
    {
        Assert.Equal(expectedWeeks, SeveranceCalculationService.NoticeWeeksFor(serviceDays));
    }

    /// <summary>
    /// 3 yıl kıdem → 8 hafta ihbar. Günlük 1.333,33 × 7 × 8 = 74.666,48
    /// İhbar tazminatı gelir vergisi + damgaya tabi, SGK'ya değil.
    /// </summary>
    [Fact]
    public void Notice_IsTaxedButNotSubjectToSocialSecurity()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        Assert.Equal(8, result.NoticeWeeks);
        Assert.Equal(74_666.48m, result.Notice.Gross);
        Assert.Equal(0m, result.Notice.SgkAmount);
        Assert.True(result.Notice.IncomeTax > 0m);
        Assert.Equal(566.72m, result.Notice.StampTax);
    }

    // ---------- Yıllık izin ----------

    /// <summary>Yıllık izin hak edişi kademeleri (İş Kanunu 53).</summary>
    [Theory]
    [InlineData(300, 0)]      // 1 yılı doldurmadı
    [InlineData(365, 14)]
    [InlineData(1825, 14)]    // 5 yıl DAHİL 14 gün
    [InlineData(1826, 20)]    // 5 yılı geçince 20
    [InlineData(5474, 20)]
    [InlineData(5475, 26)]    // 15 yıl
    public void AnnualLeaveEntitlement_FollowsSeniorityTiers(
        int serviceDays, int expectedDays)
    {
        Assert.Equal(expectedDays,
            SeveranceCalculationService.AnnualLeaveEntitlementFor(serviceDays));
    }

    /// <summary>
    /// Toplam hak ediş her tam yıl için ayrı birikir: 3 yılda 3 × 14 = 42.
    /// 6 yılda ilk beş yıl 14'er (5 yıl dahil), altıncı yıl 20 → 90.
    /// </summary>
    [Theory]
    [InlineData(1_095, 42)]
    [InlineData(2_190, 90)]
    public void TotalAnnualLeaveEntitlement_AccumulatesPerYear(
        int serviceDays, int expected)
    {
        Assert.Equal(expected,
            SeveranceCalculationService.TotalAnnualLeaveEntitlement(serviceDays));
    }

    /// <summary>
    /// İzin ücreti ücret sayılır: SGK primi de kesilir.
    /// 10 gün × 1.333,33 = 13.333,30 brüt, SGK %15 = 2.000,00 (yuvarlama)
    /// </summary>
    [Fact]
    public void UnusedLeave_IsSubjectToSocialSecurityAndTax()
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays: 1_095, unusedLeaveDays: 10m),
            TerminationRightsMatrix.For(TerminationReason.Retirement));

        Assert.Equal(13_333.30m, result.UnusedLeave.Gross);
        Assert.Equal(2_000.00m, result.UnusedLeave.SgkAmount);
        Assert.True(result.UnusedLeave.IncomeTax > 0m);
        Assert.Equal(101.20m, result.UnusedLeave.StampTax);
    }

    // ---------- Bütünlük ----------

    /// <summary>
    /// Her senaryoda net = brüt − kesintiler olmalı; eşitlik bozulursa
    /// hesap kendi içinde tutarsız demektir.
    /// </summary>
    [Theory]
    [InlineData(365, 30_000)]
    [InlineData(1_095, 40_000)]
    [InlineData(3_650, 120_000)]
    public void NetAlwaysEqualsGrossMinusDeductions(int serviceDays, decimal gross)
    {
        var result = SeveranceCalculationService.Calculate(
            Input(serviceDays, gross, unusedLeaveDays: 14m),
            TerminationRightsMatrix.For(TerminationReason.EmployerTermination));

        foreach (var component in new[]
                 { result.Severance, result.Notice, result.UnusedLeave })
        {
            Assert.Equal(
                component.Gross,
                component.Net + component.SgkAmount + component.IncomeTax +
                component.StampTax);
        }
    }

    [Fact]
    public void TerminationBeforeHire_IsRejected()
    {
        var start = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() =>
            SeveranceCalculationService.Calculate(
                new SeveranceInput(
                    EmploymentStartDate: start,
                    TerminationDate: start.AddDays(-1),
                    MonthlyGross: 40_000m,
                    MonthlyFringeBenefits: 0m,
                    UnusedLeaveDays: 0m,
                    SeveranceCeiling: SeveranceCeiling,
                    Parameters: Parameters()),
                TerminationRightsMatrix.For(TerminationReason.Resignation)));
    }
}
