using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/current-accounts")]
public sealed class CurrentAccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] CurrentAccountStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.CurrentAccounts.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderBy(x => x.Title)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.Code,
                x.Title,
                x.ShortName,
                x.Roles,
                x.Status,
                x.TaxOffice,
                x.TaxNumber,
                x.AuthorizedPerson,
                x.Phone,
                x.Email,
                x.PaymentTerm,
                x.CreditLimit,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateCurrentAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(
                x => x.Id == request.CompanyId && x.IsActive,
                cancellationToken))
        {
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.CurrentAccounts.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
        {
            return Conflict(new { message = "Bu cari kodu zaten kullanılıyor." });
        }

        var entity = new CurrentAccount
        {
            CompanyId = request.CompanyId,
            Code = code,
            Title = request.Title.Trim(),
            ShortName = request.ShortName?.Trim(),
            Roles = (CurrentAccountRoles)request.Roles,
            Status = CurrentAccountStatus.Draft,
            TaxOffice = request.TaxOffice?.Trim(),
            TaxNumber = request.TaxNumber?.Trim(),
            AuthorizedPerson = request.AuthorizedPerson?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            PaymentTerm = request.PaymentTerm?.Trim(),
            CreditLimit = request.CreditLimit
        };

        db.CurrentAccounts.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(entity);
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        if (entity.Status != CurrentAccountStatus.Draft)
            return BadRequest(new { message = "Sadece taslak cari kart onaya gönderilebilir." });

        entity.Status = CurrentAccountStatus.PendingApproval;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Cari kart onaya gönderildi.", entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        if (entity.Status != CurrentAccountStatus.PendingApproval)
            return BadRequest(new { message = "Cari kart onay bekleyen durumda değil." });

        entity.Status = CurrentAccountStatus.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Cari kart onaylandı.", entity.Id, entity.Status });
    }
}
