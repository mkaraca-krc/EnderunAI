/**
 * YOL → İZİN HARİTASI: TEK KAYNAK.
 *
 * Bu harita üç yerden okunuyor:
 *   1. middleware.ts — sayfa kapısı (URL elle yazılsa da geçilemez)
 *   2. erp-shell.tsx — menü filtresi (yetkisiz öğe hiç render edilmez)
 *   3. gerekirse ekran içi bağlantılar
 *
 * ÖNCEDEN İKİ KOPYAYDI VE AYRIŞMIŞTI. Menüde gizlenen dokuz ekran
 * (elden ödemeler ve gider merkezi dahil) adres çubuğuna yazan
 * kullanıcıya açılıyordu; filo ise menüde herkese görünüp tıklanınca
 * yetkisiz sayfasına düşüyordu. Ayrışmanın sebebi kopyanın kendisiydi:
 * biri güncellenip diğeri unutuluyordu.
 *
 * ARAYÜZ GÜVENLİK SINIRI DEĞİLDİR. Buradaki her kural yalnızca
 * GÖRÜNÜRLÜK içindir; gerçek yetki kontrolü uçlarda
 * <c>RequirePermission</c> ile yapılır ve bu dosyadaki bir hata veriyi
 * açığa çıkarmaz, yalnızca kullanıcıya işe yaramayan bir ekran ya da
 * gereksiz bir "yetkiniz yok" gösterir.
 *
 * SIRA ÖNEMLİ: spesifik kalıp genelden ÖNCE gelir. Örneğin
 * "bordro-on-kontrol" hem kendi kalıbına hem genel "bordro" kalıbına
 * uyuyor; genel kural önce gelseydi kullanıcı ekranı açar, sonra uçtan
 * 403 yerdi.
 */

export type RoutePermission = string | string[] | null;

type Rule = {
  /** Yol öneki ya da düzenli ifade. */
  match: string | RegExp;
  /**
   * Gereken izin. Dizi ise HERHANGİ BİRİ yeter (VEYA). Null ise ekran
   * bilinçli olarak açıktır — gerekçesi yorumda yazar.
   */
  permission: RoutePermission;
};

