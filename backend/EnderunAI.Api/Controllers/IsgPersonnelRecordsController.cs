using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Personel bazlı İSG kayıtları: OSGB'nin verdiği sağlık raporu,
/// eğitim ve yetki belgeleri.
///
/// Sağlık raporunun tıbbi detayı servis katmanında maskelenir; buraya
/// yetkisiz kullanıcı için zaten null gelir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/isg")]
public sealed class IsgPersonnelRecordsController(
    IIsgPersonnelRecordService service) : ControllerBase
{
    [HttpGet("personel")]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> GetPersonnelSummary(
        [FromQuery] Guid? companyId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetPersonnelSummaryAsync(
            companyId, search, cancellationToken));
    }

    [HttpGet("personel/{personnelId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> GetCard(
        Guid personnelId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetCardAsync(personnelId, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Kullanıcının kendi İSG kartı.
    ///
    /// İzin aranmaz: uç, personel kimliğini istekten değil oturumdan
    /// alır ve yalnızca çağıranın kendi kaydını döndürebilir. Ayrı bir
    /// izin koymak, kişinin kendi belgesine erişimini gereksiz yere
    /// yönetici kararına bağlardı.
    /// </summary>
    [HttpGet("benim")]
    public async Task<IActionResult> GetOwnCard(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetOwnCardAsync(cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    // --- Sağlık raporu ---

    [HttpPost("saglik-raporlari")]
    [RequirePermission(PermissionCatalog.Keys.IsgCreate)]
    public async Task<IActionResult> CreateHealthReport(
        CreateIsgHealthReportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateHealthReportAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("saglik-raporlari/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgEdit)]
    public async Task<IActionResult> UpdateHealthReport(
        Guid id, UpdateIsgHealthReportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateHealthReportAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpDelete("saglik-raporlari/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgDelete)]
    public async Task<IActionResult> DeleteHealthReport(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteHealthReportAsync(id, cancellationToken);
            return Ok(new { message = "Sağlık raporu silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    // --- Eğitim ---

    [HttpPost("egitimler")]
    [RequirePermission(PermissionCatalog.Keys.IsgCreate)]
    public async Task<IActionResult> CreateTraining(
        CreateIsgTrainingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateTrainingAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("egitimler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgEdit)]
    public async Task<IActionResult> UpdateTraining(
        Guid id, UpdateIsgTrainingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateTrainingAsync(id, request, cancellationToken));
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

    [HttpDelete("egitimler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgDelete)]
    public async Task<IActionResult> DeleteTraining(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteTrainingAsync(id, cancellationToken);
            return Ok(new { message = "Eğitim kaydı silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    // --- Sertifika ---

    [HttpPost("sertifikalar")]
    [RequirePermission(PermissionCatalog.Keys.IsgCreate)]
    public async Task<IActionResult> CreateCertificate(
        CreateIsgCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateCertificateAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("sertifikalar/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgEdit)]
    public async Task<IActionResult> UpdateCertificate(
        Guid id, UpdateIsgCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateCertificateAsync(id, request, cancellationToken));
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

    [HttpDelete("sertifikalar/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgDelete)]
    public async Task<IActionResult> DeleteCertificate(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteCertificateAsync(id, cancellationToken);
            return Ok(new { message = "Belge silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
