using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>Bir ayrılış türünün doğurduğu tazminat hakları.</summary>
/// <param name="Severance">Kıdem tazminatı hakkı.</param>
/// <param name="Notice">İhbar tazminatı hakkı.</param>
public sealed record TerminationRights(bool Severance, bool Notice)
{
    /// <summary>
    /// Kullanılmayan yıllık izin ücreti ayrılış türünden bağımsız olarak
    /// HER durumda ödenir (İş Kanunu 59) — istifada ve haklı fesihte de.
    /// </summary>
    public bool UnusedLeave => true;
}

/// <summary>
/// Ayrılış türü → tazminat hakkı matrisi. Tek kaynak burasıdır;
/// hiçbir ekrandan elle işaretlenemez.
/// </summary>
public static class TerminationRightsMatrix
{
    private static readonly IReadOnlyDictionary<TerminationReason, TerminationRights> Map =
        new Dictionary<TerminationReason, TerminationRights>
        {
            // İşveren haklı neden olmadan feshederse hem kıdem hem ihbar.
            [TerminationReason.EmployerTermination] = new(Severance: true, Notice: true),

            // İstifada işçi kendi ayrıldığı için ikisi de yok.
            [TerminationReason.Resignation] = new(false, false),

            // Haklı nedenle istifada kıdem doğar; ihbar doğmaz çünkü
            // ihbar tazminatı feshi haber vermeden yapan tarafın borcudur.
            [TerminationReason.ResignationWithJustCause] = new(true, false),

            [TerminationReason.Retirement] = new(true, false),
            [TerminationReason.MilitaryService] = new(true, false),
            [TerminationReason.Marriage] = new(true, false),

            // 25/II (ahlak ve iyi niyet kurallarına aykırılık) tazminatsız fesih.
            [TerminationReason.EmployerTerminationWithJustCause] = new(false, false),

            [TerminationReason.FixedTermContractEnd] = new(false, false),

            // Vefatta kıdem mirasçıya ödenir.
            [TerminationReason.Death] = new(true, false)
        };

    public static TerminationRights For(TerminationReason reason) =>
        Map.TryGetValue(reason, out var rights)
            ? rights
            : throw new ArgumentOutOfRangeException(
                nameof(reason), $"'{reason}' için tazminat hakkı tanımlı değil.");

    public static IReadOnlyDictionary<TerminationReason, TerminationRights> All => Map;
}

/// <summary>
/// Tazminat hesabının girdileri.
/// </summary>
/// <param name="EmploymentStartDate">İşe giriş tarihi.</param>
/// <param name="TerminationDate">Ayrılış tarihi.</param>
/// <param name="MonthlyGross">Hesaba esas aylık brüt ücret.</param>
/// <param name="MonthlyFringeBenefits">Kıdeme giren düzenli yan haklar
/// (yol, yemek vb.) aylık toplamı. Giydirilmiş ücret yalnızca KIDEM
/// hesabına girer; ihbar ve izin çıplak ücretten yürür.</param>
/// <param name="UnusedLeaveDays">Kullanılmayan yıllık izin günü.</param>
/// <param name="SeveranceCeiling">Kıdem tavanı; null ise tavan uygulanmaz.</param>
/// <param name="Parameters">Yürürlükteki bordro parametreleri — ihbar ve
/// izin ödemesinin vergisi bunlardan hesaplanır.</param>
/// <param name="CumulativeIncomeTaxBaseBefore">Çalışanın o yıl içinde
/// bu ödemeden önce oluşmuş kümülatif gelir vergisi matrahı. Tazminat
/// yılın sonunda ödendiğinde üst dilime düşebilir; bu yüzden gerekli.</param>
public sealed record SeveranceInput(
    DateTime EmploymentStartDate,
    DateTime TerminationDate,
    decimal MonthlyGross,
    decimal MonthlyFringeBenefits,
    decimal UnusedLeaveDays,
    decimal? SeveranceCeiling,
    PayrollParameters Parameters,
    decimal CumulativeIncomeTaxBaseBefore = 0m);

