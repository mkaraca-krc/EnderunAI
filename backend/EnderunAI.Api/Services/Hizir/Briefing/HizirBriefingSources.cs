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
        var critical = await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.MinimumStock > 0m &&
                           db.WarehouseStocks
                               .Where(stock => stock.InventoryItemId == item.Id)
                               .Sum(stock => (decimal?)stock.Quantity) < item.MinimumStock)
            .Select(item => item.Name)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (critical.Count == 0)
            return [];

        return
        [
            new BriefingItem(
                $"{critical.Count} malzeme minimum stok seviyesinin altında",
                string.Join(", ", critical),
                BriefingSeverity.Warning, "/stok")
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
