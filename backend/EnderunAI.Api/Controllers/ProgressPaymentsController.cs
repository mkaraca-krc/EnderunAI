using EnderunAI.Api.Contracts.ProgressPayments;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/progress-payments")]
public sealed class ProgressPaymentsController(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] ProgressPaymentStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.ProgressPayments
            .AsNoTracking()
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var result = await query
            .OrderByDescending(x => x.ProgressPaymentDate)
            .ThenByDescending(x => x.PeriodNumber)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.ProgressPaymentNumber,
                x.PeriodNumber,
                x.ProgressPaymentDate,
                x.Status,
                x.CurrencyCode,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.PriceDifferenceAmount,
                x.VatAmount,
                x.WithholdingAmount,
                x.TotalDeductionAmount,
                x.NetPayableAmount,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await BuildDetail(id, cancellationToken);

        return result is null
            ? NotFound(new { message = "Hakediş bulunamadı." })
            : Ok(result);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Create(
        CreateProgressPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty ||
            request.ProjectId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "Şirket ve proje seçilmelidir."
            });
        }

        if (string.IsNullOrWhiteSpace(
                request.ProgressPaymentNumber))
        {
            return BadRequest(new
            {
                message = "Hakediş numarası zorunludur."
            });
        }

        if (request.PeriodNumber <= 0)
        {
            return BadRequest(new
            {
                message = "Dönem numarası sıfırdan büyük olmalıdır."
            });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new
            {
                message = "En az bir hakediş poz satırı girilmelidir."
            });
        }

        var project = await db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.ProjectId &&
                     x.CompanyId == request.CompanyId,
                cancellationToken);

        if (project is null)
        {
            return BadRequest(new
            {
                message = "Seçilen proje şirkete ait değil."
            });
        }

        var number =
            request.ProgressPaymentNumber.Trim().ToUpperInvariant();

        var duplicate = await db.ProgressPayments.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.ProgressPaymentNumber == number,
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu hakediş numarası daha önce kullanılmış."
            });
        }

        var duplicatePeriod =
            await db.ProgressPayments.AnyAsync(
                x => x.ProjectId == request.ProjectId &&
                     x.PeriodNumber == request.PeriodNumber,
                cancellationToken);

        if (duplicatePeriod)
        {
            return Conflict(new
            {
                message = "Bu proje için aynı dönem numarası zaten var."
            });
        }

        var previousPayments =
            await db.ProgressPayments
                .AsNoTracking()
                .Where(x =>
                    x.ProjectId == request.ProjectId &&
                    x.Status != ProgressPaymentStatus.Cancelled &&
                    x.PeriodNumber < request.PeriodNumber)
                .Include(x => x.Items)
                .ToListAsync(cancellationToken);

        var entity = new ProgressPayment
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            ProjectMeasurementId =
                request.ProjectMeasurementId,
            ProgressPaymentNumber = number,
            PeriodNumber = request.PeriodNumber,
            PeriodStartDate = request.PeriodStartDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            PeriodEndDate = request.PeriodEndDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            ProgressPaymentDate =
                request.ProgressPaymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Status = ProgressPaymentStatus.Draft,
            CurrencyCode = string.IsNullOrWhiteSpace(
                project.CurrencyCode)
                ? "TRY"
                : project.CurrencyCode,
            ContractAmount =
                project.ContractAmount ?? 0m,
            PriceDifferenceAmount =
                request.PriceDifferenceAmount,
            VatRate = request.VatRate,
            WithholdingNumerator =
                request.WithholdingNumerator,
            WithholdingDenominator =
                request.WithholdingDenominator,
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim()
        };

        ApplyLines(
            entity,
            request.Items,
            previousPayments.SelectMany(x => x.Items));

        ApplyDeductions(entity, request.Deductions);
        CalculateHeader(entity, previousPayments);

        db.ProgressPayments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            entity.Id,
            entity.ProgressPaymentNumber,
            entity.PeriodNumber,
            entity.Status,
            entity.CurrentAmount,
            entity.NetPayableAmount
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProgressPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProgressPayments
            .Include(x => x.Items)
            .Include(x => x.Deductions)
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        if (entity.Status != ProgressPaymentStatus.Draft)
        {
            return Conflict(new
            {
                message = "Sadece taslak hakediş düzenlenebilir."
            });
        }

        var previousPayments =
            await db.ProgressPayments
                .AsNoTracking()
                .Where(x =>
                    x.ProjectId == entity.ProjectId &&
                    x.Id != entity.Id &&
                    x.Status != ProgressPaymentStatus.Cancelled &&
                    x.PeriodNumber < entity.PeriodNumber)
                .Include(x => x.Items)
                .ToListAsync(cancellationToken);

        db.ProgressPaymentItems.RemoveRange(entity.Items);
        db.ProgressPaymentDeductions.RemoveRange(entity.Deductions);

        entity.Items.Clear();
        entity.Deductions.Clear();

        entity.PeriodStartDate = request.PeriodStartDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.PeriodEndDate = request.PeriodEndDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.ProgressPaymentDate =
            request.ProgressPaymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        entity.PriceDifferenceAmount =
            request.PriceDifferenceAmount;
        entity.VatRate = request.VatRate;
        entity.WithholdingNumerator =
            request.WithholdingNumerator;
        entity.WithholdingDenominator =
            request.WithholdingDenominator;
        entity.Description = request.Description?.Trim();
        entity.Notes = request.Notes?.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        ApplyLines(
            entity,
            request.Items,
            previousPayments.SelectMany(x => x.Items));

        ApplyDeductions(entity, request.Deductions);
        CalculateHeader(entity, previousPayments);

        await db.SaveChangesAsync(cancellationToken);

        var detail = await BuildDetail(id, cancellationToken);
        return Ok(detail);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisDelete)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProgressPayments
            .Include(x => x.Items)
            .Include(x => x.Deductions)
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        if (entity.Status != ProgressPaymentStatus.Draft)
        {
            return Conflict(new
            {
                message = "Sadece taslak hakediş silinebilir."
            });
        }

        db.ProgressPayments.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken) =>
        ChangeStatus(
            id,
            ProgressPaymentStatus.Draft,
            ProgressPaymentStatus.PendingApproval,
            "Hakediş onaya gönderildi.",
            cancellationToken);

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken) =>
        ChangeStatus(
            id,
            ProgressPaymentStatus.PendingApproval,
            ProgressPaymentStatus.Approved,
            "Hakediş onaylandı.",
            cancellationToken);

    /// <summary>
    /// Hakedişi kesinleştirir ve otomatik gelir fişini üretir:
    /// 120 Alıcılar + kesinti hesapları (borç), 600 Yurtiçi Satışlar +
    /// 391 Hesaplanan KDV (alacak). Fiş doğrudan Posted olarak düşer.
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public async Task<IActionResult> Post(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProgressPayments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        if (entity.Status != ProgressPaymentStatus.Approved)
        {
            return Conflict(new
            {
                message = $"Bu işlem mevcut durumda yapılamaz. Durum: {entity.Status}"
            });
        }

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var voucherId = await accountingIntegration
                .CreateProgressPaymentVoucherAsync(entity, cancellationToken);

            entity.Status = ProgressPaymentStatus.Posted;
            entity.PostedAtUtc = DateTime.UtcNow;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.AccountingVoucherId = voucherId;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { message = exception.Message });
        }

        return Ok(ActionResponse(
            entity,
            "Hakediş kesinleştirildi; gelir fişi otomatik oluşturuldu."));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelProgressPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProgressPayments
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        if (entity.Status == ProgressPaymentStatus.Cancelled)
        {
            return Conflict(new
            {
                message = "Hakediş zaten iptal edilmiş."
            });
        }

        if (entity.AccountingVoucherId is not null)
        {
            return Conflict(new
            {
                message =
                    "Kesinleşmiş ve muhasebeleştirilmiş hakediş iptal edilemez. " +
                    "Önce muhasebe fişini iptal edip düzeltme kaydı oluşturun."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new
            {
                message = "İptal gerekçesi zorunludur."
            });
        }

        entity.Status = ProgressPaymentStatus.Cancelled;
        entity.CancelledAtUtc = DateTime.UtcNow;
        entity.CancellationReason = request.Reason.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(ActionResponse(
            entity,
            "Hakediş iptal edildi."));
    }

    private async Task<IActionResult> ChangeStatus(
        Guid id,
        ProgressPaymentStatus expected,
        ProgressPaymentStatus target,
        string message,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProgressPayments
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        if (entity.Status != expected)
        {
            return Conflict(new
            {
                message =
                    $"Bu işlem mevcut durumda yapılamaz. Durum: {entity.Status}"
            });
        }

        entity.Status = target;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (target == ProgressPaymentStatus.PendingApproval)
            entity.SubmittedAtUtc = DateTime.UtcNow;

        if (target == ProgressPaymentStatus.Approved)
            entity.ApprovedAtUtc = DateTime.UtcNow;

        if (target == ProgressPaymentStatus.Posted)
            entity.PostedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(ActionResponse(entity, message));
    }

    private static object ActionResponse(
        ProgressPayment entity,
        string message) =>
        new
        {
            entity.Id,
            entity.ProgressPaymentNumber,
            entity.Status,
            message
        };

    private static void ApplyLines(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentItemRequest> requests,
        IEnumerable<ProgressPaymentItem> previousItems)
    {
        var previousByCode = previousItems
            .GroupBy(x => x.PositionCode)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.CurrentQuantity),
                StringComparer.OrdinalIgnoreCase);

        var lineNumber = 1;

        foreach (var request in requests)
        {
            var code = request.PositionCode?.Trim() ?? string.Empty;

            previousByCode.TryGetValue(
                code,
                out var previousQuantity);

            var currentQuantity =
                Math.Max(0m, request.CurrentQuantity);

            var contractQuantity =
                Math.Max(0m, request.ContractQuantity);

            var unitPrice =
                Math.Max(0m, request.UnitPrice);

            var cumulativeQuantity =
                previousQuantity + currentQuantity;

            var item = new ProgressPaymentItem
            {
                LineNumber = lineNumber++,
                EngineeringPositionId =
                    request.EngineeringPositionId,
                PositionCode = code,
                Description =
                    request.Description?.Trim() ?? string.Empty,
                Unit = request.Unit?.Trim() ?? string.Empty,
                ContractQuantity = contractQuantity,
                PreviousQuantity = previousQuantity,
                CurrentQuantity = currentQuantity,
                CumulativeQuantity = cumulativeQuantity,
                UnitPrice = unitPrice,
                PreviousAmount =
                    previousQuantity * unitPrice,
                CurrentAmount =
                    currentQuantity * unitPrice,
                CumulativeAmount =
                    cumulativeQuantity * unitPrice,
                CompletionRate =
                    contractQuantity > 0m
                        ? cumulativeQuantity /
                          contractQuantity * 100m
                        : 0m,
                MeasurementReference =
                    request.MeasurementReference?.Trim(),
                Notes = request.Notes?.Trim()
            };

            entity.Items.Add(item);
        }
    }

    private static void ApplyDeductions(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentDeductionRequest>? requests)
    {
        if (requests is null)
            return;

        var lineNumber = 1;

        foreach (var request in requests)
        {
            var isManual = request.ManualAmount.HasValue;

            var amount = isManual
                ? request.ManualAmount!.Value
                : request.BaseAmount *
                  request.Rate / 100m;

            entity.Deductions.Add(
                new ProgressPaymentDeduction
                {
                    LineNumber = lineNumber++,
                    DeductionType = request.DeductionType,
                    Description =
                        request.Description?.Trim() ??
                        "Kesinti",
                    Rate = request.Rate,
                    BaseAmount = request.BaseAmount,
                    Amount = Math.Max(0m, amount),
                    IsManualAmount = isManual,
                    AccountingAccountId = request.AccountingAccountId,
                    Notes = request.Notes?.Trim()
                });
        }
    }

    private static void CalculateHeader(
        ProgressPayment entity,
        IEnumerable<ProgressPayment> previousPayments)
    {
        entity.PreviousAmount =
            previousPayments.Sum(x => x.CurrentAmount);

        entity.CurrentAmount =
            entity.Items.Sum(x => x.CurrentAmount);

        entity.CumulativeAmount =
            entity.PreviousAmount +
            entity.CurrentAmount;

        var taxableAmount =
            entity.CurrentAmount +
            entity.PriceDifferenceAmount;

        entity.VatAmount =
            taxableAmount *
            entity.VatRate / 100m;

        entity.WithholdingAmount =
            entity.WithholdingDenominator > 0
                ? entity.VatAmount *
                  entity.WithholdingNumerator /
                  entity.WithholdingDenominator
                : 0m;

        entity.TotalDeductionAmount =
            entity.Deductions.Sum(x => x.Amount);

        entity.GrossPayableAmount =
            taxableAmount +
            entity.VatAmount;

        entity.NetPayableAmount =
            entity.GrossPayableAmount -
            entity.WithholdingAmount -
            entity.TotalDeductionAmount;
    }

    private async Task<object?> BuildDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.ProgressPaymentNumber,
                x.PeriodNumber,
                x.PeriodStartDate,
                x.PeriodEndDate,
                x.ProgressPaymentDate,
                x.Status,
                x.CurrencyCode,
                x.ContractAmount,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.PriceDifferenceAmount,
                x.VatRate,
                x.VatAmount,
                x.WithholdingNumerator,
                x.WithholdingDenominator,
                x.WithholdingAmount,
                x.TotalDeductionAmount,
                x.GrossPayableAmount,
                x.NetPayableAmount,
                x.Description,
                x.Notes,
                x.SubmittedAtUtc,
                x.ApprovedAtUtc,
                x.PostedAtUtc,
                x.AccountingVoucherId,
                AccountingVoucherNumber = x.AccountingVoucher != null
                    ? x.AccountingVoucher.VoucherNumber
                    : null,
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.EngineeringPositionId,
                        i.LineNumber,
                        i.PositionCode,
                        i.Description,
                        i.Unit,
                        i.ContractQuantity,
                        i.PreviousQuantity,
                        i.CurrentQuantity,
                        i.CumulativeQuantity,
                        i.UnitPrice,
                        i.PreviousAmount,
                        i.CurrentAmount,
                        i.CumulativeAmount,
                        i.CompletionRate,
                        i.MeasurementReference,
                        i.Notes
                    }),
                Deductions = x.Deductions
                    .OrderBy(d => d.LineNumber)
                    .Select(d => new
                    {
                        d.Id,
                        d.LineNumber,
                        d.DeductionType,
                        d.Description,
                        d.Rate,
                        d.BaseAmount,
                        d.Amount,
                        d.IsManualAmount,
                        d.Notes
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
