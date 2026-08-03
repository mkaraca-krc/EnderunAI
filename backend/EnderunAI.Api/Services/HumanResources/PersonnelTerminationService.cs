using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>Bir tazminat kaleminin dışarıya dönen hali.</summary>
public sealed record TerminationComponentView(
    decimal Gross,
    decimal SgkAmount,
    decimal IncomeTax,
    decimal StampTax,
    decimal Net);

/// <summary>
/// Tazminat hesabının sonucu.
///
/// GİZLİLİK: <see cref="ActualNetTotal"/> ve
/// <see cref="ExtraPaymentDifference"/> elden ödeme bilgisi taşır ve
/// extra_payment.view izni olmayan kullanıcıya <c>null</c> döner.
/// Gizleme arayüzde değil burada, projeksiyon seviyesinde yapılır.
/// </summary>
public sealed record TerminationCalculationView(
    Guid PersonnelId,
    string PersonnelFullName,
    DateTime EmploymentStartDate,
    DateTime TerminationDate,
    TerminationReason Reason,
    string ReasonName,
    bool HasSeveranceRight,
    bool HasNoticeRight,
    int ServiceDays,
    int FullServiceYears,
    int NoticeWeeks,
    decimal UnusedLeaveDays,
    decimal OfficialMonthlyGross,
    bool SeveranceCeilingApplied,
    TerminationComponentView OfficialSeverance,
    TerminationComponentView OfficialNotice,
    TerminationComponentView OfficialLeave,
    decimal OfficialNetTotal,
    decimal? ExtraMonthlyAmount,
    decimal? ActualNetTotal,
    decimal? ExtraPaymentDifference,
    IReadOnlyList<string> Warnings);

public interface IPersonnelTerminationService
{
    /// <summary>
    /// Kayıt oluşturmadan hesaplar: "şu an bu nedenle çıksa ne öderim".
    /// </summary>
    Task<TerminationCalculationView> SimulateAsync(
        Guid personnelId,
        TerminationReason reason,
        DateTime terminationDate,
        decimal? unusedLeaveDaysOverride,
        CancellationToken cancellationToken);

    /// <summary>Hesabı kayda dönüştürür (taslak).</summary>
    Task<Guid> CreateAsync(
        Guid personnelId,
        TerminationReason reason,
        DateTime terminationDate,
        decimal? unusedLeaveDaysOverride,
        string? note,
        CancellationToken cancellationToken);

    /// <summary>
    /// Çıkışı kesinleştirir: personel ayrılmış sayılır ve tutarlar
    /// donar.
    /// </summary>
    Task FinalizeAsync(Guid terminationId, CancellationToken cancellationToken);
}

