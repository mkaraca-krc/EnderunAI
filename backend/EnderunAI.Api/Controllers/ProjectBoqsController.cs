using EnderunAI.Api.Services.ProjectBoqs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/project-boqs")]
public sealed class ProjectBoqsController(
    IProjectBoqService projectBoqService)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid companyId,
        [FromQuery] Guid projectId,
        [FromQuery] int? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || projectId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "Şirket ve proje kimlikleri zorunludur."
            });
        }

        if (status is < 0)
        {
            return BadRequest(new
            {
                message = "Keşif durumu sıfırdan küçük olamaz."
            });
        }

        var items = await projectBoqService.GetAllAsync(
            companyId,
            projectId,
            status,
            search,
            cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] Guid companyId,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty ||
            companyId == Guid.Empty ||
            projectId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "Geçerli keşif, şirket ve proje kimlikleri girilmelidir."
            });
        }

        var item = await projectBoqService.GetByIdAsync(
            id,
            companyId,
            projectId,
            cancellationToken);

        return item is null
            ? NotFound(new { message = "Keşif kaydı bulunamadı." })
            : Ok(item);
    }
}
