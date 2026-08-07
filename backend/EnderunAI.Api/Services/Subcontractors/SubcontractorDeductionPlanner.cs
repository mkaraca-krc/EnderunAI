using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>Hakedişe önerilen tek bir kesinti kalemi.</summary>
/// <param name="Amount">Önerilen tutar; null ise hesaplanamadı ve
/// kullanıcı elle girecek.</param>
/// <param name="Basis">Hesabın dayanağı; null ise öneri yok.</param>
public sealed record PlannedDeduction(
    int DeductionType,
    string Description,
    decimal Rate,
    decimal? Amount,
    string? Basis);

/// <summary>
/// Taşeron hakedişinin kesinti kalemlerini SÖZLEŞMEDEN kurar.
///
/// Hangi kalemlerin açılacağı kapsam tiklerine bağlıdır; kullanıcı
/// listeyi elle kurmaz. Elle kurulsaydı sözleşmeyle hakediş sessizce
/// ayrışır ve bir dönem unutulan kesinti bir daha geri gelmezdi.
///
/// TUTAR ise ÖNERİDİR: yansıtma motorundan, bordrodan ya da orandan
/// gelir; hesaplanamıyorsa kalem tutarsız açılır ve ön muhasebe
/// mutabakata göre kendi girer.
/// </summary>
public sealed class SubcontractorDeductionPlanner(
    AppDbContext db,
    SubcontractorTeamService teamService)
{
    /// <summary>
    /// Dönemin kesinti planı.
    /// </summary>
    /// <param name="cumulativeWorkAmount">Kümülatif iş tutarı —
    /// oransal kesintilerin (teminat) tabanı.</param>
    public async Task<IReadOnlyList<PlannedDeduction>> PlanAsync(
        SubcontractorContract contract,
        int year,
        int month,
        decimal cumulativeWorkAmount,
        CancellationToken cancellationToken)
    {
        var planned = new List<PlannedDeduction>();

        // --- Teminat: oransal, kümülatif tabandan ---
        if (contract.RetentionRate > 0m)
        {
            planned.Add(new PlannedDeduction(
                DeductionType: (int)HakedisDeductionType.PerformanceBond,
                Description: "Teminat kesintisi",
                Rate: contract.RetentionRate,
                // Oransal kesintide tutarı motor hesaplıyor; burada
                // yalnızca oranı taşıyoruz.
                Amount: null,
                Basis: $"Kümülatif iş {cumulativeWorkAmount:N2} × %{contract.RetentionRate:N2}"));
        }

        // --- SGK/işçilik: bizim bordromuzdaki taşeron ekibi ---
        if (contract.SocialSecurityResponsibility == SubcontractorResponsibility.Us)
        {
            var payrollCost = await teamService.CalculatePayrollCostAsync(
                contract.Id,
                contract.SocialSecurityResponsibility,
                year,
                month,
                cancellationToken);

            planned.Add(new PlannedDeduction(
                DeductionType: (int)HakedisDeductionType.Other,
                Description: "SGK / işçilik kesintisi (taşeron ekibi)",
                Rate: 0m,
                Amount: payrollCost?.Amount,
                Basis: payrollCost?.Basis
                    ?? "Bu dönemde ekibe ait onaylı bordro yok; tutarı elle girin."));
        }

        // --- İSG yansıtması ---
        if (contract.OhsResponsibility == SubcontractorResponsibility.Us)
        {
            var ohs = await PlanOhsAsync(contract, year, month, cancellationToken);
            planned.Add(ohs);
        }

        // --- Yemek ve konaklama yansıtması ---
        if (contract.MealResponsibility == SubcontractorResponsibility.Us)
        {
            planned.Add(new PlannedDeduction(
                DeductionType: (int)HakedisDeductionType.Meal,
                Description: "Yemek yansıtması",
                Rate: 0m,
                Amount: null,
                Basis:
                    "Alt kalemleri (kahvaltı/öğlen/akşam/kumanya) işveren " +
                    "birim fiyatı ve taşeron puantaj adediyle girin."));
        }

        if (contract.AccommodationResponsibility == SubcontractorResponsibility.Us)
        {
            planned.Add(new PlannedDeduction(
                DeductionType: (int)HakedisDeductionType.Accommodation,
                Description: "Konaklama yansıtması",
                Rate: 0m,
                Amount: null,
                Basis:
                    "Alt kalemleri (yatılı/evci) işveren birim fiyatı ve " +
                    "taşeron puantaj adediyle girin."));
        }

        // --- Malzeme: bizim verdiğimiz malzemenin bedeli ---
        if (contract.MaterialResponsibility == SubcontractorResponsibility.Us)
        {
            planned.Add(new PlannedDeduction(
                DeductionType: (int)HakedisDeductionType.MaterialDeduction,
                Description: "Malzeme kesintisi (bizden verilen)",
                Rate: 0m,
                Amount: null,
                Basis:
                    "Bu döneme ait malzeme çıkışının bedelini girin; " +
                    "otomatik hesap depo sarfı bağlanınca gelecek."));
        }

        return planned;
    }

    /// <summary>
    /// İSG yansıtması: işveren hakedişimizden bu dönem kesilen İSG payı
    /// × (taşeron işçisi / şantiyede puantajı olan toplam işçi).
    ///
    /// Payda FİİLEN ÇALIŞANDIR (dönem içinde puantaj kaydı olan tekil
    /// kişi): işveren İSG kesintisi de fiilen çalışan üzerinden doğduğu
    /// için iki taraf aynı tabana oturuyor.
    /// </summary>
    private async Task<PlannedDeduction> PlanOhsAsync(
        SubcontractorContract contract,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        const int ohsType = (int)HakedisDeductionType.OhsContribution;
        const string description = "İSG katılım payı yansıtması";

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        // İşveren hakedişimizden bu dönem kesilen İSG payı.
        var employerOhs = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == contract.ProjectId &&
                        x.ProgressPaymentDate >= periodStart &&
                        x.ProgressPaymentDate < periodEnd &&
                        x.Status != ProgressPaymentStatus.Cancelled)
            .SelectMany(x => x.Deductions)
            .Where(x => x.DeductionType == ohsType)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        // Dönemde şantiyede puantajı olan tekil kişiler.
        var attendanceQuery = db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.WorkDate >= periodStart && x.WorkDate < periodEnd);

        attendanceQuery = contract.ProjectSiteId is Guid siteId
            ? attendanceQuery.Where(x => x.ProjectSiteId == siteId)
            : attendanceQuery.Where(x => x.ProjectId == contract.ProjectId);

        var workerIds = await attendanceQuery
            .Select(x => x.PersonnelId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var teamIds = await db.Personnel
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contract.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var subcontractorWorkers = workerIds.Count(x => teamIds.Contains(x));

        var reflection = SubcontractorReflectionCalculator.CalculateOhs(
            contract.OhsResponsibility,
            employerOhs,
            subcontractorWorkers,
            workerIds.Count);

        return reflection is null
            ? new PlannedDeduction(
                ohsType,
                description,
                Rate: 0m,
                Amount: null,
                Basis: BuildOhsFailureReason(
                    employerOhs, subcontractorWorkers, workerIds.Count))
            : new PlannedDeduction(
                ohsType,
                description,
                Rate: 0m,
                Amount: reflection.Amount,
                Basis: reflection.Basis);
    }

    /// <summary>
    /// Öneri neden üretilemedi. "Hesaplanamadı" demek yetmez; kullanıcı
    /// eksik olanı görmeden düzeltemez.
    /// </summary>
    private static string BuildOhsFailureReason(
        decimal employerOhs, int subcontractorWorkers, int siteWorkers)
    {
        if (employerOhs <= 0m)
        {
            return
                "Bu dönemde işveren hakedişimizden İSG kesintisi yok; " +
                "yansıtılacak tutar da yok.";
        }

        if (subcontractorWorkers <= 0)
        {
            return
                "Bu dönemde taşeron ekibine ait puantaj kaydı yok; " +
                "pay hesaplanamadı.";
        }

        if (siteWorkers <= 0)
            return "Bu dönemde şantiyede puantaj kaydı yok; payda hesaplanamadı.";

        return
            $"Taşeron işçisi ({subcontractorWorkers}) şantiye toplamından " +
            $"({siteWorkers}) fazla görünüyor; puantajı kontrol edin.";
    }
}