/// <summary>Tek bir tazminat kaleminin brüt ve kesinti dökümü.</summary>
public sealed record SeveranceComponent(
    decimal Gross,
    decimal SgkAmount,
    decimal IncomeTax,
    decimal StampTax)
{
    public decimal Net => Gross - SgkAmount - IncomeTax - StampTax;
}

public sealed record SeveranceResult(
    int ServiceDays,
    int FullServiceYears,
    decimal DailyGross,
    int NoticeWeeks,
    bool CeilingApplied,
    SeveranceComponent Severance,
    SeveranceComponent Notice,
    SeveranceComponent UnusedLeave)
{
    public decimal TotalGross => Severance.Gross + Notice.Gross + UnusedLeave.Gross;
    public decimal TotalNet => Severance.Net + Notice.Net + UnusedLeave.Net;
}

/// <summary>
/// Kıdem, ihbar ve kullanılmayan yıllık izin tazminatı hesabı.
///
/// Bordro motoruyla aynı ilke: veritabanına ve zamana bağlı değil, aynı
/// girdi her zaman aynı çıktıyı üretir; bu sayede mevzuat hesabıyla
/// birebir karşılaştırmalı test yazılabiliyor.
///
/// Vergi muameleleri (mevzuat):
///   - Kıdem tazminatı: yalnızca damga vergisi. Gelir vergisi ve SGK yok.
///   - İhbar tazminatı: gelir vergisi + damga. SGK yok (ücret sayılır
///     ama prime esas kazanca girmez).
///   - Kullanılmayan izin ücreti: SGK + gelir vergisi + damga. Ücrettir.
///
/// Gelir vergisi, ihbar ve izin ödemesinin TOPLAMI üzerinden kümülatif
/// dilim mantığıyla hesaplanır ve iki kaleme brütleri oranında
/// dağıtılır. Ödeme son ayın bordrosuyla birlikte yapılacaksa o ayın
/// kümülatif matrahı <see cref="SeveranceInput.CumulativeIncomeTaxBaseBefore"/>
/// ile verilmelidir; aksi halde vergi olduğundan düşük çıkar.
///
/// Asgari ücret istisnası tazminat ödemesine uygulanmaz: istisna aylık
/// ücrete tanınır ve o ayın bordrosunda zaten kullanılmıştır.
/// </summary>
public static class SeveranceCalculationService
{
    /// <summary>
    /// İhbar süresi kademeleri (İş Kanunu 17): kıdem eşiği → hafta.
    /// </summary>
    private static readonly (int MinDays, int Weeks)[] NoticePeriods =
    [
        (0, 2),      // 6 aydan az
        (180, 4),    // 6 ay – 1,5 yıl
        (547, 6),    // 1,5 yıl – 3 yıl
        (1095, 8)    // 3 yıl ve fazlası
    ];

    /// <summary>
    /// Yıllık izin hak edişi (İş Kanunu 53): kıdem eşiği → gün.
    /// 1 yıldan az kıdemde yıllık izne hak kazanılmaz.
    ///
    /// Sınırların kapsamına dikkat: kanun "bir yıldan beş yıla kadar
    /// (BEŞ YIL DAHİL) 14 gün" diyor, yani 5 yılda hâlâ 14 gündür;
    /// 20 güne beşinci yılı GEÇTİKTEN sonra hak kazanılır. 15 yıl ise
    /// dahildir (26 gün).
    /// </summary>
    private static readonly (int MinDays, int Days)[] AnnualLeaveEntitlements =
    [
        (365, 14),    // 1 yıl – 5 yıl (5 dahil)
        (1826, 20),   // 5 yıldan fazla – 15 yıldan az
        (5475, 26)    // 15 yıl ve fazlası
    ];

