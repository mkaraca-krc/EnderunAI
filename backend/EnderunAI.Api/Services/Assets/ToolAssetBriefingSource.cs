using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Assets;

/// <summary>
/// Demirbaş uyarılarının özeti — dashboard kartı ve brifing aynı
/// kaynaktan beslenir ki iki ekran farklı sayı göstermesin.
/// </summary>
/// <param name="WarrantyExpiringCount">Garantisi yaklaşan alet.</param>
/// <param name="InServiceCount">Serviste bekleyen alet.</param>
/// <param name="OverdueReturnCount">Planlanan iade tarihi geçmiş
/// zimmet.</param>
/// <param name="FrequentFailureCount">Sık arızalanan alet.</param>
public sealed record ToolAssetAlertSummary(
    int WarrantyExpiringCount,
    int InServiceCount,
    int OverdueReturnCount,
    int FrequentFailureCount);

/// <summary>
/// Demirbaş uyarıları: garantisi yaklaşan, serviste bekleyen, iadesi
/// geciken zimmet ve sık arızalanan alet.
///
/// Dördü de "kimsenin bakmadığı sürece sessizce büyüyen" iş: garanti
/// biter ve ücretli servise düşeriz, serviste bekleyen alet sahada
/// eksik kalır, iade edilmeyen zimmet çıkışta kaybolur, sık arızalanan
/// alet her seferinde biraz daha para yakar.
/// </summary>
public sealed class ToolAssetAlertService(AppDbContext db)
{
    /// <summary>Garanti uyarısının kaç gün önceden çıkacağı.</summary>
    public const int WarrantyHorizonDays = 60;

    /// <summary>
    /// Bu sayıdan fazla arızalanan alet "sık arızalanan" sayılır.
    /// Üç, tek seferlik talihsizliği değil deseni yakalar.
    /// </summary>
    public const int FrequentFailureThreshold = 3;

    public async Task<ToolAssetAlertSummary> GetSummaryAsync(
        IReadOnlySet<Guid>? companyIds, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(WarrantyHorizonDays);

        var assets = db.ToolAssets.AsNoTracking()
            .Where(x => x.Status != ToolAssetStatus.Scrapped);

        if (companyIds is not null)
            assets = assets.Where(x => companyIds.Contains(x.CompanyId));

        var warrantyExpiring = await assets.CountAsync(
            x => x.WarrantyEndDate != null &&
                 x.WarrantyEndDate >= today &&
                 x.WarrantyEndDate <= horizon,
            cancellationToken);

        var inService = await assets.CountAsync(
            x => x.Status == ToolAssetStatus.InService, cancellationToken);

        // Planlanan iade tarihi geçmiş, hâlâ açık zimmetler.
        var assignments = db.HrAssetAssignments.AsNoTracking()
            .Where(x => x.Status == HrAssetAssignmentStatus.Assigned &&
                        x.PlannedReturnDate != null &&
                        x.PlannedReturnDate < today);

        if (companyIds is not null)
            assignments = assignments.Where(x => companyIds.Contains(x.CompanyId));

        var overdueReturns = await assignments.CountAsync(cancellationToken);

        // Sık arızalanan: eşiği aşan servis talebi olan alet sayısı.
        var serviceCounts = db.ToolServiceRequests.AsNoTracking()
            .Where(x => x.Status != ToolServiceStatus.Cancelled);

        if (companyIds is not null)
            serviceCounts = serviceCounts.Where(x => companyIds.Contains(x.CompanyId));

        var frequentFailures = await serviceCounts
            .GroupBy(x => x.ToolAssetId)
            .Where(g => g.Count() >= FrequentFailureThreshold)
            .CountAsync(cancellationToken);

        return new ToolAssetAlertSummary(
            warrantyExpiring, inService, overdueReturns, frequentFailures);
    }
}

/// <summary>
/// Demirbaş brifing kaynağı.
///
/// YETKİ: personel görme izni ister ve sorgular kullanıcının
/// görebildiği şirketlerle sınırlanır.
/// </summary>
public sealed class ToolAssetBriefingSource(
    ToolAssetAlertService alerts) : IHizirBriefingSource
{
    public string Key => "demirbas";
    public string? RequiredPermission => PermissionCatalog.Keys.PersonnelView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var summary = await alerts.GetSummaryAsync(
            context.Scope.HasGlobalAccess ? null : context.Scope.VisibleCompanyIds,
            cancellationToken);

        var items = new List<BriefingItem>();

        if (summary.InServiceCount > 0)
        {
            items.Add(new BriefingItem(
                $"{summary.InServiceCount} alet serviste bekliyor",
                "Serviste bekleyen alet sahada eksik kalır; " +
                "onarım durumunu takip edin.",
                BriefingSeverity.Warning,
                "/demirbas/servis"));
        }

        if (summary.OverdueReturnCount > 0)
        {
            items.Add(new BriefingItem(
                $"{summary.OverdueReturnCount} zimmetin iade tarihi geçti",
                "İade edilmeyen zimmet, personel çıkışında kaybolur.",
                BriefingSeverity.Warning,
                "/insan-kaynaklari/zimmetler"));
        }

        if (summary.WarrantyExpiringCount > 0)
        {
            items.Add(new BriefingItem(
                $"{summary.WarrantyExpiringCount} aletin garantisi " +
                $"{ToolAssetAlertService.WarrantyHorizonDays} gün içinde bitiyor",
                "Garanti bitmeden arıza varsa şimdi servise verin; " +
                "sonrası ücretli.",
                BriefingSeverity.Info,
                "/demirbas"));
        }

        if (summary.FrequentFailureCount > 0)
        {
            items.Add(new BriefingItem(
                $"{summary.FrequentFailureCount} alet sık arızalanıyor",
                $"{ToolAssetAlertService.FrequentFailureThreshold} ve üzeri servis " +
                "kaydı var; yenilemek onarmaktan ucuz olabilir.",
                BriefingSeverity.Info,
                "/demirbas"));
        }

        return items;
    }
}