/// <summary>
/// Personel çıkışında tazminat hesabı. İki hesap yürütür:
///
///   RESMİ  — SGK'ya bildirilen brüt üzerinden, kıdem tavanı uygulanır.
///            Belgelenen, bordroya ve muhasebeye giren tutar.
///   GERÇEK — resmi + elden toplam üzerinden, kıdem tavanı uygulanmaz.
///            Fiilen ödenecek tutar; aradaki fark elden ödenir.
///
/// Gerçek hesap yalnızca extra_payment.view izniyle döner. Yetkisiz
/// kullanıcı için elden tutar sorgusu HİÇ çalıştırılmaz — tabloya
/// uğranmaz bile.
/// </summary>
public sealed class PersonnelTerminationService(
    AppDbContext db,
    HrDbContext hrDb,
    IExtraPaymentVisibilityService extraPaymentVisibility,
    ICurrentUserService currentUser) : IPersonnelTerminationService
{
    public async Task<TerminationCalculationView> SimulateAsync(
        Guid personnelId,
        TerminationReason reason,
        DateTime terminationDate,
        decimal? unusedLeaveDaysOverride,
        CancellationToken cancellationToken)
    {
        // Sorgu dizesinden gelen tarih Kind=Unspecified olur; Npgsql
        // timestamptz kolonuna yalnızca UTC yazdığı için normalleştirilir.
        terminationDate = DateTime.SpecifyKind(terminationDate.Date, DateTimeKind.Utc);

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.FirstName,
                x.LastName,
                x.EmploymentStartDate
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Personel bulunamadı.");

        if (personnel.EmploymentStartDate is not DateTime startDate)
        {
            throw new InvalidOperationException(
                "Personelin işe giriş tarihi kayıtlı değil; kıdem hesaplanamaz.");
        }

        var settings = await LoadSettingsAsync(
            personnel.CompanyId, terminationDate.Year, cancellationToken);

        var warnings = new List<string>();

        if (settings.VerifiedAtUtc is null)
        {
            warnings.Add(
                "Bordro parametreleri doğrulanmamış; tutarlar Şirket Ayarları'ndan " +
                "onaylanana kadar bilgi amaçlıdır.");
        }

        var officialMonthlyGross = await ResolveOfficialGrossAsync(
            personnel.Id, terminationDate, cancellationToken);

        if (officialMonthlyGross <= 0m)
        {
            warnings.Add(
                "Personelin ücret kartı bulunamadı; resmi tutarlar sıfır hesaplandı.");
        }

        var serviceDays = (int)(terminationDate.Date - startDate.Date).TotalDays;

        var unusedLeaveDays = unusedLeaveDaysOverride
            ?? await CalculateUnusedLeaveDaysAsync(
                personnel.Id, serviceDays, cancellationToken);

        var rights = TerminationRightsMatrix.For(reason);
        var parameters = ToParameters(settings);

        var official = SeveranceCalculationService.Calculate(
            new SeveranceInput(
                EmploymentStartDate: startDate,
                TerminationDate: terminationDate,
                MonthlyGross: officialMonthlyGross,
                MonthlyFringeBenefits: 0m,
                UnusedLeaveDays: unusedLeaveDays,
                // Tavan yalnızca resmi hesapta.
                SeveranceCeiling: settings.SeveranceCeiling > 0m
                    ? settings.SeveranceCeiling
                    : null,
                Parameters: parameters),
            rights);

        // --- Gerçek hesap: yalnızca yetkiliye ---
        decimal? extraMonthly = null;
        decimal? actualNet = null;
        decimal? difference = null;

        if (await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
        {
            var extra = await ResolveExtraMonthlyAsync(
                personnel.Id, terminationDate, cancellationToken);

            extraMonthly = extra;

            var actual = SeveranceCalculationService.Calculate(
                new SeveranceInput(
                    EmploymentStartDate: startDate,
                    TerminationDate: terminationDate,
                    MonthlyGross: officialMonthlyGross + extra,
                    MonthlyFringeBenefits: 0m,
                    UnusedLeaveDays: unusedLeaveDays,
                    // Gerçek ödemede tavan uygulanmaz: fiilen ödenecek
                    // tutar bu.
                    SeveranceCeiling: null,
                    Parameters: parameters),
                rights);

            actualNet = actual.TotalNet;
            difference = Round(actual.TotalNet - official.TotalNet);
        }

        if (official.CeilingApplied)
        {
            warnings.Add(
                $"Kıdem tazminatı yasal tavana ({settings.SeveranceCeiling:N2} TL/yıl) " +
                "takıldı; resmi tutar tavandan hesaplandı.");
        }

        if (serviceDays < 365 && rights.Severance)
        {
            warnings.Add(
                "Hizmet süresi bir yılı doldurmadığı için kıdem tazminatı doğmadı.");
        }

        return new TerminationCalculationView(
            PersonnelId: personnel.Id,
            PersonnelFullName: $"{personnel.FirstName} {personnel.LastName}".Trim(),
            EmploymentStartDate: startDate,
            TerminationDate: terminationDate,
            Reason: reason,
            ReasonName: ReasonName(reason),
            HasSeveranceRight: rights.Severance,
            HasNoticeRight: rights.Notice,
            ServiceDays: official.ServiceDays,
            FullServiceYears: official.FullServiceYears,
            NoticeWeeks: official.NoticeWeeks,
            UnusedLeaveDays: unusedLeaveDays,
            OfficialMonthlyGross: officialMonthlyGross,
            SeveranceCeilingApplied: official.CeilingApplied,
            OfficialSeverance: ToView(official.Severance),
            OfficialNotice: ToView(official.Notice),
            OfficialLeave: ToView(official.UnusedLeave),
            OfficialNetTotal: official.TotalNet,
            ExtraMonthlyAmount: extraMonthly,
            ActualNetTotal: actualNet,
            ExtraPaymentDifference: difference,
            Warnings: warnings);
    }

    public async Task<Guid> CreateAsync(
        Guid personnelId,
        TerminationReason reason,
        DateTime terminationDate,
        decimal? unusedLeaveDaysOverride,
        string? note,
        CancellationToken cancellationToken)
    {
        var alreadyFinalized = await db.PersonnelTerminations
            .AnyAsync(x => x.PersonnelId == personnelId &&
                           x.Status == TerminationStatus.Finalized,
                cancellationToken);

        if (alreadyFinalized)
        {
            throw new InvalidOperationException(
                "Bu personelin kesinleşmiş bir çıkış kaydı zaten var.");
        }

        var view = await SimulateAsync(
            personnelId, reason, terminationDate, unusedLeaveDaysOverride,
            cancellationToken);

        var companyId = await db.Personnel
            .Where(x => x.Id == personnelId)
            .Select(x => x.CompanyId)
            .SingleAsync(cancellationToken);

        var termination = new PersonnelTermination
        {
            CompanyId = companyId,
            PersonnelId = personnelId,
            TerminationDate = DateTime.SpecifyKind(terminationDate.Date, DateTimeKind.Utc),
            Reason = reason,
            Status = TerminationStatus.Draft,
            ServiceDays = view.ServiceDays,
            UnusedLeaveDays = view.UnusedLeaveDays,
            OfficialMonthlyGross = view.OfficialMonthlyGross,
            OfficialSeveranceGross = view.OfficialSeverance.Gross,
            OfficialSeveranceStampTax = view.OfficialSeverance.StampTax,
            OfficialNoticeGross = view.OfficialNotice.Gross,
            OfficialNoticeIncomeTax = view.OfficialNotice.IncomeTax,
            OfficialNoticeStampTax = view.OfficialNotice.StampTax,
            OfficialLeaveGross = view.OfficialLeave.Gross,
            OfficialLeaveSgk = view.OfficialLeave.SgkAmount,
            OfficialLeaveIncomeTax = view.OfficialLeave.IncomeTax,
            OfficialLeaveStampTax = view.OfficialLeave.StampTax,
            OfficialNetTotal = view.OfficialNetTotal,
            SeveranceCeilingApplied = view.SeveranceCeilingApplied,
            // Gerçek tutarlar yalnızca hesabı yapanın yetkisi varsa
            // doldurulur; yetkisiz kullanıcı kaydı sıfır elden farkla
            // oluşturur ve yetkili sonradan yeniden hesaplar.
            ExtraMonthlyAmount = view.ExtraMonthlyAmount ?? 0m,
            ActualNetTotal = view.ActualNetTotal ?? view.OfficialNetTotal,
            ExtraPaymentDifference = view.ExtraPaymentDifference ?? 0m,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        db.PersonnelTerminations.Add(termination);
        await db.SaveChangesAsync(cancellationToken);

        return termination.Id;
    }

    public async Task FinalizeAsync(
        Guid terminationId, CancellationToken cancellationToken)
    {
        var termination = await db.PersonnelTerminations
            .SingleOrDefaultAsync(x => x.Id == terminationId, cancellationToken)
            ?? throw new KeyNotFoundException("Çıkış kaydı bulunamadı.");

        if (termination.Status == TerminationStatus.Finalized)
            throw new InvalidOperationException("Bu çıkış kaydı zaten kesinleşmiş.");

        var personnel = await db.Personnel
            .SingleAsync(x => x.Id == termination.PersonnelId, cancellationToken);

        termination.Status = TerminationStatus.Finalized;
        termination.FinalizedAtUtc = DateTime.UtcNow;
        termination.FinalizedByUserId = currentUser.UserId;

        personnel.EmploymentEndDate = termination.TerminationDate;
        personnel.Status = PersonnelStatus.Terminated;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CompanyPayrollSettings> LoadSettingsAsync(
        Guid companyId, int year, CancellationToken cancellationToken) =>
        await db.CompanyPayrollSettings
            .AsNoTracking()
            .Include(x => x.TaxBrackets)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken)
        ?? throw new InvalidOperationException(
            $"{year} yılı için bordro parametresi tanımlı değil; tazminat hesaplanamaz.");

    private static PayrollParameters ToParameters(CompanyPayrollSettings settings) =>
        new(
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
                .Select(x => new PayrollTaxBracketInput(x.LowerBound, x.UpperBound, x.Rate))
                .ToList());

    /// <summary>Ayrılış tarihinde yürürlükteki resmi brüt ücret.</summary>
    private async Task<decimal> ResolveOfficialGrossAsync(
        Guid personnelId, DateTime date, CancellationToken cancellationToken)
    {
        var salary = await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.EffectiveStartDate <= date &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= date))
            .OrderByDescending(x => x.EffectiveStartDate)
            .Select(x => (decimal?)x.GrossSalary)
            .FirstOrDefaultAsync(cancellationToken);

        return salary ?? 0m;
    }

    /// <summary>
    /// Ayrılış tarihinde yürürlükteki elden ödeme tutarı. Bu metot
    /// YALNIZCA izin kontrolünden sonra çağrılır.
    /// </summary>
    private async Task<decimal> ResolveExtraMonthlyAsync(
        Guid personnelId, DateTime date, CancellationToken cancellationToken)
    {
        var amount = await db.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.EffectiveStartDate <= date &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= date))
            .OrderByDescending(x => x.EffectiveStartDate)
            .Select(x => (decimal?)x.MonthlyAmount)
            .FirstOrDefaultAsync(cancellationToken);

        return amount ?? 0m;
    }

    /// <summary>
    /// Kullanılmayan yıllık izin günü: hak ediş (İş Kanunu 53) −
    /// onaylanmış yıllık izinler. Devir bakiyeleri sistemde tutulmadığı
    /// için sonuç ekranda elle düzeltilebilir.
    /// </summary>
    private async Task<decimal> CalculateUnusedLeaveDaysAsync(
        Guid personnelId, int serviceDays, CancellationToken cancellationToken)
    {
        var entitlement =
            SeveranceCalculationService.TotalAnnualLeaveEntitlement(serviceDays);

        if (entitlement == 0)
            return 0m;

        var used = await hrDb.LeaveRequests
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.LeaveType == HrLeaveType.Annual &&
                        x.Status == HrApprovalStatus.Approved)
            .SumAsync(x => (decimal?)x.TotalDays, cancellationToken) ?? 0m;

        return Math.Max(0m, entitlement - used);
    }

    private static TerminationComponentView ToView(SeveranceComponent component) =>
        new(component.Gross, component.SgkAmount, component.IncomeTax,
            component.StampTax, component.Net);

    public static string ReasonName(TerminationReason reason) => reason switch
    {
        TerminationReason.EmployerTermination => "İşveren feshi (haklı neden yok)",
        TerminationReason.Resignation => "İstifa",
        TerminationReason.ResignationWithJustCause => "İstifa (haklı nedenle)",
        TerminationReason.Retirement => "Emeklilik",
        TerminationReason.MilitaryService => "Askerlik",
        TerminationReason.Marriage => "Evlilik (kadın işçi, 1 yıl içinde)",
        TerminationReason.EmployerTerminationWithJustCause =>
            "İşveren haklı fesih (25/II)",
        TerminationReason.FixedTermContractEnd => "Belirli süreli sözleşme bitimi",
        TerminationReason.Death => "Vefat (mirasçıya)",
        _ => reason.ToString()
    };

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