    public static SeveranceResult Calculate(
        SeveranceInput input, TerminationRights rights)
    {
        Validate(input);

        var serviceDays = (int)(input.TerminationDate.Date - input.EmploymentStartDate.Date)
            .TotalDays;

        // Bordro pratiğinde ay 30 gün kabul edilir.
        var dailyGross = Round(input.MonthlyGross / 30m);

        // Kıdeme esas günlük ücret giydirilmiştir: düzenli yol/yemek gibi
        // ödemeler eklenir. İhbar ve izin çıplak ücretten yürür.
        var dailySeveranceWage = Round(
            (input.MonthlyGross + input.MonthlyFringeBenefits) / 30m);

        var (severance, ceilingApplied) = rights.Severance
            ? CalculateSeverance(
                serviceDays, dailySeveranceWage, input.SeveranceCeiling,
                input.Parameters.StampTaxPerMille)
            : (Empty, false);

        var noticeWeeks = NoticeWeeksFor(serviceDays);

        var noticeGross = rights.Notice
            ? Round(dailyGross * 7m * noticeWeeks)
            : 0m;

        var leaveGross = input.UnusedLeaveDays > 0m
            ? Round(input.UnusedLeaveDays * dailyGross)
            : 0m;

        var (notice, leave) = ApplyWageTaxes(
            noticeGross, leaveGross, input.Parameters,
            input.CumulativeIncomeTaxBaseBefore);

        return new SeveranceResult(
            ServiceDays: serviceDays,
            FullServiceYears: serviceDays / 365,
            DailyGross: dailyGross,
            NoticeWeeks: rights.Notice ? noticeWeeks : 0,
            CeilingApplied: ceilingApplied,
            Severance: severance,
            Notice: notice,
            UnusedLeave: leave);
    }

    /// <summary>
    /// Kıdem tazminatı: her tam hizmet yılı için 30 günlük giydirilmiş
    /// ücret, artan süre için oranlı (1475/14). Yıl başına ödenecek tutar
    /// kıdem tavanını aşamaz.
    ///
    /// Kesinti yalnızca damga vergisidir; kıdem tazminatı gelir
    /// vergisinden ve SGK priminden istisnadır.
    /// </summary>
    private static (SeveranceComponent Component, bool CeilingApplied) CalculateSeverance(
        int serviceDays, decimal dailyWage, decimal? ceiling, decimal stampTaxPerMille)
    {
        if (serviceDays < 365)
        {
            // Bir yılı doldurmayan işçi kıdem tazminatına hak kazanmaz.
            return (Empty, false);
        }

        // Bir yıllık kıdem tazminatı = 30 günlük ücret; tavan bu tutarı
        // sınırlar.
        var yearlyAmount = Round(dailyWage * 30m);
        var ceilingApplied = ceiling is > 0m && yearlyAmount > ceiling.Value;

        if (ceilingApplied)
            yearlyAmount = Round(ceiling!.Value);

        // Tam yıllar + artan gün oranlı.
        var gross = Round(yearlyAmount * serviceDays / 365m);

        var stampTax = Round(gross * stampTaxPerMille / 1000m);

        return (new SeveranceComponent(gross, SgkAmount: 0m, IncomeTax: 0m, stampTax),
            ceilingApplied);
    }

