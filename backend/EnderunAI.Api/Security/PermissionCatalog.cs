namespace EnderunAI.Api.Security;

public sealed record PermissionDefinition(
    string Key,
    string Module,
    string Name,
    string Description);

public sealed record RolePresetDefinition(
    string Name,
    string Description,
    IReadOnlyCollection<string> Permissions);

public static class PermissionCatalog
{
    public const string AllowPrefix = "ALLOW:";
    public const string DenyPrefix = "DENY:";

    public static class Keys
    {
        public const string DashboardView = "dashboard.view";
        public const string CompaniesView = "companies.view";
        public const string CompaniesManage = "companies.manage";
        public const string ProjectsView = "projects.view";
        public const string ProjectsManage = "projects.manage";
        public const string PersonnelView = "personnel.view";
        public const string PersonnelManage = "personnel.manage";
        public const string AttendanceView = "attendance.view";
        public const string AttendanceManage = "attendance.manage";
        public const string PayrollView = "payroll.view";
        public const string PayrollManage = "payroll.manage";
        public const string HakedisView = "hakedis.view";
        public const string HakedisManage = "hakedis.manage";
        public const string HakedisApprove = "hakedis.approve";
        public const string FinanceView = "finance.view";
        public const string FinanceManage = "finance.manage";
        public const string FinanceApprove = "finance.approve";
        public const string AccountingView = "accounting.view";
        public const string AccountingManage = "accounting.manage";
        public const string PurchasingView = "purchasing.view";
        public const string PurchasingManage = "purchasing.manage";
        public const string PurchasingApprove = "purchasing.approve";
        public const string InventoryView = "inventory.view";
        public const string InventoryManage = "inventory.manage";
        public const string EngineeringView = "engineering.view";
        public const string EngineeringManage = "engineering.manage";
        public const string SecretariatView = "secretariat.view";
        public const string SecretariatManage = "secretariat.manage";
        public const string TasksView = "tasks.view";
        public const string TasksManage = "tasks.manage";
        public const string ReportsView = "reports.view";
        public const string AiUse = "ai.use";
        public const string SystemUsersManage = "system.users.manage";
    }

