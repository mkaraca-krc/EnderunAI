namespace EnderunAI.Api.Security;

using EnderunAI.Api.Models;

/// <summary>
/// KAPSAM SÜZGECİ İÇİN TEK AD: `ApplyScope`.
///
/// Sistemde iki ad aynı işi yapıyordu: anlık görüntünün kendi
/// metodu (`kapsam.Apply(db.Personnel)`) ve uzantı
/// (`db.Warehouses.ApplyScope(kapsam)`). İkisi de doğru süzüyor ama
/// **kapsam nöbetçisi yalnız `ApplyScope` adını tanıyor**
/// (`CoverageBaselineTests`, pencerede o kelimeyi arıyor).
///
/// Sonuç: `Apply` ile yazılmış doğru bir süzgeç, nöbetçiye
/// "kapsamsız okuma" olarak görünüyordu. Yanlış alarm en pahalı
/// nöbetçi kusurudur — bir süre sonra kimse listeye bakmaz.
///
/// Bu uzantılar `Apply`'a devrediyor; süzme mantığı KOPYALANMIYOR.
/// Kopyalansaydı iki kapsam kuralı zamanla ayrışırdı.
/// </summary>
public static class OperationsScopeExtensions
{
    public static IQueryable<Personnel> ApplyScope(
        this IQueryable<Personnel> query, CurrentDataScopeSnapshot scope) =>
        scope.Apply(query);

    /// <summary>
    /// Zimmet kaydı ŞİRKET ekseninde süzülüyor.
    ///
    /// Proje ekseni bilerek yok: zimmet kişiye verilir, projeye
    /// değil. `ProjectId` yalnız maliyet kırılımı için taşınıyor ve
    /// boş olabiliyor; oradan süzmek projesiz zimmetleri herkesten
    /// gizlerdi.
    /// </summary>
    public static IQueryable<HrAssetAssignment> ApplyScope(
        this IQueryable<HrAssetAssignment> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    /// <summary>
    /// Hesap planı şirket ekseninde. Şube/proje ekseni YOK: hesap
    /// planı şirketin tamamına ait, bir şubeye değil.
    /// </summary>
    public static IQueryable<Models.AccountingAccount> ApplyScope(
        this IQueryable<Models.AccountingAccount> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));

    /// <summary>
    /// Stok hareketi de şirket ekseninde. Depo zaten şirkete bağlı;
    /// hareketin kendi `CompanyId` alanı üzerinden süzmek, deponun
    /// yüklenmesini gerektirmiyor.
    /// </summary>
    public static IQueryable<StockMovement> ApplyScope(
        this IQueryable<StockMovement> query, CurrentDataScopeSnapshot scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(x => scope.CompanyIds.Contains(x.CompanyId));
}
