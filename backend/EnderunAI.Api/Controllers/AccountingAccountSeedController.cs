using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounting-account-seed")]
public sealed class AccountingAccountSeedController(
    IAccountingAccountSeedService service)
    : ControllerBase
{
    [HttpPost("{companyId:guid}")]
    public async Task<IActionResult> Seed(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.SeedAsync(
                companyId,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (FileNotFoundException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
