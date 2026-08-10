using EnderunAI.Api.Data;
using EnderunAI.Api.Models.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// "Resmî net + elden ödeme + toplam ele geçen" üçlüsünü tek yerden
/// üretir. Ücret kartı listesi ve personel 360 kartı aynı rakamı
/// göstermek zorunda; hesap iki ekranda ayrı yazılırsa er ya da geç
/// ayrışır.
///
/// Bu servis GİZLİLİK KARARI VERMEZ: elden ödemeyi yalnızca çağıran
/// <see cref="Security.IExtraPaymentVisibilityService"/> ile izni
/// doğruladıktan sonra sorgulamalıdır.
/// </summary>
public sealed class SalaryTakeHomeService(AppDbContext appDb)
{
    /// <summary>
    /// Personel başına yürürlükteki elden ödeme tutarı. Birden çok
    /// kayıt varsa en son başlayan geçerlidir.
    /// </summary>
    public Task<Dictionary<Guid, decimal>> LoadEffectiveExtraPaymentsAsync(
        IReadOnlyCollection<Guid> personnelIds, CancellationToken cancellationToken) =>
        LoadEffectiveExtraPaymentsAsync(
            personnelIds, DateTime.UtcNow.Date, cancellationToken);

    /// <summary>
    /// Personel başına, VERİLEN TARİHTE yürürlükteki elden ödeme.
    ///
    /// Bordro geçmiş bir ay için hesaplanabildiğinden "bugün"e bakmak
    /// yanlış tutar üretir: Mart bordrosuna Ağustos'ta yürürlüğe giren
    /// zam yansımamalı.
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> LoadEffectiveExtraPaymentsAsync(
        IReadOnlyCollection<Guid> personnelIds,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        if (personnelIds.Count == 0)
            return [];

        var date = asOf.Date;

        var rows = await appDb.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= date &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= date))
            .Select(x => new { x.PersonnelId, x.MonthlyAmount, x.EffectiveStartDate })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.EffectiveStartDate).First().MonthlyAmount);
    }

    /// <summary>
    /// Şirketin o yıla ait bordro parametreleri. Parametre ya da vergi
    /// dilimi tanımlı değilse null döner — rakam uydurulmaz.
    /// </summary>
    public async Task<PayrollParameters?> TryLoadPayrollParametersAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var settings = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .Include(x => x.TaxBrackets)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (settings is null || settings.TaxBrackets.Count == 0)
            return null;

        return new PayrollParameters(
            settings.MinimumWageGross,
            settings.SgkBaseFloor,
            settings.SgkBaseCeiling,
            settings.SgkEmployeeRate,
            settings.UnemploymentEmployeeRate,
            settings.SgkEmployerRate,
            settings.UnemploymentEmployerRate,
            settings.SgkEmployerDiscountEnabled,
            settings.SgkEmployerDiscountPoints,
            settings.StampTaxPerMille,
            settings.MinimumWageIncomeTaxExemptionEnabled,
            settings.MinimumWageStampTaxExemptionEnabled,
            settings.TaxBrackets
                .OrderBy(x => x.Order)
                .Select(x => new PayrollTaxBracketInput(
                    x.LowerBound, x.UpperBound, x.Rate))
                .ToList());
    }

    /// <summary>
    /// Kartın resmî neti. Net esaslıda anlaşılan tutarın kendisi; brüt
    /// esaslıda karttaki net doluysa o, boşsa brütten ocak esasıyla
    /// hesaplanır. Parametre yoksa null — uydurulmaz.
    /// </summary>
    /// <summary>Aylık tutarın güne bölünmesi — bordroyla aynı bölen.</summary>
    public const decimal MonthlyToDailyDivisor = 30m;

    /// <summary>Bordro ayarı yoksa şirketin günlük çalışma süresi.</summary>
    public const decimal DefaultDailyWorkHours = 8m;

    /// <summary>
    /// MESAİ SAAT ÜCRETİ — tek kaynak.
    ///
    /// Taban ele geçen (resmî net + MANUEL elden) ÷ (30 × günlük
    /// çalışma saati). Mesai tabana geri beslenmez: mesai tutarı bu
    /// saatlikten türediği için tabana eklenirse kendi kendini
    /// büyütürdü.
    ///
    /// Personel kartındaki mesai paneli ve nakit akış projeksiyonu
    /// AYNI formülü kullanmak zorunda. Kopyalansaydı ikisi zamanla
    /// ayrışır ve aynı personel için iki ekran iki farklı rakam
    /// gösterirdi.
    /// </summary>
    public static decimal? ResolveOvertimeHourlyRate(
        decimal? officialNet, decimal? manualExtraMonthly, decimal? dailyWorkHours)
    {
        var hours = dailyWorkHours is > 0m ? dailyWorkHours.Value : DefaultDailyWorkHours;
        var baseTakeHome = (officialNet ?? 0m) + (manualExtraMonthly ?? 0m);

        if (baseTakeHome <= 0m || hours <= 0m)
            return null;

        return decimal.Round(baseTakeHome / (MonthlyToDailyDivisor * hours), 2);
    }

    public static decimal? ResolveOfficialNet(
        HrSalaryDefinition item, PayrollParameters? parameters)
    {
        if (item.SalaryBasis == SalaryBasis.Net)
            return item.TargetNetSalary;

        if (item.NetSalary > 0m)
            return item.NetSalary;

        if (parameters is null || item.GrossSalary <= 0m)
            return null;

        return PayrollCalculationService
            .Calculate(parameters, new PayrollInput(1, item.GrossSalary))
            .NetPay;
    }
}
