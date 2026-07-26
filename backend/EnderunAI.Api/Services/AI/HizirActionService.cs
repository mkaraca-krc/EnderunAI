using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.AI;

public sealed class HizirActionService(AppDbContext db) : IHizirActionService
{
    public Task<HizirActionPreview> PreviewAsync(
        HizirActionRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var preview = request.ActionType switch
        {
            HizirActionType.RefreshDashboard => new HizirActionPreview(
                request.ActionType,
                false,
                "Hızır dashboard verileri yeniden okunacak.",
                Array.Empty<string>()),

            HizirActionType.CreatePurchaseRequest => new HizirActionPreview(
                request.ActionType,
                true,
                $"{request.Description} açıklamasıyla taslak satın alma talebi oluşturulacak.",
                new[]
                {
                    "Talep taslak statüsünde oluşturulur.",
                    "Onay verilmeden tedarikçiye sipariş gönderilmez.",
                    "Malzeme kalemleri satın alma ekranından ayrıca eklenmelidir."
                }),

            _ => throw new ArgumentOutOfRangeException(nameof(request.ActionType))
        };

        return Task.FromResult(preview);
    }

    public async Task<HizirActionResult> ExecuteAsync(
        HizirActionRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        if (request.ActionType == HizirActionType.RefreshDashboard)
        {
            var snapshot = new
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Projects = new
                {
                    Total = await db.Projects.CountAsync(cancellationToken),
                    Active = await db.Projects.CountAsync(
                        x => x.Status == ProjectStatus.Active,
                        cancellationToken),
                    AtRisk = await db.Projects.CountAsync(
                        x => x.Status == ProjectStatus.Active &&
                             x.HealthStatus == ProjectHealthStatus.Red,
                        cancellationToken)
                },
                Purchasing = new
                {
                    TotalRequests = await db.PurchaseRequests.CountAsync(cancellationToken),
                    WaitingApproval = await db.PurchaseRequests.CountAsync(
                        x => x.Status == PurchaseRequestStatus.Submitted,
                        cancellationToken),
                    Critical = await db.PurchaseRequests.CountAsync(
                        x => x.Priority == PurchaseRequestPriority.Critical &&
                             x.Status != PurchaseRequestStatus.Completed &&
                             x.Status != PurchaseRequestStatus.Cancelled &&
                             x.Status != PurchaseRequestStatus.Rejected,
                        cancellationToken)
                },
                Personnel = new
                {
                    Total = await db.Personnel.CountAsync(cancellationToken),
                    Active = await db.Personnel.CountAsync(
                        x => x.IsActive,
                        cancellationToken)
                }
            };

            return new HizirActionResult(
                request.ActionType,
                true,
                "Dashboard verileri yenilendi.",
                null,
                snapshot,
                DateTime.UtcNow);
        }

        if (!request.Confirmed)
        {
            var preview = await PreviewAsync(request, cancellationToken);
            return new HizirActionResult(
                request.ActionType,
                false,
                "İşlem uygulanmadı. Açık kullanıcı onayı gerekiyor.",
                null,
                preview,
                DateTime.UtcNow);
        }

        var projectExists = await db.Projects.AnyAsync(
            x => x.Id == request.ProjectId,
            cancellationToken);

        if (!projectExists)
            throw new InvalidOperationException("Seçilen proje bulunamadı.");

        var requestNumber = $"HZR-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var entity = new PurchaseRequest
        {
            CompanyId = request.CompanyId!.Value,
            ProjectId = request.ProjectId!.Value,
            RequestNumber = requestNumber,
            RequestDate = DateTime.UtcNow.Date,
            NeededByDate = request.NeededByDate,
            RequestedByName = string.IsNullOrWhiteSpace(request.RequestedByName)
                ? "Hızır üzerinden"
                : request.RequestedByName.Trim(),
            Description = request.Description!.Trim(),
            Priority = PurchaseRequestPriority.Normal,
            Status = PurchaseRequestStatus.Draft,
            CreatedByUserId = userId
        };

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new HizirActionResult(
            request.ActionType,
            true,
            $"{requestNumber} numaralı satın alma talebi taslak olarak oluşturuldu.",
            entity.Id,
            new
            {
                entity.RequestNumber,
                entity.Status,
                entity.ProjectId,
                entity.NeededByDate
            },
            DateTime.UtcNow);
    }

    private static void Validate(HizirActionRequest request)
    {
        if (request.ActionType != HizirActionType.CreatePurchaseRequest)
            return;

        if (request.CompanyId is null || request.CompanyId == Guid.Empty)
            throw new ArgumentException("Şirket seçimi zorunludur.");
        if (request.ProjectId is null || request.ProjectId == Guid.Empty)
            throw new ArgumentException("Proje seçimi zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Satın alma açıklaması zorunludur.");
    }
}
