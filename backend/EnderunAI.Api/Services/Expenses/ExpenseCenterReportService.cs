using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>Raporun tek hücresi: merkez × kategori × kaynak.</summary>
public sealed record ExpenseReportRow(
    ExpenseCenterType CenterType,
    Guid CenterId,
    string CenterName,
    string CategoryCode,
    string CategoryName,
    string Source,
    decimal Amount,
    bool IsEstimated,
    /// <summary>
    /// Gider merkezinden düzeltilebilir mi. Otomatik kalemlerde
    /// FALSE: kaynağından düzeltilir, yoksa defter ile rapor ayrışır.
    /// </summary>
    bool IsEditableHere);

public sealed record ExpenseCenterTotal(
    ExpenseCenterType CenterType, Guid CenterId, string CenterName, decimal Amount);

public sealed record ExpenseCategoryTotal(
    string CategoryCode, string CategoryName, decimal Amount);

public sealed record ExpenseCenterReport(
    DateTime From,
    DateTime To,
    IReadOnlyList<ExpenseReportRow> Rows,
    IReadOnlyList<ExpenseCenterTotal> CenterTotals,
    IReadOnlyList<ExpenseCategoryTotal> CategoryTotals,
    decimal Total,
    int HiddenCount,
    string? HiddenNote,
    IReadOnlyList<string> Notes);

