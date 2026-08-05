using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Keşif–gerçekleşen takibi. Sözleşmede öngörülen metraj ile
/// hakedişlerden biriken gerçekleşen miktarı karşılaştırır ve sapmayı
/// sözleşme tipine göre yorumlar.
///
/// Tutar içerdiği için hakediş görüntüleme izniyle korunur.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public sealed class ProgressTrackingController(
    IProgressTrackingService service,
    IContractSummaryProgressService summaryService) : ControllerBase
{
    /// <summary>
    /// Sözleşme icmali ilerleme görünümü: kısım ve satır bazında
    /// sözleşme miktarı / saha gerçekleşmesi / işveren kabulü / kalan,
    /// hem yüzde hem tutar.
    ///
    /// Ayrı bir hesap motoru yazılmadı — aynı
    /// <see cref="IContractSummaryProgressService"/> portalı, hakediş
    /// önerisini ve fark raporunu da besliyor. Tek kaynak.
    ///
    /// Tutar içerdiği için hakediş görüntüleme izniyle korunur; portal
    /// aynı servisi kullanır ama yalnızca yüzdeyi dışarı verir.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/icmal-ilerleme")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> ContractSummaryProgress(
        Guid projectId, CancellationToken cancellationToken)
    {
        var view = await summaryService.BuildAsync(projectId, cancellationToken);

        if (!view.HasContractSummary)
        {
            return Ok(new
            {
                hasContractSummary = false,
                message = "Bu projede sözleşme icmali tanımlı değil."
            });
        }

        return Ok(new
        {
            hasContractSummary = true,
            view.BoqId,
            view.BoqNumber,
            view.ContractAmount,
            view.FieldRate,
            view.EmployerRate,
            FieldAmount = view.Sections.SelectMany(x => x.Items).Sum(x => x.FieldAmount),
            EmployerAmount = view.Sections.SelectMany(x => x.Items).Sum(x => x.EmployerAmount),
            Sections = view.Sections.Select(section => new
            {
                section.SectionId,
                section.Name,
                section.Order,
                section.ContractAmount,
                section.FieldRate,
                section.EmployerRate,
                FieldAmount = section.Items.Sum(x => x.FieldAmount),
                EmployerAmount = section.Items.Sum(x => x.EmployerAmount),
                Items = section.Items.Select(item => new
                {
                    item.BoqItemId,
                    item.PositionCode,
                    item.Description,
                    item.Unit,
                    item.ContractQuantity,
                    item.UnitPrice,
                    item.ContractAmount,
                    item.FieldQuantity,
                    item.EmployerQuantity,
                    item.RemainingQuantity,
                    item.PendingQuantity,
                    item.FieldAmount,
                    item.EmployerAmount,
                    item.FieldRate,
                    item.EmployerRate
                })
            })
        });
    }

    [HttpGet("projects/{projectId:guid}/progress-tracking")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Get(
        Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.BuildAsync(projectId, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Sapma uyarısı üreten projeler — dashboard bildirim merkezi için.
    /// Sözleşme tipi belirlenmemiş projeler değerlendirilmez.
    /// </summary>
    [HttpGet("progress-tracking/alerts")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Alerts(CancellationToken cancellationToken) =>
        Ok(await service.GetAlertsAsync(null, cancellationToken));
}
