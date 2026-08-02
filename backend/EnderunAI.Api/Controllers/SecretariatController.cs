using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts.Secretariat;
using EnderunAI.Api.Models.Secretariat;
using EnderunAI.Api.Services.Secretariat;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/secretariat")]
public sealed class SecretariatController(
    ISecretariatService service,
    IUploadService uploadService) : ControllerBase
{
    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetDashboardAsync(companyId, cancellationToken));

    [HttpGet("correspondence")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetCorrespondence(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] SecretariatDocumentDirection? direction,
        [FromQuery] SecretariatDocumentStatus? status,
        [FromQuery] string? search,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken) =>
        Ok(await service.GetCorrespondenceAsync(
            companyId, projectId, direction, status, search, startDate, endDate, cancellationToken));

    [HttpGet("correspondence/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetCorrespondenceById(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        CancellationToken cancellationToken)
    {
        var item = await service.GetCorrespondenceAsync(direction, id, cancellationToken);
        return item is null
            ? NotFound(new { message = "Evrak bulunamadı." })
            : Ok(item);
    }

    [HttpPost("correspondence")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsCreate)]
    public Task<IActionResult> CreateCorrespondence(
        CreateCorrespondenceRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.CreateCorrespondenceAsync(
                request, CurrentUserId(), CurrentUserName(), cancellationToken);
            return CreatedAtAction(
                nameof(GetCorrespondenceById),
                new { id = item.Document.Id, direction = item.Document.Direction },
                item.Document);
        });

    [HttpPut("correspondence/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsEdit)]
    public Task<IActionResult> UpdateCorrespondence(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        UpdateCorrespondenceRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdateCorrespondenceAsync(
                direction, id, request, CurrentUserId(), CurrentUserName(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Evrak bulunamadı." })
                : Ok(item);
        });

    [HttpDelete("correspondence/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsDelete)]
    public async Task<IActionResult> DeleteCorrespondence(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteCorrespondenceAsync(
            direction, id, CurrentUserId(), cancellationToken);
        return deleted
            ? Ok(new { message = "Evrak kaydı silindi." })
            : NotFound(new { message = "Evrak bulunamadı." });
    }

    [HttpPost("correspondence/{id:guid}/workflow")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsEdit)]
    public async Task<IActionResult> AddWorkflow(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        DocumentWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await service.AddWorkflowAsync(
            direction, id, request, CurrentUserId(), CurrentUserName(), cancellationToken);
        return updated
            ? Ok(await service.GetCorrespondenceAsync(direction, id, cancellationToken))
            : NotFound(new { message = "Evrak bulunamadı." });
    }

    [HttpPost("correspondence/{id:guid}/archive")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsEdit)]
    public async Task<IActionResult> ArchiveCorrespondence(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        CancellationToken cancellationToken)
    {
        var updated = await service.ArchiveCorrespondenceAsync(
            direction, id, CurrentUserId(), CurrentUserName(), cancellationToken);
        return updated
            ? Ok(await service.GetCorrespondenceAsync(direction, id, cancellationToken))
            : NotFound(new { message = "Evrak bulunamadı." });
    }

    [HttpPost("correspondence/{id:guid}/attachments")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsCreate)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public Task<IActionResult> AddAttachment(
        Guid id,
        [FromQuery] SecretariatDocumentDirection direction,
        [FromForm] IFormFile file,
        [FromForm] string? description,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var category = AttachmentCategory(direction, id);
            var uploaded = await uploadService.SaveAsync(file, category, cancellationToken);
            var item = await service.AddAttachmentAsync(
                direction,
                id,
                uploaded.OriginalName,
                uploaded.StoredName,
                category,
                uploaded.ContentType,
                uploaded.Size,
                description,
                CurrentUserId(),
                cancellationToken);
            if (item is null)
            {
                uploadService.DeleteFile(category, uploaded.StoredName);
                return NotFound(new { message = "Evrak bulunamadı." });
            }
            return Ok(item);
        });

    [HttpGet("attachments/{attachmentId:guid}/download")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> DownloadAttachment(
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var item = await service.GetAttachmentAsync(attachmentId, cancellationToken);
        if (item is null) return NotFound(new { message = "Ek dosya bulunamadı." });
        var file = uploadService.GetFile(item.FilePath, item.StoredFileName);
        return file is null
            ? NotFound(new { message = "Ek dosyanın fiziksel kopyası bulunamadı." })
            : PhysicalFile(file.FullPath, file.ContentType, item.FileName);
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsDelete)]
    public async Task<IActionResult> DeleteAttachment(
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var item = await service.GetAttachmentAsync(attachmentId, cancellationToken);
        if (item is null) return NotFound(new { message = "Ek dosya bulunamadı." });
        var deleted = await service.DeleteAttachmentAsync(
            attachmentId, CurrentUserId(), cancellationToken);
        if (deleted) uploadService.DeleteFile(item.FilePath, item.StoredFileName);
        return Ok(new { message = "Ek dosya silindi." });
    }

    [HttpGet("categories")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetCategoriesAsync(companyId, cancellationToken));

    [HttpPost("categories")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsCreate)]
    public Task<IActionResult> CreateCategory(
        CreateDocumentCategoryRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateCategoryAsync(request, CurrentUserId(), cancellationToken)));

    [HttpPut("categories/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsEdit)]
    public Task<IActionResult> UpdateCategory(
        Guid id,
        UpdateDocumentCategoryRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdateCategoryAsync(
                id, request, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Kategori bulunamadı." })
                : Ok(item);
        });

    [HttpGet("cargo")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public async Task<IActionResult> GetCargo(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] CargoDirection? direction,
        [FromQuery] CargoStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await service.GetCargoAsync(
            companyId, projectId, direction, status, search, cancellationToken));

    [HttpGet("cargo/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public async Task<IActionResult> GetCargoById(Guid id, CancellationToken cancellationToken)
    {
        var item = await service.GetCargoAsync(id, cancellationToken);
        return item is null
            ? NotFound(new { message = "Kargo kaydı bulunamadı." })
            : Ok(item);
    }

    [HttpPost("cargo")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CreateCargo(
        CreateCargoRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateCargoAsync(request, CurrentUserId(), cancellationToken)));

    [HttpPut("cargo/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdateCargo(
        Guid id,
        UpdateCargoRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdateCargoAsync(id, request, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Kargo kaydı bulunamadı." })
                : Ok(item);
        });

    [HttpDelete("cargo/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public async Task<IActionResult> DeleteCargo(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteCargoAsync(id, CurrentUserId(), cancellationToken);
        return deleted
            ? Ok(new { message = "Kargo kaydı silindi." })
            : NotFound(new { message = "Kargo kaydı bulunamadı." });
    }

    [HttpGet("visitors")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public async Task<IActionResult> GetVisitors(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] VisitorStatus? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await service.GetVisitorsAsync(
            companyId, projectId, status, startDate, endDate, search, cancellationToken));

    [HttpPost("visitors")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CreateVisitor(
        CreateVisitorRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateVisitorAsync(request, CurrentUserId(), cancellationToken)));

    [HttpPost("visitors/{id:guid}/check-in")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CheckInVisitor(
        Guid id,
        VisitorCheckInRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.CheckInVisitorAsync(
                id, request.ReceivedByName, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Ziyaretçi kaydı bulunamadı." })
                : Ok(item);
        });

    [HttpPost("visitors/{id:guid}/check-out")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CheckOutVisitor(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.CheckOutVisitorAsync(id, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Ziyaretçi kaydı bulunamadı." })
                : Ok(item);
        });

    [HttpDelete("visitors/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public async Task<IActionResult> DeleteVisitor(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteVisitorAsync(id, CurrentUserId(), cancellationToken);
        return deleted
            ? Ok(new { message = "Ziyaretçi kaydı silindi." })
            : NotFound(new { message = "Ziyaretçi kaydı bulunamadı." });
    }

    [HttpGet("phone-notes")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public async Task<IActionResult> GetPhoneNotes(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] PhoneNoteStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await service.GetPhoneNotesAsync(
            companyId, projectId, status, search, cancellationToken));

    [HttpPost("phone-notes")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CreatePhoneNote(
        CreatePhoneNoteRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreatePhoneNoteAsync(request, CurrentUserId(), cancellationToken)));

    [HttpPut("phone-notes/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdatePhoneNote(
        Guid id,
        UpdatePhoneNoteRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdatePhoneNoteAsync(
                id, request, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Telefon notu bulunamadı." })
                : Ok(item);
        });

    [HttpPost("phone-notes/{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdatePhoneNoteStatus(
        Guid id,
        UpdatePhoneNoteStatusRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdatePhoneNoteStatusAsync(
                id, request.Status, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Telefon notu bulunamadı." })
                : Ok(item);
        });

    [HttpDelete("phone-notes/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public async Task<IActionResult> DeletePhoneNote(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await service.DeletePhoneNoteAsync(id, CurrentUserId(), cancellationToken);
        return deleted
            ? Ok(new { message = "Telefon notu silindi." })
            : NotFound(new { message = "Telefon notu bulunamadı." });
    }

    [HttpGet("meetings")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public Task<IActionResult> GetMeetings(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] SecretariatScheduleStatus? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        GetSchedules(SecretariatScheduleType.Meeting, companyId, projectId, status, startDate, endDate, search, cancellationToken);

    [HttpPost("meetings")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CreateMeeting(
        CreateScheduleRequest request,
        CancellationToken cancellationToken) =>
        CreateSchedule(SecretariatScheduleType.Meeting, request, cancellationToken);

    [HttpPut("meetings/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdateMeeting(
        Guid id,
        UpdateScheduleRequest request,
        CancellationToken cancellationToken) =>
        UpdateSchedule(SecretariatScheduleType.Meeting, id, request, cancellationToken);

    [HttpPost("meetings/{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdateMeetingStatus(
        Guid id,
        UpdateScheduleStatusRequest request,
        CancellationToken cancellationToken) =>
        UpdateScheduleStatus(SecretariatScheduleType.Meeting, id, request.Status, cancellationToken);

    [HttpDelete("meetings/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> DeleteMeeting(Guid id, CancellationToken cancellationToken) =>
        DeleteSchedule(SecretariatScheduleType.Meeting, id, cancellationToken);

    [HttpGet("appointments")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatView)]
    public Task<IActionResult> GetAppointments(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] SecretariatScheduleStatus? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        GetSchedules(SecretariatScheduleType.Appointment, companyId, projectId, status, startDate, endDate, search, cancellationToken);

    [HttpPost("appointments")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> CreateAppointment(
        CreateScheduleRequest request,
        CancellationToken cancellationToken) =>
        CreateSchedule(SecretariatScheduleType.Appointment, request, cancellationToken);

    [HttpPut("appointments/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdateAppointment(
        Guid id,
        UpdateScheduleRequest request,
        CancellationToken cancellationToken) =>
        UpdateSchedule(SecretariatScheduleType.Appointment, id, request, cancellationToken);

    [HttpPost("appointments/{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> UpdateAppointmentStatus(
        Guid id,
        UpdateScheduleStatusRequest request,
        CancellationToken cancellationToken) =>
        UpdateScheduleStatus(SecretariatScheduleType.Appointment, id, request.Status, cancellationToken);

    [HttpDelete("appointments/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SecretariatManage)]
    public Task<IActionResult> DeleteAppointment(Guid id, CancellationToken cancellationToken) =>
        DeleteSchedule(SecretariatScheduleType.Appointment, id, cancellationToken);

    private async Task<IActionResult> GetSchedules(
        SecretariatScheduleType type,
        Guid? companyId,
        Guid? projectId,
        SecretariatScheduleStatus? status,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken) =>
        Ok(await service.GetSchedulesAsync(
            type, companyId, projectId, status, startDate, endDate, search, cancellationToken));

    private Task<IActionResult> CreateSchedule(
        SecretariatScheduleType type,
        CreateScheduleRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateScheduleAsync(
                type, request, CurrentUserId(), cancellationToken)));

    private Task<IActionResult> UpdateSchedule(
        SecretariatScheduleType type,
        Guid id,
        UpdateScheduleRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdateScheduleAsync(
                type, id, request, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Takvim kaydı bulunamadı." })
                : Ok(item);
        });

    private Task<IActionResult> UpdateScheduleStatus(
        SecretariatScheduleType type,
        Guid id,
        SecretariatScheduleStatus status,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var item = await service.UpdateScheduleStatusAsync(
                type, id, status, CurrentUserId(), cancellationToken);
            return item is null
                ? NotFound(new { message = "Takvim kaydı bulunamadı." })
                : Ok(item);
        });

    private async Task<IActionResult> DeleteSchedule(
        SecretariatScheduleType type,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteScheduleAsync(
            type, id, CurrentUserId(), cancellationToken);
        return deleted
            ? Ok(new { message = "Takvim kaydı silindi." })
            : NotFound(new { message = "Takvim kaydı bulunamadı." });
    }

    private Guid? CurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private string? CurrentUserName() =>
        User.FindFirstValue(ClaimTypes.Name) ??
        User.FindFirstValue(JwtRegisteredClaimNames.Name) ??
        User.Identity?.Name;

    private static string AttachmentCategory(
        SecretariatDocumentDirection direction,
        Guid documentId) =>
        $"secretariat-{(int)direction}-{documentId:N}";

    private static async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException exception)
        {
            return new BadRequestObjectResult(new { message = exception.Message });
        }
        catch (DbUpdateException)
        {
            return new ConflictObjectResult(new
            {
                message = "Kayıt veritabanı kısıtları nedeniyle tamamlanamadı."
            });
        }
    }
}
