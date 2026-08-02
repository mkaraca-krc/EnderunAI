namespace EnderunAI.Api.Security;

public sealed record RoleSeedDefinition(
    string Name,
    string Description,
    IReadOnlyCollection<string> PermissionKeys);

/// <summary>
/// Seed edilecek roller ve izin setleri. Bu liste yalnızca ilk kurulumda
/// (rol yoksa) uygulanır — admin daha sonra Yetki Matrisi ekranından bu
/// grantları değiştirebilir; bu dosya değişikliği geri almaz.
/// Her rolde hem yeni granüler anahtarlar hem de (Faz 1'de hâlâ path
/// heuristiğiyle çalışan) eski geniş kapsamlı anahtarlar birlikte
/// listelenir; Faz 2'de attribute tabanlı zorlama tamamlandığında eski
/// anahtarlar kademeli olarak kaldırılabilir.
/// </summary>
public static class RoleCatalog
{
    private static readonly string[] K = typeof(PermissionCatalog.Keys)
        .GetFields()
        .Select(f => (string)f.GetValue(null)!)
        .ToArray();

    public static readonly IReadOnlyList<RoleSeedDefinition> Roles =
    [
        new("Admin", "Tam sistem yetkisi.", K),

        new("Genel Müdür", "Tüm iş modülleri, kullanıcı yönetimi ve ek ödeme dahil tam yetki.", K),

        new("Finans Sorumlusu", "Finans, kasa, çek, cari ve muhasebe tam yetki; raporlar.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.FinanceCreate, PermissionCatalog.Keys.FinanceEdit,
            PermissionCatalog.Keys.FinanceDelete, PermissionCatalog.Keys.FinanceApprove, PermissionCatalog.Keys.FinanceManage,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.CurrentAccountsEdit, PermissionCatalog.Keys.CurrentAccountsDelete,
            PermissionCatalog.Keys.AccountingView, PermissionCatalog.Keys.AccountingCreate, PermissionCatalog.Keys.AccountingEdit,
            PermissionCatalog.Keys.AccountingDelete, PermissionCatalog.Keys.AccountingApprove, PermissionCatalog.Keys.AccountingManage,
            PermissionCatalog.Keys.HakedisView,
            PermissionCatalog.Keys.CompaniesManage
        ]),

