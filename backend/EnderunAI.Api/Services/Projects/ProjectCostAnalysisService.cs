using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>Bir bileşenin öngörü–gerçekleşen karşılaştırması.</summary>
/// <param name="ForecastContract">İcmalin tamamındaki öngörü.</param>
/// <param name="ForecastEarned">
/// İlerlemeye göre düzeltilmiş öngörü: icmal öngörüsü × hakediş
/// ilerleme oranı. Asıl karşılaştırma budur — sözleşmenin tamamıyla
/// kıyaslanırsa proje bitene kadar her bileşen "tasarruf" görünür.
/// </param>
/// <param name="Actual">Bu bileşene düşen gerçekleşen maliyet.</param>
public sealed record CostComponentComparison(
    int CostClass,
    string CostClassName,
    decimal ForecastContract,
    decimal ForecastEarned,
    decimal Actual,
    decimal Variance,
    decimal? VariancePercent);

public sealed record CostSectionBreakdown(
    Guid? SectionId,
    string SectionName,
    decimal MaterialAmount,
    decimal LaborAmount,
    decimal SubcontractorLaborAmount,
    decimal OverheadAmount,
    decimal TotalAmount);

public sealed record CostMonthlyPoint(
    int Year,
    int Month,
    string Label,
    decimal MaterialAmount,
    decimal LaborAmount,
    decimal SubcontractorLaborAmount,
    decimal OverheadAmount,
    decimal TotalAmount,
    decimal RevenueAmount);

public sealed record ProjectCostAnalysisResult(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CurrencyCode,
    /// <summary>Sözleşme referansı icmal bulundu mu.</summary>
    bool HasContractBaseline,
    decimal ContractForecastTotal,
    /// <summary>Kümülatif hakediş iş tutarı (KDV ve kesinti hariç).</summary>
    decimal RevenueAmount,
    /// <summary>Hakedişin icmale oranı — düzeltilmiş öngörünün çarpanı.</summary>
    decimal ProgressRatio,
    decimal TotalCost,
    decimal Profit,
    decimal? ProfitMarginPercent,
    IReadOnlyList<CostComponentComparison> Components,
    IReadOnlyList<CostSectionBreakdown> Sections,
    IReadOnlyList<CostMonthlyPoint> Monthly,
    /// <summary>
    /// İşçilikte kullanılan işveren yükü çarpanı (SGK işveren +
    /// işsizlik). Ekranda varsayımın görünmesi için dışarı verilir.
    /// </summary>
    decimal EmployerCostFactor,
    /// <summary>
    /// Elden ödemelerden bu projeye düşen pay. YALNIZCA yetkili
    /// kullanıcıda dolu; yetkisizde her zaman null.
    /// </summary>
    decimal? ExtraPaymentLaborCost,
    /// <summary>Yetkisiz kullanıcıya toplamın resmi kısmı gösterilir.</summary>
    bool IncludesExtraPayments,
    IReadOnlyList<string> Assumptions);

public interface IProjectCostAnalysisService
{
    Task<ProjectCostAnalysisResult?> AnalyzeAsync(
        Guid projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Şantiye maliyet analizi: icmal öngörüsü ↔ gerçekleşen maliyet ↔ kâr.
///
/// İşçilik maliyeti <see cref="HrProjectLaborCost"/> tablosundan CANLI
/// okunur, proje maliyet defterine kopyalanmaz: bordro yeniden
/// hesaplandığında o satırlar baştan yazılıyor; ikinci bir tabloya
/// mükerrer yazmak iki defteri sürekli senkron tutma sorunu doğururdu.
/// Kullanıcı yine tek bir görünüm görür, birleştirme burada yapılır.
///
/// ÖNEMLİ ÇERÇEVE: icmalin üç bileşeni bizim SATIŞ fiyatı kırılımımızdır,
/// maliyet bütçesi değil. Bu yüzden karşılaştırma "bütçe aşımı" değil
/// "bileşen kârlılığı" olarak okunmalıdır.
/// </summary>
public sealed class ProjectCostAnalysisService(
    AppDbContext db,
    IExtraPaymentVisibilityService extraPaymentVisibility) : IProjectCostAnalysisService
{
    public async Task<ProjectCostAnalysisResult?> AnalyzeAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.CompanyId, x.Code, x.Name, x.CurrencyCode })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return null;

        var assumptions = new List<string>();

