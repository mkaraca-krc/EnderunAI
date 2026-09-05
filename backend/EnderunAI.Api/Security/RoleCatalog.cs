using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

public sealed record RoleSeedDefinition(
    string Name,
    string Description,
    IReadOnlyCollection<string> PermissionKeys,
    RoleDataScopePolicy DataScopePolicy = RoleDataScopePolicy.All);

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
    /// <summary>
    /// HASSAS ANAHTARLAR — YANSIMANIN DIŞINDA.
    ///
    /// `K` tüm izin anahtarlarını yansımayla topluyor ve Admin ile
    /// Genel Müdür'e veriyor. Bu, kolaylık olarak başladı ama bir
    /// yan etkisi var: kod tabanına eklenen HER yeni anahtar, sonraki
    /// servis açılışında o iki role KİMSEYE SORULMADAN düşüyor.
    ///
    /// Gerçekleşmiş bir para hareketini geri alan ya da geçmişe dönük
    /// düzeltme yapan yetkiler bu yolla dağıtılmamalı; her biri ayrı
    /// bir karar olmalı ve rol tanımında AÇIKÇA görünmeli.
    ///
    /// Buradaki anahtarlar `K`'ye girmez; isteyen rol onları kendi
    /// listesinde tek tek sayar.
    /// </summary>
    /// <summary>
    /// KASITLI VERİLEN ANAHTARLAR — otomatik dağıtıma girmezler.
    ///
    /// TEK KAVRAM: bu küme "bu anahtar hiçbir role kendiliğinden
    /// geçmez" demektir. Hangi rolün hangisini alacağı AŞAĞIDA,
    /// rolün kendi listesinde AÇIKÇA yazılır. İkinci bir muafiyet
    /// listesi açılmaz.
    /// </summary>
    private static readonly HashSet<string> SensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            PermissionCatalog.Keys.ChequeEdit,
            PermissionCatalog.Keys.ChequeVoidClosed,

            // ÖDEME PLANI ONAYI — Admin'e de GİTMEZ (ÖP/1a · İ2).
            // Aşağıda yalnız Genel Müdür'ün listesinde açıkça var.
            PermissionCatalog.Keys.PaymentPlanApprove
        };

    /// <summary>
    /// HER ROLÜN ALDIĞI ANAHTARLAR.
    ///
    /// Bugün yalnız mesajlaşma burada. Sebebi: mesajlaşma bir MODÜL
    /// yetkisi değil, çalışanın birbirine ulaşma yolu — yetkiye göre
    /// dağıtılan bir şey değil, herkese açık bir kanal. Ama uçlar
    /// yine de BEYAN taşımalı (KURAL 72/E): "izin gerekmiyor" ile
    /// "izin yazılmamış" dışarıdan aynı görünür.
    ///
    /// NEDEN AYRI KÜME, 13 LİSTEYE TEK TEK YAZMAK DEĞİL: Admin ve
    /// Genel Müdür anahtarları `K` yansımasıyla alıyor, kalan 13 rol
    /// listesini ELLE taşıyor. Elle yazılan 13 yerden biri
    /// unutulduğunda o rol sessizce mesajlaşamaz. Tek küme + tek
    /// yayma, unutmayı tek noktaya indiriyor; `RolMesajlasmaTests`
    /// de o tek noktayı sınıyor — yarın eklenecek bir rol sessizce
    /// dışarıda kalamaz.
    ///
    /// ANAHTAR ÜYELİK KAPISININ YERİNE GEÇMEZ. `mesajlar.view`
    /// taşıyan biri hâlâ yalnız KENDİ konuşmasını görür.
    /// </summary>
    private static readonly string[] HerRolde =
    [
        PermissionCatalog.Keys.MesajlarView,
        PermissionCatalog.Keys.MesajlarSend
    ];

    private static readonly string[] K = typeof(PermissionCatalog.Keys)
        .GetFields()
        .Select(f => (string)f.GetValue(null)!)
        .Where(key => !SensitiveKeys.Contains(key))
        .ToArray();

    /// <summary>
    /// ADMIN'İN KÜMESİ — hassas anahtarlar TEK TEK yazılır.
    ///
    /// Eskiden `[.. K, .. SensitiveKeys]` idi: hassas kümeye eklenen
    /// HER anahtar Admin'e de sessizce geçiyordu. `payment.plan.approve`
    /// bunu kabul edilemez kıldı — ödeme onayı teknik bir rolün işi
    /// değil (İ2).
    ///
    /// Artık her rol, aldığı hassas anahtarı KENDİ listesinde
    /// gösteriyor. Yeni hassas anahtar eklendiğinde hiçbir role
    /// kendiliğinden gitmez; unutulursa kimse alamaz — sessiz
    /// genişlemenin tersi, ve doğru taraf budur.
    /// </summary>
    private static readonly string[] AdminKeys =
    [
        .. K,
        PermissionCatalog.Keys.ChequeEdit,
        PermissionCatalog.Keys.ChequeVoidClosed
    ];

    /// <summary>Genel Müdür — Admin'in kümesi ARTI ödeme planı onayı.</summary>
    private static readonly string[] GenelMudurKeys =
    [
        .. AdminKeys,
        PermissionCatalog.Keys.PaymentPlanApprove
    ];

    public static readonly IReadOnlyList<RoleSeedDefinition> Roles =
    [
        new("Admin", "Tam sistem yetkisi.", AdminKeys),

        new("Genel Müdür", "Tüm iş modülleri, kullanıcı yönetimi, ek ödeme ve ÖDEME PLANI ONAYI dahil tam yetki.", GenelMudurKeys),

        new("Finans Sorumlusu", "Finans, kasa, çek, cari ve muhasebe tam yetki; raporlar.",
        [
            .. HerRolde,
            // ÖDEME PLANI HAZIRLAMA (ÖP/1a · İ1) — onaylama YOK.
            PermissionCatalog.Keys.PaymentPlanPrepare,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.FinanceCreate, PermissionCatalog.Keys.FinanceEdit,
            PermissionCatalog.Keys.FinanceDelete, PermissionCatalog.Keys.FinanceApprove, PermissionCatalog.Keys.FinanceManage,

            // ÇEK DÜZENLEME ve KAPANMIŞ ÇEK İPTALİ yalnız burada ve
            // GM/Admin'de (onlar tüm anahtarları alıyor). Diğer roller
            // çeki görebilir ve normal akışını yürütebilir ama geçmişe
            // dönük düzeltme ya da kapanmış çeki iptal edemez.
            PermissionCatalog.Keys.ChequeEdit, PermissionCatalog.Keys.ChequeVoidClosed,
            // Nakit akış projeksiyonu: elden dahil bordro çıkışını
            // taşıdığı için ayrı anahtar. Admin ve Genel Müdür bütün
            // anahtarları aldığı için burada tekrar listelenmiyor.
            PermissionCatalog.Keys.CashFlowView,
            // Perakende satış: ONAY finansın işi. Tavan aşımını ve
            // vadeyi onaylayan rol, alacağı ve nakit akışını taşıyan
            // rolle aynı olmalı. Satış HAZIRLAMA yetkisi burada YOK —
            // onaylayan ile hazırlayan aynı kişi olmamalı.
            PermissionCatalog.Keys.SalesView, PermissionCatalog.Keys.SalesApprove,
            // Gider merkezi: "ofise ne harcadık, şantiyeye ne harcadık"
            // finansın raporu. Elden ödenen gider kalemleri ayrıca
            // extra_payment.view'a tabi, o izin de bu rolde var.
            PermissionCatalog.Keys.ExpenseView, PermissionCatalog.Keys.ExpenseManage,
            // Araç masrafı gider merkezine düşüyor; filoyu yöneten ve
            // kira/sigorta/MTV ödemesini yapan rol finans.
            PermissionCatalog.Keys.VehicleView, PermissionCatalog.Keys.VehicleManage,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.CurrentAccountsEdit, PermissionCatalog.Keys.CurrentAccountsDelete,
            PermissionCatalog.Keys.CurrentAccountsApprove,
            PermissionCatalog.Keys.AccountingView, PermissionCatalog.Keys.AccountingCreate, PermissionCatalog.Keys.AccountingEdit,
            PermissionCatalog.Keys.AccountingDelete, PermissionCatalog.Keys.AccountingApprove, PermissionCatalog.Keys.AccountingManage,
            // Hesap planı aktarımı: Admin ve GM yansımayla alıyor,
            // Finans Sorumlusu AÇIKÇA. Ön Muhasebe DIŞARIDA — fiş
            // girer, hesap planını toplu değiştiremez.
            PermissionCatalog.Keys.ChartImport,
            PermissionCatalog.Keys.HakedisView,
            // Taşeron ödemesi, avansı ve tevkifatı finansın işi.
            PermissionCatalog.Keys.SubcontractorView, PermissionCatalog.Keys.SubcontractorManage,
            PermissionCatalog.Keys.SubcontractorApprove,
            PermissionCatalog.Keys.CompaniesManage,
            // Bordro ödemesini kasadan/bankadan yapan ve aylık maliyet
            // raporunu okuyan rol; ücret rakamlarını görmesi gerekir.
            PermissionCatalog.Keys.SalaryView,
            // Elden ödemeler ve elden tazminat farkı finansın yükümlülüğü.
            PermissionCatalog.Keys.ExtraPaymentView, PermissionCatalog.Keys.ExtraPaymentManage,
            // Hangi işe teklif verildiği ve kazanma oranı finansın
            // nakit planlamasını doğrudan etkiler; teklif HAZIRLAMA
            // yetkisi verilmeden yalnız takip katmanı açılıyor.
            PermissionCatalog.Keys.OfferTrackingView, PermissionCatalog.Keys.OfferTrackingManage
        ]),

        new("Satın Alma Sorumlusu", "Talep, RFQ, sipariş, mal kabul ve stok süreçleri tam yetki; cari görüntüleme.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
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

        new("İK Sorumlusu", "Personel, puantaj ve bordro tam yetki; ücret rakamlarını görür.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate,
            PermissionCatalog.Keys.PersonnelEdit, PermissionCatalog.Keys.PersonnelDelete,
            PermissionCatalog.Keys.AttendancePayrollView, PermissionCatalog.Keys.AttendancePayrollCreate,
            PermissionCatalog.Keys.AttendancePayrollEdit, PermissionCatalog.Keys.AttendancePayrollDelete,
            PermissionCatalog.Keys.AttendancePayrollApprove,
            PermissionCatalog.Keys.PersonnelManage, PermissionCatalog.Keys.AttendanceView,
            PermissionCatalog.Keys.AttendanceManage, PermissionCatalog.Keys.PayrollView, PermissionCatalog.Keys.PayrollManage,
            PermissionCatalog.Keys.SalaryView, PermissionCatalog.Keys.SalaryManage,
            // Ek ödeme artık maaşı görenle aynı seviyede: maaş kartında
            // resmi net, elden ödeme ve toplam ele geçen birlikte
            // gösteriliyor. Maaş görmeyen roller (Şantiye Şefi, Formen,
            // Sekreterya, Teknik) ek ödemeyi de görmemeye devam ediyor.
            // Özlük belgeleri kimlik ve adli sicil taşıyor; kendi dar
            // anahtarıyla korunuyor ve yalnızca İK'da.
            PermissionCatalog.Keys.PersonnelDocumentView,
            PermissionCatalog.Keys.PersonnelDocumentManage,
            PermissionCatalog.Keys.ExtraPaymentView, PermissionCatalog.Keys.ExtraPaymentManage
        ]),

        new("Ön Muhasebe", "Fatura/cari tam yetki, muhasebe fiş girişi, satın alma görüntüleme.",
        [
            .. HerRolde,
            // ÖDEME PLANI HAZIRLAMA (ÖP/1a · İ1) — onaylama YOK.
            PermissionCatalog.Keys.PaymentPlanPrepare,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView, PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.ReportsView,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.CurrentAccountsEdit, PermissionCatalog.Keys.CurrentAccountsDelete,
            PermissionCatalog.Keys.AccountingView, PermissionCatalog.Keys.AccountingCreate, PermissionCatalog.Keys.AccountingEdit,
            PermissionCatalog.Keys.PurchasingRequestsView, PermissionCatalog.Keys.PurchasingRfqView,
            PermissionCatalog.Keys.PurchasingOrdersView, PermissionCatalog.Keys.PurchasingReceiptsView,
            PermissionCatalog.Keys.CompaniesManage, PermissionCatalog.Keys.AccountingManage, PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.SalaryView,
            PermissionCatalog.Keys.ExtraPaymentView, PermissionCatalog.Keys.ExtraPaymentManage,
            PermissionCatalog.Keys.AiUse
        ]),

        new("Teknik Ofis", "Projeler; keşif/metraj/hakediş tam yetki; dosyalar tam yetki; maliyet ve kâr görünür.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.ProjectsCreate,
            PermissionCatalog.Keys.ProjectsEdit,
            PermissionCatalog.Keys.SitesView,
            PermissionCatalog.Keys.EngineeringView, PermissionCatalog.Keys.EngineeringManage,
            PermissionCatalog.Keys.OfferTrackingView, PermissionCatalog.Keys.OfferTrackingManage,
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
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.ProjectsCreate,
            PermissionCatalog.Keys.ProjectsEdit,
            PermissionCatalog.Keys.SitesView, PermissionCatalog.Keys.SitesCreate,
            PermissionCatalog.Keys.SitesEdit, PermissionCatalog.Keys.SitesDelete,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsApprove,
            PermissionCatalog.Keys.EngineeringView, PermissionCatalog.Keys.EngineeringManage,
            PermissionCatalog.Keys.OfferTrackingView, PermissionCatalog.Keys.OfferTrackingManage,
            PermissionCatalog.Keys.HakedisView, PermissionCatalog.Keys.HakedisCreate,
            PermissionCatalog.Keys.HakedisEdit, PermissionCatalog.Keys.HakedisDelete,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.DocumentsEdit, PermissionCatalog.Keys.DocumentsDelete,
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate,
            PermissionCatalog.Keys.PersonnelEdit, PermissionCatalog.Keys.PersonnelDelete,
            PermissionCatalog.Keys.AttendancePayrollView,
            // Sahadaki taşeronu yöneten ve hakedişini hazırlayan rol.
            // Elden tutarlar extra_payment.* ile ayrıca korunuyor;
            // bu rolde o izin YOK, dolayısıyla elden kısmı görmez.
            PermissionCatalog.Keys.SubcontractorView, PermissionCatalog.Keys.SubcontractorManage,
            PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.ProjectsManage, PermissionCatalog.Keys.HakedisManage,
            PermissionCatalog.Keys.PersonnelManage, PermissionCatalog.Keys.AttendanceView, PermissionCatalog.Keys.PayrollView,
            // İş programını DÜZENLER. Tarih ve bağımlılık değiştirmek
            // bütün zinciri kaydırdığı için bu yetki bilinçli olarak dar:
            // Genel Müdür ve Teknik Koordinatör. Diğer roller okur.
            PermissionCatalog.Keys.ScheduleManage,
            // Saha İSG kayıtları girebilir; sağlık raporunun tıbbi detayı
            // ve kaza kayıt defteri kasıtlı olarak verilmedi.
            PermissionCatalog.Keys.IsgView, PermissionCatalog.Keys.IsgCreate, PermissionCatalog.Keys.IsgEdit
        ]),

        new("İSG Sorumlusu",
            "İSG tam yetki: OSGB sözleşmesi, sağlık raporu (tıbbi detay dahil), eğitim, sertifika, kaza kayıtları ve saha belgeleri.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView, PermissionCatalog.Keys.SitesView,
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.CurrentAccountsView,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.ReportsView, PermissionCatalog.Keys.AiUse,
            PermissionCatalog.Keys.IsgView, PermissionCatalog.Keys.IsgCreate,
            PermissionCatalog.Keys.IsgEdit, PermissionCatalog.Keys.IsgDelete,
            PermissionCatalog.Keys.IsgHealthView,
            PermissionCatalog.Keys.IsgIncidentView, PermissionCatalog.Keys.IsgIncidentManage
        ]),

        new("Şantiye Şefi", "Sadece atandığı şantiyeler: günlük rapor girme, şantiye personelini görüntüleme, sarf talebi.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.SitesView,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsCreate,
            PermissionCatalog.Keys.SiteReportsEdit, PermissionCatalog.Keys.SiteReportsDelete,
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.InventoryView, PermissionCatalog.Keys.InventoryCreate,
            PermissionCatalog.Keys.PurchasingRequestsView, PermissionCatalog.Keys.PurchasingRequestsCreate,
            PermissionCatalog.Keys.InventoryManage, PermissionCatalog.Keys.PurchasingView, PermissionCatalog.Keys.PurchasingManage,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            // İş programını OKUR: planı uygulayan saha, terminini
            // görmeden çalışamaz. Veri kapsamı zaten kendi şantiyesiyle
            // sınırlı; düzenleme yetkisi yok.
            PermissionCatalog.Keys.ScheduleView,
            // Şantiyedeki aracı GÖRÜR (hangi araç bende, muayenesi ne
            // zaman) ama atamasını değiştiremez. Elden kalemler ayrıca
            // extra_payment.view maskesinde kalır.
            PermissionCatalog.Keys.VehicleView,
            PermissionCatalog.Keys.AiUse
        ], RoleDataScopePolicy.SiteOnly),

        new("Formen", "Sadece atandığı şantiyede günlük rapor girme (taslak), kendi ekibini görüntüleme.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.SitesView,
            PermissionCatalog.Keys.SiteReportsView, PermissionCatalog.Keys.SiteReportsCreate,
            PermissionCatalog.Keys.SiteReportsEdit,
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            // İş programını OKUR: planı uygulayan saha, terminini
            // görmeden çalışamaz. Veri kapsamı zaten kendi şantiyesiyle
            // sınırlı; düzenleme yetkisi yok.
            PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.AiUse
        ], RoleDataScopePolicy.SiteOnly),

        new("Satış Personeli",
            "Merkez depodan perakende satış hazırlar. Stok adedini ve satış fiyatını görür, MALİYETİ GÖRMEZ; iskonto tavanını aşamaz, elden satış açamaz.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView,
            PermissionCatalog.Keys.SalesView, PermissionCatalog.Keys.SalesCreate,
            // Cari GÖRÜNTÜLEME var, oluşturma yok: vadeli satışta müşteri
            // seçebilmeli ama yeni cari açmak muhasebenin işi.
            PermissionCatalog.Keys.CurrentAccountsView
            // BİLEREK YOK:
            //   inventory.view  -> maliyet ve stok değeri döndürüyor;
            //                      satış ekranı kendi dar ucunu kullanır
            //   sales.cash      -> elden satış ayrı yetki
            //   sales.approve   -> kendi iskontosunu kendi onaylayamaz
        ]),

        new("Sekreterya", "Dosyalar tam yetki, cari kart oluşturma/görüntüleme, projeler görüntüleme.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.DocumentsView, PermissionCatalog.Keys.DocumentsCreate,
            PermissionCatalog.Keys.DocumentsEdit, PermissionCatalog.Keys.DocumentsDelete,
            PermissionCatalog.Keys.CurrentAccountsView, PermissionCatalog.Keys.CurrentAccountsCreate,
            PermissionCatalog.Keys.SecretariatView, PermissionCatalog.Keys.SecretariatManage,
            PermissionCatalog.Keys.CompaniesView,
            PermissionCatalog.Keys.AiUse
        ]),

        // Filo modülü geldi: bu rol tam da onu bekliyordu. Araç kartı,
        // atama ve araç masraf dökümü artık bu rolün işi; masrafın
        // kendisi gider modülünden girildiği için gider yetkisi de
        // veriliyor. Elden kalemler yine extra_payment.view maskesinde
        // — bu rolde o anahtar yok.
        new("Araç Sorumlusu", "Filo: araç kartları, atamalar ve araç masrafları.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.VehicleView, PermissionCatalog.Keys.VehicleManage,
            PermissionCatalog.Keys.ExpenseView, PermissionCatalog.Keys.ExpenseManage,
            PermissionCatalog.Keys.AiUse
        ]),

        new("Depo Sorumlusu", "Stok giriş-çıkış, transfer, rezervasyon ve mal kabul.",
        [
            .. HerRolde,
            PermissionCatalog.Keys.DashboardView, PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ScheduleView,
            PermissionCatalog.Keys.InventoryView, PermissionCatalog.Keys.InventoryCreate,
            PermissionCatalog.Keys.InventoryEdit, PermissionCatalog.Keys.InventoryDelete,
            PermissionCatalog.Keys.PurchasingReceiptsView, PermissionCatalog.Keys.PurchasingReceiptsCreate,
            PermissionCatalog.Keys.PurchasingReceiptsEdit, PermissionCatalog.Keys.PurchasingReceiptsApprove,
            PermissionCatalog.Keys.InventoryManage, PermissionCatalog.Keys.PurchasingView,
            PermissionCatalog.Keys.PurchasingManage, PermissionCatalog.Keys.PurchasingApprove,
            PermissionCatalog.Keys.AiUse
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
