using EnderunAI.Api.Formatting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Procurement;
using EnderunAI.Api.Services.Projects;

namespace EnderunAI.Api.Services.Management;

/// <summary>Bir KPI'nın değerinin nasıl biçimleneceği.</summary>
public enum KpiValueKind
{
    Money = 0,
    Count = 1,
    Percent = 2
}

/// <summary>
/// Tek bir yönetim göstergesi.
///
/// <c>PreviousValue</c> yalnızca kaynağı DÖNEM ALAN KPI'larda dolu.
/// Dönemi olmayan bir kaynağa yön oku uydurmak, olmayan bir eğilimi
/// varmış gibi gösterirdi.
/// </summary>
public sealed record ManagementKpi(
    string Key,
    string Title,
    decimal Value,
    KpiValueKind Kind,
    /// <summary>İkincil satır: "en kötü: X projesi", "3 çek 7 gün içinde".</summary>
    string? Detail,
    /// <summary>Maskeleme/eksik veri uyarısı; kaynağın kendi notu.</summary>
    string? Note,
    decimal? PreviousValue,
    /// <summary>Kartın tıklandığında gideceği ekran.</summary>
    string Link);

/// <summary>Yetkisi olan ama KAYNAĞI üretilemeyen KPI.</summary>
public sealed record ManagementKpiUnavailable(string Key, string Title, string Reason);

public sealed record ManagementKpiResponse(
    Guid CompanyId,
    int Year,
    int Month,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ManagementKpi> Kpis,
    IReadOnlyList<ManagementKpiUnavailable> Unavailable);

