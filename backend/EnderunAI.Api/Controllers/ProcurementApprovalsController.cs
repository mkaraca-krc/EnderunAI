using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/procurement-approvals")]
[Authorize]
public sealed class ProcurementApprovalsController(
    ProcurementApprovalDbContext db,
    IProcurementApprovalService service) : ControllerBase
{
    public sealed record RuleStepInput(int SequenceNo, string RoleName, bool IsRequired = true);
    public sealed record CreateRuleRequest(
        Guid CompanyId,
        ProcurementApprovalDocumentType DocumentType,
        string Name,
        decimal MinimumAmount,
        decimal? MaximumAmount,
        string CurrencyCode,
        ApprovalFlowMode FlowMode,
        int Priority,
        IReadOnlyList<RuleStepInput> Steps);
    public sealed record ActionRequest(ApprovalActionType Action, string? Comment);

    [HttpGet("rules")]
    public async Task<ActionResult> ListRules([FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        var result = await db.Rules
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.DocumentType)
            .ThenBy(x => x.MinimumAmount)
            .ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("rules")]
    public async Task<ActionResult> CreateRule(CreateRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Kural adı zorunludur.");
        if (request.MinimumAmount < 0 || request.MaximumAmount < request.MinimumAmount)
            return BadRequest("Tutar aralığı geçersizdir.");
        if (request.Steps.Count == 0)
            return BadRequest("En az bir onay adımı tanımlanmalıdır.");
        if (request.Steps.Any(x => x.SequenceNo <= 0 || string.IsNullOrWhiteSpace(x.RoleName)))
            return BadRequest("Onay adımlarındaki sıra ve rol bilgisi geçerli olmalıdır.");
        if (request.Steps.GroupBy(x => new { x.SequenceNo, Role = x.RoleName.Trim().ToUpperInvariant() }).Any(x => x.Count() > 1))
            return BadRequest("Aynı sıra ve rol birden fazla tanımlanamaz.");

        var entity = new ProcurementApprovalRule
        {
            CompanyId = request.CompanyId,
            DocumentType = request.DocumentType,
            Name = request.Name.Trim(),
            MinimumAmount = request.MinimumAmount,
            MaximumAmount = request.MaximumAmount,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "TRY" : request.CurrencyCode.Trim().ToUpperInvariant(),
            FlowMode = request.FlowMode,
            Priority = request.Priority,
            IsActive = true,
            Steps = request.Steps.Select(x => new ProcurementApprovalRuleStep
            {
                SequenceNo = x.SequenceNo,
                RoleName = x.RoleName.Trim(),
                IsRequired = x.IsRequired
            }).ToList()
        };

        db.Rules.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListRules), new { companyId = entity.CompanyId }, entity);
    }

    [HttpPost("purchase-orders/{orderId:guid}/submit")]
    public async Task<ActionResult> SubmitPurchaseOrder(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SubmitPurchaseOrderAsync(orderId, BuildActor(), cancellationToken);
            return Ok(new { result.Id, result.DocumentId, result.Status, result.Amount, result.CurrencyCode, result.Steps });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("instances")]
    public async Task<ActionResult> ListInstances(
        [FromQuery] Guid? companyId,
        [FromQuery] ApprovalInstanceStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Instances.AsNoTracking().Include(x => x.Steps).AsQueryable();
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var result = await query.OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("instances/{id:guid}")]
    public async Task<ActionResult> GetInstance(Guid id, CancellationToken cancellationToken)
    {
        var result = await db.Instances
            .AsNoTracking()
            .Include(x => x.Steps)
            .Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("instances/{instanceId:guid}/steps/{stepId:guid}/action")]
    public async Task<ActionResult> Act(
        Guid instanceId,
        Guid stepId,
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ActAsync(instanceId, stepId, request.Action, request.Comment, BuildActor(), cancellationToken);
            return Ok(new { result.Id, result.Status, result.CompletedAtUtc, result.Steps });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("my-pending")]
    public async Task<ActionResult> MyPending(CancellationToken cancellationToken)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        var result = await db.InstanceSteps
            .AsNoTracking()
            .Include(x => x.Instance)
            .Where(x => x.Status == ApprovalStepStatus.Pending && roles.Contains(x.RoleName))
            .OrderBy(x => x.Instance.SubmittedAtUtc)
            .Select(x => new
            {
                StepId = x.Id,
                InstanceId = x.InstanceId,
                x.RoleName,
                x.SequenceNo,
                x.Instance.DocumentType,
                x.Instance.DocumentId,
                x.Instance.DocumentNumber,
                x.Instance.Amount,
                x.Instance.CurrencyCode,
                x.Instance.SubmittedAtUtc
            })
            .ToListAsync(cancellationToken);
        return Ok(result);
    }

    private ApprovalActor BuildActor()
    {
        Guid? userId = null;
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out var parsed))
            userId = parsed;

        var name = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Bilinmeyen kullanıcı";
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        return new ApprovalActor(userId, name, roles, ip);
    }
}
