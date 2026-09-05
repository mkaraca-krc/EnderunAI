namespace EnderunAI.Api.Security;

public sealed record PermissionDefinition(
    string Key,
    string Module,
    string Name,
    string Description);

public static class PermissionCatalog
{
    /// <summary>
    /// Kullanıcı KATALOGDAKİ HER İZNE sahip mi.
    ///
    /// TEK KURAL: hem token üretimi (all_permissions talebi) hem oturum
    /// ucu (/auth/me) buradan okuyor. İki yerde ayrı hesaplansaydı biri
    /// "tam yetkili" derken diğeri demezdi ve arayüz ile token farklı
    /// davranırdı.
    ///
    /// ROL ADINA BAKILMAZ: "Admin" ya da "Genel Müdür" adı yeniden
    /// adlandırılabilir, başka bir role de tüm izinler verilebilir.
    /// Doğru soru "her izne sahip mi", "adı ne" değil.
    /// </summary>
    public static bool HasEveryPermission(IEnumerable<string> permissions)
    {
        var granted = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Permissions.All(definition => granted.Contains(definition.Key));
    }

    public static class Keys
    {
        // Genel
        public const string DashboardView = "dashboard.view";
        public const string CompaniesView = "companies.view";
        public const string CompaniesManage = "companies.manage";

        // Projeler
        public const string ProjectsView = "projects.view";
        public const string ProjectsManage = "projects.manage";
        public const string ProjectsCreate = "projects.create";
        public const string ProjectsEdit = "projects.edit";
        public const string ProjectsDelete = "projects.delete";

        // Şantiyeler
        public const string SitesView = "sites.view";
        public const string SitesCreate = "sites.create";
        public const string SitesEdit = "sites.edit";
        public const string SitesDelete = "sites.delete";

        // Günlük Saha Raporu
        public const string SiteReportsView = "site-reports.view";
        public const string SiteReportsCreate = "site-reports.create";
        public const string SiteReportsEdit = "site-reports.edit";
        public const string SiteReportsDelete = "site-reports.delete";
        public const string SiteReportsApprove = "site-reports.approve";

        // İşveren Portalı
        public const string EmployerPortalView = "employer-portal.view";
        public const string EmployerPortalCreate = "employer-portal.create";
        public const string EmployerPortalEdit = "employer-portal.edit";
        public const string EmployerPortalDelete = "employer-portal.delete";

        // Satın Alma (eski, geniş kapsamlı — geçiş dönemi için korunuyor)
        public const string PurchasingView = "purchasing.view";
        public const string PurchasingManage = "purchasing.manage";
        public const string PurchasingApprove = "purchasing.approve";

        // Satın Alma - Talep
        public const string PurchasingRequestsView = "purchasing-requests.view";
        public const string PurchasingRequestsCreate = "purchasing-requests.create";
        public const string PurchasingRequestsEdit = "purchasing-requests.edit";
        public const string PurchasingRequestsDelete = "purchasing-requests.delete";
        public const string PurchasingRequestsApprove = "purchasing-requests.approve";

        // Satın Alma - RFQ
        public const string PurchasingRfqView = "purchasing-rfq.view";
        public const string PurchasingRfqCreate = "purchasing-rfq.create";
        public const string PurchasingRfqEdit = "purchasing-rfq.edit";
        public const string PurchasingRfqDelete = "purchasing-rfq.delete";
        public const string PurchasingRfqApprove = "purchasing-rfq.approve";

        // Satın Alma - Sipariş
        public const string PurchasingOrdersView = "purchasing-orders.view";
        public const string PurchasingOrdersCreate = "purchasing-orders.create";
        public const string PurchasingOrdersEdit = "purchasing-orders.edit";
        public const string PurchasingOrdersDelete = "purchasing-orders.delete";
        public const string PurchasingOrdersApprove = "purchasing-orders.approve";

        // Satın Alma - Mal Kabul
        public const string PurchasingReceiptsView = "purchasing-receipts.view";
        public const string PurchasingReceiptsCreate = "purchasing-receipts.create";
        public const string PurchasingReceiptsEdit = "purchasing-receipts.edit";
        public const string PurchasingReceiptsDelete = "purchasing-receipts.delete";
        public const string PurchasingReceiptsApprove = "purchasing-receipts.approve";

        // Stok
        public const string InventoryView = "inventory.view";
        public const string InventoryManage = "inventory.manage";
        public const string InventoryCreate = "inventory.create";
        public const string InventoryEdit = "inventory.edit";
        public const string InventoryDelete = "inventory.delete";

        // Perakende satış
        //
        // ÜÇ AYRI ANAHTAR, ÇÜNKÜ ÜÇ AYRI GÜVEN SEVİYESİ:
        //   sales.*        satış hazırlar (fiyatı ve tavanı karttan okur)
        //   sales.cash     satışın bir kısmını ELDEN işaretleyebilir
        //   sales.approve  tavan aşımını ve vadeyi onaylar — yalnız finans
        //
        // Tek anahtar olsaydı satış hazırlayan personel kendi iskontosunu
        // kendi onaylar, kendi elden satışını kendi açardı.
        public const string SalesView = "sales.view";
        public const string SalesCreate = "sales.create";
        public const string SalesCash = "sales.cash";
        public const string SalesApprove = "sales.approve";

