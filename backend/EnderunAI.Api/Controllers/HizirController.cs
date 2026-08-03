using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Hızır asistanı. Kullanıcının kendi izinleriyle sınırlı canlı veri
/// erişimi ve sistemin kullanım kılavuzu.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hizir")]
public sealed class HizirController(
    IHizirChatService service,
    IHizirPendingActionStore pendingActions) : ControllerBase
{
    /// <summary>Kullanıcının onay bekleyen eylemleri.</summary>
    [HttpGet("actions/pending")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> PendingActions(CancellationToken cancellationToken) =>
        Ok(await pendingActions.GetPendingAsync(cancellationToken));

    /// <summary>
    /// Bekleyen eylemi onaylar ve YÜRÜTÜR. Eylemin gerçekten çalıştığı
    /// tek yer burasıdır ve yalnızca kullanıcının kendi oturumuyla
    /// çağrılabilir — dil modeline bu uca giden bir araç tanıtılmaz.
    /// </summary>
    [HttpPost("actions/{id:guid}/confirm")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> ConfirmAction(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await pendingActions.ConfirmAsync(id, cancellationToken));
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

    [HttpPost("actions/{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> CancelAction(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await pendingActions.CancelAsync(id, cancellationToken));
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

    /// <summary>Asistanın yapılandırılıp yapılandırılmadığı — arayüz buna göre uyarır.</summary>
    [HttpGet("status")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public IActionResult Status() =>
        Ok(new
        {
            isConfigured = service.IsConfigured,
            message = service.IsConfigured
                ? null
                : "Hızır henüz yapılandırılmadı. Sistem yöneticisinin yapay " +
                  "zekâ anahtarını sunucu ayarlarına eklemesi gerekiyor."
        });

    [HttpPost("chat")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> Chat(
        HizirChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.AskAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            // Yapılandırma eksikliği ve servis erişilemezliği buraya düşer;
            // kullanıcı sebebini görmeli.
            return StatusCode(503, new { message = exception.Message });
        }
    }

    [HttpGet("conversations")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken) =>
        Ok(await service.GetConversationsAsync(cancellationToken));

    [HttpGet("conversations/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AiUse)]
    public async Task<IActionResult> Messages(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetMessagesAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