    /// <summary>
    /// İhbar ve izin ödemelerinin vergileri. İkisi de ücret sayıldığı
    /// için gelir vergisi ve damgaya tabidir; aralarındaki tek fark
    /// SGK'dır: izin ücreti prime esas kazanca girer, ihbar tazminatı
    /// girmez.
    ///
    /// Gelir vergisi toplam matrah üzerinden kümülatif dilimle bulunur
    /// ve iki kaleme brütleri oranında dağıtılır — böylece dilim atlama
    /// doğru yakalanır, kalemler ayrı ayrı vergilendirilseydi vergi
    /// eksik çıkardı.
    /// </summary>
    private static (SeveranceComponent Notice, SeveranceComponent Leave) ApplyWageTaxes(
        decimal noticeGross,
        decimal leaveGross,
        PayrollParameters parameters,
        decimal cumulativeBefore)
    {
        if (noticeGross <= 0m && leaveGross <= 0m)
            return (Empty, Empty);

        // İzin ücreti prime esas kazanç; tavan aşılırsa fazlasından prim
        // kesilmez.
        var leaveSgkBase = Math.Min(leaveGross, parameters.SgkBaseCeiling);
        var leaveSgk = Round(leaveSgkBase *
            (parameters.SgkEmployeeRate + parameters.UnemploymentEmployeeRate) / 100m);

        // Gelir vergisi matrahı: brütlerden yalnızca işçi payı primler düşülür.
        var taxBase = Math.Max(0m, Round(noticeGross + leaveGross - leaveSgk));

        var before = Math.Max(0m, Round(cumulativeBefore));

        var incomeTax = Round(
            PayrollCalculationService.TaxOnCumulative(
                parameters.TaxBrackets, before + taxBase)
            - PayrollCalculationService.TaxOnCumulative(
                parameters.TaxBrackets, before));

        // Vergiyi brütler oranında dağıt.
        var totalGross = noticeGross + leaveGross;
        var noticeIncomeTax = totalGross > 0m
            ? Round(incomeTax * noticeGross / totalGross)
            : 0m;
        var leaveIncomeTax = Round(incomeTax - noticeIncomeTax);

        var noticeStamp = Round(noticeGross * parameters.StampTaxPerMille / 1000m);
        var leaveStamp = Round(leaveGross * parameters.StampTaxPerMille / 1000m);

        return (
            new SeveranceComponent(noticeGross, 0m, noticeIncomeTax, noticeStamp),
            new SeveranceComponent(leaveGross, leaveSgk, leaveIncomeTax, leaveStamp));
    }

    /// <summary>Kıdeme karşılık gelen ihbar süresi (hafta).</summary>
    public static int NoticeWeeksFor(int serviceDays)
    {
        var weeks = NoticePeriods[0].Weeks;

        foreach (var (minDays, value) in NoticePeriods)
        {
            if (serviceDays >= minDays)
                weeks = value;
        }

        return weeks;
    }

    /// <summary>
    /// Kıdeme karşılık gelen yıllık izin hak edişi (gün/yıl).
    /// 1 yılı doldurmayan işçide 0.
    /// </summary>
    public static int AnnualLeaveEntitlementFor(int serviceDays)
    {
        var days = 0;

        foreach (var (minDays, value) in AnnualLeaveEntitlements)
        {
            if (serviceDays >= minDays)
                days = value;
        }

        return days;
    }

    /// <summary>
    /// Toplam hak edilen yıllık izin günü: her tam hizmet yılı için o
    /// yılın kıdemine karşılık gelen hak ediş.
    /// </summary>
    public static int TotalAnnualLeaveEntitlement(int serviceDays)
    {
        var total = 0;
        var fullYears = serviceDays / 365;

        for (var year = 1; year <= fullYears; year++)
        {
            // year. yılın sonunda hak edilen izin, o andaki kıdeme göre.
            total += AnnualLeaveEntitlementFor(year * 365);
        }

        return total;
    }

    private static readonly SeveranceComponent Empty = new(0m, 0m, 0m, 0m);

    private static void Validate(SeveranceInput input)
    {
        if (input.TerminationDate.Date < input.EmploymentStartDate.Date)
        {
            throw new ArgumentException(
                "Ayrılış tarihi işe giriş tarihinden önce olamaz.", nameof(input));
        }

        if (input.MonthlyGross < 0m)
            throw new ArgumentException("Aylık brüt ücret negatif olamaz.", nameof(input));

        if (input.UnusedLeaveDays < 0m)
            throw new ArgumentException("İzin günü negatif olamaz.", nameof(input));
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