        // Personel
        public const string PersonnelView = "personnel.view";
        public const string PersonnelManage = "personnel.manage";
        public const string PersonnelCreate = "personnel.create";
        public const string PersonnelEdit = "personnel.edit";
        public const string PersonnelDelete = "personnel.delete";

        // Puantaj (eski, geniş kapsamlı — geçiş dönemi için korunuyor)
        public const string AttendanceView = "attendance.view";
        public const string AttendanceManage = "attendance.manage";

        // Ücret/Bordro (eski, geniş kapsamlı — geçiş dönemi için korunuyor)
        public const string PayrollView = "payroll.view";
        public const string PayrollManage = "payroll.manage";

        // Puantaj-Maaş (yeni, birleşik)
        public const string AttendancePayrollView = "attendance-payroll.view";
        public const string AttendancePayrollCreate = "attendance-payroll.create";
        public const string AttendancePayrollEdit = "attendance-payroll.edit";
        public const string AttendancePayrollDelete = "attendance-payroll.delete";
        public const string AttendancePayrollApprove = "attendance-payroll.approve";

        // Ücret gizliliği — maaş, bordro ve işçilik maliyeti rakamlarını
        // görme yetkisi. Bu izin olmadan personel listesinde ve bordro
        // ekranlarında tutar alanları hiç dönmez (yalnızca gizlenmez).
        public const string SalaryView = "salary.view";
        public const string SalaryManage = "salary.manage";

        // Ek ödeme (elden) — resmi bordroda görünmeyen tutarlar.
        // salary.view'dan AYRI ve daha dar bir izindir: bordroyu yöneten
        // herkes elden ödemeleri görmez. Bu izin olmadan ilgili alanlar
        // hiç dönmez ve uçlar 403 verir.
        public const string ExtraPaymentView = "extra_payment.view";
        public const string ExtraPaymentManage = "extra_payment.manage";

        // Taşeron — sözleşme, hakediş, avans ve evrak.
        // Elden ödeme ve elden avans AYRICA extra_payment.* ister:
        // taşeron yönetebilen herkes elden tutarları göremez.
        public const string SubcontractorView = "subcontractor.view";
        public const string SubcontractorManage = "subcontractor.manage";
        public const string SubcontractorApprove = "subcontractor.approve";

        // Hakediş
        public const string HakedisView = "hakedis.view";
        public const string HakedisManage = "hakedis.manage";
        public const string HakedisApprove = "hakedis.approve";
        public const string HakedisCreate = "hakedis.create";
        public const string HakedisEdit = "hakedis.edit";
        public const string HakedisDelete = "hakedis.delete";

        // Finans
        public const string FinanceView = "finance.view";

        /*
         * BANKA HESABI OKUMA — DAR VE AYRI.
         *
         * Banka hesapları bugün yalnız `company-settings.view` ile
         * okunabiliyordu ve o anahtar TÜM şirket ayarları paketini
         * açıyor (logo, mesai pencereleri, hepsi). Bu anahtar yalnız
         * banka hesaplarını açar — aynı kitleye daha az veri.
         *
         * KİTLE GENİŞLETİLMEDİ: yalnız Admin ve Genel Müdür'e
         * veriliyor, yani IBAN görebilen rol sayısı bugünküyle AYNI.
         * Finans Sorumlusu ve İK Sorumlusu'na verilmesi DURUM.md
         * "BEKLEYEN KARARLAR" listesinde: bordro ödemesini İK yapıyor
         * ve bugün yapamıyor, ama IBAN kitlesini genişletmek ayrı bir
         * karar.
         */
        public const string BankAccountView = "bank_account.view";

        // HESAP PLANI AKTARIMI — muhasebe yönetimi düzeyi.
        //
        // accounting.create'ten AYRI: tek hesap açmak ile bir dosyadan
        // toplu hesap üretmek aynı iş değil. Aktarım yanlış giderse
        // hata tek satırda kalmaz, hesap planının tamamına yayılır.
        public const string ChartImport = "chart.import";
        public const string FinanceManage = "finance.manage";
        public const string FinanceApprove = "finance.approve";
        public const string FinanceCreate = "finance.create";

        /// <summary>
        /// Nakit akış projeksiyonu. AYRI ANAHTAR: tablo bordroyu ELDEN
        /// DAHİL tam tutarla gösteriyor, yani finance.view kadar geniş
        /// bir kapıya bırakılamaz — o izin Teknik Ofis ve Teknik
        /// Koordinatör'de de var ve ikisinde ek ödeme yetkisi yok.
        /// </summary>
        public const string CashFlowView = "cashflow.view";

        /// <summary>
        /// Gider merkezi raporu ve gider kayıtları. AYRI ANAHTAR:
        /// rapor merkez × kategori kırılımında şirketin bütün
        /// giderlerini tek ekranda topluyor; proje bazlı maliyet
        /// yetkisi (projects.view) olan biri buradan şirket geneline
        /// bakamamalı.
        /// </summary>
        public const string ExpenseView = "expense.view";
        public const string ExpenseManage = "expense.manage";

        /// <summary>Araç (filo) kartlarını görüntüleme.</summary>
        public const string VehicleView = "vehicle.view";

