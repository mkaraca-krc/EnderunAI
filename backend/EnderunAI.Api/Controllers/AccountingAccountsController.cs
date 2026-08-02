using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounting-accounts")]
public sealed class AccountingAccountsController(
    IAccountingAccountService service)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? parentAccountId,
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId,
            parentAccountId,
            isActive,
            search,
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetByIdAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Id },
                result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateAsync(
                id,
                request,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
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

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCatalog.Keys.AccountingDelete)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeactivateAsync(id, cancellationToken);
            return Ok(new { message = "Muhasebe hesabı pasife alındı." });
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
