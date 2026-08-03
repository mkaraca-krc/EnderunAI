using System.Globalization;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir;

public interface IHizirToolRegistry
{
    IReadOnlyList<HizirTool> All { get; }

    /// <summary>Kullanıcının izinlerine göre kullanabileceği araçlar.</summary>
    IReadOnlyList<HizirTool> AvailableFor(HizirToolContext context);

    HizirTool? Find(string name);
}

/// <summary>
/// Hızır'ın canlı veri araçları. Hepsi salt-okunur.
///
/// Yetki burada yapısal olarak uygulanır: modele yalnızca kullanıcının
/// izin verdiği araçlar tanıtılır, üstelik her araç kendi içinde izni
/// tekrar kontrol eder ve sorguyu kullanıcının veri kapsamıyla sınırlar.
/// Model, kullanıcının göremeyeceği veriyi elde edemez — filtrelenmiş
/// metni "görmezden gelmesi" beklenmiyor, veri hiç dönmüyor.
///
/// Araçlar özet döner (ham satır değil) ve satır tavanı uygular; bağlama
/// giren token miktarı böyle sınırlanıyor.
/// </summary>
public sealed class HizirToolRegistry : IHizirToolRegistry
{
    /// <summary>Bir araç sonucunda dönebilecek en fazla satır.</summary>
    private const int RowLimit = 25;

    private static readonly CultureInfo Tr = new("tr-TR");

    private readonly AppDbContext _db;
    private readonly IHizirKnowledgeBase _knowledgeBase;
    private readonly List<HizirTool> _tools;

    public HizirToolRegistry(
        AppDbContext db,
        IHizirKnowledgeBase knowledgeBase,
        HizirActionTools actionTools)
    {
        _db = db;
        _knowledgeBase = knowledgeBase;

        // Katman 1 okuma araçları + Katman 2 eylem araçları.
        _tools = [.. BuildTools(), .. actionTools.Build()];
    }

    public IReadOnlyList<HizirTool> All => _tools;

    public IReadOnlyList<HizirTool> AvailableFor(HizirToolContext context) =>
        _tools
            .Where(tool =>
                tool.RequiredPermission is null || context.Has(tool.RequiredPermission))
            .ToList();

    public HizirTool? Find(string name) =>
        _tools.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    private static object EmptySchema() => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    private static object SchemaWith(params (string Name, string Type, string Description)[] fields)
    {
        var properties = new Dictionary<string, object>();

        foreach (var field in fields)
        {
            properties[field.Name] = new
            {
                type = field.Type,
                description = field.Description
            };
        }

        return new
        {
            type = "object",
            properties,
            required = Array.Empty<string>()
        };
    }

