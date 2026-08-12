using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Subcontractors;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>
/// Bir projenin kârlılık satırı.
///
/// <c>OtherCost</c> her zaman sıfır: maliyet sınıfları dörde indi ama
/// alan, arayüz sözleşmesi bozulmasın diye duruyor.
/// </summary>
public sealed record ProjectProfitabilityRow(
    Guid ProjectId,
    string ProjectName,
    decimal Revenue,
    decimal MaterialCost,
    decimal LaborCost,
    decimal SubcontractorCost,
    decimal GeneralExpenseCost,
    decimal OtherCost,
    decimal TotalCost,
    decimal Profit,
    decimal ProfitMargin);

/// <summary>
/// Proje kârlılık özetinin tek kaynağı.
///
/// NEDEN SERVİS: bu hesap eskiden controller'ın içindeydi. Yönetim
/// KPI'ı "en kötü marjlı proje"yi göstermek için aynı dönüşümü ikinci
/// kez yazmak zorunda kalacaktı; iki kopya zamanla ayrışır ve aynı
/// proje iki ekranda iki farklı marjla görünürdü.
///
/// ELDEN TAŞERON MALİYETİ YETKİYE BAĞLI: yetkisi olmayan kullanıcıya
/// sıfır ekleniyor ve bu "gizlendi" diye İŞARETLENMİYOR — toplamın
/// eksik olduğunu bilmek, elden ödeme yapıldığı bilgisini sızdırırdı.
/// KPI da aynı davranışı miras alır: yetkisiz kullanıcı daha iyi bir
/// marj görür ve bunu bilmez. Bu bilinçli bir karardır, kusur değil.
/// </summary>
public sealed class ProjectProfitabilitySummaryService(
    AppDbContext db,
    IProjectCostAnalysisService analysisService,
    SubcontractorLedgerService subcontractorLedger,
    IExtraPaymentVisibilityService extraPaymentVisibility)
{
    /// <summary>
    /// Tüm iptal edilmemiş projelerin kârlılığı. Her proje için analiz
    /// ayrı çalışır; proje sayısı arttığında bu uç sayfalanmalı.
    /// </summary>
    public async Task<IReadOnlyList<ProjectProfitabilityRow>> GetSummaryAsync(
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.Projects
            .AsNoTracking()
            .Where(x => x.Status != ProjectStatus.Cancelled);

        if (companyId is Guid company)
            query = query.Where(x => x.CompanyId == company);

        var projectIds = await query
            .OrderBy(x => x.Code)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var rows = new List<ProjectProfitabilityRow>(projectIds.Count);

        foreach (var projectId in projectIds)
        {
            var analysis = await analysisService.AnalyzeAsync(projectId, cancellationToken);

            if (analysis is null)
                continue;

            rows.Add(ToRow(
                analysis,
                await GetCashCostAsync(projectId, cancellationToken)));
        }

        return rows;
    }

    /// <summary>Tek projenin kârlılığı — aynı dönüşüm, tek satır.</summary>
    public async Task<ProjectProfitabilityRow?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var analysis = await analysisService.AnalyzeAsync(projectId, cancellationToken);

        return analysis is null
            ? null
            : ToRow(analysis, await GetCashCostAsync(projectId, cancellationToken));
    }

    /// <summary>
    /// Projenin elden taşeron maliyeti. Yetkisiz kullanıcıya SIFIR
    /// döner ve bu işaretlenmez; gerekçesi sınıf notunda.
    /// </summary>
    private async Task<decimal> GetCashCostAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var canViewCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        return await subcontractorLedger.GetProjectCashCostAsync(
            projectId, canViewCash, cancellationToken);
    }

    private static ProjectProfitabilityRow ToRow(
        ProjectCostAnalysisResult analysis, decimal subcontractorCashCost)
    {
        decimal ByClass(ProjectCostClass costClass) =>
            analysis.Components
                .Where(x => x.CostClass == (int)costClass)
                .Select(x => x.Actual)
                .FirstOrDefault();

        // İşçilik bileşeni taşeronu da içeriyor; ayrı satır olduğu için
        // burada tekrar toplanmamalı.
        var labor = ByClass(ProjectCostClass.Labor) -
                    ByClass(ProjectCostClass.SubcontractorLabor);

        var profit = decimal.Round(analysis.Profit - subcontractorCashCost, 2);

        return new ProjectProfitabilityRow(
            analysis.ProjectId,
            analysis.ProjectName,
            analysis.RevenueAmount,
            ByClass(ProjectCostClass.Material),
            decimal.Round(labor, 2),
            decimal.Round(
                ByClass(ProjectCostClass.SubcontractorLabor) + subcontractorCashCost, 2),
            ByClass(ProjectCostClass.Overhead),
            0m,
            decimal.Round(analysis.TotalCost + subcontractorCashCost, 2),
            profit,
            analysis.RevenueAmount > 0m
                ? decimal.Round(profit / analysis.RevenueAmount * 100m, 2)
                : 0m);
    }
}
