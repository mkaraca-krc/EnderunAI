using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir.Briefing;

/// <summary>
/// Brifing kaynakları. Her biri tek bir konuyu bilir ve yalnızca
/// veriden konuşur — "veri yoksa madde yok" ilkesi burada uygulanır,
/// böylece brifingde dolgu cümle çıkmaz (istek #5).
///
/// Yeni modül geldiğinde bu dosyaya bir sınıf eklenip Program.cs'te
/// kaydedilmesi yeterlidir (istek #6).
/// </summary>
internal static class BriefingFormat
{
    public static string Money(decimal amount) => $"{amount:N2} TL";
}

/// <summary>Onay bekleyen işler — kullanıcının kendi izin alanında.</summary>
public sealed class PendingApprovalsBriefingSource(AppDbContext db)
    : IHizirBriefingSource
{
    public string Key => "bekleyen_onaylar";

    // İç kırılım izin izin kontrol edildiği için kaynak seviyesinde
    // ayrıca izin istenmiyor.
    public string? RequiredPermission => null;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var items = new List<BriefingItem>();

        if (context.Has(PermissionCatalog.Keys.AccountingView))
        {
            var count = await db.SupplierInvoices.CountAsync(
                x => x.Status == SupplierInvoiceStatus.PendingApproval,
                cancellationToken);

            if (count > 0)
            {
                items.Add(new BriefingItem(
                    $"{count} tedarikçi faturası onay bekliyor",
                    null, BriefingSeverity.Warning, "/muhasebe/tedarikci-faturalari"));
            }
        }

        if (context.Has(PermissionCatalog.Keys.HakedisView))
        {
            var count = await db.ProgressPayments.CountAsync(
                x => x.Status == ProgressPaymentStatus.PendingApproval,
                cancellationToken);

            if (count > 0)
            {
                items.Add(new BriefingItem(
                    $"{count} hakediş onay bekliyor",
                    null, BriefingSeverity.Warning, "/hakedis"));
            }
        }

        return items;
    }
}

/// <summary>
/// Keşif–gerçekleşen sapması: keşfin %110'unu aşan kalemler ve anahtar
/// teslim projelerde kâr erozyon alarmı.
///
/// Sözleşme tipine göre farklı konuşur: birim fiyatlıda aşım bir
/// fırsattır (ilave hakediş), anahtar teslimde kayıptır. Aynı sayıyı
/// aynı tonda söylemek yanlış olurdu.
/// </summary>
public sealed class ProgressDeviationBriefingSource(
    Services.Hakedis.IProgressTrackingService tracking) : IHizirBriefingSource
{
    public string Key => "metraj_sapmasi";
    public string? RequiredPermission => PermissionCatalog.Keys.HakedisView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        // Kapsam dışı projelerin sapması kullanıcıyı ilgilendirmez.
        var projectIds = context.Scope.HasGlobalAccess
            ? null
            : context.Scope.ProjectIds.ToList();

        if (projectIds is { Count: 0 })
            return [];

        var alerts = await tracking.GetAlertsAsync(projectIds, cancellationToken);

        if (alerts.Count == 0)
            return [];

        var items = new List<BriefingItem>();

        foreach (var alert in alerts.Where(x => x.ErosionAlarm))
        {
            items.Add(new BriefingItem(
                $"{alert.ProjectCode}: kâr erozyonu eşiği aşıldı",
                $"Keşif üstü gerçekleşmelerin net etkisi " +
                $"{BriefingFormat.Money(alert.NetDeviationAmount)}. " +
                "Anahtar teslim projede bu doğrudan kâr kaybıdır.",
                BriefingSeverity.Critical,
                $"/projeler/{alert.ProjectId}/metraj-takip"));
        }

        var exceedingOnly = alerts
            .Where(x => !x.ErosionAlarm && x.ExceedingItemCount > 0)
            .ToList();

        foreach (var alert in exceedingOnly)
        {
            var isUnitPrice =
                alert.ContractType == (int)ProjectContractType.UnitPrice;

            items.Add(new BriefingItem(
                $"{alert.ProjectCode}: {alert.ExceedingItemCount} kalem keşfin " +
                "%110'unu aştı",
                isUnitPrice
                    ? "Birim fiyatlı proje — ilave hakedişe eklenebilir."
                    : "Keşif üstü gerçekleşme kârı aşındırıyor.",
                isUnitPrice ? BriefingSeverity.Info : BriefingSeverity.Warning,
                $"/projeler/{alert.ProjectId}/metraj-takip"));
        }

        return items;
    }
}

