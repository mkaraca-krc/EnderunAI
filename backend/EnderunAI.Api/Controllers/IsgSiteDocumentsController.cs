using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Şantiye İSG belgeleri: risk değerlendirmesi, acil durum planı, kurul
/// tutanağı, saha denetim formu, KKD zimmet formu — geçerlilik takipli.
/// </summary>
[ApiController]
[Authorize]
[Route("api/isg/saha-belgeleri")]
public sealed class IsgSiteDocumentsController(
    IIsgSiteDocumentService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? projectSiteId,
        [FromQuery] int? documentType,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, projectId, projectSiteId, documentType, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.IsgCreate)]
    [RequestSizeLimit(60L * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid companyId,
        [FromForm] Guid projectId,
        [FromForm] Guid? projectSiteId,
        [FromForm] int documentType,
        [FromForm] string title,
        [FromForm] DateOnly issueDate,
        [FromForm] DateOnly? validUntil,
        [FromForm] string? notes,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Belge dosyası seçilmedi." });

        try
        {
            return Ok(await service.UploadAsync(
                companyId, projectId, projectSiteId, documentType, title,
                issueDate, validUntil, notes, file, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            // Dosya tipi/boyutu reddi IUploadService'ten gelir.
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgEdit)]
    public async Task<IActionResult> Update(
        Guid id, UpdateIsgSiteDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("{id:guid}/dosya")]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var file = await service.GetFileAsync(id, cancellationToken);
            return PhysicalFile(file.FullPath, file.ContentType, file.StoredName);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            return Ok(new { message = "Belge silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