    public static readonly IReadOnlyList<PermissionDefinition> Permissions =
    [
        new(Keys.DashboardView, "Genel", "Dashboard", "Genel özet ve göstergeleri görüntüler."),
        new(Keys.CompaniesView, "Organizasyon", "Şirket ve şubeleri görüntüleme", "Şirket ve şube kartlarını görüntüler."),
        new(Keys.CompaniesManage, "Organizasyon", "Şirket ve şube yönetimi", "Şirket ve şube kaydı oluşturur ve günceller."),
        new(Keys.ProjectsView, "Proje", "Projeleri görüntüleme", "Proje ve şantiye kayıtlarını görüntüler."),
        new(Keys.ProjectsManage, "Proje", "Proje yönetimi", "Proje ve şantiye kayıtlarını yönetir."),
        new(Keys.PersonnelView, "İnsan Kaynakları", "Personeli görüntüleme", "Personel ve organizasyon bilgilerini görüntüler."),
        new(Keys.PersonnelManage, "İnsan Kaynakları", "Personel yönetimi", "Personel, işe alım, kariyer ve İK kayıtlarını yönetir."),
        new(Keys.AttendanceView, "İnsan Kaynakları", "Puantajı görüntüleme", "Puantaj, izin ve fazla mesai kayıtlarını görüntüler."),
        new(Keys.AttendanceManage, "İnsan Kaynakları", "Puantaj yönetimi", "Puantaj, izin ve fazla mesai kaydı oluşturur ve onay sürecine gönderir."),
        new(Keys.PayrollView, "İnsan Kaynakları", "Ücret ve bordroyu görüntüleme", "Maaş, bordro, ek ücret ve avans bilgilerini görüntüler."),
        new(Keys.PayrollManage, "İnsan Kaynakları", "Ücret ve bordro yönetimi", "Maaş, bordro, ek ücret ve avans kayıtlarını yönetir."),
        new(Keys.HakedisView, "Hakediş", "Hakedişi görüntüleme", "Hakediş, metraj ve fiyat farkı kayıtlarını görüntüler."),
        new(Keys.HakedisManage, "Hakediş", "Hakediş yönetimi", "Hakediş, metraj ve fiyat farkı kaydı oluşturur ve günceller."),
        new(Keys.HakedisApprove, "Hakediş", "Hakediş onayı", "Hakediş ve fiyat farkı kayıtlarını onaylar."),
        new(Keys.FinanceView, "Finans", "Finansı görüntüleme", "Finans merkezi ve ödeme verilerini görüntüler."),
        new(Keys.FinanceManage, "Finans", "Finans yönetimi", "Tahsilat, ödeme ve finans kayıtlarını yönetir."),
        new(Keys.FinanceApprove, "Finans", "Finans onayı", "Ödeme ve finans işlemlerini onaylar."),
        new(Keys.AccountingView, "Muhasebe", "Muhasebeyi görüntüleme", "Hesap planı, fiş ve defterleri görüntüler."),
        new(Keys.AccountingManage, "Muhasebe", "Muhasebe yönetimi", "Muhasebe fişi ve hesap planı kayıtlarını yönetir."),
        new(Keys.PurchasingView, "Satın Alma", "Satın almayı görüntüleme", "Talep, RFQ ve siparişleri görüntüler."),
        new(Keys.PurchasingManage, "Satın Alma", "Satın alma yönetimi", "Talep, RFQ, teklif karşılaştırma ve siparişleri yönetir."),
        new(Keys.PurchasingApprove, "Satın Alma", "Satın alma onayı", "Satın alma talebi ve siparişleri onaylar."),
        new(Keys.InventoryView, "Depo ve Stok", "Depoyu görüntüleme", "Depo, stok ve mal kabul kayıtlarını görüntüler."),
        new(Keys.InventoryManage, "Depo ve Stok", "Depo yönetimi", "Stok giriş-çıkış, transfer, rezervasyon ve mal kabul işlemlerini yapar."),
        new(Keys.EngineeringView, "Mühendislik", "Mühendisliği görüntüleme", "Poz, reçete, keşif ve teknik kayıtları görüntüler."),
        new(Keys.EngineeringManage, "Mühendislik", "Mühendislik yönetimi", "Poz, reçete, keşif ve teknik kayıtları yönetir."),
        new(Keys.SecretariatView, "Sekreterya", "Sekreteryayı görüntüleme", "Evrak, kargo, ziyaretçi ve toplantı kayıtlarını görüntüler."),
        new(Keys.SecretariatManage, "Sekreterya", "Sekreterya yönetimi", "Evrak, kargo, ziyaretçi, telefon notu ve toplantıları yönetir."),
        new(Keys.TasksView, "Görev Yönetimi", "Görevleri görüntüleme", "Kendisine açık görevleri görüntüler."),
        new(Keys.TasksManage, "Görev Yönetimi", "Görev yönetimi", "Görev oluşturur, atar ve durumunu günceller."),
        new(Keys.ReportsView, "Raporlama", "Raporları görüntüleme", "Yetkili olduğu modüllerin raporlarını görüntüler."),
        new(Keys.AiUse, "Enderun AI", "AI asistan kullanımı", "Enderun AI asistan ve analiz özelliklerini kullanır."),
        new(Keys.SystemUsersManage, "Sistem Yönetimi", "Kullanıcı ve yetki yönetimi", "Kullanıcı oluşturur, rol ve izin verir, şifre sıfırlar.")
    ];