/// <summary>
/// Yönetim KPI'ları.
///
/// ANA KURAL — OKUR, YENİDEN HESAPLAMAZ. Her KPI, o alanın yetkili
/// servisinden geliyor: nakit projeksiyonu, kârlılık özeti, gider
/// merkezi raporu, bordro özeti, satın alma dashboard'u, çek özeti.
/// Buraya tek bir toplama ya da filtre yazılırsa, aynı sayı iki yerde
/// hesaplanmış olur ve zamanla ayrışır. Testler bunu birebir
/// karşılaştırıyor.
///
/// YETKİSİZ KPI YANITA HİÇ GİRMEZ — "unavailable" listesine bile.
/// Kilitli bir kart göstermek, o KPI'nın var olduğunu ve (kart
/// boyutundan) mertebesini ele verirdi. <c>Unavailable</c> yalnızca
/// kullanıcının YETKİSİ OLDUĞU ama kaynağın üretilemediği durumlar
/// için.
///
/// KAYNAK HATASI TÜM SAYFAYI DÜŞÜRMEZ: bir servis patlarsa o KPI
/// "unavailable" olur, kalanı gelir. Tek bozuk sorgu yüzünden
/// yöneticinin hiçbir göstergeyi görememesi daha kötü.
/// </summary>
public sealed class ManagementKpiService(
    ICurrentUserService currentUser,
    ICurrentDataScopeService dataScope,
    ICashFlowProjectionService cashFlow,
    ProjectProfitabilitySummaryService profitability,
    ExpenseCenterReportService expenses,
    IHrApprovalService payroll,
    ProcurementDashboardService procurement,
    IChequeService cheques,
    ILogger<ManagementKpiService> logger)
{
    public async Task<ManagementKpiResponse> GetAsync(
        Guid companyId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var kpis = new List<ManagementKpi>();
        var unavailable = new List<ManagementKpiUnavailable>();

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var previousStart = periodStart.AddMonths(-1);
        var previousEnd = periodStart.AddDays(-1);

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.CashFlowView, "cash.closing", "Nakit kapanış",
            async () =>
            {
                var projection = await cashFlow.GetAsync(
                    companyId, 6, null, cancellationToken);

                // Finansman açığı ayrı bir KPI değil, aynı kartın ikinci
                // satırı: "ne zaman ve ne kadar" tek bakışta okunmalı.
                var detail = projection.Shortfall is { } shortfall
                    ? $"{shortfall.FirstNegativeDate:dd.MM.yyyy} tarihinde açık; " +
                      $"gereken finansman {TurkishFormat.Amount(shortfall.RequiredFinancing)}"
                    : "Projeksiyon boyunca açık yok.";

                return new ManagementKpi(
                    "cash.closing", "Nakit kapanış",
                    projection.ClosingBalance, KpiValueKind.Money,
                    detail, null, null, "/finans/nakit-akis");
            });

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.HakedisView, "project.margin", "En düşük kâr marjı",
            async () =>
            {
                var rows = await profitability.GetSummaryAsync(companyId, cancellationToken);

                // Cirosu olmayan proje marjı 0 döndürüyor; "en kötü"
                // sıralamasına girerse gerçek sorunlu projeyi gizler.
                var withRevenue = rows.Where(x => x.Revenue > 0m).ToList();

                if (withRevenue.Count == 0)
                {
                    return new ManagementKpi(
                        "project.margin", "En düşük kâr marjı",
                        0m, KpiValueKind.Percent,
                        "Cirosu olan proje yok.", null, null, "/projeler");
                }

                var worst = withRevenue.OrderBy(x => x.ProfitMargin).ToList();

                return new ManagementKpi(
                    "project.margin", "En düşük kâr marjı",
                    worst[0].ProfitMargin, KpiValueKind.Percent,
                    string.Join(" · ", worst.Take(3).Select(x =>
                        $"{x.ProjectName}: %{TurkishFormat.Amount(x.ProfitMargin)}")),
                    null, null, "/projeler");
            });

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.ExpenseView, "expense.total", "Gider merkezi toplamı",
            async () =>
            {
                var current = await expenses.BuildAsync(
                    companyId, periodStart, periodEnd, cancellationToken);

                var previous = await expenses.BuildAsync(
                    companyId, previousStart, previousEnd, cancellationToken);

                return new ManagementKpi(
                    "expense.total", "Gider merkezi toplamı",
                    current.Total, KpiValueKind.Money,
                    $"{current.Rows.Count} kalem",
                    // Kaynağın kendi maskeleme notu AYNEN taşınıyor:
                    // toplam yalnız görünen kalemleri kapsıyor ve iki
                    // kullanıcı farklı sayı görebilir. Not düşmezsek
                    // "rakamlar tutmuyor" tartışması çıkar.
                    current.HiddenNote,
                    previous.Total,
                    "/finans/gider-merkezi");
            });

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.SalaryView, "payroll.cost", "Bordro maliyeti",
            async () =>
            {
                var current = await payroll.GetPayrollSummaryAsync(
                    companyId, year, month, cancellationToken);

                var previousPeriod = periodStart.AddMonths(-1);
                var previous = await payroll.GetPayrollSummaryAsync(
                    companyId, previousPeriod.Year, previousPeriod.Month, cancellationToken);

                return new ManagementKpi(
                    "payroll.cost", "Bordro maliyeti",
                    current.TotalGrossSalary, KpiValueKind.Money,
                    $"{current.PayrollCount} bordro · {current.ApprovedCount} onaylı",
                    null,
                    previous.TotalGrossSalary,
                    "/insan-kaynaklari/bordro");
            });

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.PurchasingView, "purchasing.open", "Açık sipariş",
            async () =>
            {
                var scope = await dataScope.GetAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Veri kapsamı okunamadı.");

                var dashboard = await procurement.GetAsync(
                    companyId, null, scope, cancellationToken);

                return new ManagementKpi(
                    "purchasing.open", "Açık sipariş",
                    dashboard.PurchaseOrders.Open, KpiValueKind.Count,
                    $"{dashboard.PurchaseOrders.OverdueDelivery} teslim tarihi geçmiş",
                    null, null, "/satin-alma/siparis");
            });

        await AddAsync(kpis, unavailable,
            PermissionCatalog.Keys.FinanceView, "cheque.open", "Açık çek",
            async () =>
            {
                var summary = await cheques.GetSummaryAsync(companyId, cancellationToken);

                return new ManagementKpi(
                    "cheque.open", "Açık çek (borç)",
                    summary.IssuedOpenAmount, KpiValueKind.Money,
                    $"{summary.IssuedOpenCount} keşide · " +
                    $"portföyde {TurkishFormat.Amount(summary.ReceivedPortfolioAmount)}",
                    null, null, "/finans/cekler");
            });

        return new ManagementKpiResponse(
            companyId, year, month, DateTime.UtcNow, kpis, unavailable);
    }

    /// <summary>
    /// Yetki varsa KPI'yı üretir. Yetki yoksa HİÇBİR ŞEY eklemez —
    /// listeye de, unavailable'a da. Kaynak patlarsa yalnızca o KPI
    /// unavailable olur ve sebebi günlüğe yazılır.
    /// </summary>
    private async Task AddAsync(
        List<ManagementKpi> kpis,
        List<ManagementKpiUnavailable> unavailable,
        string permission,
        string key,
        string title,
        Func<Task<ManagementKpi>> build)
    {
        if (!currentUser.HasPermission(permission))
            return;

        try
        {
            kpis.Add(await build());
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "KPI üretilemedi: {Key}", key);

            unavailable.Add(new ManagementKpiUnavailable(
                key, title, "Kaynak şu anda okunamadı."));
        }
    }
}