        /// <summary>Araç kartı ve atama yönetimi.</summary>
        public const string VehicleManage = "vehicle.manage";

        public const string FinanceEdit = "finance.edit";
        public const string FinanceDelete = "finance.delete";

        // Çek — iki ayrı riski iki ayrı anahtara bağlıyoruz.
        //
        // ÇEK DÜZENLEME: yanlış giriş için ASIL yol. İptal, numarayı
        // serbest bıraktığı ve muhasebeyi storno ile geri aldığı için
        // düzeltmeye göre çok daha ağır bir işlem; "yanlış yazdım"
        // vakası düzenleme ile çözülmeli.
        public const string ChequeEdit = "cheque.edit";

        // KAPANMIŞ ÇEK İPTALİ: tahsil edilmiş, ödenmiş, bankada,
        // faktoringde, karşılıksız ya da iade alınmış çekin iptali.
        // Portföydeki çeki iptal etmekle bunlar aynı şey değil:
        // kapanmış bir çekin iptali gerçekleşmiş bir para hareketini
        // geri alır VE numarayı yeniden kullanıma açar. Yanlış satıra
        // tıklamak yetiyor, kötü niyet gerekmiyor.
        public const string ChequeVoidClosed = "cheque.void-closed";

        // ── HAFTALIK ÖDEME PLANI (ÖP/1a) ──────────────────────────

        // PLANI HAZIRLAMA: satırları kurar, tutar önerir, onaya sunar.
        // Onaylayamaz — hazırlayan ile onaylayan aynı kişi olamaz (K4).
        public const string PaymentPlanPrepare = "payment.plan.prepare";

        // PLANI ONAYLAMA: YALNIZ GENEL MÜDÜR. Admin AÇIKÇA HARİÇ.
        //
        // Bu, sistemdeki en dar anahtar. Gerekçe: onay, paranın kime
        // ve ne kadar çıkacağına dair karardır; "tam sistem yetkisi"
        // teknik bir roldür, ödeme kararı vermez.
        //
        // RoleCatalog'da hassas sayılıyor, yani otomatik dağıtıma
        // GİRMİYOR ve yalnız Genel Müdür'e AÇIKÇA yazılıyor.
        public const string PaymentPlanApprove = "payment.plan.approve";

        // Cari
        public const string CurrentAccountsView = "current-accounts.view";
        public const string CurrentAccountsCreate = "current-accounts.create";
        public const string CurrentAccountsEdit = "current-accounts.edit";
        public const string CurrentAccountsDelete = "current-accounts.delete";
        public const string CurrentAccountsApprove = "current-accounts.approve";

        // Muhasebe
        public const string AccountingView = "accounting.view";
        public const string AccountingManage = "accounting.manage";
        public const string AccountingCreate = "accounting.create";
        public const string AccountingEdit = "accounting.edit";
        public const string AccountingDelete = "accounting.delete";
        public const string AccountingApprove = "accounting.approve";

        // Mühendislik
        public const string EngineeringView = "engineering.view";
        public const string EngineeringManage = "engineering.manage";

        // Teklif / iş takibi (fırsat hunisi)
        //
        // Teklif HAZIRLAMA engineering.manage'de kalır; aşağıdakiler
        // takip katmanıdır. Ayrı anahtar, Finans'ın teklif hazırlama
        // yetkisi almadan huniyi ve kazanma oranını görebilmesi için.
        public const string OfferTrackingView = "offer_tracking.view";
        public const string OfferTrackingManage = "offer_tracking.manage";

        // İş programı (Gantt). Okuma geniş tutuldu: planı görmesi
        // gereken herkes sahadadır. Düzenleme dar: tarih ve bağımlılık
        // değiştirmek bütün zinciri kaydırır.
        // Özlük belgeleri kimlik fotokopisi ve adli sicil taşıyor;
        // personnel.view sahada da var (Şantiye Şefi, Formen). Elden
        // ödemedeki gibi kendi dar anahtarıyla korunuyor.
        public const string PersonnelDocumentView = "personnel_document.view";
        public const string PersonnelDocumentManage = "personnel_document.manage";

        public const string ScheduleView = "schedule.view";
        public const string ScheduleManage = "schedule.manage";

        // Sekreterya
        public const string SecretariatView = "secretariat.view";
        public const string SecretariatManage = "secretariat.manage";

        // Dosya/Dokümanlar
        public const string DocumentsView = "documents.view";
        public const string DocumentsCreate = "documents.create";
        public const string DocumentsEdit = "documents.edit";
        public const string DocumentsDelete = "documents.delete";

        // Görev Yönetimi
        public const string TasksView = "tasks.view";
        public const string TasksManage = "tasks.manage";

        /*
         * MESAJLAŞMA — İKİ ANAHTAR, YOL TÜRETİMİNE BIRAKILMADI.
         *
         * Sekiz mesaj ucu bugüne kadar yalnız `[Authorize]` taşıyordu.
         * Gerekçesi kayıtlıydı ve makuldü: erişimi ÜYELİK belirliyor,
         * "yetkisi olan görür" işi değil. Ama beyansızlık, gerekçesi
         * ne olursa olsun okunamaz: bir ucun neye izin verdiği, o ucun
         * üstünde YAZILI olmalı (KURAL 72/E).
         *
         * ÜYELİK KAPISI KALKMIYOR — bu anahtarlar onun YERİNE değil,
         * ÜSTÜNE geliyor. `mesajlar.view` taşıyan biri hâlâ yalnız
         * kendi konuşmasını görür; anahtar "mesajlaşma özelliğini
         * kullanabilir" demektir, "her mesajı okur" değil.
         */
        public const string MesajlarView = "mesajlar.view";
        public const string MesajlarSend = "mesajlar.send";

