using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/procurement/approval-control")]
public sealed class ProcurementApprovalController : ControllerBase
{
    private readonly IProcurementApprovalService service;

    public ProcurementApprovalController(
        AppDbContext db,
        ICurrentDataScopeService dataScope,
        ICurrentUserService currentUser)
    {
        service = new ProcurementApprovalService(
            db,
            dataScope,
            currentUser,
            () => HttpContext);
    }

    // Bütçe ve bekleyen tutarları taşıyor; sınıftaki [Authorize] tek
    // başına "oturum açan herkes" demekti.
    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] Guid companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetDashboardAsync(
            companyId,
            projectId,
            cancellationToken));

    [HttpGet("orders/{purchaseOrderId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<IActionResult> GetOrderContext(
        Guid purchaseOrderId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetOrderContextAsync(
            purchaseOrderId,
            cancellationToken));

    [HttpPut("companies/{companyId:guid}/policy")]
    [RequirePermission(PermissionCatalog.Keys.SystemUsersManage)]
    public async Task<IActionResult> ConfigurePolicy(
        Guid companyId,
        ConfigureProcurementApprovalPolicyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ConfigurePolicyAsync(
            companyId,
            request,
            cancellationToken));

    // Bütçe açmak bir kontrol işlemi: onay yetkisi aranıyor.
    [HttpPost("projects/{projectId:guid}/budgets")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingApprove)]
    public async Task<IActionResult> CreateBudget(
        Guid projectId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CreateBudgetAsync(
            projectId,
            request,
            cancellationToken));

    [HttpPut("projects/{projectId:guid}/budgets/{budgetId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingApprove)]
    public async Task<IActionResult> UpdateBudget(
        Guid projectId,
        Guid budgetId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.UpdateBudgetAsync(
            projectId,
            budgetId,
            request,
            cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ProcurementNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ProcurementValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = exception.Message });
        }
    }
}
