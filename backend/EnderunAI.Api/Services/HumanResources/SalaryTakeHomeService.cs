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
    public async Task<Dictionary<Guid, decimal>> LoadEffectiveExtraPaymentsAsync(
        IReadOnlyCollection<Guid> personnelIds, CancellationToken cancellationToken)
    {
        if (personnelIds.Count == 0)
            return [];

        var today = DateTime.UtcNow.Date;

        var rows = await appDb.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= today &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= today))
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
