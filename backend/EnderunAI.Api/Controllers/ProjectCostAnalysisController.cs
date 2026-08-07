using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Services.Subcontractors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Şantiye maliyet analizi: icmal öngörüsü ↔ gerçekleşen ↔ kâr.
///
/// Kârlılık uçları (profitability / profitability-summary) arayüz
/// tarafından zaten çağrılıyordu ama backend'de KARŞILIĞI YOKTU; panel
/// hatayı yutup "veri bulunamadı" gösteriyordu. Analiz servisi bu
/// hesabı zaten yaptığı için uçlar buraya, aynı kaynağa bağlandı —
/// iki ayrı "doğru" maliyet rakamı oluşmasın.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectCostAnalysisController(
    AppDbContext db,
    IProjectCostAnalysisService analysisService,
    SubcontractorLedgerService subcontractorLedger,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    [HttpGet("{id:guid}/cost-analysis")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetCostAnalysis(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await analysisService.AnalyzeAsync(id, cancellationToken);

        return result is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(result);
    }

    [HttpGet("{id:guid}/profitability")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetProfitability(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await analysisService.AnalyzeAsync(id, cancellationToken);

        if (result is null)
            return NotFound(new { message = "Proje bulunamadı." });

        return Ok(ToProfitability(
            result, await GetCashCostAsync(id, cancellationToken)));
    }

    /// <summary>
    /// Projenin elden taşeron maliyeti. Yetkisiz kullanıcıya SIFIR
    /// döner ve bu "gizlendi" diye işaretlenmez: toplamın eksik
    /// olduğunu bilmek, elden ödeme yapıldığı bilgisini sızdırmak
    /// demektir.
    /// </summary>
    private async Task<decimal> GetCashCostAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var canViewCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        return await subcontractorLedger.GetProjectCashCostAsync(
            projectId, canViewCash, cancellationToken);
    }

    /// <summary>
    /// Tüm aktif projelerin kârlılık özeti. Her proje için analiz ayrı
    /// çalışır; proje sayısı arttığında bu uç sayfalanmalı.
    /// </summary>
    [HttpGet("profitability-summary")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetProfitabilitySummary(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.Projects.AsNoTracking().Where(x => x.Status != ProjectStatus.Cancelled);

        if (companyId is Guid company)
            query = query.Where(x => x.CompanyId == company);

        var projectIds = await query
            .OrderBy(x => x.Code)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var results = new List<object>(projectIds.Count);

        foreach (var projectId in projectIds)
        {
            var analysis = await analysisService.AnalyzeAsync(projectId, cancellationToken);

            if (analysis is not null)
            {
                results.Add(ToProfitability(
                    analysis,
                    await GetCashCostAsync(projectId, cancellationToken)));
            }
        }

        return Ok(results);
    }

    /// <summary>
    /// Analiz sonucunu mevcut kârlılık ekranının beklediği biçime
    /// çevirir. Sınıflar dörde indiği için "otherCost" her zaman sıfır;
    /// alan arayüz sözleşmesi bozulmasın diye duruyor.
    /// </summary>
    private static object ToProfitability(
        ProjectCostAnalysisResult analysis, decimal subcontractorCashCost)
    {
        decimal ByClass(ProjectCostClass costClass) =>
            analysis.Components
                .Where(x => x.CostClass == (int)costClass)
                .Select(x => x.Actual)
                .FirstOrDefault();

        // İşçilik bileşeni taşeronu da içeriyor; ekranda ayrı satır
        // olduğu için burada tekrar toplanmamalı.
        var labor = ByClass(ProjectCostClass.Labor) -
                    ByClass(ProjectCostClass.SubcontractorLabor);

        return new
        {
            projectId = analysis.ProjectId,
            projectName = analysis.ProjectName,
            revenue = analysis.RevenueAmount,
            materialCost = ByClass(ProjectCostClass.Material),
            laborCost = decimal.Round(labor, 2),
            // Elden taşeron ödemesi maliyet defterinde durmuyor; yetkisi
            // olana okuma anında ekleniyor, olmayana sıfır geliyor.
            subcontractorCost = decimal.Round(
                ByClass(ProjectCostClass.SubcontractorLabor) + subcontractorCashCost, 2),
            generalExpenseCost = ByClass(ProjectCostClass.Overhead),
            otherCost = 0m,
            totalCost = decimal.Round(analysis.TotalCost + subcontractorCashCost, 2),
            profit = decimal.Round(analysis.Profit - subcontractorCashCost, 2),
            profitMargin = analysis.RevenueAmount > 0m
                ? decimal.Round(
                    (analysis.Profit - subcontractorCashCost) /
                    analysis.RevenueAmount * 100m, 2)
                : 0m
        };
    }
}