    private static readonly IReadOnlyDictionary<string, string[]> PresetPermissions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = AllPermissionKeys(),
            ["Genel Müdür"] = AllPermissionKeys(),
            ["Teknik Koordinatör"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.ProjectsManage,
                Keys.PersonnelView, Keys.AttendanceView, Keys.HakedisView, Keys.HakedisManage,
                Keys.PurchasingView, Keys.InventoryView, Keys.EngineeringView, Keys.EngineeringManage,
                Keys.SecretariatView, Keys.TasksView, Keys.TasksManage, Keys.ReportsView, Keys.AiUse
            ],
            ["Proje Müdürü"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.ProjectsManage,
                Keys.PersonnelView, Keys.AttendanceView, Keys.AttendanceManage,
                Keys.HakedisView, Keys.HakedisManage, Keys.PurchasingView, Keys.PurchasingManage,
                Keys.InventoryView, Keys.InventoryManage, Keys.EngineeringView, Keys.EngineeringManage,
                Keys.TasksView, Keys.TasksManage, Keys.ReportsView, Keys.AiUse
            ],
            ["Şantiye Şefi"] =
            [
                Keys.DashboardView, Keys.ProjectsView, Keys.PersonnelView,
                Keys.AttendanceView, Keys.AttendanceManage, Keys.HakedisView,
                Keys.PurchasingView, Keys.PurchasingManage, Keys.InventoryView, Keys.InventoryManage,
                Keys.EngineeringView, Keys.TasksView, Keys.TasksManage, Keys.ReportsView
            ],
            ["Tekniker"] =
            [
                Keys.DashboardView, Keys.ProjectsView, Keys.PurchasingView, Keys.InventoryView,
                Keys.EngineeringView, Keys.EngineeringManage, Keys.TasksView, Keys.TasksManage, Keys.AiUse
            ],
            ["Formen"] =
            [
                Keys.DashboardView, Keys.ProjectsView, Keys.PersonnelView,
                Keys.AttendanceView, Keys.AttendanceManage, Keys.PurchasingView,
                Keys.InventoryView, Keys.TasksView, Keys.TasksManage
            ],
            ["Muhasebe"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.PersonnelView,
                Keys.PayrollView, Keys.PayrollManage, Keys.HakedisView, Keys.FinanceView,
                Keys.AccountingView, Keys.AccountingManage, Keys.PurchasingView, Keys.ReportsView
            ],
            ["Finans"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.HakedisView,
                Keys.HakedisManage, Keys.HakedisApprove, Keys.FinanceView, Keys.FinanceManage,
                Keys.FinanceApprove, Keys.AccountingView, Keys.ReportsView, Keys.AiUse
            ],
            ["Satın Alma"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView,
                Keys.PurchasingView, Keys.PurchasingManage, Keys.PurchasingApprove,
                Keys.InventoryView, Keys.InventoryManage, Keys.TasksView, Keys.TasksManage, Keys.ReportsView
            ],
            ["Depo Sorumlusu"] =
            [
                Keys.DashboardView, Keys.ProjectsView, Keys.PurchasingView,
                Keys.InventoryView, Keys.InventoryManage, Keys.TasksView, Keys.TasksManage
            ],
            ["İnsan Kaynakları"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView,
                Keys.PersonnelView, Keys.PersonnelManage, Keys.AttendanceView, Keys.AttendanceManage,
                Keys.PayrollView, Keys.PayrollManage, Keys.TasksView, Keys.TasksManage, Keys.ReportsView
            ],
            ["Sekreterya"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.PersonnelView,
                Keys.SecretariatView, Keys.SecretariatManage, Keys.TasksView, Keys.TasksManage
            ],
            ["Rapor Görüntüleyici"] =
            [
                Keys.DashboardView, Keys.CompaniesView, Keys.ProjectsView, Keys.HakedisView,
                Keys.FinanceView, Keys.AccountingView, Keys.PurchasingView, Keys.InventoryView,
                Keys.ReportsView
            ]
        };

    private static readonly IReadOnlyDictionary<string, string> PresetDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = "Sistem yönetimi dahil tüm yetkiler.",
            ["Genel Müdür"] = "Tüm iş modülleri ve kullanıcı yönetimi.",
            ["Teknik Koordinatör"] = "Projeler, teknik ekip, mühendislik ve operasyon koordinasyonu.",
            ["Proje Müdürü"] = "Proje, şantiye, hakediş ve saha operasyonları.",
            ["Şantiye Şefi"] = "Şantiye personeli, puantaj, talep, depo ve saha kayıtları.",
            ["Tekniker"] = "Teknik kayıtlar, saha görevleri ve malzeme görüntüleme.",
            ["Formen"] = "Günlük puantaj, saha personeli, malzeme talebi ve görevler.",
            ["Muhasebe"] = "Muhasebe, bordro, finans ve hakediş görüntüleme.",
            ["Finans"] = "Finans, hakediş, ödeme ve onay süreçleri.",
            ["Satın Alma"] = "Talep, RFQ, sipariş, mal kabul ve stok süreçleri.",
            ["Depo Sorumlusu"] = "Stok giriş-çıkış, transfer, rezervasyon ve mal kabul.",
            ["İnsan Kaynakları"] = "Personel, puantaj, bordro ve tüm İK süreçleri.",
            ["Sekreterya"] = "Evrak, kargo, ziyaretçi, toplantı ve görev kayıtları.",
            ["Rapor Görüntüleyici"] = "Yalnızca yönetim raporları ve özet ekranları."
        };

    public static IReadOnlyList<RolePresetDefinition> RolePresets =>
        PresetPermissions
            .Select(item => new RolePresetDefinition(
                item.Key,
                PresetDescriptions[item.Key],
                item.Value))
            .ToArray();

    public static bool IsPresetRole(string roleName) =>
        PresetPermissions.ContainsKey(roleName);

    public static bool IsKnownPermission(string permission) =>
        Permissions.Any(item =>
            string.Equals(item.Key, permission, StringComparison.OrdinalIgnoreCase));

    public static string? GetPrimaryRole(IEnumerable<string> roleNames) =>
        roleNames.FirstOrDefault(IsPresetRole);

    public static HashSet<string> Resolve(IEnumerable<string> roleNames)
    {
        var names = roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in names.Where(IsPresetRole))
        {
            result.UnionWith(PresetPermissions[roleName]);
        }

        foreach (var roleName in names.Where(x =>
                     x.StartsWith(AllowPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var permission = roleName[AllowPrefix.Length..];
            if (IsKnownPermission(permission))
                result.Add(permission);
        }

        foreach (var roleName in names.Where(x =>
                     x.StartsWith(DenyPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            result.Remove(roleName[DenyPrefix.Length..]);
        }

        return result;
    }

    public static string[] SanitizeOverrides(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(IsKnownPermission)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
    }

    private static string[] AllPermissionKeys() =>
        Permissions.Select(item => item.Key).ToArray();
}
