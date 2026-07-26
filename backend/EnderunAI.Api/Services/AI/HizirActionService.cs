using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public sealed class HizirActionService(IHizirDashboardAggregator dashboardAggregator)
    : IHizirActionService
{
    public Task<HizirActionPreview> PreviewAsync(
        HizirActionRequest request,
        CancellationToken cancellationToken = default)
    {
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
                "Satın alma talebi oluşturma isteği hazırlandı.",
                new[]
                {
                    "Satın alma veri modeli main branch'e henüz taşınmadığı için kayıt oluşturulmayacaktır.",
                    "Onay verilse bile sistem mevcut olmayan tabloya yazma yapmaz."
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
        if (request.ActionType == HizirActionType.RefreshDashboard)
        {
            var snapshot = await dashboardAggregator.GetSnapshotAsync(cancellationToken);
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

        return new HizirActionResult(
            request.ActionType,
            false,
            "Satın alma modülü main branch'e taşınmadan kayıt oluşturulamaz.",
            null,
            null,
            DateTime.UtcNow);
    }
}
