using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Assets;

/// <summary>
/// Servis akışının geçerli durum geçişleri.
///
/// Saf ve veritabanısız: hangi durumdan hangisine geçilebileceği tek
/// yerde durur ve test edilebilir. Geçiş kuralı serviste dağınık
/// if'lere yayılsaydı, bir ekranın atladığı kontrol diğerinde
/// yakalanmaz ve alet "serviste" görünürken kullanımda olabilirdi.
/// </summary>
public static class ToolServiceTransitions
{
    /// <summary>
    /// Verilen durumdan geçilebilecek durumlar.
    /// </summary>
    public static IReadOnlyList<ToolServiceStatus> Allowed(ToolServiceStatus from) =>
        from switch
        {
            ToolServiceStatus.Requested =>
            [
                ToolServiceStatus.Transferred,
                // Şantiyede yerinde çözülürse merkeze hiç gelmeden
                // kapanabilir.
                ToolServiceStatus.Completed,
                ToolServiceStatus.Cancelled
            ],
            ToolServiceStatus.Transferred =>
            [
                ToolServiceStatus.InService,
                ToolServiceStatus.Scrapped,
                ToolServiceStatus.Cancelled
            ],
            ToolServiceStatus.InService =>
            [
                ToolServiceStatus.Completed,
                ToolServiceStatus.Scrapped
            ],
            // Kapanmış talep yeniden açılmaz: yeni arıza yeni taleptir,
            // yoksa aynı kayıt üzerinde iki farklı arızanın maliyeti
            // birbirine karışır.
            _ => []
        };

    public static bool CanTransition(ToolServiceStatus from, ToolServiceStatus to) =>
        Allowed(from).Contains(to);

    /// <summary>
    /// Karara göre aletin varacağı durum.
    /// </summary>
    public static ToolAssetStatus AssetStatusFor(ToolServiceStatus serviceStatus) =>
        serviceStatus switch
        {
            ToolServiceStatus.Requested => ToolAssetStatus.InService,
            ToolServiceStatus.Transferred => ToolAssetStatus.InService,
            ToolServiceStatus.InService => ToolAssetStatus.InService,
            ToolServiceStatus.Scrapped => ToolAssetStatus.Scrapped,
            _ => ToolAssetStatus.InUse
        };

    /// <summary>
    /// Bu kararda proje maliyeti doğar mı.
    ///
    /// GARANTİ SIFIRDIR: garanti kapsamındaki onarımın bedelini
    /// projeye yazmak, ödemediğimiz bir masrafı işin maliyetine
    /// eklemek olurdu.
    /// </summary>
    public static bool ProducesCost(ToolServiceDecision decision, decimal cost) =>
        cost > 0m &&
        decision is ToolServiceDecision.ExternalPaid or ToolServiceDecision.InHouse;
}

/// <summary>
/// Alet servis akışı: durum geçişleri, maliyetin doğru projeye
/// yazılması ve hurda sonrası yerine alım talebi.
/// </summary>
public sealed class ToolServiceWorkflow(AppDbContext db)
{
    /// <summary>
    /// Servis talebi için şirket içinde benzersiz numara.
    ///
    /// SANİYE YETMEZ: yalnızca zaman damgası kullanıldığında aynı
    /// saniyede açılan iki talep aynı numarayı alıyor ve tekil indekse
    /// takılıyordu (canlıda 500 dönerdi). Aynı saniyedeki kayıtlara
    /// sayaç ekleniyor.
    /// </summary>
    public async Task<string> NextServiceNumberAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var basePrefix = $"SRV-{stamp}";