    private static string? Text(IReadOnlyDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static string Money(decimal value) =>
        value.ToString("N2", Tr) + " TL";

    private static HizirToolOutcome NoData(string what) =>
        new($"KAYIT YOK: {what} için sistemde veri bulunamadı. " +
            "Kullanıcıya veri olmadığını söyle, tahmin veya örnek üretme.");

    private List<HizirTool> BuildTools() =>
    [
        new HizirTool(
            "projeleri_listele",
            "Kullanıcının erişebildiği projeleri ve durumlarını listeler. " +
            "Belirli bir projeyi aramak için 'arama' parametresini kullan.",
            SchemaWith(("arama", "string", "Proje kodu veya adında aranacak metin")),
            PermissionCatalog.Keys.ProjectsView,
            ListProjectsAsync),

        new HizirTool(
            "santiye_gunluk_raporlari",
            "Şantiyelerin son günlük saha raporlarını özetler: tarih, " +
            "şantiye, personel sayısı ve rapor durumu.",
            SchemaWith(
                ("proje", "string", "Proje kodu veya adı"),
                ("gun_sayisi", "integer", "Kaç günlük geçmişe bakılacak (varsayılan 7)")),
            PermissionCatalog.Keys.SiteReportsView,
            ListDailyReportsAsync),

        new HizirTool(
            "stok_durumu",
            "Depolardaki stok durumunu verir. Belirli bir malzeme için " +
            "'arama' parametresini kullan.",
            SchemaWith(("arama", "string", "Malzeme kodu veya adında aranacak metin")),
            PermissionCatalog.Keys.InventoryView,
            ListStockAsync),

        new HizirTool(
            "cari_bakiye",
            "Cari hesapların borç/alacak bakiyesini muhasebe defterinden " +
            "hesaplar. Belirli bir cari için 'arama' parametresini kullan.",
            SchemaWith(("arama", "string", "Cari unvanında veya kodunda aranacak metin")),
            PermissionCatalog.Keys.CurrentAccountsView,
            CurrentAccountBalanceAsync),

        new HizirTool(
            "cek_defteri",
            "Alınan ve verilen çekleri durum ve vadeye göre özetler. " +
            "Yaklaşan vadeleri sormak için 'gun_sayisi' kullan.",
            SchemaWith(
                ("yon", "string", "'alinan' veya 'verilen'"),
                ("gun_sayisi", "integer", "Önümüzdeki kaç günün vadesi (varsayılan 30)")),
            PermissionCatalog.Keys.FinanceView,
            ChequeRegisterAsync),

        new HizirTool(
            "nakit_akis",
            "Vade bazlı nakit akışı: mevcut kasa/banka bakiyesi ve " +
            "önümüzdeki 30/60/90 gün beklenen tahsilat/ödeme.",
            EmptySchema(),
            PermissionCatalog.Keys.FinanceView,
            CashFlowAsync),

        new HizirTool(
            "muhasebe_ozeti",
            "Son muhasebe fişlerini ve dönem borç/alacak toplamlarını özetler.",
            SchemaWith(("gun_sayisi", "integer", "Kaç günlük geçmiş (varsayılan 30)")),
            PermissionCatalog.Keys.AccountingView,
            AccountingSummaryAsync),

        new HizirTool(
            "bordro_ozeti",
            "Aylık bordro özeti: personel sayısı, toplam brüt, net ve " +
            "işverene toplam maliyet. Ücret bilgisi gizlidir.",
            SchemaWith(
                ("yil", "integer", "Yıl"),
                ("ay", "integer", "Ay (1-12)")),
            PermissionCatalog.Keys.SalaryView,
            PayrollSummaryAsync),

        new HizirTool(
            "bekleyen_onaylar",
            "Kullanıcının görebildiği bekleyen onayları sayar: tedarikçi " +
            "faturaları, hakedişler, günlük saha raporları.",
            EmptySchema(),
            null,
            PendingApprovalsAsync),

        new HizirTool(
            "kilavuz_ara",
            "Sistemin kullanım kılavuzunda arama yapar: hangi işlem hangi " +
            "sayfadan yapılır, menüde nerededir. Kullanıcı 'nereden " +
            "yaparım', 'bulamıyorum' gibi bir şey sorduğunda bunu kullan.",
            SchemaWith(("konu", "string", "Aranan işlem veya modül")),
            null,
            SearchKnowledgeBaseAsync)
    ];

    // --- Araç uygulamaları ---

    private async Task<HizirToolOutcome> ListProjectsAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var query = context.Scope.Apply(_db.Projects.AsNoTracking());

        var search = Text(args, "arama");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term));
        }

        var rows = await query
            .OrderBy(x => x.Code)
            .Take(RowLimit)
            .Select(x => new
            {
                x.Code,
                x.Name,
                x.Status,
                EmployerName = x.EmployerCurrentAccount != null
                    ? x.EmployerCurrentAccount.Title
                    : null,
                x.ContractAmount,
                x.CurrencyCode
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return NoData("Projeler");

        var builder = new StringBuilder($"{rows.Count} proje bulundu:\n");

        foreach (var row in rows)
        {
            builder.AppendLine(
                $"- {row.Code} | {row.Name} | durum: {ProjectStatusName(row.Status)}" +
                $"{(row.EmployerName is null ? "" : $" | işveren: {row.EmployerName}")}" +
                $"{(row.ContractAmount is null ? "" : $" | sözleşme: {Money(row.ContractAmount.Value)}")}");
        }

        return new HizirToolOutcome(builder.ToString());
    }

    private static string ProjectStatusName(ProjectStatus status) => status switch
    {
        ProjectStatus.Kesif => "Keşif",
        ProjectStatus.Active => "Aktif",
        ProjectStatus.Completed => "Tamamlandı",
        ProjectStatus.Cancelled => "İptal",
        _ => status.ToString()
    };

    private async Task<HizirToolOutcome> ListDailyReportsAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var days = ReadInt(args, "gun_sayisi", 7, 1, 90);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var query = _db.ProjectSiteDailyReports
            .AsNoTracking()
            .Where(x => x.ReportDate >= since);

        // Şantiye kapsamı: global erişimi olmayan kullanıcı yalnızca
        // atandığı şantiyelerin raporlarını görür.
        if (!context.Scope.HasGlobalAccess)
        {
            var siteIds = context.Scope.SiteIds;
            var projectIds = context.Scope.ProjectIds;

            query = query.Where(x =>
                siteIds.Contains(x.ProjectSiteId) ||
                projectIds.Contains(x.ProjectSite.ProjectId) ||
                context.Scope.CompanyIds.Contains(x.ProjectSite.Project.CompanyId));
        }

        var project = Text(args, "proje");
        if (!string.IsNullOrWhiteSpace(project))
        {
            var term = project.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.ProjectSite.Project.Code.ToLower().Contains(term) ||
                x.ProjectSite.Project.Name.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(x => x.ReportDate)
            .Take(RowLimit)
            .Select(x => new
            {
                x.ReportDate,
                SiteName = x.ProjectSite.Name,
                ProjectCode = x.ProjectSite.Project.Code,
                Total = x.EngineerCount + x.ForemanCount + x.CraftsmanCount +
                        x.WorkerCount + x.OtherCount,
                x.Status,
                x.WeatherCondition
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return NoData($"Son {days} günün günlük saha raporları");

        var builder = new StringBuilder(
            $"Son {days} günde {rows.Count} günlük rapor:\n");

        foreach (var row in rows)
        {
            builder.AppendLine(
                $"- {row.ReportDate:dd.MM.yyyy} | {row.ProjectCode} / {row.SiteName} " +
                $"| toplam personel: {row.Total} | durum: {row.Status}" +
                $"{(row.WeatherCondition is null ? "" : $" | hava: {row.WeatherCondition}")}");
        }

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> ListStockAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var query = _db.WarehouseStocks.AsNoTracking().Where(x => x.Quantity != 0m);

        var search = Text(args, "arama");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.InventoryItem.Code.ToLower().Contains(term) ||
                x.InventoryItem.Name.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(x => x.Quantity)
            .Take(RowLimit)
            .Select(x => new
            {
                ItemCode = x.InventoryItem.Code,
                ItemName = x.InventoryItem.Name,
                WarehouseName = x.Warehouse.Name,
                x.Quantity,
                x.ReservedQuantity,
                Unit = x.InventoryItem.Unit
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return NoData("Stok");

        var builder = new StringBuilder($"{rows.Count} stok kaydı:\n");

        foreach (var row in rows)
        {
            builder.AppendLine(
                $"- {row.ItemCode} {row.ItemName} | {row.WarehouseName} | " +
                $"mevcut: {row.Quantity:N2} {row.Unit} | rezerve: {row.ReservedQuantity:N2}");
        }

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> CurrentAccountBalanceAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var accounts = _db.CurrentAccounts.AsNoTracking().AsQueryable();

        var search = Text(args, "arama");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            accounts = accounts.Where(x =>
                x.Title.ToLower().Contains(term) || x.Code.ToLower().Contains(term));
        }

        var candidates = await accounts
            .OrderBy(x => x.Title)
            .Take(RowLimit)
            .Select(x => new { x.Id, x.Code, x.Title })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return NoData("Cari hesap");

        var ids = candidates.Select(x => x.Id).ToList();

        // Bakiye paralel bir defterden değil, kesinleşmiş muhasebe
        // fişlerinin cari boyutundan hesaplanır (Faz C ilkesi).
        var balances = await _db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => x.CurrentAccountId != null &&
                        ids.Contains(x.CurrentAccountId.Value) &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted)
            .GroupBy(x => x.CurrentAccountId!.Value)
            .Select(g => new
            {
                CurrentAccountId = g.Key,
                Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount)
            })
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder("Cari bakiyeler (kesinleşmiş fişlerden):\n");
        var any = false;

        foreach (var account in candidates)
        {
            var balance = balances.FirstOrDefault(x => x.CurrentAccountId == account.Id);

            if (balance is null)
            {
                builder.AppendLine($"- {account.Code} {account.Title} | hareket yok");
                continue;
            }

            any = true;
            var net = balance.Debit - balance.Credit;
            var label = net > 0 ? "bizden alacaklı değil, borçlu" : "bize borçlu değil, alacaklı";

            builder.AppendLine(
                $"- {account.Code} {account.Title} | borç: {Money(balance.Debit)} | " +
                $"alacak: {Money(balance.Credit)} | bakiye: {Money(Math.Abs(net))} " +
                $"({(net > 0 ? "borçlu" : net < 0 ? "alacaklı" : "kapalı")})");
        }

        if (!any)
            builder.AppendLine("(Hiçbir carinin kesinleşmiş fiş hareketi yok.)");

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> ChequeRegisterAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var days = ReadInt(args, "gun_sayisi", 30, 1, 365);
        var until = DateTime.UtcNow.Date.AddDays(days);

        var query = _db.Cheques.AsNoTracking().AsQueryable();

        var direction = Text(args, "yon")?.Trim().ToLowerInvariant();
        if (direction is "alinan" or "alınan")
            query = query.Where(x => x.Direction == ChequeDirection.Received);
        else if (direction is "verilen")
            query = query.Where(x => x.Direction == ChequeDirection.Issued);

        // Yalnızca açık (henüz kapanmamış) çekler ve yaklaşan vadeler.
        var openStatuses = new[]
        {
            ChequeStatus.Portfolio, ChequeStatus.AtBank,
            ChequeStatus.AtFactoring, ChequeStatus.Issued
        };

        var rows = await query
            .Where(x => openStatuses.Contains(x.Status) && x.DueDate <= until)
            .OrderBy(x => x.DueDate)
            .Take(RowLimit)
            .Select(x => new
            {
                x.Direction,
                x.ChequeNumber,
                x.BankName,
                x.Amount,
                x.DueDate,
                x.Status,
                CurrentAccountTitle = x.CurrentAccount != null ? x.CurrentAccount.Title : null
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return NoData($"Önümüzdeki {days} gün içinde vadesi gelen açık çek");

        var builder = new StringBuilder(
            $"Önümüzdeki {days} günde vadesi gelen {rows.Count} çek:\n");

        foreach (var row in rows)
        {
            var kind = row.Direction == ChequeDirection.Received ? "Alınan" : "Verilen";

            builder.AppendLine(
                $"- {kind} | çek no {row.ChequeNumber} | {row.BankName} | " +
                $"vade: {row.DueDate:dd.MM.yyyy} | {Money(row.Amount)} | " +
                $"durum: {row.Status}" +
                $"{(row.CurrentAccountTitle is null ? "" : $" | cari: {row.CurrentAccountTitle}")}");
        }

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> CashFlowAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var companyIds = context.Scope.HasGlobalAccess
            ? await _db.Companies.Select(x => x.Id).ToListAsync(cancellationToken)
            : context.Scope.VisibleCompanyIds.ToList();

        if (companyIds.Count == 0)
            return NoData("Nakit akışı");

        var opening = await _db.CashAccounts
            .Where(x => companyIds.Contains(x.CompanyId) && x.IsActive)
            .SumAsync(x => (decimal?)x.OpeningBalance, cancellationToken) ?? 0m;

        var movements = await _db.CashTransactions
            .Where(x => companyIds.Contains(x.CashAccount.CompanyId))
            .GroupBy(x => x.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var balance = opening
            + movements.Where(x => x.Direction == CashTransactionDirection.In).Sum(x => x.Total)
            - movements.Where(x => x.Direction == CashTransactionDirection.Out).Sum(x => x.Total);

        var today = DateTime.UtcNow.Date;
        var builder = new StringBuilder(
            $"Mevcut kasa/banka bakiyesi: {Money(balance)}\n");

        foreach (var days in new[] { 30, 60, 90 })
        {
            var limit = today.AddDays(days);

            var inflow = await _db.Cheques
                .Where(x => companyIds.Contains(x.CompanyId) &&
                            x.Direction == ChequeDirection.Received &&
                            (x.Status == ChequeStatus.Portfolio || x.Status == ChequeStatus.AtBank) &&
                            x.DueDate >= today && x.DueDate <= limit)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

            var outflow = await _db.Cheques
                .Where(x => companyIds.Contains(x.CompanyId) &&
                            x.Direction == ChequeDirection.Issued &&
                            x.Status == ChequeStatus.Issued &&
                            x.DueDate >= today && x.DueDate <= limit)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

            builder.AppendLine(
                $"- {days} gün: girecek {Money(inflow)} | çıkacak {Money(outflow)} | " +
                $"net {Money(inflow - outflow)} | tahmini bakiye {Money(balance + inflow - outflow)}");
        }

        builder.AppendLine(
            "(Yalnızca çek vadeleri; hakediş ve fatura vadeleri Nakit Akışı " +
            "ekranında ayrıca listelenir.)");

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> AccountingSummaryAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var days = ReadInt(args, "gun_sayisi", 30, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var query = _db.AccountingVouchers
            .AsNoTracking()
            .Where(x => x.VoucherDate >= since &&
                        x.Status == AccountingVoucherStatus.Posted);

        if (!context.Scope.HasGlobalAccess)
        {
            var companyIds = context.Scope.VisibleCompanyIds;
            query = query.Where(x => companyIds.Contains(x.CompanyId));
        }

        var rows = await query
            .OrderByDescending(x => x.VoucherDate)
            .Take(RowLimit)
            .Select(x => new
            {
                x.VoucherNumber,
                x.VoucherDate,
                x.Description,
                x.TotalDebit,
                x.SourceModule
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return NoData($"Son {days} günün kesinleşmiş muhasebe fişleri");

        var total = rows.Sum(x => x.TotalDebit);

        var builder = new StringBuilder(
            $"Son {days} günde {rows.Count} kesinleşmiş fiş, toplam {Money(total)}:\n");

        foreach (var row in rows)
        {
            builder.AppendLine(
                $"- {row.VoucherNumber} | {row.VoucherDate:dd.MM.yyyy} | " +
                $"{Money(row.TotalDebit)} | {row.SourceModule ?? "elle"} | {row.Description}");
        }

        return new HizirToolOutcome(builder.ToString());
    }

    private async Task<HizirToolOutcome> PayrollSummaryAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var year = ReadInt(args, "yil", now.Year, 2000, 2100);
        var month = ReadInt(args, "ay", now.Month, 1, 12);

        // Bordro kayıtları HrDbContext'te; aynı veritabanında olduğu için
        // salt-okunur sorgu buradan yürütülür.
        var rows = await _db.Database
            .SqlQueryRaw<PayrollSummaryRow>(
                """
                SELECT COUNT(*)::int                       AS "PersonnelCount",
                       COALESCE(SUM("TotalEarnings"), 0)   AS "TotalEarnings",
                       COALESCE(SUM("OfficialNetPayableAmount"), 0) AS "NetPayable",
                       COALESCE(SUM("TotalEmployerCost"), 0)       AS "TotalEmployerCost"
                  FROM hr_payroll_records
                 WHERE "Year" = {0} AND "Month" = {1} AND "IsDeleted" = false
                """,
                year, month)
            .ToListAsync(cancellationToken);

        var summary = rows.FirstOrDefault();

        if (summary is null || summary.PersonnelCount == 0)
            return NoData($"{month:00}/{year} dönemi bordrosu");

        return new HizirToolOutcome(
            $"{month:00}/{year} bordro özeti: {summary.PersonnelCount} personel | " +
            $"toplam brüt kazanç {Money(summary.TotalEarnings)} | " +
            $"net ödenecek {Money(summary.NetPayable)} | " +
            $"işverene toplam maliyet {Money(summary.TotalEmployerCost)}");
    }

    private sealed record PayrollSummaryRow(
        int PersonnelCount,
        decimal TotalEarnings,
        decimal NetPayable,
        decimal TotalEmployerCost);

    private async Task<HizirToolOutcome> PendingApprovalsAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        if (context.Has(PermissionCatalog.Keys.AccountingView))
        {
            var count = await _db.SupplierInvoices
                .CountAsync(x => x.Status == SupplierInvoiceStatus.PendingApproval,
                    cancellationToken);

            if (count > 0)
                lines.Add($"- {count} tedarikçi faturası onay bekliyor");
        }

        if (context.Has(PermissionCatalog.Keys.HakedisView))
        {
            var count = await _db.ProgressPayments
                .CountAsync(x => x.Status == ProgressPaymentStatus.PendingApproval,
                    cancellationToken);

            if (count > 0)
                lines.Add($"- {count} hakediş onay bekliyor");
        }

        if (context.Has(PermissionCatalog.Keys.SiteReportsView))
        {
            var query = _db.ProjectSiteDailyReports
                .Where(x => x.Status == ProjectSiteDailyReportStatus.Draft);

            if (!context.Scope.HasGlobalAccess)
            {
                var siteIds = context.Scope.SiteIds;
                query = query.Where(x => siteIds.Contains(x.ProjectSiteId));
            }

            var count = await query.CountAsync(cancellationToken);

            if (count > 0)
                lines.Add($"- {count} günlük saha raporu taslak durumda (onaylanmamış)");
        }

        if (lines.Count == 0)
            return new HizirToolOutcome("Bekleyen onay yok.");

        return new HizirToolOutcome(
            "Bekleyen onaylar:\n" + string.Join("\n", lines));
    }

    private Task<HizirToolOutcome> SearchKnowledgeBaseAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var topic = Text(args, "konu") ?? string.Empty;
        var result = _knowledgeBase.Search(topic, context.Permissions);

        return Task.FromResult(string.IsNullOrWhiteSpace(result)
            ? new HizirToolOutcome(
                "KAYIT YOK: Kullanım kılavuzunda bu konuyla eşleşen ve " +
                "kullanıcının yetkisi olan bir sayfa bulunamadı. " +
                "Yetkisi olmayan sayfaları tarif etme.")
            : new HizirToolOutcome(result));
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, object?> args, string key,
        int fallback, int min, int max)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
            return fallback;

        var parsed = value switch
        {
            long longValue => (int)longValue,
            int intValue => intValue,
            double doubleValue => (int)doubleValue,
            string text when int.TryParse(text, out var textValue) => textValue,
            _ => fallback
        };

        return Math.Clamp(parsed, min, max);
    }
}