const RULES: Rule[] = [
  // --- Sistem yönetimi ---
  {
    // Şirket ayarları hem ayar hem kullanıcı yönetimi tarafından
    // kullanılıyor; ikisinden biri yeter.
    match: "/sistem-yonetimi/sirket-ayarlari",
    permission: ["company-settings.view", "system.users.manage"],
  },
  { match: "/sistem-yonetimi", permission: "system.users.manage" },

  // --- İnsan kaynakları ---
  //
  // Elden ödemeler kendi dar izniyle korunur; bordroyu yöneten herkese
  // görünmez.
  { match: "/insan-kaynaklari/ek-odemeler", permission: "extra_payment.view" },
  {
    // Bordro ön kontrolü ve SGK dökümü puantaj+bordro kesişiminde;
    // genel "bordro" kalıbından ÖNCE.
    match: /^\/insan-kaynaklari\/(bordro-on-kontrol|sgk-bildirim|izin-bakiye)/,
    permission: "attendance-payroll.view",
  },
  {
    match:
      /^\/insan-kaynaklari\/(bordro|ucret-kartlari|ek-ucretler|cikis-tazminat|avanslar)/,
    permission: "payroll.view",
  },
  {
    // Puantaj cetveli tatil takviminden dolduğu için ikisi aynı yetkiyle.
    match:
      /^\/insan-kaynaklari\/(puantaj|gunluk-puantaj|izinler|fazla-mesai|tatil-takvimi)/,
    permission: "attendance.view",
  },
  { match: "/insan-kaynaklari", permission: "personnel.view" },

  // --- Taşeron ---
  //
  // Sözleşme birim fiyat ve bedel taşır; saha ve ofis rollerine
  // görünmez.
  { match: "/taseronlar", permission: "subcontractor.view" },

  // --- İSG ---
  { match: "/isg/kazalar", permission: "isg.incident.view" },
  {
    // Personelin KENDİ belgeleri: izin gerekmez, uç zaten yalnız kendi
    // kaydını döndürüyor. Bilinçli olarak açık.
    match: "/isg/benim",
    permission: null,
  },
  { match: "/isg", permission: "isg.view" },

  // --- Muhasebe ve finans ---
  /*
   * TEK İŞİ BİR AKSİYON OLAN EKRAN, O AKSİYONUN İZNİYLE AÇILIR.
   *
   * "/muhasebe" yalnız accounting.view istiyordu; yani yalnızca
   * görüntüleme yetkisi olan biri "Yeni Fiş" ekranını açıp uzun formu
   * doldurabiliyor, reddi ancak KAYDEDERKEN yiyordu. Düğmeyi gizlemek
   * bunu çözmez — ekranın kendisi zaten aksiyon.
   *
   * Sıra önemli: spesifik kalıplar genel "/muhasebe" kuralından ÖNCE.
   */
  { match: /^\/muhasebe\/[^/]+\/yeni/, permission: "accounting.create" },
  { match: /^\/muhasebe\/[^/]+\/[^/]+\/duzenle/, permission: "accounting.edit" },
  { match: "/muhasebe", permission: "accounting.view" },
  {
    // Gider merkezi şirket geneli tabloyu tek ekranda topluyor;
    // finance.view kadar geniş bir kapıya bırakılamaz.
    match: "/finans/gider-merkezi",
    permission: "expense.view",
  },
  /*
   * ÖDEME PLANI — HAZIRLAYAN VEYA ONAYLAYAN.
   *
   * Genel "/finans" kuralından ÖNCE olmak ZORUNDA: sonra kalsaydı ekran
   * finance.view olan herkese açılırdı ve haftanın kime ne ödeneceği
   * finans modülünü görebilen herkesin önüne düşerdi.
   *
   * İKİ ANAHTARIN BİRİ YETER: onaylayanın hazırlama izni olmak zorunda
   * değil. Tek anahtara bağlansaydı planı onaylayacak kişi kendi onay
   * ekranını açamazdı.
   *
   * Ekranın AÇILMASI ile İŞLEM YAPILMASI ayrı: onay düğmeleri
   * payment.plan.approve ile görünür, uçta da aynı izin aranır.
   */
  {
    match: "/finans/odeme-planlari",
    permission: ["payment.plan.prepare", "payment.plan.approve"],
  },
  { match: "/finans", permission: "finance.view" },

  // --- Hakediş ---
  /*
   * "yeni" ve "duzenle" TAM SAYFA AKSİYON EKRANI. Muhasebede olduğu
   * gibi burada da düğme kapısı yetmez: ekranın kendisi tek bir
   * yazma işleminden ibaret, o yüzden o işlemin izniyle açılır.
   */
  { match: /^\/hakedis\/yeni/, permission: "hakedis.create" },
  { match: /^\/hakedis\/[^/]+\/duzenle/, permission: "hakedis.edit" },
  { match: "/hakedis", permission: "hakedis.view" },
  { match: /^\/fiyat-farki\/[^/]+\/yeni/, permission: "hakedis.create" },
  { match: "/fiyat-farki", permission: "hakedis.view" },
  { match: /^\/metrajlar\/yeni/, permission: "hakedis.create" },
  { match: "/metrajlar", permission: "hakedis.view" },

  // --- Satın alma ---
  {
    match: "/satin-alma/butce-onay",
    permission: ["purchasing.view", "finance.view"],
  },
  { match: "/satin-alma", permission: "purchasing.view" },

  // --- Depo ---
  { match: /^\/depo-stok\/yeni/, permission: "inventory.create" },
  // Kategori/özellik bakımı depo YÖNETİMİ işi — hareket açma yetkisi
  // (inventory.create) yetmez.
  { match: /^\/depo-stok\/kategoriler/, permission: "inventory.view" },
  // Mutabakat mizan okuyor: depo değil MUHASEBE izni. Uç de
  // accounting.view zorluyor, rota kapısı onunla aynı olmalı.
  { match: /^\/depo-stok\/muhasebe-mutabakat/, permission: "accounting.view" },
  { match: /^\/depo-stok\/donemsel-sayim/, permission: "inventory.view" },
  { match: /^\/depo-stok\/stok-seviyeleri/, permission: "inventory.view" },
  { match: /^\/depo-stok\/etiket/, permission: "inventory.view" },
  // Raf QR'ı okutulunca açılır; görüntüleme yetkisi yeterli.
  { match: /^\/depo-stok\/raf\//, permission: "inventory.view" },
  { match: /^\/depo-stok\/mal-kabul\/yeni/, permission: "purchasing-receipts.create" },
  {
    match: /^\/depo-stok\/malzeme-talepleri\/yeni/,
    permission: "purchasing-requests.create",
  },
  { match: "/depo", permission: "inventory.view" },

  // --- Filo ---
  { match: "/filo", permission: "vehicle.view" },

  // --- Mühendislik ---
  {
    // İçe aktarma ekranları YAZMA yetkisi ister (uçlar
    // engineering.manage istiyor); genel kuraldan önce.
    match: /^\/muhendislik\/(pozlar|receteler)\/ice-aktar/,
    permission: "engineering.manage",
  },
  { match: /^\/muhendislik\/pozlar\/(yeni|ozel)/, permission: "engineering.manage" },
  { match: "/muhendislik", permission: "engineering.view" },
  { match: /^\/kesifler\/yeni/, permission: "hakedis.create" },
  { match: "/kesifler", permission: "engineering.view" },

  // --- Proje alt ekranları (spesifik → genel) ---
  {
    // Tutar ve kâr marjı taşıyan ekranlar hakediş iznine bağlı.
    match: /^\/projeler\/[^/]+\/(kar-analizi|icmal-ilerleme|maliyet-analizi)/,
    permission: "hakedis.view",
  },
  {
    // Malzeme ihtiyacı ekranının ucu satın alma talebi görüntüleme
    // izni istiyor.
    match: /^\/projeler\/[^/]+\/malzeme-ihtiyaci/,
    permission: "purchasing-requests.view",
  },
  {
    match: /^\/projeler\/[^/]+\/is-programi/,
    permission: ["projects.view", "schedule.view"],
  },
  {
    match: /^\/projeler\/[^/]+\/santiyeler\/yeni/,
    permission: "sites.create",
  },

  // İş programını okuma bilinçli olarak geniş: planı uygulayan saha
  // (Şantiye Şefi, Formen) proje listesini görmez ama kendi terminini
  // görmeden çalışamaz. Veri kapsamı zaten şantiyeleriyle sınırlı.
  { match: "/is-programi", permission: "schedule.view" },

  { match: "/projeler", permission: "projects.view" },
  { match: /^\/teklifler\/yeni/, permission: "engineering.manage" },
  { match: "/teklifler", permission: "projects.view" },

  // --- Sekreterya ---
  { match: "/sekreterya", permission: "secretariat.view" },
  { match: "/dokumanlar", permission: "secretariat.view" },

  // --- Diğer ---
  { match: "/gorevler", permission: "tasks.view" },
  { match: "/raporlar", permission: "reports.view" },
  { match: "/ai-asistan", permission: "ai.use" },

  { match: "/sirketler", permission: "companies.view" },
  { match: "/subeler", permission: "companies.view" },
  // Fiyat/tavan belirleme YÖNETİM ekranı: maliyet ve marj taşıyor.
  // Satış ekranından ÖNCE gelmeli — "/perakende" kalıbı bu yolu da
  // yakalar ve satış izniyle açılmasına yol açardı.
  { match: "/perakende/fiyatlar", permission: "inventory.edit" },
  { match: "/perakende/raporlar", permission: "sales.view" },
  { match: "/perakende", permission: "sales.view" },
  { match: "/cariler", permission: "companies.view" },
];

/** Yolun gerektirdiği izin; kural yoksa null (açık ekran). */
export function routePermission(pathname: string): RoutePermission {
  for (const rule of RULES) {
    const matched =
      typeof rule.match === "string"
        ? pathname === rule.match || pathname.startsWith(`${rule.match}/`) ||
          pathname.startsWith(rule.match)
        : rule.match.test(pathname);

    if (matched) return rule.permission;
  }

  return null;
}

/**
 * Kullanıcı bu yolu görebilir mi.
 *
 * @param hasAllPermissions Backend'in "bu kullanıcı katalogdaki her
 * izne sahip" bayrağı. ROL ADINA BAKILMAZ: rol yeniden adlandırılırsa
 * ya da başka bir role tüm izinler verilirse ad kontrolü yanlış cevap
 * verirdi.
 */
export function canAccessRoute(
  pathname: string,
  permissions: Iterable<string>,
  hasAllPermissions: boolean,
): boolean {
  if (hasAllPermissions) return true;

  const required = routePermission(pathname);

  if (!required) return true;

  const granted = permissions instanceof Set ? permissions : new Set(permissions);

  return Array.isArray(required)
    ? required.some((permission) => granted.has(permission))
    : granted.has(required);
}
