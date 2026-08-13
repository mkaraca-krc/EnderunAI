using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>
/// Projeye düşmüş tek bir gerçekleşen maliyet satırı — hangi defterden
/// geldiği fark etmeksizin aynı biçimde.
/// </summary>
public sealed record RealizedCostRow(
    ProjectCostClass CostClass,
    decimal Amount,
    DateTime CostDate,
    Guid? SectionId,
    /// <summary>
    /// Poz bağı. YALNIZ maliyet defteri satırlarında dolu olabilir;
    /// elle gider kaydında poz alanı yok. Poz kâr analizi bu yüzden
    /// gider kayıtlarını göremez — sessizce sıfır saymak yerine
    /// "poza bağlanmamış" olarak ayrıca gösterilir.
    /// </summary>
    Guid? BoqItemId,
    RealizedCostSource Source);

public enum RealizedCostSource
{
    /// <summary>Proje maliyet defteri (fatura, sarf, taşeron, görev…).</summary>
    CostLedger = 0,

    /// <summary>Elle girilen gider kaydı (kira, faturalar, araç…).</summary>
    ManualExpense = 1
}

public interface IProjectRealizedCostReader
{
    /// <summary>
    /// Projenin gerçekleşen maliyet satırları.
    /// </summary>
    /// <param name="includeMaskedExpenses">
    /// Elden / faturasız gider kalemleri dahil edilsin mi. Çağıran bunu
    /// <c>IExtraPaymentVisibilityService</c>'ten alır — maske kuralı
    /// burada yeniden yazılmaz.
    /// </param>
    Task<IReadOnlyList<RealizedCostRow>> ReadAsync(
        Guid projectId,
        DateTime? from,
        DateTime? toExclusive,
        bool includeMaskedExpenses,
        CancellationToken cancellationToken);

    /// <summary>
    /// Şirket genelinde PROJELERE düşmüş gerçekleşen maliyet toplamı —
    /// finans panosunun dönem gideri.
    ///
    /// MERKEZ/ŞUBE GİDERLERİ DAHİL DEĞİL: bu rakam bugüne kadar "proje
    /// maliyeti" anlamına geliyordu; ofis kirasını da katmak panonun
    /// anlamını sessizce değiştirirdi. Merkez giderleri gider merkezi
    /// raporunda görünüyor.
    /// </summary>
    Task<decimal> ReadProjectCostTotalAsync(
        Guid? companyId,
        DateTime from,
        DateTime toExclusive,
        bool includeMaskedExpenses,
        CancellationToken cancellationToken);
}

