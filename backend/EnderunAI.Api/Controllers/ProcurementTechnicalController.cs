using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/procurement-technical")]
[Authorize]
public sealed class ProcurementTechnicalController(
    ProcurementTechnicalDbContext db,
    ITechnicalComplianceService service) : ControllerBase
{
    public sealed record CriterionInput(
        Guid? RfqItemId,
        string Code,
        string Name,
        TechnicalCriterionType Type,
        string? ExpectedValue,
        decimal? NumericValue,
        string? Unit,
        bool IsMandatory,
        decimal Weight);

    public sealed record CreateSpecificationRequest(
        Guid CompanyId,
        Guid ProjectId,
        Guid? RfqId,
        string Code,
        string Name,
        string? Description,
        IReadOnlyList<CriterionInput> Criteria);

    public sealed record ResponseInput(
        Guid SupplierOfferId,
        Guid SupplierOfferItemId,
        Guid TechnicalCriterionId,
        string? OfferedValue,
        decimal? OfferedNumericValue,
        bool? IsProvided,
        string? EvidenceReference);

    public sealed record ManualReviewRequest(
        TechnicalComplianceStatus Status,
        decimal Score,
        string Note);

    [HttpPost("specifications")]
    public async Task<ActionResult> CreateSpecification(CreateSpecificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Şartname kodu ve adı zorunludur.");
        if (request.Criteria.Count == 0)
            return BadRequest("En az bir teknik kriter girilmelidir.");
        if (request.Criteria.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) || x.Weight < 0))
            return BadRequest("Kriter kodu, adı ve ağırlığı geçerli olmalıdır.");

        var duplicate = await db.Specifications.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.Code == request.Code.Trim(), cancellationToken);
        if (duplicate)
            return Conflict("Bu şartname kodu daha önce kullanılmış.");

        var entity = new TechnicalSpecification
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            RfqId = request.RfqId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Criteria = request.Criteria.Select(x => new TechnicalCriterion
            {
                RfqItemId = x.RfqItemId,
                Code = x.Code.Trim(),
                Name = x.Name.Trim(),
                Type = x.Type,
                ExpectedValue = x.ExpectedValue?.Trim(),
                NumericValue = x.NumericValue,
                Unit = x.Unit?.Trim(),
                IsMandatory = x.IsMandatory,
                Weight = x.Weight
            }).ToList()
        };

        db.Specifications.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetSpecification), new { id = entity.Id }, entity);
    }

    [HttpGet("specifications/{id:guid}")]
    public async Task<ActionResult> GetSpecification(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Specifications.AsNoTracking().Include(x => x.Criteria)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpGet("specifications")]
    public async Task<ActionResult> ListSpecifications(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? rfqId,
        CancellationToken cancellationToken)
    {
        var query = db.Specifications.AsNoTracking().Include(x => x.Criteria).AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (rfqId.HasValue) query = query.Where(x => x.RfqId == rfqId.Value);
        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpPut("responses")]
    public async Task<ActionResult> UpsertResponse(ResponseInput request, CancellationToken cancellationToken)
    {
        var entity = await db.Responses.FirstOrDefaultAsync(
            x => x.SupplierOfferItemId == request.SupplierOfferItemId && x.TechnicalCriterionId == request.TechnicalCriterionId,
            cancellationToken);

        if (entity is null)
        {
            entity = new SupplierOfferTechnicalResponse
            {
                SupplierOfferId = request.SupplierOfferId,
                SupplierOfferItemId = request.SupplierOfferItemId,
                TechnicalCriterionId = request.TechnicalCriterionId
            };
            db.Responses.Add(entity);
        }

        entity.OfferedValue = request.OfferedValue?.Trim();
        entity.OfferedNumericValue = request.OfferedNumericValue;
        entity.IsProvided = request.IsProvided;
        entity.EvidenceReference = request.EvidenceReference?.Trim();
        entity.Status = TechnicalComplianceStatus.NotEvaluated;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpPost("offers/{supplierOfferId:guid}/evaluate")]
    public async Task<ActionResult> Evaluate(Guid supplierOfferId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.EvaluateOfferAsync(supplierOfferId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("offers/{supplierOfferId:guid}/responses")]
    public async Task<ActionResult> GetOfferResponses(Guid supplierOfferId, CancellationToken cancellationToken)
    {
        return Ok(await db.Responses.AsNoTracking()
            .Where(x => x.SupplierOfferId == supplierOfferId)
            .OrderBy(x => x.SupplierOfferItemId)
            .ThenBy(x => x.TechnicalCriterionId)
            .ToListAsync(cancellationToken));
    }

    [HttpPost("responses/{id:guid}/manual-review")]
    public async Task<ActionResult> ManualReview(Guid id, ManualReviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Status == TechnicalComplianceStatus.NotEvaluated || request.Score is < 0 or > 100 || string.IsNullOrWhiteSpace(request.Note))
            return BadRequest("Manuel değerlendirme durumu, 0-100 puan ve açıklama zorunludur.");

        var entity = await db.Responses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        Guid? userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var parsed) ? parsed : null;
        entity.Status = request.Status;
        entity.Score = request.Score;
        entity.EvaluationNote = request.Note.Trim();
        entity.EvaluatedByUserId = userId;
        entity.EvaluatedByName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Bilinmeyen kullanıcı";
        entity.EvaluatedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }
}
