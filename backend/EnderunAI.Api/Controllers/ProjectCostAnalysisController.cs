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
/// Yetki hakediş görüntülemedir, proje görüntüleme DEĞİL: üç uç da
/// projenin kâr marjını döndürüyor, projects.view ise depo, araç,
/// sekreterya, satın alma, ön muhasebe, İK ve İSG rollerinde de var.
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
    IProjectCostAnalysisService analysisService,
    ProjectProfitabilitySummaryService profitability) : ControllerBase
{
    [HttpGet("{id:guid}/cost-analysis")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetCostAnalysis(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await analysisService.AnalyzeAsync(id, cancellationToken);

        return result is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(result);
    }

    [HttpGet("{id:guid}/profitability")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetProfitability(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await profitability.GetAsync(id, cancellationToken);

        return result is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(result);
    }

    /// <summary>
    /// Tüm aktif projelerin kârlılık özeti. Hesap
    /// <see cref="ProjectProfitabilitySummaryService"/> içinde; yönetim
    /// KPI'ı da oradan okuyor ki iki ekranda iki farklı marj çıkmasın.
    /// </summary>
    [HttpGet("profitability-summary")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetProfitabilitySummary(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken) =>
        Ok(await profitability.GetSummaryAsync(companyId, cancellationToken));
}
