namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Hesaplamaya giren tek bir gelir vergisi dilimi.
/// </summary>
public sealed record PayrollTaxBracketInput(
    decimal LowerBound,
    decimal? UpperBound,
    decimal Rate);

/// <summary>
/// Bordro parametrelerinin hesaplama motoruna verilen sadeleştirilmiş
/// hali. CompanyPayrollSettings'ten kopyalanır; motorun veritabanına
/// hiç bağlanmaması test edilebilirliği için bilinçli bir tercih.
/// </summary>
public sealed record PayrollParameters(
    decimal MinimumWageGross,
    decimal SgkBaseFloor,
    decimal SgkBaseCeiling,
    decimal SgkEmployeeRate,
    decimal UnemploymentEmployeeRate,
    decimal SgkEmployerRate,
    decimal UnemploymentEmployerRate,
    bool SgkEmployerDiscountEnabled,
    decimal SgkEmployerDiscountPoints,
    decimal StampTaxPerMille,
    bool MinimumWageIncomeTaxExemptionEnabled,
    bool MinimumWageStampTaxExemptionEnabled,
    IReadOnlyList<PayrollTaxBracketInput> TaxBrackets);

/// <summary>
/// Tek bir personelin tek bir aya ait bordro girdisi.
/// </summary>
/// <param name="Month">Ay (1-12). Asgari ücret istisnasının kümülatif
/// hesabı için gerekli.</param>
/// <param name="GrossEarnings">Aylık toplam brüt kazanç (normal ücret +
/// fazla mesai + tatil çalışması + prim vb.).</param>
/// <param name="SgkExemptEarnings">Brütün içindeki, SGK primine tabi
/// olmayan kısım (ör. istisna sınırındaki yemek/yol yardımı).</param>
/// <param name="IncomeTaxExemptEarnings">Brütün içindeki, gelir
/// vergisine tabi olmayan kısım.</param>
/// <param name="CumulativeIncomeTaxBaseBefore">Aynı yıl içinde bu aydan
/// önce oluşmuş kümülatif gelir vergisi matrahı.</param>
/// <param name="OtherDeductions">Avans, icra vb. net üzerinden
/// yapılan kesintiler.</param>
public sealed record PayrollInput(
    int Month,
    decimal GrossEarnings,
    decimal SgkExemptEarnings = 0m,
    decimal IncomeTaxExemptEarnings = 0m,
    decimal CumulativeIncomeTaxBaseBefore = 0m,
    decimal OtherDeductions = 0m);

/// <summary>
/// Bordro hesabının tüm ara ve nihai değerleri. Ara değerler bordro
/// ekranında gösterilebilsin ve denetlenebilsin diye ayrı ayrı döner.
/// </summary>
public sealed record PayrollResult(
    decimal GrossEarnings,
    decimal SgkBase,
    decimal SgkEmployeeAmount,
    decimal UnemploymentEmployeeAmount,
    decimal IncomeTaxBase,
    decimal CumulativeIncomeTaxBaseAfter,
    decimal IncomeTaxBeforeExemption,
    decimal IncomeTaxExemption,
    decimal IncomeTaxAmount,
    decimal StampTaxBeforeExemption,
    decimal StampTaxExemption,
    decimal StampTaxAmount,
    decimal OtherDeductions,
    decimal TotalDeductions,
    decimal NetPay,
    decimal SgkEmployerAmount,
    decimal UnemploymentEmployerAmount,
    decimal TotalEmployerCost);