        new("Satın Alma Sorumlusu", "Talep, RFQ, sipariş, mal kabul ve stok süreçleri tam yetki; cari görüntüleme.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.PurchasingRequestsView, PermissionCatalog.Keys.PurchasingRequestsCreate,
            PermissionCatalog.Keys.PurchasingRequestsEdit, PermissionCatalog.Keys.PurchasingRequestsDelete,
            PermissionCatalog.Keys.PurchasingRequestsApprove,
            PermissionCatalog.Keys.PurchasingRfqView, PermissionCatalog.Keys.PurchasingRfqCreate,
            PermissionCatalog.Keys.PurchasingRfqEdit, PermissionCatalog.Keys.PurchasingRfqDelete,
            PermissionCatalog.Keys.PurchasingRfqApprove,
            PermissionCatalog.Keys.PurchasingOrdersView, PermissionCatalog.Keys.PurchasingOrdersCreate,
            PermissionCatalog.Keys.PurchasingOrdersEdit, PermissionCatalog.Keys.PurchasingOrdersDelete,
            PermissionCatalog.Keys.PurchasingOrdersApprove,
            PermissionCatalog.Keys.PurchasingReceiptsView, PermissionCatalog.Keys.PurchasingReceiptsCreate,
            PermissionCatalog.Keys.PurchasingReceiptsEdit, PermissionCatalog.Keys.PurchasingReceiptsDelete,
            PermissionCatalog.Keys.PurchasingReceiptsApprove,
            PermissionCatalog.Keys.InventoryView, PermissionCatalog.Keys.InventoryCreate,
            PermissionCatalog.Keys.InventoryEdit, PermissionCatalog.Keys.InventoryDelete,
            PermissionCatalog.Keys.CurrentAccountsView,
            PermissionCatalog.Keys.PurchasingView, PermissionCatalog.Keys.PurchasingManage,
            PermissionCatalog.Keys.PurchasingApprove, PermissionCatalog.Keys.InventoryManage
        ]),

        new("İK Sorumlusu", "Personel ve puantaj-maaş tam yetki (Ek Ödeme hariç).",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate,
            PermissionCatalog.Keys.PersonnelEdit, PermissionCatalog.Keys.PersonnelDelete,
            PermissionCatalog.Keys.AttendancePayrollView, PermissionCatalog.Keys.AttendancePayrollCreate,
            PermissionCatalog.Keys.AttendancePayrollEdit, PermissionCatalog.Keys.AttendancePayrollDelete,
            PermissionCatalog.Keys.AttendancePayrollApprove,
            PermissionCatalog.Keys.PersonnelManage, PermissionCatalog.Keys.AttendanceView,
            PermissionCatalog.Keys.AttendanceManage, PermissionCatalog.Keys.PayrollView, PermissionCatalog.Keys.PayrollManage
        ]),

        new("Ön Muhasebe", "Fatura/cari tam yetki, muhasebe fiş girişi, satın alma görüntüleme.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView, PermissionCatalog.Keys.ProjectsView,
            PermissionCatalog.Keys.ReportsView,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.CurrentAccountsEdit, PermissionCatalog.Keys.CurrentAccountsDelete,
            PermissionCatalog.Keys.AccountingView, PermissionCatalog.Keys.AccountingCreate, PermissionCatalog.Keys.AccountingEdit,
            PermissionCatalog.Keys.PurchasingRequestsView, PermissionCatalog.Keys.PurchasingRfqView,
            PermissionCatalog.Keys.PurchasingOrdersView, PermissionCatalog.Keys.PurchasingReceiptsView,
            PermissionCatalog.Keys.CompaniesManage, PermissionCatalog.Keys.AccountingManage, PermissionCatalog.Keys.PurchasingView
        ]),

        new("Teknik Ofis", "Projeler; keşif/metraj/hakediş tam yetki; dosyalar tam yetki; maliyet ve kâr görünür.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ProjectsCreate,
            PermissionCatalog.Keys.ProjectsEdit, PermissionCatalog.Keys.ProjectsDelete,
            PermissionCatalog.Keys.SitesView,
            PermissionCatalog.Keys.EngineeringView, PermissionCatalog.Keys.EngineeringManage,
            PermissionCatalog.Keys.HakedisView, PermissionCatalog.Keys.HakedisCreate,
            PermissionCatalog.Keys.HakedisEdit, PermissionCatalog.Keys.HakedisDelete,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.DocumentsEdit, PermissionCatalog.Keys.DocumentsDelete,
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.ProjectsManage, PermissionCatalog.Keys.HakedisManage
        ]),

        new("Teknik Koordinatör", "Teknik Ofis + tüm şantiyeler + günlük rapor onaylama + saha personel yönetimi.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ProjectsCreate,
            PermissionCatalog.Keys.ProjectsEdit, PermissionCatalog.Keys.ProjectsDelete,
            PermissionCatalog.Keys.SitesView, PermissionCatalog.Keys.SitesCreate,
            PermissionCatalog.Keys.SitesEdit, PermissionCatalog.Keys.SitesDelete,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsApprove,
            PermissionCatalog.Keys.EngineeringView, PermissionCatalog.Keys.EngineeringManage,
            PermissionCatalog.Keys.HakedisView, PermissionCatalog.Keys.HakedisCreate,
            PermissionCatalog.Keys.HakedisEdit, PermissionCatalog.Keys.HakedisDelete,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.DocumentsEdit, PermissionCatalog.Keys.DocumentsDelete,
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate,
            PermissionCatalog.Keys.PersonnelEdit, PermissionCatalog.Keys.PersonnelDelete,
            PermissionCatalog.Keys.AttendancePayrollView,
            PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.ProjectsManage, PermissionCatalog.Keys.HakedisManage,
            PermissionCatalog.Keys.PersonnelManage, PermissionCatalog.Keys.AttendanceView, PermissionCatalog.Keys.PayrollView
        ]),

        new("Şantiye Şefi", "Sadece atandığı şantiyeler: günlük rapor girme, şantiye personelini görüntüleme, sarf talebi.",
        [
            PermissionCatalog.Keys.DashboardView,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsCreate,
            PermissionCatalog.Keys.SiteReportsEdit, PermissionCatalog.Keys.SiteReportsDelete,
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.InventoryView, PermissionCatalog.Keys.InventoryCreate,
            PermissionCatalog.Keys.PurchasingRequestsView, PermissionCatalog.Keys.PurchasingRequestsCreate,
            PermissionCatalog.Keys.InventoryManage, PermissionCatalog.Keys.PurchasingView, PermissionCatalog.Keys.PurchasingManage
        ]),

        new("Formen", "Sadece atandığı şantiyede günlük rapor girme (taslak), kendi ekibini görüntüleme.",
        [
            PermissionCatalog.Keys.DashboardView,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsCreate,
            PermissionCatalog.Keys.SiteReportsEdit,
            PermissionCatalog.Keys.PersonnelView
        ]),

        new("Sekreterya", "Dosyalar tam yetki, cari kart oluşturma/görüntüleme, projeler görüntüleme.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.DocumentsEdit, PermissionCatalog.Keys.DocumentsDelete,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.SecretariatView, PermissionCatalog.Keys.SecretariatManage,
            PermissionCatalog.Keys.CompaniesView
        ]),

        new("Araç Sorumlusu", "Filo modülü devreye girene kadar görüntüleme ağırlıklı dar kapsamlı rol.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView
        ]),

        new("Depo Sorumlusu", "Stok giriş-çıkış, transfer, rezervasyon ve mal kabul.",
        [
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView,
            PermissionCatalog.Keys.InventoryView, PermissionCatalog.Keys.InventoryCreate,
            PermissionCatalog.Keys.InventoryEdit, PermissionCatalog.Keys.InventoryDelete,
            PermissionCatalog.Keys.PurchasingReceiptsView, PermissionCatalog.Keys.PurchasingReceiptsCreate,
            PermissionCatalog.Keys.PurchasingReceiptsEdit, PermissionCatalog.Keys.PurchasingReceiptsApprove,
            PermissionCatalog.Keys.InventoryManage, PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.PurchasingManage, PermissionCatalog.Keys.PurchasingApprove
        ])
    ];

    /// <summary>
    /// Artık yeni rol listesinde olmayan ve canlıda kullanıcısı bulunmayan
    /// eski preset roller — cutover sırasında silinir.
    /// </summary>
    public static readonly string[] RetiredRoleNames =
    [
        "Rapor Görüntüleyici", "Proje Müdürü", "Tekniker", "Muhasebe", "Finans",
        "Satın Alma", "İnsan Kaynakları"
    ];
}