/// <summary>Yaklaşan ve geçmiş çek vadeleri.</summary>
public sealed class ChequeDueBriefingSource(AppDbContext db) : IHizirBriefingSource
{
    private const int Horizon = 7;

    private static readonly ChequeStatus[] OpenStatuses =
    [
        ChequeStatus.Portfolio, ChequeStatus.AtBank,
        ChequeStatus.AtFactoring, ChequeStatus.Issued
    ];

    public string Key => "cek_vadeleri";
    public string? RequiredPermission => PermissionCatalog.Keys.FinanceView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var until = today.AddDays(Horizon);

        var rows = await db.Cheques
            .AsNoTracking()
            .Where(x => OpenStatuses.Contains(x.Status) && x.DueDate <= until)
            .Select(x => new { x.Direction, x.Amount, x.DueDate })
            .ToListAsync(cancellationToken);

        var items = new List<BriefingItem>();

        // Vadesi geçmiş ama hâlâ açık çek en kritik uyarı.
        var overdue = rows.Where(x => x.DueDate.Date < today).ToList();

        if (overdue.Count > 0)
        {
            items.Add(new BriefingItem(
                $"{overdue.Count} çekin vadesi geçmiş, hâlâ açık",
                $"Toplam {BriefingFormat.Money(overdue.Sum(x => x.Amount))}",
                BriefingSeverity.Critical, "/finans/cekler"));
        }

        var issued = rows
            .Where(x => x.DueDate.Date >= today && x.Direction == ChequeDirection.Issued)
            .ToList();

        if (issued.Count > 0)
        {
            items.Add(new BriefingItem(
                $"{Horizon} gün içinde {issued.Count} verilen çekin vadesi geliyor",
                $"Ödenecek toplam {BriefingFormat.Money(issued.Sum(x => x.Amount))}",
                BriefingSeverity.Warning, "/finans/cekler"));
        }

        var received = rows
            .Where(x => x.DueDate.Date >= today && x.Direction == ChequeDirection.Received)
            .ToList();

        if (received.Count > 0)
        {
            items.Add(new BriefingItem(
                $"{Horizon} gün içinde {received.Count} alınan çekin vadesi geliyor",
                $"Tahsil edilecek toplam {BriefingFormat.Money(received.Sum(x => x.Amount))}",
                BriefingSeverity.Info, "/finans/cekler"));
        }

        return items;
    }
}

/// <summary>Dün girilmemiş günlük saha raporları.</summary>
public sealed class MissingSiteReportBriefingSource(AppDbContext db)
    : IHizirBriefingSource
{
    public string Key => "eksik_saha_raporu";
    public string? RequiredPermission => PermissionCatalog.Keys.SiteReportsView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        var sites = db.ProjectSites.AsNoTracking().Where(x => x.IsActive);

        if (!context.Scope.HasGlobalAccess)
        {
            var siteIds = context.Scope.SiteIds;
            sites = sites.Where(x => siteIds.Contains(x.Id));
        }

        var missing = await sites
            .Where(site => !db.ProjectSiteDailyReports
                .Any(report => report.ProjectSiteId == site.Id &&
                               report.ReportDate.Date == yesterday))
            .CountAsync(cancellationToken);

        if (missing == 0)
            return [];

        return
        [
            new BriefingItem(
                $"{missing} şantiyenin dünkü günlük raporu girilmemiş",
                $"{yesterday:dd.MM.yyyy} tarihi için rapor bekleniyor",
                BriefingSeverity.Warning, "/santiye/gunluk-raporlar")
        ];
    }
}

/// <summary>Kritik seviyenin altına düşen stoklar.</summary>
public sealed class CriticalStockBriefingSource(AppDbContext db)
    : IHizirBriefingSource
{
    public string Key => "kritik_stok";
    public string? RequiredPermission => PermissionCatalog.Keys.InventoryView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        // Minimum stok tanımlanmamış (0) kalemler uyarı üretmemeli;
        // aksi halde her kalem kritik görünürdü.
        var criticalQuery = db.InventoryItems
            .AsNoTracking()
            .Where(item => item.MinimumStock > 0m &&
                           db.WarehouseStocks
                               .Where(stock => stock.InventoryItemId == item.Id)
                               .Sum(stock => (decimal?)stock.Quantity) < item.MinimumStock);

        // Sayım Take'ten ÖNCE alınıyor: daha önce ilk 5 kayıt çekilip
        // "critical.Count" raporlandığı için 30 malzeme kritikken de
        // "5 malzeme" yazıyordu.
        var criticalCount = await criticalQuery.CountAsync(cancellationToken);

        if (criticalCount == 0)
            return [];

        var names = await criticalQuery
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .Take(5)
            .ToListAsync(cancellationToken);

        var detail = string.Join(", ", names);
        if (criticalCount > names.Count)
            detail += $" ve {criticalCount - names.Count} kalem daha";

        return
        [
            new BriefingItem(
                $"{criticalCount} malzeme minimum stok seviyesinin altında",
                detail,
                // Bağlantı /stok idi; böyle bir sayfa yok, 404 veriyordu.
                BriefingSeverity.Warning, "/depo-stok")
        ];
    }
}