/// <summary>
/// Türk mevzuatına göre aylık bordro hesabı. Veritabanına ve zamana
/// bağlı değil — aynı girdi her zaman aynı çıktıyı üretir, bu sayede
/// mevzuat hesabıyla birebir karşılaştırmalı test yazılabiliyor.
///
/// Akış: brüt → SGK matrahı (taban/tavan) → işçi primleri → gelir
/// vergisi matrahı → kümülatif matrah üzerinden dilim → asgari ücret
/// istisnası → damga vergisi → net.
/// </summary>
public static class PayrollCalculationService
{
    public static PayrollResult Calculate(
        PayrollParameters parameters,
        PayrollInput input)
    {
        Validate(parameters, input);

        var gross = Round(input.GrossEarnings);

        // --- SGK ---
        // Prime esas kazanç, brütün SGK'ya tabi kısmıdır; taban ve tavan
        // arasına sıkıştırılır. Tavanı aşan kazançtan prim kesilmez.
        var sgkLiable = Math.Max(0m, gross - Round(input.SgkExemptEarnings));
        var sgkBase = Math.Clamp(
            sgkLiable, parameters.SgkBaseFloor, parameters.SgkBaseCeiling);

        // Kazanç tabanın altındaysa (eksik gün, kısmi ay) tabana çekmek
        // yanlış olur — taban yalnızca tam ay çalışan için anlamlıdır.
        if (sgkLiable < parameters.SgkBaseFloor)
            sgkBase = sgkLiable;

        var sgkEmployee = Round(sgkBase * parameters.SgkEmployeeRate / 100m);
        var unemploymentEmployee =
            Round(sgkBase * parameters.UnemploymentEmployeeRate / 100m);

        var employerRate = parameters.SgkEmployerRate
            - (parameters.SgkEmployerDiscountEnabled
                ? parameters.SgkEmployerDiscountPoints
                : 0m);
        var sgkEmployer = Round(sgkBase * Math.Max(0m, employerRate) / 100m);
        var unemploymentEmployer =
            Round(sgkBase * parameters.UnemploymentEmployerRate / 100m);

        // --- Gelir vergisi ---
        // Matrah = vergiye tabi brüt − işçi payı primler.
        var taxableGross = Math.Max(0m, gross - Round(input.IncomeTaxExemptEarnings));
        var incomeTaxBase = Math.Max(
            0m, Round(taxableGross - sgkEmployee - unemploymentEmployee));

        var cumulativeBefore = Math.Max(0m, Round(input.CumulativeIncomeTaxBaseBefore));
        var cumulativeAfter = cumulativeBefore + incomeTaxBase;

        // Vergi, kümülatif matrahın bu ay büyüyen kısmına düşer: yıl
        // içinde üst dilime geçiş bu farkla doğru yakalanır.
        var incomeTaxBeforeExemption = Round(
            TaxOnCumulative(parameters.TaxBrackets, cumulativeAfter)
            - TaxOnCumulative(parameters.TaxBrackets, cumulativeBefore));

        var incomeTaxExemption = parameters.MinimumWageIncomeTaxExemptionEnabled
            ? Round(MinimumWageIncomeTaxExemption(parameters, input.Month))
            : 0m;

        // İstisna, hesaplanan vergiden fazla olamaz.
        incomeTaxExemption = Math.Min(incomeTaxExemption, incomeTaxBeforeExemption);
        var incomeTax = Math.Max(0m, incomeTaxBeforeExemption - incomeTaxExemption);

        // --- Damga vergisi ---
        var stampBeforeExemption = Round(gross * parameters.StampTaxPerMille / 1000m);

        var stampExemption = parameters.MinimumWageStampTaxExemptionEnabled
            ? Round(parameters.MinimumWageGross * parameters.StampTaxPerMille / 1000m)
            : 0m;

        stampExemption = Math.Min(stampExemption, stampBeforeExemption);
        var stampTax = Math.Max(0m, stampBeforeExemption - stampExemption);

        // --- Net ---
        var otherDeductions = Math.Max(0m, Round(input.OtherDeductions));
        var totalDeductions = Round(
            sgkEmployee + unemploymentEmployee + incomeTax + stampTax + otherDeductions);
        var net = Round(gross - totalDeductions);

        return new PayrollResult(
            GrossEarnings: gross,
            SgkBase: Round(sgkBase),
            SgkEmployeeAmount: sgkEmployee,
            UnemploymentEmployeeAmount: unemploymentEmployee,
            IncomeTaxBase: incomeTaxBase,
            CumulativeIncomeTaxBaseAfter: Round(cumulativeAfter),
            IncomeTaxBeforeExemption: incomeTaxBeforeExemption,
            IncomeTaxExemption: incomeTaxExemption,
            IncomeTaxAmount: incomeTax,
            StampTaxBeforeExemption: stampBeforeExemption,
            StampTaxExemption: stampExemption,
            StampTaxAmount: stampTax,
            OtherDeductions: otherDeductions,
            TotalDeductions: totalDeductions,
            NetPay: net,
            SgkEmployerAmount: sgkEmployer,
            UnemploymentEmployerAmount: unemploymentEmployer,
            TotalEmployerCost: Round(gross + sgkEmployer + unemploymentEmployer));
    }