        // --- İcmal öngörüsü (sözleşme referansı keşif) ---
        var baseline = await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsContractBaseline && x.IsCurrentRevision)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new { x.Id, x.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken);

        var forecast = new Dictionary<ProjectCostClass, decimal>
        {
            [ProjectCostClass.Material] = 0m,
            [ProjectCostClass.Labor] = 0m,
            [ProjectCostClass.Overhead] = 0m
        };

        var contractForecastTotal = 0m;

        if (baseline is not null)
        {
            var componentTotals = await db.ProjectBoqItems
                .AsNoTracking()
                .Where(x => x.ProjectBoqId == baseline.Id)
                .GroupBy(x => 1)
                .Select(g => new
                {
                    Material = g.Sum(x => x.MaterialUnitPrice * x.ContractQuantity),
                    Labor = g.Sum(x => x.LaborUnitPrice * x.ContractQuantity),
                    Overhead = g.Sum(x => x.OverheadUnitPrice * x.ContractQuantity),
                    Total = g.Sum(x => x.UnitPrice * x.ContractQuantity)
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (componentTotals is not null)
            {
                forecast[ProjectCostClass.Material] = decimal.Round(componentTotals.Material, 2);
                forecast[ProjectCostClass.Labor] = decimal.Round(componentTotals.Labor, 2);
                forecast[ProjectCostClass.Overhead] = decimal.Round(componentTotals.Overhead, 2);
                contractForecastTotal = decimal.Round(componentTotals.Total, 2);
            }
        }
        else
        {
            assumptions.Add(
                "Projede sözleşme referansı icmal yok; öngörü sütunları boş. " +
                "Keşif ekranından bir icmali sözleşme referansı işaretleyin.");
        }

        // --- Gelir: kesinleşmiş hakedişlerin kümülatif iş tutarı ---
        var revenue = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status == ProgressPaymentStatus.Approved)
            .OrderByDescending(x => x.ProgressPaymentDate)
            .Select(x => (decimal?)x.CumulativeAmount)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        // İlerleme oranı: hakedilen iş / icmal toplamı. İcmal yoksa 1
        // kabul edilir; öngörü zaten boş olduğu için sonucu etkilemez.
        var progressRatio = contractForecastTotal > 0m
            ? decimal.Round(revenue / contractForecastTotal, 6)
            : 1m;

        // --- Gerçekleşen: maliyet defteri ---
        var ledger = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.CostClass,
                x.Amount,
                x.CostDate,
                x.ProjectHakedisSectionId
            })
            .ToListAsync(cancellationToken);

        // --- Gerçekleşen: işçilik (puantaj/bordro) ---
        var employerFactor = await ResolveEmployerCostFactorAsync(
            project.CompanyId, cancellationToken);

        assumptions.Add(
            $"İşçilik, brüt kazanca işveren yükü çarpanı ({employerFactor:0.###}) " +
            "uygulanarak hesaplandı; 5510 teşvik indirimleri dikkate alınmadı.");

        var laborRows = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.TotalLaborCost,
                x.WorkDate,
                x.ProjectHakedisSectionId,
                x.PersonnelId
            })
            .ToListAsync(cancellationToken);

        var officialLabor = decimal.Round(
            laborRows.Sum(x => x.TotalLaborCost) * employerFactor, 2);

        // --- Elden ödeme payı (yetkiye bağlı) ---
        var canSeeExtraPayments =
            await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);

        decimal? extraPaymentLabor = null;

        if (canSeeExtraPayments)
        {
            extraPaymentLabor = await CalculateExtraPaymentShareAsync(
                project.CompanyId, projectId, cancellationToken);

            assumptions.Add(
                "Elden ödemeler personelin o aydaki proje gün sayısına oranla " +
                "dağıtıldı; resmi bordroya ve muhasebeye yansımaz.");
        }

        var totalLabor = officialLabor + (extraPaymentLabor ?? 0m);

        // --- Bileşen karşılaştırması ---
        var actualByClass = new Dictionary<ProjectCostClass, decimal>
        {
            [ProjectCostClass.Material] = decimal.Round(
                ledger.Where(x => x.CostClass == ProjectCostClass.Material)
                      .Sum(x => x.Amount), 2),
            [ProjectCostClass.Labor] = decimal.Round(
                ledger.Where(x => x.CostClass == ProjectCostClass.Labor)
                      .Sum(x => x.Amount), 2) + totalLabor,
            [ProjectCostClass.SubcontractorLabor] = decimal.Round(
                ledger.Where(x => x.CostClass == ProjectCostClass.SubcontractorLabor)
                      .Sum(x => x.Amount), 2),
            [ProjectCostClass.Overhead] = decimal.Round(
                ledger.Where(x => x.CostClass == ProjectCostClass.Overhead)
                      .Sum(x => x.Amount), 2)
        };

        var components = new List<CostComponentComparison>
        {
            BuildComparison(ProjectCostClass.Material,
                forecast[ProjectCostClass.Material], progressRatio,
                actualByClass[ProjectCostClass.Material]),

            // Taşeron işçiliği icmalde ayrı bir bileşen değildir; işçilik
            // öngörüsü kendi işçiliğimizle taşeronun TOPLAMINI karşılar.
            BuildComparison(ProjectCostClass.Labor,
                forecast[ProjectCostClass.Labor], progressRatio,
                actualByClass[ProjectCostClass.Labor] +
                actualByClass[ProjectCostClass.SubcontractorLabor]),

            BuildComparison(ProjectCostClass.SubcontractorLabor,
                0m, progressRatio,
                actualByClass[ProjectCostClass.SubcontractorLabor]),

            BuildComparison(ProjectCostClass.Overhead,
                forecast[ProjectCostClass.Overhead], progressRatio,
                actualByClass[ProjectCostClass.Overhead])
        };

        var totalCost = actualByClass.Values.Sum();

        // --- Kısım kırılımı ---
        var sectionNames = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var sectionKeys = ledger.Select(x => x.ProjectHakedisSectionId)
            .Concat(laborRows.Select(x => x.ProjectHakedisSectionId))
            .Distinct()
            .ToList();

        var sections = sectionKeys
            .Select(sectionId =>
            {
                var ledgerRows = ledger.Where(x => x.ProjectHakedisSectionId == sectionId).ToList();

                var sectionLabor = decimal.Round(
                    laborRows.Where(x => x.ProjectHakedisSectionId == sectionId)
                             .Sum(x => x.TotalLaborCost) * employerFactor, 2);

                var material = decimal.Round(
                    ledgerRows.Where(x => x.CostClass == ProjectCostClass.Material)
                              .Sum(x => x.Amount), 2);
                var labor = decimal.Round(
                    ledgerRows.Where(x => x.CostClass == ProjectCostClass.Labor)
                              .Sum(x => x.Amount), 2) + sectionLabor;
                var subcontractor = decimal.Round(
                    ledgerRows.Where(x => x.CostClass == ProjectCostClass.SubcontractorLabor)
                              .Sum(x => x.Amount), 2);
                var overhead = decimal.Round(
                    ledgerRows.Where(x => x.CostClass == ProjectCostClass.Overhead)
                              .Sum(x => x.Amount), 2);

                return new CostSectionBreakdown(
                    sectionId,
                    sectionId is Guid id && sectionNames.TryGetValue(id, out var name)
                        ? name
                        : "Genel (kısım seçilmemiş)",
                    material, labor, subcontractor, overhead,
                    material + labor + subcontractor + overhead);
            })
            .Where(x => x.TotalAmount != 0m)
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        // --- Aylık trend ---
        var monthly = BuildMonthly(
            ledger.Select(x => (x.CostDate, x.CostClass, x.Amount)),
            laborRows.Select(x => (x.WorkDate, x.TotalLaborCost * employerFactor)),
            await LoadMonthlyRevenueAsync(projectId, cancellationToken));

        return new ProjectCostAnalysisResult(
            project.Id,
            project.Code,
            project.Name,
            project.CurrencyCode,
            baseline is not null,
            contractForecastTotal,
            decimal.Round(revenue, 2),
            progressRatio,
            decimal.Round(totalCost, 2),
            decimal.Round(revenue - totalCost, 2),
            revenue > 0m
                ? decimal.Round((revenue - totalCost) / revenue * 100m, 2)
                : null,
            components,
            sections,
            monthly,
            employerFactor,
            extraPaymentLabor,
            canSeeExtraPayments,
            assumptions);
    }

    private static CostComponentComparison BuildComparison(
        ProjectCostClass costClass,
        decimal forecastContract,
        decimal progressRatio,
        decimal actual)
    {
        var forecastEarned = decimal.Round(forecastContract * progressRatio, 2);
        var variance = decimal.Round(actual - forecastEarned, 2);

        return new CostComponentComparison(
            (int)costClass,
            ProjectCostClassifier.Name(costClass),
            forecastContract,
            forecastEarned,
            decimal.Round(actual, 2),
            variance,
            forecastEarned != 0m
                ? decimal.Round(variance / forecastEarned * 100m, 2)
                : null);
    }

    /// <summary>
    /// İşveren yükü çarpanı: 1 + (SGK işveren + işsizlik işveren) / 100.
    /// Ayar bulunamazsa 1 döner ve işçilik brüt kazançla sınırlı kalır —
    /// uydurma bir oranla maliyeti şişirmektense eksik göstermek ve
    /// varsayımı ekranda yazmak daha dürüst.
    /// </summary>
    private async Task<decimal> ResolveEmployerCostFactorAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Year)
            .Select(x => new { x.SgkEmployerRate, x.UnemploymentEmployerRate })
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return 1m;

        return 1m + ((settings.SgkEmployerRate + settings.UnemploymentEmployerRate) / 100m);
    }

    /// <summary>
    /// Elden ödemelerin bu projeye düşen payı: personelin o ay bu
    /// projede çalıştığı gün / o ay toplam çalıştığı gün.
    ///
    /// Doğrudan aylık tutarın tamamı yazılsaydı, birden fazla projede
    /// çalışan personelin elden ödemesi her projeye ayrı ayrı yüklenir
    /// ve toplam maliyet gerçekte ödenenin katı çıkardı.
    /// </summary>
    private async Task<decimal> CalculateExtraPaymentShareAsync(
        Guid companyId, Guid projectId, CancellationToken cancellationToken)
    {
        var payments = await db.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.PersonnelId,
                x.MonthlyAmount,
                x.EffectiveStartDate,
                x.EffectiveEndDate
            })
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return 0m;

        var personnelIds = payments.Select(x => x.PersonnelId).Distinct().ToList();

        // Yalnızca çalışılan günler; izin/rapor günü projeye yüklenmez.
        var days = await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsApproved &&
                        x.ProjectId != null &&
                        personnelIds.Contains(x.PersonnelId))
            .Select(x => new { x.PersonnelId, x.ProjectId, x.WorkDate })
            .ToListAsync(cancellationToken);

        if (days.Count == 0)
            return 0m;

        var total = 0m;

        foreach (var group in days.GroupBy(x => new
        {
            x.PersonnelId,
            x.WorkDate.Year,
            x.WorkDate.Month
        }))
        {
            var monthDays = group.Count();

            if (monthDays == 0)
                continue;

            var projectDays = group.Count(x => x.ProjectId == projectId);

            if (projectDays == 0)
                continue;

            var monthStart = new DateTime(group.Key.Year, group.Key.Month, 1);

            var monthlyAmount = payments
                .Where(x => x.PersonnelId == group.Key.PersonnelId &&
                            x.EffectiveStartDate.Date <= monthStart.AddMonths(1).AddDays(-1) &&
                            (x.EffectiveEndDate == null ||
                             x.EffectiveEndDate.Value.Date >= monthStart))
                .Sum(x => x.MonthlyAmount);

            total += monthlyAmount * projectDays / monthDays;
        }

        return decimal.Round(total, 2);
    }

    private async Task<IReadOnlyList<(int Year, int Month, decimal Amount)>>
        LoadMonthlyRevenueAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status == ProgressPaymentStatus.Approved)
            .Select(x => new { x.ProgressPaymentDate, x.CurrentAmount })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => new { x.ProgressPaymentDate.Year, x.ProgressPaymentDate.Month })
            .Select(g => (g.Key.Year, g.Key.Month, decimal.Round(g.Sum(x => x.CurrentAmount), 2)))
            .ToList();
    }

    private static IReadOnlyList<CostMonthlyPoint> BuildMonthly(
        IEnumerable<(DateTime Date, ProjectCostClass CostClass, decimal Amount)> ledger,
        IEnumerable<(DateTime Date, decimal Amount)> labor,
        IReadOnlyList<(int Year, int Month, decimal Amount)> revenue)
    {
        var points = new Dictionary<(int Year, int Month), decimal[]>();

        void Add((int Year, int Month) key, int slot, decimal amount)
        {
            if (!points.TryGetValue(key, out var values))
            {
                values = new decimal[5];
                points[key] = values;
            }

            values[slot] += amount;
        }

        foreach (var row in ledger)
        {
            var slot = row.CostClass switch
            {
                ProjectCostClass.Material => 0,
                ProjectCostClass.Labor => 1,
                ProjectCostClass.SubcontractorLabor => 2,
                _ => 3
            };

            Add((row.Date.Year, row.Date.Month), slot, row.Amount);
        }

        foreach (var row in labor)
            Add((row.Date.Year, row.Date.Month), 1, row.Amount);

        foreach (var row in revenue)
            Add((row.Year, row.Month), 4, row.Amount);

        return points
            .OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month)
            .Select(x => new CostMonthlyPoint(
                x.Key.Year,
                x.Key.Month,
                $"{x.Key.Month:00}.{x.Key.Year}",
                decimal.Round(x.Value[0], 2),
                decimal.Round(x.Value[1], 2),
                decimal.Round(x.Value[2], 2),
                decimal.Round(x.Value[3], 2),
                decimal.Round(x.Value[0] + x.Value[1] + x.Value[2] + x.Value[3], 2),
                decimal.Round(x.Value[4], 2)))
            .ToList();
    }
}
