using EnderunAI.Api.Contracts.ProgressPayments;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/progress-payments")]
public sealed class ProgressPaymentsController(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration,
    IChequeService chequeService)
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

    /// <summary>
    /// Projenin açık ihzarat kalemleri — sonraki hakedişte mahsup
    /// edilebilecek olanlar. Her kalem için imalata dönen tutara göre
    /// önerilen mahsup da hesaplanır.
    /// </summary>
    [HttpGet("open-advance-materials")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> OpenAdvanceMaterials(
        [FromQuery] Guid projectId,
        [FromQuery] Guid? excludeProgressPaymentId,
        CancellationToken cancellationToken)
    {
        var query = db.ProgressPaymentAdvanceMaterials
            .AsNoTracking()
            .Where(x => x.ProgressPayment.ProjectId == projectId &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled &&
                        x.Amount > x.OffsetAmount);

        if (excludeProgressPaymentId is Guid excluded)
            query = query.Where(x => x.ProgressPaymentId != excluded);

        return Ok(await query
            .OrderBy(x => x.PositionCode)
            .Select(x => new
            {
                x.Id,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.Quantity,
                x.UnitPrice,
                x.ValuationRate,
                x.Amount,
                x.OffsetAmount,
                OpenAmount = x.Amount - x.OffsetAmount,
                SourceProgressPaymentNumber = x.ProgressPayment.ProgressPaymentNumber,
                SourcePeriodNumber = x.ProgressPayment.PeriodNumber
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Hazırlanan hakedişin önceki dönem bağlamı: poz bazında önceki
    /// miktarlar, kesinti türü bazında önceden kesilen tutarlar ve
    /// minha edilecek toplam.
    ///
    /// Tek çağrıda döner; arayüzün her önceki hakedişin detayını ayrı
    /// ayrı çekmesi gerekmesin diye.
    /// </summary>
    [HttpGet("previous-context")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> PreviousContext(
        [FromQuery] Guid projectId,
        [FromQuery] int periodNumber,
        [FromQuery] Guid? excludeProgressPaymentId,
        CancellationToken cancellationToken)
    {
        var payments = db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status != ProgressPaymentStatus.Cancelled &&
                        x.PeriodNumber < periodNumber);

        if (excludeProgressPaymentId is Guid excluded)
            payments = payments.Where(x => x.Id != excluded);

        var previousTotal = await payments
            .SumAsync(x => (decimal?)x.CurrentAmount, cancellationToken) ?? 0m;

        var quantities = await payments
            .SelectMany(x => x.Items)
            .GroupBy(x => x.PositionCode)
            .Select(g => new
            {
                PositionCode = g.Key,
                Quantity = g.Sum(x => x.CurrentQuantity)
            })
            .ToListAsync(cancellationToken);

        var deductions = await payments
            .SelectMany(x => x.Deductions)
            .GroupBy(x => x.DeductionType)
            .Select(g => new
            {
                DeductionType = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            previousTotalAmount = previousTotal,
            previousQuantities = quantities,
            previousDeductions = deductions
        });
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
            IncomeTaxWithholdingRate = request.IncomeTaxWithholdingRate,
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim()
        };

        var sectionMap = await BuildSectionsAsync(
            entity, request.ProjectId, request.Items, cancellationToken);

        ApplyLines(
            entity,
            request.Items,
            previousPayments.SelectMany(x => x.Items),
            sectionMap);

        ApplyAdvanceMaterials(entity, request.AdvanceMaterials);

        var offsetError = await ApplyAdvanceOffsetsAsync(
            entity, request.AdvanceOffsets, cancellationToken);

        if (offsetError is not null)
            return Conflict(new { message = offsetError });

        entity.CumulativeAdvanceMaterialAmount =
            await CalculateOpenAdvanceAmountAsync(entity, cancellationToken);

        var previousDeductions = await LoadPreviousDeductionsAsync(
            request.ProjectId, null, request.PeriodNumber, cancellationToken);

        // Kesintilerin varsayılan tabanı hakedişin kümülatif toplamı;
        // üst hesaptan önce bilinmesi gerektiği için burada kurulur.
        var cumulativeBase = entity.Items.Sum(x => x.CumulativeAmount)
            + entity.CumulativeAdvanceMaterialAmount;

        ApplyDeductions(entity, request.Deductions, previousDeductions, cumulativeBase);
        CalculateHeader(entity, previousPayments);

        // Ödeme dağılımı net tutar hesaplandıktan SONRA kurulur.
        var planError = ApplyPaymentPlans(entity, request.PaymentPlans);
        if (planError is not null)
            return BadRequest(new { message = planError });

        db.ProgressPayments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await SyncBarterLedgerAsync(entity, cancellationToken);

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
            .Include(x => x.Sections)
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

        // Bu hakedişin açtığı ihzarat kalemine başka bir hakediş mahsup
        // yapmışsa satırlar silinemez; silinseydi o mahsubun dayanağı
        // ortadan kalkar ve bakiye takibi kopardı.
        var lockedByOthers = await db.ProgressPaymentAdvanceMaterialOffsets
            .AnyAsync(x => x.AdvanceMaterial.ProgressPaymentId == entity.Id &&
                           x.ProgressPaymentId != entity.Id,
                cancellationToken);

        if (lockedByOthers)
        {
            return Conflict(new
            {
                message = "Bu hakedişin ihzarat kalemlerine sonraki bir hakedişte " +
                          "mahsup yapılmış; düzenlenemez. Önce o mahsubu kaldırın."
            });
        }

        var ownAdvanceMaterials = await db.ProgressPaymentAdvanceMaterials
            .Where(x => x.ProgressPaymentId == entity.Id)
            .ToListAsync(cancellationToken);

        var ownOffsets = await db.ProgressPaymentAdvanceMaterialOffsets
            .Include(x => x.AdvanceMaterial)
            .Where(x => x.ProgressPaymentId == entity.Id)
            .ToListAsync(cancellationToken);

        // Kaldırılan mahsuplar, mahsup edildikleri kalemin bakiyesine
        // geri döner.
        foreach (var offset in ownOffsets)
        {
            offset.AdvanceMaterial.OffsetAmount -= offset.Amount;
            offset.AdvanceMaterial.UpdatedAtUtc = DateTime.UtcNow;
        }

        db.ProgressPaymentAdvanceMaterialOffsets.RemoveRange(ownOffsets);
        db.ProgressPaymentAdvanceMaterials.RemoveRange(ownAdvanceMaterials);

        db.ProgressPaymentItems.RemoveRange(entity.Items);
        db.ProgressPaymentDeductions.RemoveRange(entity.Deductions);
        db.ProgressPaymentSections.RemoveRange(entity.Sections);

        entity.Items.Clear();
        entity.Deductions.Clear();
        entity.Sections.Clear();
        entity.AdvanceMaterials.Clear();
        entity.AdvanceMaterialOffsets.Clear();

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
        entity.IncomeTaxWithholdingRate = request.IncomeTaxWithholdingRate;
        entity.Description = request.Description?.Trim();
        entity.Notes = request.Notes?.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        var sectionMap = await BuildSectionsAsync(
            entity, entity.ProjectId, request.Items, cancellationToken);

        ApplyLines(
            entity,
            request.Items,
            previousPayments.SelectMany(x => x.Items),
            sectionMap);

        ApplyAdvanceMaterials(entity, request.AdvanceMaterials);

        var offsetError = await ApplyAdvanceOffsetsAsync(
            entity, request.AdvanceOffsets, cancellationToken);

        if (offsetError is not null)
            return Conflict(new { message = offsetError });

        entity.CumulativeAdvanceMaterialAmount =
            await CalculateOpenAdvanceAmountAsync(entity, cancellationToken);

        var previousDeductions = await LoadPreviousDeductionsAsync(
            entity.ProjectId, entity.Id, entity.PeriodNumber, cancellationToken);

        // Kesintilerin varsayılan tabanı hakedişin kümülatif toplamı;
        // üst hesaptan önce bilinmesi gerektiği için burada kurulur.
        var cumulativeBase = entity.Items.Sum(x => x.CumulativeAmount)
            + entity.CumulativeAdvanceMaterialAmount;

        ApplyDeductions(entity, request.Deductions, previousDeductions, cumulativeBase);
        CalculateHeader(entity, previousPayments);

        var planError = ApplyPaymentPlans(entity, request.PaymentPlans);
        if (planError is not null)
            return BadRequest(new { message = planError });

        await db.SaveChangesAsync(cancellationToken);

        await SyncBarterLedgerAsync(entity, cancellationToken);

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
            .Include(x => x.Sections)
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
            .Include(x => x.PaymentPlans)
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

        // Ödeme dağılımı tanımlıysa toplamı net tutarı tutmalı; aksi
        // halde tahsilat takibi eksik kurulur.
        if (entity.PaymentPlans.Count > 0)
        {
            var planTotal = decimal.Round(entity.PaymentPlans.Sum(x => x.Amount), 2);

            if (planTotal != decimal.Round(entity.NetPayableAmount, 2))
            {
                return Conflict(new
                {
                    message = $"Ödeme dağılımı toplamı ({planTotal:N2}) tahsil " +
                              $"edilecek tutarı ({entity.NetPayableAmount:N2}) tutmuyor. " +
                              "Hakedişi düzenleyip dağılımı yenileyin."
                });
            }
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

            // Vadeli çekler kesinleştirmede açılır; taslak hakediş çek
            // defterini kirletmemeli.
            await CreateChequesForPaymentPlanAsync(entity, cancellationToken);

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

    /// <summary>
    /// Bu hakedişte açılan ihzarat kalemlerini kurar.
    /// </summary>
    private static void ApplyAdvanceMaterials(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentAdvanceMaterialRequest>? requests)
    {
        if (requests is null)
            return;

        var lineNumber = 1;

        foreach (var request in requests)
        {
            var calculated = HakedisCalculationService.CalculateAdvanceMaterial(
                new HakedisCalculationService.AdvanceMaterialInput(
                    Id: Guid.Empty,
                    PositionCode: request.PositionCode?.Trim() ?? string.Empty,
                    Quantity: request.Quantity,
                    UnitPrice: request.UnitPrice,
                    ValuationRate: request.ValuationRate,
                    PreviouslyOffsetAmount: 0m));

            entity.AdvanceMaterials.Add(new ProgressPaymentAdvanceMaterial
            {
                LineNumber = lineNumber++,
                PositionCode = calculated.PositionCode,
                Description = request.Description?.Trim() ?? string.Empty,
                Unit = request.Unit?.Trim() ?? string.Empty,
                Quantity = Math.Max(0m, request.Quantity),
                UnitPrice = Math.Max(0m, request.UnitPrice),
                ValuationRate = Math.Clamp(request.ValuationRate, 0m, 100m),
                Amount = calculated.Amount,
                OffsetAmount = 0m,
                Notes = request.Notes?.Trim()
            });
        }
    }

    /// <summary>
    /// Önceki hakedişlerde açılmış ihzarat kalemlerinden bu hakedişte
    /// yapılan mahsupları uygular.
    ///
    /// Açık bakiyeyi aşan mahsup burada reddedilir — çift tahsilatın
    /// engeli arayüzde değil serviste.
    /// </summary>
    /// <returns>Hata mesajı; geçerliyse null.</returns>
    private async Task<string?> ApplyAdvanceOffsetsAsync(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentAdvanceOffsetRequest>? requests,
        CancellationToken cancellationToken)
    {
        if (requests is null)
            return null;

        var requestList = requests.Where(x => x.Amount != 0m).ToList();
        if (requestList.Count == 0)
            return null;

        var ids = requestList.Select(x => x.AdvanceMaterialId).Distinct().ToList();

        var materials = await db.ProgressPaymentAdvanceMaterials
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var request in requestList)
        {
            var material = materials.SingleOrDefault(x => x.Id == request.AdvanceMaterialId);

            if (material is null)
                return "Mahsup edilmek istenen ihzarat kalemi bulunamadı.";

            // Düzenleme akışında bu hakedişin eski mahsupları çağrıdan
            // önce geri alındığı için bakiye burada doğrudan okunabilir.
            var openAmount = material.Amount - material.OffsetAmount;

            var error = HakedisCalculationService.ValidateOffset(
                material.PositionCode, openAmount, request.Amount);

            if (error is not null)
                return error;

            material.OffsetAmount += request.Amount;
            material.UpdatedAtUtc = DateTime.UtcNow;

            entity.AdvanceMaterialOffsets.Add(
                new ProgressPaymentAdvanceMaterialOffset
                {
                    AdvanceMaterialId = material.Id,
                    Amount = request.Amount,
                    Notes = request.Notes?.Trim()
                });
        }

        return null;
    }

    /// <summary>
    /// Projenin açık ihzarat bakiyesi: bu hakedişte açılanlar + önceki
    /// hakedişlerden kalan açık bakiye. Üst hesapta kümülatif toplama
    /// girer.
    /// </summary>
    private async Task<decimal> CalculateOpenAdvanceAmountAsync(
        ProgressPayment entity, CancellationToken cancellationToken)
    {
        var fromPreviousPeriods = await db.ProgressPaymentAdvanceMaterials
            .Where(x => x.ProgressPayment.ProjectId == entity.ProjectId &&
                        x.ProgressPaymentId != entity.Id &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled)
            .SumAsync(x => (decimal?)(x.Amount - x.OffsetAmount), cancellationToken) ?? 0m;

        var openedNow = entity.AdvanceMaterials.Sum(x => x.Amount - x.OffsetAmount);

        // Bu hakedişte yapılan mahsuplar önceki dönem bakiyesinden düşer.
        var offsetNow = entity.AdvanceMaterialOffsets.Sum(x => x.Amount);

        return Math.Max(0m, fromPreviousPeriods + openedNow - offsetNow);
    }

    /// <summary>
    /// Hakedişin kendi bölüm satırlarını proje şablonundan kopyalar.
    /// Kopyalanmasının sebebi: proje şablonu sonradan değişse bile
    /// kesinleşmiş hakedişin icmali oynamamalı.
    /// </summary>
    /// <returns>Proje bölümü → hakediş bölümü eşlemesi.</returns>
    private async Task<Dictionary<Guid, ProgressPaymentSection>> BuildSectionsAsync(
        ProgressPayment entity,
        Guid projectId,
        IEnumerable<ProgressPaymentItemRequest> items,
        CancellationToken cancellationToken)
    {
        var referenced = items
            .Select(x => x.SectionId)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var map = new Dictionary<Guid, ProgressPaymentSection>();

        if (referenced.Count == 0)
            return map;

        var projectSections = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && referenced.Contains(x.Id))
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        foreach (var source in projectSections)
        {
            var section = new ProgressPaymentSection
            {
                ProjectHakedisSectionId = source.Id,
                Order = source.Order,
                Name = source.Name,
                Code = source.Code
            };

            entity.Sections.Add(section);
            map[source.Id] = section;
        }

        return map;
    }

    /// <summary>
    /// Poz satırlarını kurar. Hesabın kendisi
    /// <see cref="HakedisCalculationService"/> içinde; burada yalnızca
    /// önceki dönem miktarları toplanıp sonuç entity'ye yazılır.
    /// </summary>
    private static void ApplyLines(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentItemRequest> requests,
        IEnumerable<ProgressPaymentItem> previousItems,
        IReadOnlyDictionary<Guid, ProgressPaymentSection>? sectionMap = null)
    {
        var previousByCode = previousItems
            .GroupBy(x => x.PositionCode)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.CurrentQuantity),
                StringComparer.OrdinalIgnoreCase);

        var requestList = requests.ToList();

        var inputs = requestList.Select(request =>
        {
            var code = request.PositionCode?.Trim() ?? string.Empty;
            previousByCode.TryGetValue(code, out var previousQuantity);

            // Bileşenler verilmemişse (eski istemci) tek birim fiyat
            // malzemeye yazılır; toplam değişmez.
            var hasComponents =
                request.MaterialUnitPrice.HasValue ||
                request.LaborUnitPrice.HasValue ||
                request.OverheadUnitPrice.HasValue;

            return new HakedisItemInput(
                PositionCode: code,
                ContractQuantity: request.ContractQuantity,
                PreviousQuantity: previousQuantity,
                CurrentQuantity: request.CurrentQuantity,
                MaterialUnitPrice: hasComponents
                    ? request.MaterialUnitPrice ?? 0m
                    : request.UnitPrice,
                LaborUnitPrice: request.LaborUnitPrice ?? 0m,
                OverheadUnitPrice: request.OverheadUnitPrice ?? 0m,
                SectionId: request.SectionId);
        }).ToList();

        var calculation = HakedisCalculationService.CalculateItems(inputs);

        var lineNumber = 1;

        foreach (var (request, result) in requestList.Zip(calculation.Items))
        {
            ProgressPaymentSection? section = null;

            if (request.SectionId is Guid sectionId)
                sectionMap?.TryGetValue(sectionId, out section);

            entity.Items.Add(new ProgressPaymentItem
            {
                LineNumber = lineNumber++,
                EngineeringPositionId = request.EngineeringPositionId,
                Section = section,
                PositionCode = result.PositionCode,
                Description = request.Description?.Trim() ?? string.Empty,
                Unit = request.Unit?.Trim() ?? string.Empty,
                ContractQuantity = result.ContractQuantity,
                PreviousQuantity = result.PreviousQuantity,
                CurrentQuantity = result.CurrentQuantity,
                CumulativeQuantity = result.CumulativeQuantity,
                MaterialUnitPrice = result.MaterialUnitPrice,
                LaborUnitPrice = result.LaborUnitPrice,
                OverheadUnitPrice = result.OverheadUnitPrice,
                UnitPrice = result.UnitPrice,
                MaterialAmount = result.MaterialAmount,
                LaborAmount = result.LaborAmount,
                OverheadAmount = result.OverheadAmount,
                PreviousAmount = result.PreviousAmount,
                CurrentAmount = result.CurrentAmount,
                CumulativeAmount = result.CumulativeAmount,
                CompletionRate = result.CompletionRate,
                MeasurementReference = request.MeasurementReference?.Trim(),
                Notes = request.Notes?.Trim()
            });
        }
    }

    /// <summary>
    /// Kesintileri kurar.
    ///
    /// KÜMÜLATİF: her kesinti "kümülatif taban × oran − önceki
    /// dönemlerde kesilen" olarak hesaplanır. Oran dönemler arasında
    /// değişse bile toplam doğru kalır — "bu dönem × oran" yaklaşımı
    /// geçmişi düzeltemezdi.
    /// </summary>
    /// <param name="previousByType">Önceki hakedişlerde her kesinti
    /// türünden kesilmiş toplam.</param>
    /// <param name="defaultCumulativeBase">Taban verilmemiş kesintiler
    /// için varsayılan: hakedişin kümülatif toplamı. Kesin teminat,
    /// all-risk ve malzeme kesintisi gibi oransal kalemler zaten bunun
    /// üzerinden yürür; her çağıranın ayrıca hesaplaması gereksiz ve
    /// hataya açıktı.</param>
    private static void ApplyDeductions(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentDeductionRequest>? requests,
        IReadOnlyDictionary<int, decimal>? previousByType = null,
        decimal defaultCumulativeBase = 0m)
    {
        if (requests is null)
            return;

        var lineNumber = 1;

        foreach (var request in requests)
        {
            var previousAmount = previousByType is null
                ? 0m
                : previousByType.TryGetValue(request.DeductionType, out var value)
                    ? value
                    : 0m;

            var lines = request.Lines?
                .Select(line => new HakedisCalculationService.DeductionLineInput(
                    line.Name?.Trim() ?? string.Empty,
                    line.UnitPrice,
                    line.Quantity,
                    line.VatRate))
                .ToList();

            var result = HakedisCalculationService.CalculateDeduction(
                new HakedisCalculationService.DeductionInput(
                    DeductionType: request.DeductionType,
                    Description: request.Description?.Trim() ?? "Kesinti",
                    Rate: request.Rate,
                    // Taban açıkça verilmemişse hakedişin kümülatif
                    // toplamı kullanılır; eski istemcinin gönderdiği
                    // BaseAmount hâlâ önceliklidir.
                    CumulativeBaseAmount: request.CumulativeBaseAmount
                        ?? (request.BaseAmount != 0m
                            ? request.BaseAmount
                            : defaultCumulativeBase),
                    PreviousAmount: previousAmount,
                    ManualAmount: request.ManualAmount,
                    Lines: lines));

            var deduction = new ProgressPaymentDeduction
            {
                LineNumber = lineNumber++,
                DeductionType = result.DeductionType,
                Description = result.Description,
                Rate = result.Rate,
                BaseAmount = request.BaseAmount,
                CumulativeBaseAmount = result.CumulativeBaseAmount,
                PreviousAmount = result.PreviousAmount,
                CumulativeAmount = result.CumulativeAmount,
                Amount = result.Amount,
                IsManualAmount = result.IsManualAmount,
                AccountingAccountId = request.AccountingAccountId,
                Notes = request.Notes?.Trim()
            };

            var subLineNumber = 1;

            foreach (var line in result.Lines)
            {
                deduction.Lines.Add(new ProgressPaymentDeductionLine
                {
                    LineNumber = subLineNumber++,
                    Name = line.Name,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Quantity,
                    VatRate = line.VatRate,
                    NetAmount = line.NetAmount,
                    VatAmount = line.VatAmount,
                    GrossAmount = line.GrossAmount
                });
            }

            entity.Deductions.Add(deduction);
        }
    }

    /// <summary>
    /// Ödeme dağılımını kurar. Tutarlar tahsil edilecek net üzerinden
    /// bölünür; yuvarlama farkı son parçaya yazılır ki parçaların
    /// toplamı hakedişten kuruş kadar bile sapmasın.
    /// </summary>
    /// <returns>Hata mesajı; geçerliyse null.</returns>
    private static string? ApplyPaymentPlans(
        ProgressPayment entity,
        IEnumerable<ProgressPaymentPaymentPlanRequest>? requests)
    {
        var parts = (requests ?? [])
            .Select(x => new HakedisCalculationService.PaymentPlanInput(
                x.PaymentType, x.Rate, x.MaturityDays, x.Description?.Trim()))
            .ToList();

        var error = HakedisCalculationService.ValidatePaymentPlan(parts);
        if (error is not null)
            return error;

        var results = HakedisCalculationService.CalculatePaymentPlan(
            entity.NetPayableAmount, entity.ProgressPaymentDate, parts);

        var lineNumber = 1;

        foreach (var result in results)
        {
            entity.PaymentPlans.Add(new ProgressPaymentPaymentPlan
            {
                LineNumber = lineNumber++,
                PaymentType = (ProgressPaymentPaymentType)result.PaymentType,
                Rate = result.Rate,
                Amount = result.Amount,
                MaturityDays = result.MaturityDays,
                DueDate = result.DueDate is DateTime due
                    ? DateTime.SpecifyKind(due, DateTimeKind.Utc)
                    : null,
                Description = result.Description
            });
        }

        return null;
    }

    /// <summary>
    /// Kesinleştirmede vadeli çek parçaları için çek defterine alınan
    /// çek kaydı açar. Çekler ProgressPaymentId ile hakedişe bağlanır ve
    /// vadeleri nakit akışına kendiliğinden düşer.
    /// </summary>
    private async Task CreateChequesForPaymentPlanAsync(
        ProgressPayment entity, CancellationToken cancellationToken)
    {
        var chequeParts = entity.PaymentPlans
            .Where(x => x.PaymentType == ProgressPaymentPaymentType.Cheque &&
                        x.ChequeId is null &&
                        x.Amount > 0m)
            .OrderBy(x => x.LineNumber)
            .ToList();

        if (chequeParts.Count == 0)
            return;

        var project = await db.Projects
            .AsNoTracking()
            .SingleAsync(x => x.Id == entity.ProjectId, cancellationToken);

        foreach (var part in chequeParts)
        {
            var dueDate = part.DueDate
                ?? entity.ProgressPaymentDate.AddDays(part.MaturityDays ?? 0);

            var cheque = await chequeService.CreateAsync(
                new Contracts.Accounting.CreateChequeRequest(
                    CompanyId: entity.CompanyId,
                    Direction: (int)ChequeDirection.Received,
                    // Çek numarası fiziksel çek elde edilince güncellenir;
                    // şimdilik hakediş numarasından türetilir.
                    ChequeNumber:
                        $"{entity.ProgressPaymentNumber}-{part.LineNumber}",
                    BankName: "Belirlenecek",
                    BankBranch: null,
                    Drawer: null,
                    CurrentAccountId: project.EmployerCurrentAccountId,
                    ProjectId: entity.ProjectId,
                    Amount: part.Amount,
                    CurrencyCode: entity.CurrencyCode,
                    IssueDate: entity.ProgressPaymentDate,
                    DueDate: DateTime.SpecifyKind(dueDate.Date, DateTimeKind.Utc),
                    ProgressPaymentId: entity.Id,
                    SupplierInvoiceId: null,
                    Description:
                        $"{entity.ProgressPaymentNumber} hakediş tahsilatı " +
                        $"({part.MaturityDays} gün vadeli)"),
                cancellationToken);

            part.ChequeId = cheque.Id;
            part.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Hakedişteki barter kesintisini barter defterine yansıtır.
    ///
    /// Barter, hakedişin mal/hizmet olarak ödenecek kısmıdır: kesinti
    /// yapıldığında işverenden o tutarda mal/hizmet alacağımız doğar.
    /// Defter kaydı hakediş başına tektir; düzenlemede güncellenir.
    /// </summary>
    private async Task SyncBarterLedgerAsync(
        ProgressPayment entity, CancellationToken cancellationToken)
    {
        var barterAmount = entity.Deductions
            .Where(x => x.DeductionType == (int)HakedisDeductionType.Barter)
            .Sum(x => x.Amount);

        var existing = await db.BarterLedgerEntries
            .SingleOrDefaultAsync(
                x => x.ProgressPaymentId == entity.Id &&
                     x.EntryType == BarterEntryType.Deduction,
                cancellationToken);

        if (barterAmount <= 0m)
        {
            if (existing is not null)
                db.BarterLedgerEntries.Remove(existing);

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (existing is null)
        {
            db.BarterLedgerEntries.Add(new BarterLedgerEntry
            {
                ProjectId = entity.ProjectId,
                ProgressPaymentId = entity.Id,
                EntryType = BarterEntryType.Deduction,
                EntryDate = entity.ProgressPaymentDate,
                Amount = barterAmount,
                Description = $"{entity.ProgressPaymentNumber} barter kesintisi"
            });
        }
        else
        {
            existing.Amount = barterAmount;
            existing.EntryDate = entity.ProgressPaymentDate;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Önceki hakedişlerde kesinti türü bazında kesilmiş toplamlar.
    /// Kümülatif kesinti hesabının "önceden kesilen" tarafı.
    /// </summary>
    private async Task<Dictionary<int, decimal>> LoadPreviousDeductionsAsync(
        Guid projectId,
        Guid? excludeProgressPaymentId,
        int periodNumber,
        CancellationToken cancellationToken)
    {
        var query = db.ProgressPaymentDeductions
            .AsNoTracking()
            .Where(x => x.ProgressPayment.ProjectId == projectId &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled &&
                        x.ProgressPayment.PeriodNumber < periodNumber);

        if (excludeProgressPaymentId is Guid excluded)
            query = query.Where(x => x.ProgressPaymentId != excluded);

        return await query
            .GroupBy(x => x.DeductionType)
            .Select(g => new { Type = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Type, x => x.Total, cancellationToken);
    }

    /// <summary>
    /// Üst hesap. Hesap <see cref="HakedisCalculationService"/> içinde;
    /// burada girdiler toplanıp sonuç entity'ye yazılır.
    ///
    /// Bu dönem tutarı artık "satırların toplamı" değil, "kümülatif
    /// toplam − önceki hakedişler" olarak bulunuyor (minha mantığı).
    /// İkisi birim fiyat sabitken aynı sonucu verir; birim fiyat dönemler
    /// arasında değiştiğinde yalnızca minha doğru sonucu verir.
    /// </summary>
    private static void CalculateHeader(
        ProgressPayment entity,
        IEnumerable<ProgressPayment> previousPayments)
    {
        var cumulativeWork = entity.Items.Sum(x => x.CumulativeAmount);

        var result = HakedisCalculationService.CalculateHeader(
            new HakedisCalculationService.HakedisHeaderInput(
                CumulativeWorkAmount: cumulativeWork,
                CumulativeAdvanceMaterialAmount:
                    entity.CumulativeAdvanceMaterialAmount,
                PreviousTotalAmount: previousPayments.Sum(x => x.CurrentAmount),
                PriceDifferenceAmount: entity.PriceDifferenceAmount,
                VatRate: entity.VatRate,
                WithholdingNumerator: entity.WithholdingNumerator,
                WithholdingDenominator: entity.WithholdingDenominator,
                IncomeTaxWithholdingRate: entity.IncomeTaxWithholdingRate,
                TotalDeductionAmount: entity.Deductions.Sum(x => x.Amount)));

        entity.CumulativeWorkAmount = result.CumulativeWorkAmount;
        entity.PreviousAmount = result.PreviousTotalAmount;
        entity.CurrentAmount = result.CurrentAmount;
        entity.CumulativeAmount = result.CumulativeTotalAmount;
        entity.VatAmount = result.VatAmount;
        entity.WithholdingAmount = result.WithholdingAmount;
        entity.IncomeTaxWithholdingAmount = result.IncomeTaxWithholdingAmount;
        entity.TotalDeductionAmount = result.TotalDeductionAmount;
        entity.GrossPayableAmount = result.GrossPayableAmount;
        entity.NetPayableAmount = result.NetPayableAmount;

        ApplySectionSummaries(entity);
    }

    /// <summary>Bölüm icmalini poz satırlarından yeniden kurar.</summary>
    private static void ApplySectionSummaries(ProgressPayment entity)
    {
        foreach (var section in entity.Sections)
        {
            // Kayıt henüz veritabanına yazılmadığı için yabancı anahtar
            // dolu olmayabilir; eşleşme navigasyon üzerinden yapılır.
            var items = entity.Items
                .Where(x => ReferenceEquals(x.Section, section) ||
                            x.ProgressPaymentSectionId == section.Id)
                .ToList();

            section.MaterialAmount = items.Sum(x => x.MaterialAmount);
            section.LaborAmount = items.Sum(x => x.LaborAmount);
            section.OverheadAmount = items.Sum(x => x.OverheadAmount);
            section.CurrentAmount = items.Sum(x => x.CurrentAmount);
            section.PreviousAmount = items.Sum(x => x.PreviousAmount);
            section.CumulativeAmount = items.Sum(x => x.CumulativeAmount);
        }
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
                x.CumulativeWorkAmount,
                x.CumulativeAdvanceMaterialAmount,
                x.PriceDifferenceAmount,
                x.VatRate,
                x.VatAmount,
                x.WithholdingNumerator,
                x.WithholdingDenominator,
                x.WithholdingAmount,
                x.IncomeTaxWithholdingRate,
                x.IncomeTaxWithholdingAmount,
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
                Sections = x.Sections
                    .OrderBy(s => s.Order)
                    .Select(s => new
                    {
                        s.Id,
                        s.ProjectHakedisSectionId,
                        s.Order,
                        s.Name,
                        s.Code,
                        s.MaterialAmount,
                        s.LaborAmount,
                        s.OverheadAmount,
                        s.PreviousAmount,
                        s.CurrentAmount,
                        s.CumulativeAmount
                    }),
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.EngineeringPositionId,
                        i.ProgressPaymentSectionId,
                        i.LineNumber,
                        i.PositionCode,
                        i.Description,
                        i.Unit,
                        i.ContractQuantity,
                        i.PreviousQuantity,
                        i.CurrentQuantity,
                        i.CumulativeQuantity,
                        i.MaterialUnitPrice,
                        i.LaborUnitPrice,
                        i.OverheadUnitPrice,
                        i.UnitPrice,
                        i.MaterialAmount,
                        i.LaborAmount,
                        i.OverheadAmount,
                        i.PreviousAmount,
                        i.CurrentAmount,
                        i.CumulativeAmount,
                        i.CompletionRate,
                        i.MeasurementReference,
                        i.Notes
                    }),
                PaymentPlans = x.PaymentPlans
                    .OrderBy(p => p.LineNumber)
                    .Select(p => new
                    {
                        p.Id,
                        p.LineNumber,
                        PaymentType = (int)p.PaymentType,
                        p.Rate,
                        p.Amount,
                        p.MaturityDays,
                        p.DueDate,
                        p.ChequeId,
                        ChequeNumber = p.Cheque != null ? p.Cheque.InternalNumber : null,
                        p.Description
                    }),
                AdvanceMaterials = x.AdvanceMaterials
                    .OrderBy(a => a.LineNumber)
                    .Select(a => new
                    {
                        a.Id,
                        a.LineNumber,
                        a.PositionCode,
                        a.Description,
                        a.Unit,
                        a.Quantity,
                        a.UnitPrice,
                        a.ValuationRate,
                        a.Amount,
                        a.OffsetAmount,
                        OpenAmount = a.Amount - a.OffsetAmount,
                        a.Notes
                    }),
                AdvanceOffsets = x.AdvanceMaterialOffsets
                    .Select(o => new
                    {
                        o.Id,
                        o.AdvanceMaterialId,
                        PositionCode = o.AdvanceMaterial.PositionCode,
                        AdvanceDescription = o.AdvanceMaterial.Description,
                        o.Amount,
                        o.Notes
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
                        d.CumulativeBaseAmount,
                        d.PreviousAmount,
                        d.CumulativeAmount,
                        d.Amount,
                        d.IsManualAmount,
                        d.Notes,
                        Lines = d.Lines
                            .OrderBy(l => l.LineNumber)
                            .Select(l => new
                            {
                                l.Id,
                                l.LineNumber,
                                l.Name,
                                l.UnitPrice,
                                l.Quantity,
                                l.VatRate,
                                l.NetAmount,
                                l.VatAmount,
                                l.GrossAmount
                            })
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