    /// <summary>
    /// Asgari ücret gelir vergisi istisnası: asgari ücretli bir çalışanın
    /// o ay ödeyeceği gelir vergisi kadar indirim uygulanır. İstisnanın
    /// kendisi de kümülatif olduğu için yılın ilerleyen aylarında dilim
    /// atlar — bu yüzden ay numarasıyla hesaplanır.
    /// </summary>
    private static decimal MinimumWageIncomeTaxExemption(
        PayrollParameters parameters, int month)
    {
        var monthlyBase = MinimumWageMonthlyTaxBase(parameters);

        var cumulativeBefore = monthlyBase * (month - 1);
        var cumulativeAfter = monthlyBase * month;

        return TaxOnCumulative(parameters.TaxBrackets, cumulativeAfter)
            - TaxOnCumulative(parameters.TaxBrackets, cumulativeBefore);
    }

    /// <summary>
    /// Asgari ücretin aylık gelir vergisi matrahı: brütten yalnızca işçi
    /// payı SGK ve işsizlik primleri düşülür.
    /// </summary>
    private static decimal MinimumWageMonthlyTaxBase(PayrollParameters parameters)
    {
        var gross = parameters.MinimumWageGross;
        var sgk = Round(gross * parameters.SgkEmployeeRate / 100m);
        var unemployment = Round(gross * parameters.UnemploymentEmployeeRate / 100m);

        return Math.Max(0m, Round(gross - sgk - unemployment));
    }

    /// <summary>
    /// Verilen kümülatif matrahın tamamı üzerinden toplam gelir vergisi.
    /// Aylık vergi, iki kümülatif değerin farkı olarak bulunur.
    /// </summary>
    private static decimal TaxOnCumulative(
        IReadOnlyList<PayrollTaxBracketInput> brackets, decimal cumulativeBase)
    {
        if (cumulativeBase <= 0m)
            return 0m;

        var tax = 0m;

        foreach (var bracket in brackets.OrderBy(x => x.LowerBound))
        {
            if (cumulativeBase <= bracket.LowerBound)
                break;

            var upper = bracket.UpperBound ?? decimal.MaxValue;
            var slice = Math.Min(cumulativeBase, upper) - bracket.LowerBound;

            if (slice <= 0m)
                continue;

            tax += slice * bracket.Rate / 100m;
        }

        return tax;
    }

    private static void Validate(PayrollParameters parameters, PayrollInput input)
    {
        if (parameters.TaxBrackets.Count == 0)
        {
            throw new InvalidOperationException(
                "Gelir vergisi dilimleri tanımlı değil; bordro hesaplanamaz.");
        }

        if (parameters.MinimumWageGross <= 0m)
        {
            throw new InvalidOperationException(
                "Brüt asgari ücret tanımlı değil; bordro hesaplanamaz.");
        }

        if (parameters.SgkBaseCeiling <= 0m)
        {
            throw new InvalidOperationException(
                "SGK tavanı tanımlı değil; bordro hesaplanamaz.");
        }

        if (input.Month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(input), "Ay 1 ile 12 arasında olmalıdır.");

        if (input.GrossEarnings < 0m)
            throw new ArgumentOutOfRangeException(nameof(input), "Brüt kazanç negatif olamaz.");
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