/// <summary>Geçerlilik süresi dolmak üzere olan teklifler.</summary>
public sealed class OfferValidityBriefingSource(AppDbContext db)
    : IHizirBriefingSource
{
    private const int Horizon = 7;

    public string Key => "teklif_gecerliligi";
    public string? RequiredPermission => PermissionCatalog.Keys.EngineeringView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var until = today.AddDays(Horizon);

        var count = await db.Offers
            .AsNoTracking()
            .CountAsync(x => x.ValidUntil != null &&
                             x.ValidUntil.Value.Date >= today &&
                             x.ValidUntil.Value.Date <= until &&
                             (x.Status == OfferStatus.Submitted ||
                              x.Status == OfferStatus.Draft),
                cancellationToken);

        if (count == 0)
            return [];

        return
        [
            new BriefingItem(
                $"{count} teklifin geçerlilik süresi {Horizon} gün içinde doluyor",
                null, BriefingSeverity.Warning, "/teklifler")
        ];
    }
}

/// <summary>
/// Hızır'ın hazırlayıp kullanıcının henüz onaylamadığı eylemler.
/// Kullanıcı unuttuysa hatırlatır; süresi dolmadan görsün.
/// </summary>
public sealed class HizirPendingActionBriefingSource(AppDbContext db)
    : IHizirBriefingSource
{
    public string Key => "onay_bekleyen_hizir_eylemleri";
    public string? RequiredPermission => PermissionCatalog.Keys.AiUse;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var count = await db.HizirPendingActions
            .AsNoTracking()
            .CountAsync(x => x.UserId == context.UserId &&
                             x.Status == HizirPendingActionStatus.Pending &&
                             x.ExpiresAtUtc > now,
                cancellationToken);

        if (count == 0)
            return [];

        return
        [
            new BriefingItem(
                $"Hazırladığım {count} işlem onayınızı bekliyor",
                "Onaylamazsanız bu işlemler yapılmaz.",
                BriefingSeverity.Info, "/ai-asistan")
        ];
    }
}

/// <summary>
/// Maliyet aşımı: gerçekleşen maliyet, icmal öngörüsünün ilerlemeye
/// göre düzeltilmiş halini aşan bileşenler.
///
/// Eşik projenin kendi <c>DeviationAlertThresholdRate</c> ayarından
/// gelir; tanımlı değilse %10 kullanılır. Sabit bir eşik kodlansaydı
/// dar marjlı projede geç, geniş marjlıda gereksiz uyarı verirdi.
/// </summary>
public sealed class ProjectCostOverrunBriefingSource(
    AppDbContext db,
    Services.Projects.IProjectCostAnalysisService analysis) : IHizirBriefingSource
{
    /// <summary>Ayarı olmayan projede kullanılan varsayılan sapma eşiği.</summary>
    private const decimal DefaultThresholdPercent = 10m;

    public string Key => "maliyet_asimi";
    public string? RequiredPermission => PermissionCatalog.Keys.ProjectsView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var query = db.Projects
            .AsNoTracking()
            .Where(x => x.Status == ProjectStatus.Active);

        // Kapsam dışı projenin maliyeti kullanıcıyı ilgilendirmez —
        // göstermediğimiz veriyi hesaplamayız da.
        if (!context.Scope.HasGlobalAccess)
        {
            var scopeIds = context.Scope.ProjectIds.ToList();

            if (scopeIds.Count == 0)
                return [];

            query = query.Where(x => scopeIds.Contains(x.Id));
        }

        var projects = await query
            .Select(x => new { x.Id, x.Code, x.DeviationAlertThresholdRate })
            .ToListAsync(cancellationToken);

        var items = new List<BriefingItem>();

        foreach (var project in projects)
        {
            var result = await analysis.AnalyzeAsync(project.Id, cancellationToken);

            // İcmali olmayan projede karşılaştırılacak öngörü yok;
            // uyarı üretmek uydurma olurdu.
            if (result is null || !result.HasContractBaseline)
                continue;

            var threshold = project.DeviationAlertThresholdRate > 0m
                ? project.DeviationAlertThresholdRate
                : DefaultThresholdPercent;

            foreach (var component in result.Components)
            {
                // Taşeron satırı işçilik satırının içinde de sayıldığı
                // için ayrıca uyarı üretmez; yoksa aynı aşım iki kez
                // bildirilirdi.
                if (component.CostClass == (int)ProjectCostClass.SubcontractorLabor)
                    continue;

                if (component.ForecastEarned <= 0m ||
                    component.VariancePercent is not decimal percent ||
                    percent < threshold)
                {
                    continue;
                }

                items.Add(new BriefingItem(
                    $"{project.Code}: {component.CostClassName.ToLowerInvariant()} maliyeti " +
                    $"icmal öngörüsünü %{percent:N1} aştı",
                    $"Hakedilen öngörü {BriefingFormat.Money(component.ForecastEarned)}, " +
                    $"gerçekleşen {BriefingFormat.Money(component.Actual)}.",
                    percent >= threshold * 2m
                        ? BriefingSeverity.Critical
                        : BriefingSeverity.Warning,
                    $"/projeler/{project.Id}/maliyet-analizi"));
            }
        }

        return items;
    }
}

