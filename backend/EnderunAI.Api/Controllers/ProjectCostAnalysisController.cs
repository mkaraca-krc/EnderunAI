using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
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
    IProjectCostAnalysisService analysisService) : ControllerBase
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

        return result is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(ToProfitability(result));
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
                results.Add(ToProfitability(analysis));
        }

        return Ok(results);
    }

    /// <summary>
    /// Analiz sonucunu mevcut kârlılık ekranının beklediği biçime
    /// çevirir. Sınıflar dörde indiği için "otherCost" her zaman sıfır;
    /// alan arayüz sözleşmesi bozulmasın diye duruyor.
    /// </summary>
    private static object ToProfitability(ProjectCostAnalysisResult analysis)
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
            subcontractorCost = ByClass(ProjectCostClass.SubcontractorLabor),
            generalExpenseCost = ByClass(ProjectCostClass.Overhead),
            otherCost = 0m,
            totalCost = analysis.TotalCost,
            profit = analysis.Profit,
            profitMargin = analysis.ProfitMarginPercent ?? 0m
        };
    }
}