/// <summary>
/// PROJENİN GERÇEKLEŞEN MALİYETİ — TEK OKUMA NOKTASI.
///
/// Sistemde maliyet iki yerde duruyor:
///   1) <see cref="ProjectCostTransaction"/> — otomatik kaynaklar
///      (tedarikçi faturası, depo sarfı, taşeron, görevlendirme, alet
///      servisi) ve elle maliyet kaydı.
///   2) <see cref="Models.Expenses.ExpenseEntry"/> — elle gider kaydı
///      (kira, faturalar, kırtasiye, araç masrafı…).
///
/// Bunlar AYRIK KÜMELER: gider modülü otomatik kategorileri (malzeme,
/// işçilik, taşeron, yol) elle girişte reddediyor — tam olarak aynı
/// gideri iki kaynaktan saymamak için. Bu yüzden ikisini toplamak çift
/// sayım değildir.
///
/// SATIR KOPYALANMAZ. Gider kayıtlarını maliyet defterine yazmak her
/// okuyucuyu tek hamlede beslerdi ama aynı parayı iki tabloda tutup
/// güncelleme/silme senkronu taşımak gerekirdi. Kod tabanı bu kararı
/// işçilik için zaten vermiş: "ikinci bir tabloya mükerrer yazmak iki
/// defteri sürekli senkron tutma sorunu doğururdu." Aynı gerekçe.
///
/// NEDEN ORTAK OKUYUCU: maliyet analizi, hakediş kârı ve finans panosu
/// aynı soruyu soruyor. Her biri kendi sorgusunu yazsaydı biri gider
/// kayıtlarını sayarken diğeri saymaz ve aynı proje için iki farklı
/// maliyet çıkardı.
///
/// MUHASEBE MUTABAKATI BU OKUYUCUYU KULLANMAZ: gider kaydı bilerek fiş
/// üretmiyor; mutabakata katılsaydı her satır "muhasebeleşmemiş" diye
/// kırmızı görünürdü.
/// </summary>
public sealed class ProjectRealizedCostReader(AppDbContext db)
    : IProjectRealizedCostReader
{
    public async Task<IReadOnlyList<RealizedCostRow>> ReadAsync(
        Guid projectId,
        DateTime? from,
        DateTime? toExclusive,
        bool includeMaskedExpenses,
        CancellationToken cancellationToken)
    {
        var ledgerQuery = db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (from is DateTime start)
            ledgerQuery = ledgerQuery.Where(x => x.CostDate >= start);

        if (toExclusive is DateTime end)
            ledgerQuery = ledgerQuery.Where(x => x.CostDate < end);

        var ledger = await ledgerQuery
            .Select(x => new
            {
                x.CostClass,
                x.Amount,
                x.CostDate,
                x.ProjectHakedisSectionId,
                x.ProjectBoqItemId
            })
            .ToListAsync(cancellationToken);

        // Şantiye merkezli gider kayıtları da projeye yazılıyor
        // (ExpenseEntryService.ApplyCenter şantiyede ProjectId'yi de
        // dolduruyor), bu yüzden tek koşul yetiyor.
        var expenseQuery = db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (from is DateTime expenseStart)
            expenseQuery = expenseQuery.Where(x => x.ExpenseDate >= expenseStart);

        if (toExclusive is DateTime expenseEnd)
            expenseQuery = expenseQuery.Where(x => x.ExpenseDate < expenseEnd);

        // ELDEN İZOLASYONU: yüklem gider modülünün kendi ifadesinden
        // geliyor. Burada yeniden yazılsaydı iki maske zamanla ayrışır
        // ve biri delinirdi.
        if (!includeMaskedExpenses)
            expenseQuery = expenseQuery.Where(ExpenseEntryService.IsVisibleExpense);

        var expenses = await expenseQuery
            .Select(x => new
            {
                x.Amount,
                x.ExpenseDate,
                x.ProjectSiteId,
                CategoryCode = x.ExpenseCategory.Code
            })
            .ToListAsync(cancellationToken);

        var rows = new List<RealizedCostRow>(ledger.Count + expenses.Count);

        rows.AddRange(ledger.Select(x => new RealizedCostRow(
            x.CostClass,
            x.Amount,
            x.CostDate,
            x.ProjectHakedisSectionId,
            x.ProjectBoqItemId,
            RealizedCostSource.CostLedger)));

        rows.AddRange(expenses.Select(x => new RealizedCostRow(
            ExpenseSourceCategoryMap.CostClassForCategory(x.CategoryCode),
            x.Amount,
            x.ExpenseDate,

            // Gider kaydında icmal KISMI yok; kısım kırılımında
            // "Genel" satırına düşer. Şantiye bilgisi kısım demek
            // değildir — biri lokasyon, diğeri imalat kırılımı.
            null,
            null,
            RealizedCostSource.ManualExpense)));

        return rows;
    }

    public async Task<decimal> ReadProjectCostTotalAsync(
        Guid? companyId,
        DateTime from,
        DateTime toExclusive,
        bool includeMaskedExpenses,
        CancellationToken cancellationToken)
    {
        var ledgerQuery = db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.CostDate >= from && x.CostDate < toExclusive);

        // Yalnız PROJEYE yazılmış gider kayıtları: merkez/şube gideri
        // proje maliyeti değildir.
        var expenseQuery = db.ExpenseEntries
            .AsNoTracking()
            .Where(x =>
                x.ProjectId != null &&
                x.ExpenseDate >= from &&
                x.ExpenseDate < toExclusive);

        if (companyId is Guid company)
        {
            ledgerQuery = ledgerQuery.Where(x => x.Project.CompanyId == company);
            expenseQuery = expenseQuery.Where(x => x.CompanyId == company);
        }

        if (!includeMaskedExpenses)
            expenseQuery = expenseQuery.Where(ExpenseEntryService.IsVisibleExpense);

        var ledgerTotal = await ledgerQuery
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var expenseTotal = await expenseQuery
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return decimal.Round(ledgerTotal + expenseTotal, 2);
    }
}
