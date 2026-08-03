using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record CreateTerminationRequest(
    Guid PersonnelId,
    TerminationReason Reason,
    DateTime TerminationDate,
    decimal? UnusedLeaveDays,
    string? Note);

/// <summary>
/// Personel çıkışı ve tazminat hesabı.
///
/// Tutarlar ücret bilgisi taşıdığı için salary.view ile korunur.
/// Elden ödeme farkı ayrıca extra_payment.view ister ve yetkisiz
/// kullanıcıya null döner — bkz. PersonnelTerminationService.
/// </summary>
[ApiController]
[Authorize]
[Route("api/personnel-terminations")]
public sealed class PersonnelTerminationsController(
    AppDbContext db,
    IPersonnelTerminationService service) : ControllerBase
{
    /// <summary>
    /// "Şu an bu nedenle çıksa ne öderim" — kayıt oluşturmaz.
    /// Gizli yükümlülüğün aktif personel için görünür olmasını sağlar.
    /// </summary>
    [HttpGet("simulation")]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> Simulate(
        [FromQuery] Guid personnelId,
        [FromQuery] TerminationReason reason,
        [FromQuery] DateTime? terminationDate,
        [FromQuery] decimal? unusedLeaveDays,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.SimulateAsync(
                personnelId,
                reason,
                terminationDate ?? DateTime.UtcNow.Date,
                unusedLeaveDays,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>Ayrılış türü → tazminat hakkı matrisi (ekran bunu gösterir).</summary>
    [HttpGet("reasons")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public IActionResult Reasons() =>
        Ok(TerminationRightsMatrix.All
            .Select(pair => new
            {
                reason = (int)pair.Key,
                name = PersonnelTerminationService.ReasonName(pair.Key),
                hasSeverance = pair.Value.Severance,
                hasNotice = pair.Value.Notice,
                hasUnusedLeave = pair.Value.UnusedLeave
            })
            .OrderBy(x => x.reason)
            .ToList());

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await db.PersonnelTerminations
            .AsNoTracking()
            .OrderByDescending(x => x.TerminationDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.TerminationDate,
                Reason = (int)x.Reason,
                Status = (int)x.Status,
                x.ServiceDays,
                x.UnusedLeaveDays,
                x.OfficialNetTotal,
                x.SeveranceCeilingApplied,
                x.FinalizedAtUtc
            })
            .ToListAsync(cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SalaryManage)]
    public async Task<IActionResult> Create(
        CreateTerminationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var id = await service.CreateAsync(
                request.PersonnelId,
                request.Reason,
                request.TerminationDate,
                request.UnusedLeaveDays,
                request.Note,
                cancellationToken);

            return Ok(new { id, message = "Çıkış kaydı taslak olarak oluşturuldu." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Çıkışı kesinleştirir. Personel "Ayrıldı" durumuna geçer ve
    /// hesaplanan tutarlar donar.
    /// </summary>
    [HttpPost("{id:guid}/finalize")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public async Task<IActionResult> Finalize(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.FinalizeAsync(id, cancellationToken);
            return Ok(new { message = "Çıkış kesinleştirildi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
