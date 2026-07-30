using EnderunAI.Api.Contracts.ProjectBoqs;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.ProjectBoqs;

public sealed class ProjectBoqService(AppDbContext db)
    : IProjectBoqService
{
    public async Task<IReadOnlyList<ProjectBoqListItemDto>> GetAllAsync(
        Guid companyId,
        Guid projectId,
        int? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.ProjectBoqs
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProjectId == projectId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchPattern = $"%{search.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.BoqNumber, searchPattern) ||
                EF.Functions.ILike(x.Name, searchPattern) ||
                EF.Functions.ILike(x.Project.Code, searchPattern) ||
                EF.Functions.ILike(x.Project.Name, searchPattern));
        }

        return await query
            .OrderByDescending(x => x.IsCurrentRevision)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new ProjectBoqListItemDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.BoqNumber,
                x.Name,
                x.RevisionNumber,
                x.Status,
                x.IsCurrentRevision,
                x.CurrencyCode,
                x.TotalAmount,
                x.CreatedAtUtc,
                x.IsActive,
                x.Items.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectBoqDetailDto?> GetByIdAsync(
        Guid id,
        Guid companyId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await db.ProjectBoqs
            .AsNoTracking()
            .Where(x =>
                x.Id == id &&
                x.CompanyId == companyId &&
                x.ProjectId == projectId)
            .Select(x => new ProjectBoqDetailDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.ProjectId,
                x.Project.Code,
                x.Project.Name,
                x.BoqNumber,
                x.Name,
                x.RevisionNumber,
                x.Status,
                x.IsCurrentRevision,
                x.CurrencyCode,
                x.TotalAmount,
                x.ApprovedAtUtc,
                x.ApprovedByUserId,
                x.Description,
                x.Notes,
                x.CreatedAtUtc,
                x.IsActive,
                x.Items
                    .OrderBy(item => item.LineNumber)
                    .Select(item => new ProjectBoqItemDto(
                        item.Id,
                        item.ProjectBoqId,
                        item.EngineeringPositionId,
                        item.LineNumber,
                        item.PositionCode,
                        item.Description,
                        item.Unit,
                        item.ContractQuantity,
                        item.UnitPrice,
                        item.TotalAmount,
                        item.ItemType,
                        item.Category,
                        item.Notes,
                        item.IsActive))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