/// <summary>
/// Merkez × kategori gider raporu.
///
/// ANA KURAL — OKUR, KOPYALAMAZ: satın alma, görev masrafı, taşeron
/// ve işçilik kendi kaynaklarında zaten kayıtlı. Gider merkezi bu
/// satırları OKUYOR, ikinci bir deftere yazmıyor. Yazsaydı iki
/// defter kaçınılmaz olarak ayrışır ve aynı gider iki kez sayılırdı;
/// DutyExpensePostingService'in yorumu da bu sözleşmeyi baştan şart
/// koşuyor.
///
/// ÇİFT SAYIM NOKTALARI ve nasıl kapatıldıkları:
/// - Mal kabullü fatura: proje maliyeti stok çıkışında oluşuyor,
///   fatura ayrıca yazmıyor (SupplierInvoiceService'teki mevcut
///   koruma). Rapor defterden okuduğu için o koruma burada da
///   geçerli.
/// - Merkez gider faturası: deftere hiç girmiyor, bu yüzden
///   AYRICA okunuyor. Projesi olan gider faturası defterde zaten
///   var, tekrar okunmuyor.
/// - İşçilik: yalnızca hr_project_labor_costs köprüsünden. Bordro
///   maliyet defterine satır yazmıyor, dolayısıyla köprü + defter
///   toplamı aynı ücreti iki kez saymaz.
/// - Tekrarlayan gider: gerçekleşen açıldıysa o dönemin tahminisi
///   düşer (RecurringExpenseService).
/// - Ödemeler (çek, kasa) HİÇ SAYILMAZ: rapor tahakkuk esaslı.
///   Ödeme de sayılsaydı fatura ve ödemesi iki ayrı gider olurdu.
/// </summary>
public sealed class ExpenseCenterReportService(
    AppDbContext db,
    ExpenseCenterResolver centers,
    RecurringExpenseService recurring,
    IExtraPaymentVisibilityService extraPaymentVisibility)
{
    public async Task<ExpenseCenterReport> BuildAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        var start = ExpenseEntryService.AsUtcDate(from);
        var end = ExpenseEntryService.AsUtcDate(to);

        var notes = new List<string>();
        var rows = new List<ExpenseReportRow>();

        var canSeeCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        await ExpenseCategoryProvisioner.EnsureAsync(db, companyId, cancellationToken);

        var categories = await db.ExpenseCategories
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Code, x => x.Name, cancellationToken);

        var centerList = await centers.ListAsync(companyId, cancellationToken);

        var centerNames = centerList
            .ToDictionary(x => (x.Type, x.Id), x => x.Name);

        var headOffice = centerList.FirstOrDefault(x => x.IsHeadOffice)
            ?? centerList.FirstOrDefault(x => x.Type == ExpenseCenterType.Branch);

        string CategoryName(string code) =>
            categories.TryGetValue(code, out var name) ? name : code;

        string CenterName(ExpenseCenterType type, Guid id) =>
            centerNames.TryGetValue((type, id), out var name) ? name : "(bilinmeyen)";

        var hiddenCount = 0;

        // ---------- 1) Elle girilen giderler ----------
        var manualQuery = db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ExpenseDate >= start && x.ExpenseDate <= end);

        if (!canSeeCash)
        {
            // Elden VE şahıs carisinden mahsup: ikisi de faturasız,
            // ikisi de aynı maskede.
            hiddenCount += await manualQuery.CountAsync(
                x => x.PaymentMethod != ExpensePaymentMethod.Bank, cancellationToken);

            manualQuery = manualQuery.Where(
                x => x.PaymentMethod == ExpensePaymentMethod.Bank);
        }

        var manual = await manualQuery
            .Select(x => new
            {
                x.CenterType,
                x.BranchId,
                x.ProjectId,
                x.ProjectSiteId,
                CategoryCode = x.ExpenseCategory.Code,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        foreach (var group in manual.GroupBy(x => new
        {
            x.CenterType,
            CenterId = CenterIdOf(x.CenterType, x.BranchId, x.ProjectId, x.ProjectSiteId),
            x.CategoryCode
        }))
        {
            rows.Add(new ExpenseReportRow(
                group.Key.CenterType, group.Key.CenterId,
                CenterName(group.Key.CenterType, group.Key.CenterId),
                group.Key.CategoryCode, CategoryName(group.Key.CategoryCode),
                "Elle girilen", group.Sum(x => x.Amount), false, true));
        }

        // ---------- 2) Tekrarlayan giderin GERÇEKLEŞMEMİŞ dönemleri ----------
        var states = await recurring.GetPeriodStatesAsync(
            companyId, start, end, cancellationToken);

        var pending = states.Where(x => x.ActualEntryId is null).ToList();

        if (pending.Count > 0)
        {
            var templateIds = pending.Select(x => x.TemplateId).Distinct().ToList();

            var templates = await db.RecurringExpenseTemplates
                .AsNoTracking()
                .Where(x => templateIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.CenterType,
                    x.BranchId,
                    x.ProjectId,
                    x.ProjectSiteId,
                    CategoryCode = x.ExpenseCategory.Code,
                    x.PaymentMethod
                })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            var estimated = pending
                .Where(x => x.DueDate >= start && x.DueDate <= end)
                .Select(x => new { State = x, Template = templates[x.TemplateId] })
                .Where(x =>
                {
                    if (canSeeCash || x.Template.PaymentMethod != ExpensePaymentMethod.Cash)
                        return true;

                    hiddenCount++;
                    return false;
                })
                .ToList();

            foreach (var group in estimated.GroupBy(x => new
            {
                x.Template.CenterType,
                CenterId = CenterIdOf(
                    x.Template.CenterType, x.Template.BranchId,
                    x.Template.ProjectId, x.Template.ProjectSiteId),
                x.Template.CategoryCode
            }))
            {
                rows.Add(new ExpenseReportRow(
                    group.Key.CenterType, group.Key.CenterId,
                    CenterName(group.Key.CenterType, group.Key.CenterId),
                    group.Key.CategoryCode, CategoryName(group.Key.CategoryCode),
                    "Tekrarlayan (tahmini)",
                    group.Sum(x => x.State.EstimatedAmount), true, false));
            }
        }

        // ---------- 3) Proje maliyet defteri ----------
        var ledger = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.Project.CompanyId == companyId &&
                        x.CostDate >= start && x.CostDate <= end)
            .Select(x => new
            {
                x.ProjectId,
                x.ProjectSiteId,
                x.ReferenceType,
                x.CostClass,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        foreach (var group in ledger.GroupBy(x => new
        {
            CenterType = x.ProjectSiteId is null
                ? ExpenseCenterType.Project
                : ExpenseCenterType.ProjectSite,
            CenterId = x.ProjectSiteId ?? x.ProjectId,
            CategoryCode = ExpenseSourceCategoryMap.ForLedgerRow(
                x.ReferenceType, x.CostClass),
            Source = ExpenseSourceCategoryMap.SourceLabel(x.ReferenceType)
        }))
        {
            var amount = group.Sum(x => x.Amount);

            if (amount == 0m)
                continue;

            rows.Add(new ExpenseReportRow(
                group.Key.CenterType, group.Key.CenterId,
                CenterName(group.Key.CenterType, group.Key.CenterId),
                group.Key.CategoryCode, CategoryName(group.Key.CategoryCode),
                group.Key.Source, amount, false, false));
        }

        // ---------- 4) İşçilik köprüsü ----------
        var employerFactor = await ResolveEmployerCostFactorAsync(
            companyId, cancellationToken);

        var labor = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate >= start && x.WorkDate <= end)
            .Select(x => new { x.ProjectId, x.ProjectSiteId, x.TotalLaborCost })
            .ToListAsync(cancellationToken);

        if (labor.Count > 0)
        {
            notes.Add(
                $"İşçilik, brüt kazanca işveren yükü çarpanı ({employerFactor:0.###}) " +
                "uygulanarak hesaplandı; 5510 teşvik indirimleri dikkate alınmadı.");

            foreach (var group in labor.GroupBy(x => new
            {
                CenterType = x.ProjectSiteId is null
                    ? ExpenseCenterType.Project
                    : ExpenseCenterType.ProjectSite,
                CenterId = x.ProjectSiteId ?? x.ProjectId
            }))
            {
                rows.Add(new ExpenseReportRow(
                    group.Key.CenterType, group.Key.CenterId,
                    CenterName(group.Key.CenterType, group.Key.CenterId),
                    ExpenseCategoryCatalog.Labor,
                    CategoryName(ExpenseCategoryCatalog.Labor),
                    "Puantaj/bordro",
                    decimal.Round(group.Sum(x => x.TotalLaborCost) * employerFactor, 2),
                    false, false));
            }

            // Elden işçilik payı bilinçli olarak DIŞARIDA: dağıtım
            // aylık gün sayısına göre yapılıyor ve serbest tarih
            // aralığında anlamı yok. Sessizce yaklaşık bir sayı
            // üretmek yerine eksikliği söylüyoruz.
            notes.Add(
                "Elden ödenen işçilik payı bu raporda yok; proje maliyet " +
                "analizinde aylık dağıtımla görünür.");
        }

        // ---------- 5) Merkez (projesiz) gider faturaları ----------
        var officeInvoices = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.InvoiceType == SupplierInvoiceType.Expense &&
                        x.ProjectId == null &&
                        x.Status == SupplierInvoiceStatus.Approved &&
                        x.InvoiceDate >= start && x.InvoiceDate <= end)
            .Select(x => new { x.Subtotal, x.IsReturn })
            .ToListAsync(cancellationToken);

        if (officeInvoices.Count > 0)
        {
            if (headOffice is null)
            {
                notes.Add(
                    "Projesiz gider faturaları var ama şirkette şube tanımlı " +
                    "değil; bu kalemler raporda görünmüyor.");
            }
            else
            {
                // Faturada şube alanı YOK: projesiz gider faturası
                // merkez ofise yazılıyor. Varsayım gizlenmiyor, nota
                // düşüyor — ikinci şube açıldığında burada ayrıştırma
                // gerekecek.
                notes.Add(
                    "Projesiz gider faturaları merkez ofise yazıldı; faturada " +
                    "şube alanı bulunmuyor.");

                var amount = officeInvoices.Sum(x => x.IsReturn ? -x.Subtotal : x.Subtotal);

                if (amount != 0m)
                    rows.Add(new ExpenseReportRow(
                        ExpenseCenterType.Branch, headOffice.Id, headOffice.Name,
                        ExpenseCategoryCatalog.Other,
                        CategoryName(ExpenseCategoryCatalog.Other),
                        "Gider faturası", decimal.Round(amount, 2), false, false));
            }
        }

        // ---------- Toplamlar ----------
        var centerTotals = rows
            .GroupBy(x => (x.CenterType, x.CenterId, x.CenterName))
            .Select(x => new ExpenseCenterTotal(
                x.Key.CenterType, x.Key.CenterId, x.Key.CenterName,
                decimal.Round(x.Sum(r => r.Amount), 2)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var categoryTotals = rows
            .GroupBy(x => (x.CategoryCode, x.CategoryName))
            .Select(x => new ExpenseCategoryTotal(
                x.Key.CategoryCode, x.Key.CategoryName,
                decimal.Round(x.Sum(r => r.Amount), 2)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new ExpenseCenterReport(
            start, end,
            rows.OrderBy(x => x.CenterName).ThenBy(x => x.CategoryName).ToList(),
            centerTotals,
            categoryTotals,
            // TOPLAM = yalnız görünen kalemler. Tam toplam verilseydi
            // fark, gizlenen elden tutarı birebir ele verirdi.
            decimal.Round(rows.Sum(x => x.Amount), 2),
            hiddenCount,
            hiddenCount > 0
                ? "Elden ödenen kalemler gösterilmiyor; toplam yalnızca " +
                  "görünen kalemleri kapsıyor."
                : null,
            notes);
    }

    private static Guid CenterIdOf(
        ExpenseCenterType type, Guid? branchId, Guid? projectId, Guid? siteId) =>
        type switch
        {
            ExpenseCenterType.Branch => branchId ?? Guid.Empty,
            ExpenseCenterType.ProjectSite => siteId ?? Guid.Empty,
            _ => projectId ?? Guid.Empty
        };

    /// <summary>
    /// İşveren yükü çarpanı — proje maliyet analiziyle aynı formül.
    /// </summary>
    private async Task<decimal> ResolveEmployerCostFactorAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Year)
            .Select(x => new { x.SgkEmployerRate, x.UnemploymentEmployerRate })
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return 1m;

        return 1m + ((settings.SgkEmployerRate + settings.UnemploymentEmployerRate) / 100m);
    }
}
