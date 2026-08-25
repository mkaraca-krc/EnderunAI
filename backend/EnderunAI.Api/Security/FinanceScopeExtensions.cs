using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

/// <summary>
/// PARA/MAAŞ SORGULARI İÇİN KAPSAM SÜZGEÇLERİ.
///
/// NEDEN AYRI DOSYA: satın alma ailesinin kendi süzgeçleri
/// `ProcurementServiceSupport` içinde. Finans tarafı farklı varlıklara
/// dokunuyor ve aynı dosyaya yığmak, iki modülün kurallarını
/// birbirine karıştırırdı.
///
/// PANO UÇLARINDA SIZINTI SATIR DEĞİL, RAKAM OLARAK OLUR: bu uçlar
/// kayıt döndürmüyor, TOPLAM döndürüyor. Süzgeç eksik olduğunda kimse
/// "başka şirketin kaydını gördüm" demez — o şirketin cirosu sessizce
/// toplama karışır ve fark edilmesi neredeyse imkânsızdır. Bu yüzden
/// süzgeç TOPLAMA SORGUSUNUN İÇİNDE; sonuç üzerinde ayıklama yok.
/// </summary>
public static class FinanceScopeExtensions
{
    /// <summary>
    /// Şirket banka hesapları — YALNIZ ŞİRKET EKSENİ.
    ///
    /// Proje ve şube eksenleri BİLEREK yok: banka hesabı şirkete ait,
    /// projeye değil. Proje kapsamı olan bir kullanıcıya şirketin
    /// banka hesaplarını açmak, kapsamı genişletmek olurdu.
    /// </summary>
    public static IQueryable<CompanyBankAccount> ApplyScope(
        this IQueryable<CompanyBankAccount> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    public static IQueryable<Project> ApplyScope(
        this IQueryable<Project> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.BranchId) ||
                scope.ProjectIds.Contains(x.Id));

    public static IQueryable<ProgressPayment> ApplyScope(
        this IQueryable<ProgressPayment> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.Project.BranchId) ||
                scope.ProjectIds.Contains(x.ProjectId));

    /// <summary>
    /// Proje maliyet hareketi ŞİRKET TAŞIMIYOR; kapsam projesinden
    /// türetiliyor. Doğrudan `CompanyId` aranması derleme hatası
    /// vermezdi ama sessizce yanlış sonuç üretirdi.
    /// </summary>
    public static IQueryable<ProjectCostTransaction> ApplyScope(
        this IQueryable<ProjectCostTransaction> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.Project.CompanyId) ||
                scope.BranchIds.Contains(x.Project.BranchId) ||
                scope.ProjectIds.Contains(x.ProjectId));

    public static IQueryable<CurrentAccount> ApplyScope(
        this IQueryable<CurrentAccount> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    /// <summary>
    /// PERAKENDE SATIŞ = CİRO. Süzgeçsiz liste, başka şirketin
    /// satış tutarlarını kendi cirosuymuş gibi gösterir.
    /// </summary>
    public static IQueryable<RetailSale> ApplyScope(
        this IQueryable<RetailSale> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    public static IQueryable<Warehouse> ApplyScope(
        this IQueryable<Warehouse> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                scope.BranchIds.Contains(x.BranchId));

    public static IQueryable<CashAccount> ApplyScope(
        this IQueryable<CashAccount> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    /// <summary>
    /// Stok kartı fiyat listesi: MALİYET ve SATIŞ fiyatı taşıyor.
    /// Bu bir "ürün kataloğu" değil, ticari sırdır.
    /// </summary>
    public static IQueryable<InventoryItem> ApplyScope(
        this IQueryable<InventoryItem> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    /// <summary>
    /// Gider kaydı: şirket zorunlu, şube seçimli. Şube boşken yalnız
    /// şirket eşleşmesi geçerli — şube kapsamlı kullanıcı, şubesi
    /// belirtilmemiş şirket giderini görmez.
    /// </summary>
    public static IQueryable<Models.Expenses.ExpenseEntry> ApplyScope(
        this IQueryable<Models.Expenses.ExpenseEntry> query,
        CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                (x.BranchId != null && scope.BranchIds.Contains(x.BranchId.Value)) ||
                (x.ProjectId != null && scope.ProjectIds.Contains(x.ProjectId.Value)));

    /// <summary>
    /// Şirket kapsamı zaten <see cref="CurrentDataScopeSnapshot.Apply"/>
    /// içinde tanımlı; burada YALNIZCA ona devrediliyor.
    ///
    /// NEDEN İKİNCİ BİR AD: kapsam bekçisi (CoverageBaselineTests)
    /// okumanın süzülüp süzülmediğini `ApplyScope` yazısını arayarak
    /// anlıyor. `scope.Apply(...)` biçimi doğru çalışıyordu ama bekçiye
    /// KAPSAMSIZ görünüyordu. İki farklı yazım, bekçinin gözünde iki
    /// farklı dünya demekti; tek yazıma indirildi. Kural burada
    /// KOPYALANMADI — kopyalansaydı ikisi zamanla ayrışırdı.
    /// </summary>
    public static IQueryable<Company> ApplyScope(
        this IQueryable<Company> query, CurrentDataScopeSnapshot scope) =>
        scope.Apply(query);

    /// <summary>
    /// Kasa hareketi ŞİRKET TAŞIMIYOR; kapsam bağlı olduğu kasa
    /// hesabından türetiliyor. Doğrudan `CompanyId` aranması derleme
    /// hatası vermezdi — o alan yok — ama benzer bir varlıkta sessizce
    /// yanlış sonuç üretebilirdi.
    /// </summary>
    public static IQueryable<CashTransaction> ApplyScope(
        this IQueryable<CashTransaction> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CashAccount.CompanyId));

    public static IQueryable<SalesInvoice> ApplyScope(
        this IQueryable<SalesInvoice> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    // ---------------------------------------------------------------
    // M1 — İŞ AKIŞI ÇEKİRDEĞİ
    // ---------------------------------------------------------------

    /*
     * YENİ TABLOLAR KAPSAMLI DOĞUYOR.
     *
     * G3 paketinin tamamı, şirket kimliği olan tablolara sonradan
     * kapsam süzgeci takmakla geçti: 480 kapsamsız okuma o yüzden
     * birikmişti. M1'in üç tablosu ilk günden süzgeçli — cırcır
     * çizgisine tek satır borç eklenmiyor.
     */

    public static IQueryable<WorkTask> ApplyScope(
        this IQueryable<WorkTask> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x =>
                scope.CompanyIds.Contains(x.CompanyId) ||
                (x.BranchId != null && scope.BranchIds.Contains(x.BranchId.Value)) ||
                (x.ProjectId != null && scope.ProjectIds.Contains(x.ProjectId.Value)) ||
                (x.ProjectSiteId != null && scope.SiteIds.Contains(x.ProjectSiteId.Value)));

    public static IQueryable<TaskComment> ApplyScope(
        this IQueryable<TaskComment> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    public static IQueryable<Attachment> ApplyScope(
        this IQueryable<Attachment> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));
}