/// <summary>
/// Yaklaşan vergi ödemeleri ve gecikmiş yükümlülükler.
///
/// Tutarlar TAHMİNİDİR ve bu her maddede yazar: kesin beyan müşavirde
/// yapılır, brifing yalnızca "şu kadar nakit ayırın" der.
/// </summary>
public sealed class TaxDueBriefingSource(
    AppDbContext db,
    Services.Tax.ITaxObligationService taxObligations) : IHizirBriefingSource
{
    /// <summary>Kaç gün önceden hatırlatılacağı.</summary>
    private const int ReminderWindowDays = 15;

    public string Key => "vergi_odemeleri";
    public string? RequiredPermission => PermissionCatalog.Keys.AccountingView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        // Vergi şirket düzeyinde bir yükümlülük; şirket kapsamı olmayan
        // kullanıcıya gösterilmez.
        var companyIds = context.Scope.HasGlobalAccess
            ? await db.Companies.AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync(cancellationToken)
            : context.Scope.VisibleCompanyIds.ToList();

        if (companyIds.Count == 0)
            return [];

        var today = DateTime.UtcNow.Date;
        var items = new List<BriefingItem>();

        foreach (var companyId in companyIds)
        {
            var obligations = await taxObligations.GetObligationsAsync(
                companyId,
                today.AddDays(-90),
                today.AddDays(ReminderWindowDays),
                cancellationToken);

            var unpaid = obligations
                .Where(x => !x.IsPaid && x.EstimatedAmount > 0m)
                .ToList();

            foreach (var overdue in unpaid.Where(x => x.IsOverdue))
            {
                items.Add(new BriefingItem(
                    $"{overdue.KindName} ödemesi gecikti — {overdue.PeriodLabel}",
                    $"Vadesi {overdue.DueDate:dd.MM.yyyy}, tahmini tutar " +
                    $"{BriefingFormat.Money(overdue.EstimatedAmount)}. " +
                    "Ödendiyse vergi takviminden işaretleyin.",
                    BriefingSeverity.Critical,
                    "/finans/vergi"));
            }

            var upcoming = unpaid.Where(x => !x.IsOverdue).ToList();

            if (upcoming.Count == 0)
                continue;

            var total = upcoming.Sum(x => x.EstimatedAmount);
            var nearest = upcoming.Min(x => x.DueDate);

            items.Add(new BriefingItem(
                $"Yaklaşan vergi ödemesi: {BriefingFormat.Money(total)} (tahmini)",
                $"İlk vade {nearest:dd.MM.yyyy}. " +
                string.Join(", ", upcoming.Select(x =>
                    $"{x.KindName} {BriefingFormat.Money(x.EstimatedAmount)}")) +
                ". Tutarlar defterden hesaplanan tahminlerdir; beyan müşavirde kesinleşir.",
                BriefingSeverity.Warning,
                "/finans/vergi"));
        }

        return items;
    }
}