        var taken = await db.ToolServiceRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.RequestNumber.StartsWith(basePrefix))
            .Select(x => x.RequestNumber)
            .ToListAsync(cancellationToken);

        return Unique(basePrefix, taken);
    }

    /// <summary>
    /// Yerine alım talebi için benzersiz numara; aynı çakışma riski
    /// satın alma talebinde de var.
    /// </summary>
    private async Task<string> NextReplacementNumberAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var basePrefix = $"ALT-{stamp}";

        var taken = await db.PurchaseRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.RequestNumber.StartsWith(basePrefix))
            .Select(x => x.RequestNumber)
            .ToListAsync(cancellationToken);

        return Unique(basePrefix, taken);
    }

    private static string Unique(string basePrefix, List<string> taken)
    {
        if (!taken.Contains(basePrefix))
            return basePrefix;

        for (var suffix = 1; suffix < 1000; suffix++)
        {
            var candidate = $"{basePrefix}-{suffix}";

            if (!taken.Contains(candidate))
                return candidate;
        }

        // Bir saniyede 1000 kayıt gerçekçi değil; yine de sessizce
        // çakışmaktansa benzersizliği garanti ediyoruz.
        return $"{basePrefix}-{Guid.NewGuid():N}"[..24];
    }

    /// <summary>
    /// Servis talebini bir sonraki duruma taşır.
    /// </summary>
    /// <exception cref="InvalidOperationException">Geçiş geçersizse.</exception>
    public async Task AdvanceAsync(
        ToolServiceRequest request,
        ToolServiceStatus target,
        CancellationToken cancellationToken)
    {
        if (!ToolServiceTransitions.CanTransition(request.Status, target))
        {
            throw new InvalidOperationException(
                $"{StatusName(request.Status)} durumundan " +
                $"{StatusName(target)} durumuna geçilemez.");
        }

        var asset = await db.ToolAssets
            .SingleAsync(x => x.Id == request.ToolAssetId, cancellationToken);

        request.Status = target;

        switch (target)
        {
            case ToolServiceStatus.Transferred:
                request.TransferredAtUtc = DateTime.UtcNow;
                break;

            case ToolServiceStatus.InService:
                request.DecidedAtUtc ??= DateTime.UtcNow;
                break;

            case ToolServiceStatus.Completed:
            case ToolServiceStatus.Scrapped:
                request.CompletedAtUtc = DateTime.UtcNow;
                await ApplyCostAsync(request, asset, cancellationToken);
                break;
        }

        // Alet durumu servisin durumundan türetilir; iki yerde ayrı
        // güncellenirse er ya da geç ayrışır.
        asset.Status = target == ToolServiceStatus.Cancelled
            ? ToolAssetStatus.InUse
            : ToolServiceTransitions.AssetStatusFor(target);

        // ZİMMET KAPANMAZ: kişi servis boyunca da sorumludur. Alet
        // hurdaya ayrıldıysa zimmet kapatılır, çünkü iade edilecek bir
        // şey kalmamıştır.
        if (target == ToolServiceStatus.Scrapped)
        {
            asset.AssignedPersonnelId = null;

            var openAssignments = await db.HrAssetAssignments
                .Where(x => x.ToolAssetId == asset.Id &&
                            x.Status == HrAssetAssignmentStatus.Assigned)
                .ToListAsync(cancellationToken);

            foreach (var assignment in openAssignments)
            {
                assignment.Status = HrAssetAssignmentStatus.Returned;
                assignment.ActualReturnDate = DateTime.UtcNow.Date;
                assignment.ConditionAtReturn =
                    "Hurdaya ayrıldı (servis kararı).";
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ücretli servisin bedelini talebi AÇAN projeye yazar.
    ///
    /// Merkez talebinde proje yoktur; maliyet genel gider olarak kalır
    /// ve hiçbir projeye yüklenmez — rastgele bir projeye yazmak o
    /// projenin kârını haksız yere düşürürdü.
    /// </summary>
    private async Task ApplyCostAsync(
        ToolServiceRequest request,
        ToolAsset asset,
        CancellationToken cancellationToken)
    {
        if (request.ProjectCostTransactionId is not null)
            return;

        if (!ToolServiceTransitions.ProducesCost(request.Decision, request.ServiceCost))
            return;

        if (request.ProjectId is not Guid projectId)
            return;

        var cost = new ProjectCostTransaction
        {
            ProjectId = projectId,
            ProjectSiteId = request.ProjectSiteId,
            CostType = ProjectCostType.Equipment,
            // Alet onarımı imalata doğrudan girmez; genel gider.
            CostClass = ProjectCostClass.Overhead,
            CostDate = DateTime.UtcNow.Date,
            Amount = decimal.Round(request.ServiceCost, 2),
            Description =
                $"Alet servisi {request.RequestNumber} — {asset.Code} {asset.Name}",
            ReferenceType = nameof(ToolServiceRequest),
            ReferenceId = request.Id
        };

        db.ProjectCostTransactions.Add(cost);
        request.ProjectCostTransactionId = cost.Id;
    }

    /// <summary>
    /// Hurdaya ayrılan alet için yerine alım talebi taslağı üretir.
    ///
    /// Talep TASLAK açılır: yenisinin alınıp alınmayacağı ve hangi
    /// özellikte olacağı satın almanın kararıdır; hurda kaydı bunu
    /// tek başına belirleyemez.
    /// </summary>
    /// <returns>Oluşturulan talep; üretilemezse null ve nedeni.</returns>
    public async Task<(PurchaseRequest? Request, string? Skipped)>
        CreateReplacementRequestAsync(
            ToolServiceRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
    {
        if (request.Status != ToolServiceStatus.Scrapped)
            return (null, "Yalnızca hurdaya ayrılan alet için yenisi istenebilir.");

        if (request.ReplacementPurchaseRequestId is not null)
            return (null, "Bu hurda için zaten alım talebi açılmış.");

        if (request.ProjectId is not Guid projectId)
        {
            return (null,
                "Merkez talebinde proje yok; alım talebini satın almadan " +
                "elle açın.");
        }

        var asset = await db.ToolAssets
            .AsNoTracking()
            .SingleAsync(x => x.Id == request.ToolAssetId, cancellationToken);

        var purchaseRequest = new PurchaseRequest
        {
            CompanyId = request.CompanyId,
            ProjectId = projectId,
            RequestNumber = await NextReplacementNumberAsync(
                request.CompanyId, cancellationToken),
            RequestDate = DateTime.UtcNow.Date,
            RequestedByName = "Demirbaş (otomatik)",
            RequestedByUserId = actorUserId,
            Status = PurchaseRequestStatus.Draft,
            Description =
                $"{asset.Code} {asset.Name} hurdaya ayrıldı " +
                $"({request.RequestNumber}); yerine alım.",
            Items =
            [
                new PurchaseRequestItem
                {
                    LineNumber = 1,
                    MaterialDescription =
                        $"{asset.Name}" +
                        (string.IsNullOrWhiteSpace(asset.Brand)
                            ? string.Empty
                            : $" ({asset.Brand} {asset.Model})".TrimEnd()),
                    Quantity = 1m,
                    Unit = "AD"
                }
            ]
        };

        db.PurchaseRequests.Add(purchaseRequest);

        var tracked = await db.ToolServiceRequests
            .SingleAsync(x => x.Id == request.Id, cancellationToken);

        tracked.ReplacementPurchaseRequestId = purchaseRequest.Id;

        await db.SaveChangesAsync(cancellationToken);

        return (purchaseRequest, null);
    }

    /// <summary>
    /// Aletin servis geçmişi özeti — kaç kez arızalandı, toplam ne
    /// kadara mal oldu.
    /// </summary>
    public async Task<(int Count, decimal TotalCost, DateTime? LastDate)>
        GetHistorySummaryAsync(Guid toolAssetId, CancellationToken cancellationToken)
    {
        var rows = await db.ToolServiceRequests
            .AsNoTracking()
            .Where(x => x.ToolAssetId == toolAssetId &&
                        x.Status != ToolServiceStatus.Cancelled)
            .Select(x => new { x.ServiceCost, x.RequestDate })
            .ToListAsync(cancellationToken);

        return (
            rows.Count,
            decimal.Round(rows.Sum(x => x.ServiceCost), 2),
            rows.Count == 0 ? null : rows.Max(x => x.RequestDate));
    }

    private static string StatusName(ToolServiceStatus status) => status switch
    {
        ToolServiceStatus.Requested => "Talep edildi",
        ToolServiceStatus.Transferred => "Merkeze transfer edildi",
        ToolServiceStatus.InService => "Serviste",
        ToolServiceStatus.Completed => "Tamamlandı",
        ToolServiceStatus.Scrapped => "Hurda",
        _ => "İptal"
    };
}