        // Raporlama
        public const string ReportsView = "reports.view";

        // Enderun AI
        public const string AiUse = "ai.use";

        // Sistem Yönetimi (eski, geniş kapsamlı — geçiş dönemi için korunuyor)
        public const string SystemUsersManage = "system.users.manage";

        // Kullanıcı Yönetimi
        public const string UserManagementView = "user-management.view";
        public const string UserManagementCreate = "user-management.create";
        public const string UserManagementEdit = "user-management.edit";
        public const string UserManagementDelete = "user-management.delete";

        // Şirket Ayarları
        public const string CompanySettingsView = "company-settings.view";
        public const string CompanySettingsEdit = "company-settings.edit";

        // Audit Log
        public const string AuditLogView = "audit-log.view";

        // İş Sağlığı ve Güvenliği
        public const string IsgView = "isg.view";
        public const string IsgCreate = "isg.create";
        public const string IsgEdit = "isg.edit";
        public const string IsgDelete = "isg.delete";

        /// <summary>
        /// Sağlık raporunun TIBBİ DETAYI (teşhis, kısıtlama notu, rapor
        /// dosyası). Rapor tarihi ve geçerlilik bitişi bu izin olmadan da
        /// görünür — süre takibi bunsuz çalışmaz. Dar kapı: İSG
        /// sorumlusu ve Genel Müdür dışında verilmemeli.
        /// </summary>
        public const string IsgHealthView = "isg.health.view";

        /// <summary>
        /// Kaza/ramak kala kayıt defteri. isg.view'dan ayrı tutuluyor:
        /// sahada İSG kaydı girebilen herkesin kaza defterini görmesi
        /// gerekmiyor.
        /// </summary>
        public const string IsgIncidentView = "isg.incident.view";
        public const string IsgIncidentManage = "isg.incident.manage";
    }

