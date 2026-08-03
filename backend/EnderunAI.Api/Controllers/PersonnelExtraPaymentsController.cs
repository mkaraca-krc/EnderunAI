using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record UpsertExtraPaymentRequest(
    Guid PersonnelId,
    decimal MonthlyAmount,
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate,
    string? Note);

/// <summary>
/// Elden ödeme tutarları — resmi bordroda görünmeyen kısım.
///
/// Bu ucun TAMAMI extra_payment.* izinleriyle korunur. Şantiye Şefi,
/// Formen, Teknik Ofis, Sekreterya ve hatta İK Sorumlusu bu izne sahip
/// değildir; kendilerine 403 döner. Resmi muhasebeye hiçbir kayıt
/// yazılmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/personnel-extra-payments")]
public sealed class PersonnelExtraPaymentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? personnelId, CancellationToken cancellationToken)
    {
        var query = db.PersonnelExtraPayments.AsNoTracking();

        if (personnelId is Guid id)
            query = query.Where(x => x.PersonnelId == id);

        return Ok(await query
            .OrderBy(x => x.Personnel.FirstName)
            .ThenByDescending(x => x.EffectiveStartDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.MonthlyAmount,
                x.EffectiveStartDate,
                x.EffectiveEndDate,
                x.Note
            })
            .ToListAsync(cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> Create(
        UpsertExtraPaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.MonthlyAmount < 0m)
            return BadRequest(new { message = "Tutar negatif olamaz." });

        var companyId = await db.Personnel
            .Where(x => x.Id == request.PersonnelId)
            .Select(x => (Guid?)x.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (companyId is null)
            return NotFound(new { message = "Personel bulunamadı." });

        if (request.EffectiveEndDate is DateTime end &&
            end.Date < request.EffectiveStartDate.Date)
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var record = new PersonnelExtraPayment
        {
            CompanyId = companyId.Value,
            PersonnelId = request.PersonnelId,
            MonthlyAmount = request.MonthlyAmount,
            EffectiveStartDate = DateTime.SpecifyKind(
                request.EffectiveStartDate.Date, DateTimeKind.Utc),
            EffectiveEndDate = request.EffectiveEndDate is DateTime value
                ? DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)
                : null,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        db.PersonnelExtraPayments.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { record.Id, message = "Ek ödeme kaydedildi." });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> Update(
        Guid id, UpsertExtraPaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.MonthlyAmount < 0m)
            return BadRequest(new { message = "Tutar negatif olamaz." });

        var record = await db.PersonnelExtraPayments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (record is null)
            return NotFound(new { message = "Ek ödeme kaydı bulunamadı." });

        record.MonthlyAmount = request.MonthlyAmount;
        record.EffectiveStartDate = DateTime.SpecifyKind(
            request.EffectiveStartDate.Date, DateTimeKind.Utc);
        record.EffectiveEndDate = request.EffectiveEndDate is DateTime value
            ? DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)
            : null;
        record.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        record.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Ek ödeme güncellendi." });
    }
}