    public static readonly IReadOnlyList<PermissionDefinition> Permissions =
    [
        new(Keys.DashboardView, "Genel", "Dashboard", "Genel özet ve göstergeleri görüntüler."),
        new(Keys.CompaniesView, "Organizasyon", "Şirket ve şubeleri görüntüleme", "Şirket ve şube kartlarını görüntüler."),
        new(Keys.CompaniesManage, "Organizasyon", "Şirket ve şube yönetimi", "Şirket ve şube kaydı oluşturur ve günceller."),

        new(Keys.ProjectsView, "Projeler", "Projeleri görüntüleme", "Proje kayıtlarını görüntüler."),
        new(Keys.ProjectsManage, "Projeler", "Proje yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı proje yönetim izni."),
        new(Keys.ProjectsCreate, "Projeler", "Proje oluşturma", "Yeni proje kaydı oluşturur."),
        new(Keys.ProjectsEdit, "Projeler", "Proje düzenleme", "Mevcut proje kaydını günceller."),
        new(Keys.ProjectsDelete, "Projeler", "Proje silme/arşivleme",
            "Projeyi arşive alır veya (kesinleşmiş kaydı yoksa) kalıcı siler. " +
            "Yalnızca Genel Müdür ve Admin rollerinde bulunur."),

        new(Keys.SitesView, "Şantiyeler", "Şantiyeleri görüntüleme", "Şantiye kayıtlarını görüntüler."),
        new(Keys.SitesCreate, "Şantiyeler", "Şantiye oluşturma", "Yeni şantiye kaydı oluşturur."),
        new(Keys.SitesEdit, "Şantiyeler", "Şantiye düzenleme", "Mevcut şantiye kaydını günceller."),
        new(Keys.SitesDelete, "Şantiyeler", "Şantiye silme", "Şantiye kaydını siler."),

        new(Keys.SiteReportsView, "Günlük Saha Raporu", "Raporları görüntüleme", "Günlük saha raporlarını görüntüler."),
        new(Keys.SiteReportsCreate, "Günlük Saha Raporu", "Rapor oluşturma", "Yeni günlük saha raporu (taslak) oluşturur."),
        new(Keys.SiteReportsEdit, "Günlük Saha Raporu", "Rapor düzenleme", "Taslak günlük saha raporunu düzenler."),
        new(Keys.SiteReportsDelete, "Günlük Saha Raporu", "Rapor silme", "Günlük saha raporunu siler."),
        new(Keys.SiteReportsApprove, "Günlük Saha Raporu", "Rapor onaylama", "Taslak günlük saha raporunu onaylar; onaylanan raporlar işveren portalına düşer."),

        new(Keys.EmployerPortalView, "İşveren Portalı", "Portalı görüntüleme", "İşveren portalı bağlantılarını ve gönderim geçmişini görüntüler."),
        new(Keys.EmployerPortalCreate, "İşveren Portalı", "Portal bağlantısı oluşturma", "Yeni işveren portalı bağlantısı oluşturur."),
        new(Keys.EmployerPortalEdit, "İşveren Portalı", "Portal bağlantısı düzenleme", "Mevcut işveren portalı bağlantısını günceller."),
        new(Keys.EmployerPortalDelete, "İşveren Portalı", "Portal bağlantısı iptali", "İşveren portalı bağlantısını iptal eder."),

        new(Keys.PurchasingView, "Satın Alma", "Satın almayı görüntüleme (eski)", "Geçiş dönemi için korunan geniş kapsamlı görüntüleme izni."),
        new(Keys.PurchasingManage, "Satın Alma", "Satın alma yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı yönetim izni."),
        new(Keys.PurchasingApprove, "Satın Alma", "Satın alma onayı (eski)", "Geçiş dönemi için korunan geniş kapsamlı onay izni."),

        new(Keys.PurchasingRequestsView, "Satın Alma - Talep", "Talepleri görüntüleme", "Satın alma taleplerini görüntüler."),
        new(Keys.PurchasingRequestsCreate, "Satın Alma - Talep", "Talep oluşturma", "Yeni satın alma talebi oluşturur."),
        new(Keys.PurchasingRequestsEdit, "Satın Alma - Talep", "Talep düzenleme", "Satın alma talebini günceller."),
        new(Keys.PurchasingRequestsDelete, "Satın Alma - Talep", "Talep silme", "Satın alma talebini siler."),
        new(Keys.PurchasingRequestsApprove, "Satın Alma - Talep", "Talep onayı", "Satın alma talebini onaylar."),

        new(Keys.PurchasingRfqView, "Satın Alma - RFQ", "RFQ görüntüleme", "Teklif toplama süreçlerini görüntüler."),
        new(Keys.PurchasingRfqCreate, "Satın Alma - RFQ", "RFQ oluşturma", "Yeni RFQ oluşturur."),
        new(Keys.PurchasingRfqEdit, "Satın Alma - RFQ", "RFQ düzenleme", "RFQ ve tedarikçi tekliflerini günceller."),
        new(Keys.PurchasingRfqDelete, "Satın Alma - RFQ", "RFQ silme", "RFQ kaydını siler."),
        new(Keys.PurchasingRfqApprove, "Satın Alma - RFQ", "RFQ sonuçlandırma", "Kazanan tedarikçiyi seçip RFQ'yu sonuçlandırır."),

        new(Keys.PurchasingOrdersView, "Satın Alma - Sipariş", "Siparişleri görüntüleme", "Satın alma siparişlerini görüntüler."),
        new(Keys.PurchasingOrdersCreate, "Satın Alma - Sipariş", "Sipariş oluşturma", "Yeni satın alma siparişi oluşturur."),
        new(Keys.PurchasingOrdersEdit, "Satın Alma - Sipariş", "Sipariş düzenleme", "Satın alma siparişini günceller."),
        new(Keys.PurchasingOrdersDelete, "Satın Alma - Sipariş", "Sipariş silme", "Satın alma siparişini siler."),
        new(Keys.PurchasingOrdersApprove, "Satın Alma - Sipariş", "Sipariş onayı", "Satın alma siparişini onaylar."),

        new(Keys.PurchasingReceiptsView, "Satın Alma - Mal Kabul", "Mal kabulleri görüntüleme", "Mal kabul kayıtlarını görüntüler."),
        new(Keys.PurchasingReceiptsCreate, "Satın Alma - Mal Kabul", "Mal kabul oluşturma", "Yeni mal kabul taslağı oluşturur."),
        new(Keys.PurchasingReceiptsEdit, "Satın Alma - Mal Kabul", "Mal kabul düzenleme", "Taslak mal kabul kaydını günceller."),
        new(Keys.PurchasingReceiptsDelete, "Satın Alma - Mal Kabul", "Mal kabul iptali", "Taslak mal kabul kaydını iptal eder."),
        new(Keys.PurchasingReceiptsApprove, "Satın Alma - Mal Kabul", "Mal kabul onayı (stoklama)", "Mal kabulü stoklara işler."),

        new(Keys.InventoryView, "Stok", "Depoyu görüntüleme", "Depo, stok ve mal kabul kayıtlarını görüntüler."),
        new(Keys.InventoryManage, "Stok", "Depo yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı depo yönetim izni."),
        new(Keys.InventoryCreate, "Stok", "Stok hareketi oluşturma", "Giriş, çıkış, transfer ve rezervasyon kaydı oluşturur."),
        new(Keys.InventoryEdit, "Stok", "Stok kaydı düzenleme", "Stok kartı ve hareket kaydını günceller."),
        new(Keys.InventoryDelete, "Stok", "Stok kaydı silme", "Stok kartı veya hareket kaydını siler."),

        new(Keys.PersonnelView, "Personel", "Personeli görüntüleme", "Personel bilgilerini görüntüler."),
        new(Keys.PersonnelManage, "Personel", "Personel yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı personel yönetim izni."),
        new(Keys.PersonnelCreate, "Personel", "Personel oluşturma", "Yeni personel kaydı oluşturur."),
        new(Keys.PersonnelEdit, "Personel", "Personel düzenleme", "Personel kaydını günceller."),
        new(Keys.PersonnelDelete, "Personel", "Personel silme", "Personel kaydını siler."),

        new(Keys.AttendanceView, "İnsan Kaynakları", "Puantajı görüntüleme (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.AttendanceManage, "İnsan Kaynakları", "Puantaj yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.PayrollView, "İnsan Kaynakları", "Ücret ve bordroyu görüntüleme (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.PayrollManage, "İnsan Kaynakları", "Ücret ve bordro yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),

        new(Keys.AttendancePayrollView, "Puantaj-Maaş", "Görüntüleme", "Puantaj, izin, fazla mesai, maaş ve bordro kayıtlarını görüntüler."),
        new(Keys.AttendancePayrollCreate, "Puantaj-Maaş", "Oluşturma", "Puantaj, izin, fazla mesai, maaş ve bordro kaydı oluşturur."),
        new(Keys.AttendancePayrollEdit, "Puantaj-Maaş", "Düzenleme", "Puantaj, izin, fazla mesai, maaş ve bordro kaydını günceller."),
        new(Keys.AttendancePayrollDelete, "Puantaj-Maaş", "Silme", "Puantaj, izin, fazla mesai, maaş ve bordro kaydını siler."),
        new(Keys.AttendancePayrollApprove, "Puantaj-Maaş", "Onaylama", "Puantaj, izin, fazla mesai ve bordro kaydını onaylar."),

        new(Keys.SalaryView, "Ücret Gizliliği", "Ücret görüntüleme", "Maaş, bordro ve işçilik maliyeti tutarlarını görür."),
        new(Keys.SalaryManage, "Ücret Gizliliği", "Ücret yönetimi", "Ücret kartı ve bordro tutarlarını değiştirir."),

        new(Keys.ExtraPaymentView, "Ek Ödeme", "Ek ödeme görüntüleme", "Resmi bordroda görünmeyen elden ödeme tutarlarını ve elden tazminat farkını görür."),
        new(Keys.ExtraPaymentManage, "Ek Ödeme", "Ek ödeme yönetimi", "Personelin elden ödeme tutarlarını tanımlar ve değiştirir."),

        new(Keys.SubcontractorView, "Taşeron", "Taşeronu görüntüleme", "Taşeron sözleşmelerini, hakedişlerini ve evraklarını görüntüler."),
        new(Keys.SubcontractorManage, "Taşeron", "Taşeron yönetimi", "Taşeron sözleşmesi, hakedişi ve avansı oluşturur ve günceller."),
        new(Keys.SubcontractorApprove, "Taşeron", "Taşeron onayı", "Taşeron hakedişini ve avansını onaylar."),

        new(Keys.HakedisView, "Hakediş", "Hakedişi görüntüleme", "Hakediş, metraj ve fiyat farkı kayıtlarını görüntüler."),
        new(Keys.HakedisManage, "Hakediş", "Hakediş yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.HakedisApprove, "Hakediş", "Hakediş onayı", "Hakediş ve fiyat farkı kayıtlarını onaylar."),
        new(Keys.HakedisCreate, "Hakediş", "Hakediş oluşturma", "Yeni hakediş, metraj veya fiyat farkı kaydı oluşturur."),
        new(Keys.HakedisEdit, "Hakediş", "Hakediş düzenleme", "Taslak hakediş, metraj veya fiyat farkı kaydını günceller."),
        new(Keys.HakedisDelete, "Hakediş", "Hakediş silme", "Hakediş, metraj veya fiyat farkı kaydını siler."),

        new(Keys.FinanceView, "Finans", "Finansı görüntüleme", "Finans merkezi ve ödeme verilerini görüntüler."),
        new(Keys.BankAccountView, "Banka Hesapları", "Banka hesaplarını görüntüleme", "Şirket banka hesaplarını ve maskeli IBAN'ı görüntüler."),
        new(Keys.ChartImport, "Hesap Planı Aktarımı", "Hesap planını dosyadan aktarma", "Dosyadan toplu hesap ekler. Mevcut hesapları GÜNCELLEMEZ, eksik üst hesap OLUŞTURMAZ."),
        new(Keys.FinanceManage, "Finans", "Finans yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.FinanceApprove, "Finans", "Finans onayı", "Ödeme ve finans işlemlerini onaylar."),
        new(Keys.FinanceCreate, "Finans", "Finans kaydı oluşturma", "Tahsilat, ödeme ve finans kaydı oluşturur."),
        new(Keys.CashFlowView, "Finans", "Nakit akış projeksiyonu", "Likidite takvimini görüntüler; bordro çıkışı elden dahil tam tutarla görünür."),
        new(Keys.ExpenseView, "Finans", "Gider merkezi raporu", "Merkez ve kategori kırılımında giderleri görüntüler."),
        new(Keys.ExpenseManage, "Finans", "Gider kaydı yönetimi", "Elle gider kaydı, kategori ve tekrarlayan gider şablonu yönetir."),
        new(Keys.VehicleView, "Filo", "Araç görüntüleme", "Araç kartlarını, atamalarını ve araç masraf dökümünü görüntüler."),
        new(Keys.VehicleManage, "Filo", "Araç yönetimi", "Araç kartı açar, günceller ve araçları projelere atar."),
        new(Keys.FinanceEdit, "Finans", "Finans kaydı düzenleme", "Finans kaydını günceller."),
        new(Keys.FinanceDelete, "Finans", "Finans kaydı silme", "Finans kaydını siler."),

        // ÇEK — iki hassas yetki. Tanım listesine de girmeleri ŞART:
        // tohumlayıcı izin satırlarını buradan üretiyor ve "tüm
        // yetkiler" kısayolu da bu listeyi okuyor. Yalnız `Keys`e
        // eklenirse anahtar kodda var ama sistemde YOK olur.
        new(Keys.ChequeEdit, "Finans", "Çek düzenleme",
            "Portföydeki/yeni verilen çekin alanlarını düzeltir; tutar, para birimi ya da cari değişirse muhasebe fişi ters kayıtla kapatılıp yenisi kesilir."),
        new(Keys.ChequeVoidClosed, "Finans", "Çek — kapanmış iptal",
            "Tahsil edilmiş, ödenmiş, bankada/faktoringde, karşılıksız ya da iade alınmış çeki iptal eder. Gerçekleşmiş para hareketini storno ile geri alır ve çek numarasını yeniden kullanıma açar."),

        new(Keys.PaymentPlanPrepare, "Finans", "Ödeme planı — hazırlama",
            "Haftalık ödeme planının satırlarını kurar, tutar önerir ve planı onaya sunar. ONAYLAYAMAZ: hazırlayan ile onaylayan aynı kişi olamaz."),
        new(Keys.PaymentPlanApprove, "Finans", "Ödeme planı — onaylama",
            "Haftalık ödeme planını SATIR SATIR onaylar; kimin parasının ne kadar ve hangi yöntemle çıkacağına karar verir. YALNIZ GENEL MÜDÜR — Admin dahil hiçbir role otomatik verilmez."),

        new(Keys.CurrentAccountsView, "Cari", "Cari kartları görüntüleme", "Cari hesap kartlarını ve hareketlerini görüntüler."),
        new(Keys.CurrentAccountsCreate, "Cari", "Cari kart oluşturma", "Yeni cari hesap kartı oluşturur."),
        new(Keys.CurrentAccountsEdit, "Cari", "Cari kart düzenleme", "Cari hesap kartını günceller."),
        new(Keys.CurrentAccountsDelete, "Cari", "Cari kart silme", "Cari hesap kartını siler."),
        new(Keys.CurrentAccountsApprove, "Cari", "Cari kart onayı", "Onay bekleyen cari kartı onaylar."),

        new(Keys.AccountingView, "Muhasebe", "Muhasebeyi görüntüleme", "Hesap planı, fiş ve defterleri görüntüler."),
        new(Keys.AccountingManage, "Muhasebe", "Muhasebe yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),
        new(Keys.AccountingCreate, "Muhasebe", "Fiş oluşturma", "Yeni muhasebe fişi (taslak) oluşturur."),
        new(Keys.AccountingEdit, "Muhasebe", "Fiş düzenleme", "Taslak muhasebe fişini günceller."),
        new(Keys.AccountingDelete, "Muhasebe", "Fiş silme", "Muhasebe fişini siler."),
        new(Keys.AccountingApprove, "Muhasebe", "Fiş kesinleştirme", "Taslak muhasebe fişini kesinleştirir (postalar)."),

        new(Keys.EngineeringView, "Mühendislik", "Mühendisliği görüntüleme", "Poz, reçete, keşif ve teknik kayıtları görüntüler."),
        new(Keys.EngineeringManage, "Mühendislik", "Mühendislik yönetimi", "Poz, reçete, keşif ve teknik kayıtları yönetir."),

        new(Keys.OfferTrackingView, "Teklif Takibi", "İş/teklif takibini görüntüleme", "Verilen teklifleri, durumlarını ve kazanma oranını görüntüler."),
        new(Keys.OfferTrackingManage, "Teklif Takibi", "İş/teklif takibi yönetimi", "Teklif durumunu değiştirir (verildi/beklemede/kazanıldı/kaybedildi) ve karşı tarafı belirler."),

        new(Keys.PersonnelDocumentView, "Özlük Belgeleri", "Özlük belgelerini görüntüleme", "Kimlik, sözleşme, diploma gibi özlük dosyası belgelerini görüntüler ve indirir."),
        new(Keys.PersonnelDocumentManage, "Özlük Belgeleri", "Özlük belgesi yönetimi", "Özlük dosyasına belge yükler ve siler."),

        new(Keys.ScheduleView, "İş Programı", "İş programını görüntüleme", "Gantt şemasını, kritik yolu ve gecikme durumunu görüntüler."),
        new(Keys.ScheduleManage, "İş Programı", "İş programı yönetimi", "Aktivite, tarih, bağımlılık ve baseline düzenler; kaynak atar."),

        new(Keys.SecretariatView, "Sekreterya", "Sekreteryayı görüntüleme", "Evrak, kargo, ziyaretçi ve toplantı kayıtlarını görüntüler."),
        new(Keys.SecretariatManage, "Sekreterya", "Sekreterya yönetimi", "Evrak, kargo, ziyaretçi, telefon notu ve toplantıları yönetir."),

        new(Keys.DocumentsView, "Dosya/Dokümanlar", "Görüntüleme", "Proje ve şirket dosyalarını görüntüler."),
        new(Keys.DocumentsCreate, "Dosya/Dokümanlar", "Yükleme", "Yeni dosya/doküman yükler."),
        new(Keys.DocumentsEdit, "Dosya/Dokümanlar", "Düzenleme", "Doküman bilgilerini günceller."),
        new(Keys.DocumentsDelete, "Dosya/Dokümanlar", "Silme", "Dosya/doküman kaydını siler."),

        new(Keys.TasksView, "Görev Yönetimi", "Görevleri görüntüleme", "Kendisine açık görevleri görüntüler."),
        new(Keys.TasksManage, "Görev Yönetimi", "Görev yönetimi", "Görev oluşturur, atar ve durumunu günceller."),

        new(Keys.MesajlarView, "Mesajlaşma", "Mesajları görüntüleme", "Üyesi olduğu konuşmaları ve mesajları görüntüler."),
        new(Keys.MesajlarSend, "Mesajlaşma", "Mesaj gönderme", "Üyesi olduğu konuşmalara mesaj gönderir ve birebir konuşma açar."),

        new(Keys.ReportsView, "Raporlama", "Raporları görüntüleme", "Yetkili olduğu modüllerin raporlarını görüntüler."),

        new(Keys.AiUse, "Enderun AI", "AI asistan kullanımı", "Enderun AI asistan ve analiz özelliklerini kullanır."),

        new(Keys.SystemUsersManage, "Sistem Yönetimi", "Kullanıcı ve yetki yönetimi (eski)", "Geçiş dönemi için korunan geniş kapsamlı izin."),

        new(Keys.UserManagementView, "Kullanıcı Yönetimi", "Görüntüleme", "Kullanıcı, rol ve yetki kayıtlarını görüntüler."),
        new(Keys.UserManagementCreate, "Kullanıcı Yönetimi", "Kullanıcı oluşturma", "Yeni kullanıcı oluşturur."),
        new(Keys.UserManagementEdit, "Kullanıcı Yönetimi", "Kullanıcı/rol/yetki düzenleme", "Kullanıcı bilgisi, rol ataması ve yetki matrisini günceller."),
        new(Keys.UserManagementDelete, "Kullanıcı Yönetimi", "Kullanıcı pasife alma", "Kullanıcıyı pasife alır."),

        new(Keys.CompanySettingsView, "Şirket Ayarları", "Görüntüleme", "Şirket kurumsal bilgilerini görüntüler."),
        new(Keys.CompanySettingsEdit, "Şirket Ayarları", "Düzenleme", "Şirket kurumsal bilgilerini (unvan, vergi, IBAN, logo) günceller."),

        new(Keys.AuditLogView, "Audit Log", "Görüntüleme", "Sistem denetim kayıtlarını (kim ne yaptı) görüntüler."),

        new(Keys.IsgView, "İSG", "Görüntüleme", "OSGB sözleşmesi, sağlık raporu, eğitim, sertifika ve saha İSG belgelerini görüntüler."),
        new(Keys.IsgCreate, "İSG", "Kayıt oluşturma", "Yeni İSG kaydı (rapor, eğitim, sertifika, belge) oluşturur."),
        new(Keys.IsgEdit, "İSG", "Düzenleme", "Mevcut İSG kaydını günceller."),
        new(Keys.IsgDelete, "İSG", "Silme", "İSG kaydını siler."),
        new(Keys.IsgHealthView, "İSG", "Sağlık raporu detayı", "Sağlık raporunun tıbbi detayını (teşhis, kısıtlama, rapor dosyası) görür. Rapor tarihi ve geçerliliği bu izin olmadan da görünür."),
        new(Keys.IsgIncidentView, "İSG", "Kaza kayıtlarını görüntüleme", "Kaza ve ramak kala kayıt defterini görüntüler."),
        new(Keys.IsgIncidentManage, "İSG", "Kaza kaydı yönetimi", "Kaza ve ramak kala kaydı oluşturur, günceller ve SGK bildirimini işler."),

        new(Keys.SalesView, "Perakende Satış", "Görüntüleme", "Perakende satış fişlerini ve merkez depo satış stoğunu görüntüler. Maliyet bu izinle GÖRÜNMEZ."),
        new(Keys.SalesCreate, "Perakende Satış", "Satış hazırlama", "Perakende satış fişi hazırlar. Fiyat ve iskonto tavanı stok kartından gelir; tavan aşımı finans onayına düşer."),
        new(Keys.SalesCash, "Perakende Satış", "Elden tutar işaretleme", "Satışın bir kısmını elden olarak işaretler ve elden tutarları görür. Bu izin olmadan yalnızca kayıtlı satış yapılabilir."),
        new(Keys.SalesApprove, "Perakende Satış", "Finans onayı", "İskonto tavanı aşılan veya vadeli satışları onaylar. Onaya kadar stok düşmez, fatura ve tahsilat oluşmaz.")
    ];

    public static bool IsKnownPermission(string permission) =>
        Permissions.Any(item =>
            string.Equals(item.Key, permission, StringComparison.OrdinalIgnoreCase));

    public static string[] SanitizeOverrides(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(IsKnownPermission)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
    }
}
