import { canAccessRoute } from "@/lib/auth/route-permissions";
import { foldTurkish } from "@/lib/search/fold";

/**
 * MENÜ AĞACI — uygulamanın TEK gezinme kaynağı.
 *
 * Kabuk (yan menü), komut paleti (Ctrl+K) ve kırıntı yolu aynı listeden
 * besleniyor. Ayrı listeler tutulsaydı, yeni bir sayfa menüye eklenip
 * palete eklenmezdi ve kullanıcı aradığını bulamazdı; daha kötüsü,
 * birinde gizlenip diğerinde görünen bir sayfa çıkardı.
 *
 * İZİN BURADA TANIMLANMAZ: her yolun izni lib/auth/route-permissions
 * içinde. Menü yalnızca "ne var, nerede duruyor" bilgisini taşır.
 */

export type MenuItem = {
  label: string;
  href: string;
  icon?: string;
};

export type MenuGroup = {
  key: string;
  label: string;
  items: MenuItem[];
};

export const MENU_GROUPS: MenuGroup[] = [
  {
    key: "management",
    label: "YÖNETİM",
    items: [
      {
        label: "Göstergeler",
        href: "/yonetim",
        icon: "◈",
      },
    ],
  },
  {
    key: "organization",
    label: "ORGANİZASYON",
    items: [
      {
        label: "Şirketler",
        href: "/sirketler",
        icon: "▦",
      },
      {
        label: "Şubeler",
        href: "/subeler",
        icon: "▤",
      },
    ],
  },
  {
    key: "accounting",
    label: "MUHASEBE",
    items: [
      {
        label: "Muhasebe Merkezi",
        href: "/muhasebe",
        icon: "▦",
      },
      {
        label: "Hesap Planı",
        href: "/muhasebe/hesap-plani",
        icon: "○",
      },
      {
        label: "Hesap Planı Aktar",
        href: "/muhasebe/hesap-plani/aktar",
        icon: "○",
      },
      {
        label: "Kesinti Hesapları",
        href: "/muhasebe/kesinti-hesaplari",
        icon: "⊟",
      },
      {
        label: "Tedarikçi Faturaları",
        href: "/muhasebe/faturalar",
        icon: "○",
      },
      {
        label: "Satış Faturaları",
        href: "/muhasebe/satis-faturalari",
        icon: "○",
      },
      {
        label: "E-Fatura İçe Aktar",
        href: "/muhasebe/e-fatura-ice-aktar",
        icon: "○",
      },
      {
        label: "Muhasebe Fişleri",
        href: "/muhasebe/fisler",
        icon: "○",
      },
      {
        label: "Yeni Muhasebe Fişi",
        href: "/muhasebe/fisler/yeni",
        icon: "○",
      },
      {
        label: "Yevmiye Defteri",
        href: "/muhasebe/yevmiye",
        icon: "○",
      },
      {
        label: "Büyük Defter",
        href: "/muhasebe/buyuk-defter",
        icon: "○",
      },
      {
        label: "Kur Değerlemesi",
        href: "/muhasebe/kur-degerlemesi",
        icon: "○",
      },
      {
        label: "Rapor Merkezi",
        href: "/raporlar",
        icon: "▤",
      },
    ],
  },
  {
    key: "finance",
    label: "FİNANS",
    items: [
      {
        label: "Finans Merkezi",
        href: "/finans",
        icon: "▨",
      },
      {
        label: "Cari Kartlar",
        href: "/cariler",
        icon: "○",
      },
      {
        label: "Kasa / Banka",
        href: "/finans/kasa-banka",
        icon: "▣",
      },
      {
        label: "Çek Defteri",
        href: "/finans/cekler",
        icon: "▩",
      },
      {
        label: "Nakit Akışı",
        href: "/finans/nakit-akis",
        icon: "≈",
      },
      {
        label: "Gider Merkezi",
        href: "/finans/gider-merkezi",
        icon: "◫",
      },
      {
        label: "Finansal Araçlar",
        href: "/finans/finansal-araclar",
        icon: "◈",
      },
      {
        label: "Vergi Yükü",
        href: "/finans/vergi",
        icon: "⚖",
      },
      {
        label: "Piyasa (Bakır/Kur)",
        href: "/finans/piyasa",
        icon: "◭",
      },
      {
        label: "Hakedişler",
        href: "/hakedis",
        icon: "▧",
      },
      {
        label: "Yeni Hakediş",
        href: "/hakedis/yeni",
        icon: "○",
      },
      {
        label: "Hakediş Takip",
        href: "/hakedis/takip",
        icon: "≡",
      },
      {
        label: "Hakediş Dosyaları",
        href: "/hakedis/dosyalar",
        icon: "⎙",
      },
      {
        label: "Fiyat Farkı",
        href: "/fiyat-farki",
        icon: "∆",
      },
    ],
  },
  {
    key: "purchasing",
    label: "SATIN ALMA",
    items: [
      {
        label: "Satın Alma Talepleri",
        href: "/satin-alma",
        icon: "⌑",
      },
      {
        label: "RFQ Süreçleri",
        href: "/satin-alma/rfq",
        icon: "≋",
      },
      {
        label: "Siparişler",
        href: "/satin-alma/siparis",
        icon: "▤",
      },
      {
        label: "Satın Alma Raporları",
        href: "/satin-alma/raporlar",
        icon: "▦",
      },
      {
        label: "Karar Destek",
        href: "/satin-alma/karar-destek",
        icon: "★",
      },
      {
        label: "Bütçe ve Onay",
        href: "/satin-alma/butce-onay",
        icon: "✓",
      },
      {
        label: "Alış İadeleri",
        href: "/depo-stok/iadeler",
        icon: "○",
      },
      {
        label: "Mal Kabul",
        href: "/depo-stok/mal-kabul",
        icon: "○",
      },
    ],
  },
  {
    key: "fleet",
    label: "FİLO",
    items: [
      {
        label: "Araçlar",
        href: "/filo",
        icon: "⛟",
      },
    ],
  },
  {
    key: "inventory",
    label: "DEPO VE STOK",
    items: [
      {
        label: "Depo Merkezi",
        href: "/depo-stok",
        icon: "⌂",
      },
      {
        label: "Depolar",
        href: "/depo-stok/depolar",
        icon: "▤",
      },
      {
        // Kategori SİSTEM GENELİ: özellik şablonu, izin verilen
        // birimler ve tip burada tanımlanır; kart açma ekranı
        // şablonun tamamını buradan alır.
        label: "Stok Kategorileri",
        href: "/depo-stok/kategoriler",
        icon: "≡",
      },
      {
        // Etiket "Yeni Depo" idi ama bağlantı MALZEME KARTI formuna
        // gidiyordu; depo açan kullanıcı yanlış ekrana düşüyordu.
        label: "Yeni Malzeme Kartı",
        href: "/depo-stok/yeni",
        icon: "○",
      },
      {
        label: "Stok Giriş",
        href: "/depo-stok/giris",
        icon: "○",
      },
      {
        label: "Stok Çıkış",
        href: "/depo-stok/cikis",
        icon: "○",
      },
      {
        label: "Stok Hareketleri",
        href: "/depo-stok/hareketler",
        icon: "○",
      },
      {
        label: "Depo Transferi",
        href: "/depo-stok/transfer",
        icon: "○",
      },
      {
        // Sayfa vardı ama menüde hiç yoktu; kimse ulaşamıyordu.
        label: "Stok Sayımı",
        href: "/depo-stok/sayim",
        icon: "○",
      },
      {
        label: "Mal Kabul",
        href: "/depo-stok/mal-kabul",
        icon: "○",
      },
      {
        label: "Malzeme Talepleri",
        href: "/depo-stok/malzeme-talepleri",
        icon: "○",
      },
    ],
  },
  {
    key: "projects",
    label: "PROJE VE OPERASYON",
    items: [
      {
        label: "Projeler",
        href: "/projeler",
        icon: "▣",
      },
      {
        // Proje listesine giremeyen saha (Şantiye Şefi, Formen) kendi
        // şantiyelerinin iş programına buradan ulaşır.
        label: "İş Programı",
        href: "/is-programi",
        icon: "▰",
      },
      {
        // Aynı kayıt: projenin sözleşme icmali. "Keşif" adı kodda
        // (ProjectBoq) duruyor, kullanıcı tarafında icmal deniyor.
        label: "Sözleşme İcmali",
        href: "/kesifler",
        icon: "▤",
      },
      {
        label: "Taşeronlar",
        href: "/taseronlar",
        icon: "▦",
      },
      {
        label: "Metrajlar",
        href: "/metrajlar",
        icon: "▥",
      },
      {
        label: "İş / Teklif Takibi",
        href: "/teklifler/takip",
        icon: "◷",
      },
      {
        label: "Teklifler",
        href: "/teklifler",
        icon: "₺",
      },
    ],
  },
  {
    key: "human-resources",
    label: "İNSAN KAYNAKLARI",
    items: [
      {
        label: "İK Dashboard",
        href: "/insan-kaynaklari",
        icon: "▦",
      },
      {
        label: "Personeller",
        href: "/insan-kaynaklari/personeller",
        icon: "♙",
      },
      {
        label: "Veri Eksikleri",
        href: "/insan-kaynaklari/veri-eksikleri",
        icon: "!",
      },
      {
        label: "Personel 360°",
        href: "/insan-kaynaklari/personel-360",
        icon: "◎",
      },
      {
        label: "Maaş Kartları",
        href: "/insan-kaynaklari/ucret-kartlari",
        icon: "₺",
      },
      {
        label: "Bordro Ön Kontrol",
        href: "/insan-kaynaklari/bordro-on-kontrol",
        icon: "✓",
      },
      {
        label: "SGK Bildirim",
        href: "/insan-kaynaklari/sgk-bildirim",
        icon: "⇄",
      },
      {
        label: "Ek Ücretler",
        href: "/insan-kaynaklari/ek-ucretler",
        icon: "+",
      },
      {
        label: "Ek Ödemeler",
        href: "/insan-kaynaklari/ek-odemeler",
        icon: "◆",
      },
      {
        label: "Çıkış ve Tazminat",
        href: "/insan-kaynaklari/cikis-tazminat",
        icon: "⇥",
      },
      {
        label: "Organizasyon",
        href: "/insan-kaynaklari/organizasyon",
        icon: "▤",
      },
      {
        label: "İşe Alım",
        href: "/insan-kaynaklari/ise-alim",
        icon: "+",
      },
      {
        label: "Puantaj Cetveli",
        href: "/insan-kaynaklari/puantaj-cetveli",
        icon: "▦",
      },
      {
        label: "Tatil Takvimi",
        href: "/insan-kaynaklari/tatil-takvimi",
        icon: "◵",
      },
      {
        label: "Puantaj",
        href: "/insan-kaynaklari/puantaj",
        icon: "◷",
      },
      {
        label: "İzin Yönetimi",
        href: "/insan-kaynaklari/izinler",
        icon: "○",
      },
      {
        label: "İzin Bakiyesi",
        href: "/insan-kaynaklari/izin-bakiye",
        icon: "◔",
      },
      {
        label: "Fazla Mesai",
        href: "/insan-kaynaklari/fazla-mesai",
        icon: "○",
      },
      {
        label: "Görevlendirmeler",
        href: "/insan-kaynaklari/gorevlendirmeler",
        icon: "➤",
      },
      {
        label: "Avanslar",
        href: "/insan-kaynaklari/avanslar",
        icon: "₺",
      },
      {
        label: "Bordro",
        href: "/insan-kaynaklari/bordro",
        icon: "▧",
      },
      {
        label: "Bordro Maliyet Raporu",
        href: "/insan-kaynaklari/maliyet-raporu",
        icon: "₼",
      },
      {
        label: "İK Raporları",
        href: "/insan-kaynaklari/raporlar",
        icon: "▤",
      },
      {
        label: "Onay Merkezi",
        href: "/insan-kaynaklari/onay-merkezi",
        icon: "✓",
      },
      // Eğitim ve sertifika takibi İSG menüsündeki "Personel Kayıtları"
      // ekranına taşındı; buradaki iki bağlantı var olmayan bir uca
      // bağlı taslak sayfaya gidiyordu. Eski adresler yönlendiriliyor.
      //
      // Yetkinlikler, Performans ve Disiplin de aynı durumdaydı: üçünün
      // de arkasında model, tablo ve uç YOKTU; ekran yalnızca "yakında"
      // plakası gösteriyordu. Gidecek bir yerleri olmadığı için
      // yönlendirilmediler, menüden kaldırıldılar. Modül gerçekten
      // geldiğinde menü satırı geri eklenir.
      {
        label: "Demirbaş / Aletler",
        href: "/demirbas",
        icon: "○",
      },
      {
        label: "Alet Servisi",
        href: "/demirbas/servis",
        icon: "○",
      },
      {
        label: "Zimmetler",
        href: "/insan-kaynaklari/zimmetler",
        icon: "▣",
      },
      {
        label: "Kariyer",
        href: "/insan-kaynaklari/kariyer",
        icon: "↑",
      },
    ],
  },
  {
    key: "isg",
    label: "İSG",
    items: [
      {
        label: "İSG Paneli",
        href: "/isg",
        icon: "▦",
      },
      {
        label: "Personel Kayıtları",
        href: "/isg/personel",
        icon: "♙",
      },
      {
        label: "Kaza / Ramak Kala",
        href: "/isg/kazalar",
        icon: "!",
      },
      {
        label: "Saha Belgeleri",
        href: "/isg/belgeler",
        icon: "□",
      },
      {
        label: "OSGB Sözleşmeleri",
        href: "/isg/osgb",
        icon: "▤",
      },
      {
        // İzin gerekmez: uç yalnızca çağıranın kendi kaydını döndürür.
        label: "İSG Belgelerim",
        href: "/isg/benim",
        icon: "○",
      },
    ],
  },
  {
    key: "engineering",
    label: "MÜHENDİSLİK",
    items: [
      {
        label: "Mühendislik Merkezi",
        href: "/muhendislik",
        icon: "◇",
      },
      {
        label: "Poz Kütüphanesi",
        href: "/muhendislik/pozlar",
        icon: "▦",
      },
      {
        label: "Özel Pozlar",
        href: "/muhendislik/pozlar/ozel",
        icon: "Ö",
      },
      {
        label: "Reçeteler",
        href: "/muhendislik/receteler",
        icon: "⚙",
      },
      {
        label: "Reçete İçe Aktar",
        href: "/muhendislik/receteler/ice-aktar",
        icon: "⇪",
      },
      {
        label: "Fiyat Listeleri",
        href: "/teklifler/fiyatlar",
        icon: "₺",
      },
    ],
  },
  {
    key: "secretariat",
    label: "SEKRETERYA",
    items: [
      {
        label: "Gelen / Giden Evrak",
        href: "/sekreterya/evrak",
        icon: "✉",
      },
      {
        label: "Kargo Takibi",
        href: "/sekreterya/kargo",
        icon: "□",
      },
      {
        label: "Ziyaretçiler",
        href: "/sekreterya/ziyaretciler",
        icon: "♙",
      },
      {
        label: "Telefon Notları",
        href: "/sekreterya/telefon-notlari",
        icon: "☎",
      },
      {
        label: "Toplantılar",
        href: "/sekreterya/toplantilar",
        icon: "▤",
      },
      {
        label: "Randevular",
        href: "/sekreterya/randevular",
        icon: "◷",
      },
    ],
  },
  {
    key: "management",
    label: "YÖNETİM",
    items: [
      {
        label: "Onay Merkezi",
        href: "/onay-merkezi",
        icon: "✓",
      },
      {
        label: "Görevler",
        href: "/gorevler",
        icon: "☑",
      },
      {
        label: "Dokümanlar",
        href: "/dokumanlar",
        icon: "□",
      },
      {
        label: "Raporlar",
        href: "/raporlar",
        icon: "▤",
      },
    ],
  },
  {
    key: "system",
    label: "SİSTEM YÖNETİMİ",
    items: [
      {
        label: "Kullanıcılar ve Yetkiler",
        href: "/sistem-yonetimi/kullanicilar",
        icon: "⚿",
      },
      {
        label: "Yetki Matrisi",
        href: "/sistem-yonetimi/yetki-matrisi",
        icon: "▦",
      },
      {
        label: "Denetim Kayıtları",
        href: "/sistem-yonetimi/denetim-kayitlari",
        icon: "⚑",
      },
      {
        label: "Şirket Ayarları",
        href: "/sistem-yonetimi/sirket-ayarlari",
        icon: "⚙",
      },
      {
        label: "Erişim Talepleri",
        href: "/sistem-yonetimi/erisim-talepleri",
        icon: "⏱",
      },
    ],
  },
  {
    key: "ai",
    label: "ENDERUN AI",
    items: [
      {
        label: "AI Asistan",
        href: "/ai-asistan",
        icon: "⌘",
      },
    ],
  },
];

export function pathOnly(href: string) {
  return href.split("?")[0];
}

/**
 * Türkçe arama katlaması artık `lib/search/fold` içinde: aynı kural
 * ekranlardaki arama kutularında da geçerli, menüye özel değil.
 * Buradan yeniden dışa veriliyor ki menüyü kullanan çağıranlar
 * bozulmasın.
 */
export { foldTurkish };

/**
 * Kullanıcının GÖREBİLECEĞİ menü. Boş kalan bölüm başlığı da düşer:
 * boş bir bölüm, erişilemeyen bir alan varmış izlenimi verirdi.
 *
 * Oturum yoksa menü BOŞ döner — dolu menüyü gösterip sonra öğe
 * kaybetmek, kullanıcıya olmayan yetkiyi bir an için göstermek olurdu.
 */
export function visibleMenuGroups(
  permissions: Set<string> | null,
  hasAllPermissions: boolean,
): MenuGroup[] {
  if (!permissions) return [];

  return MENU_GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) =>
      canAccessRoute(pathOnly(item.href), permissions, hasAllPermissions),
    ),
  })).filter((group) => group.items.length > 0);
}

/** Bir yolun menüdeki karşılığı — kırıntı yolu bundan türer. */
export function findMenuEntry(
  pathname: string,
  groups: MenuGroup[] = MENU_GROUPS,
): { group: MenuGroup; item: MenuItem } | null {
  let best: { group: MenuGroup; item: MenuItem; length: number } | null = null;

  for (const group of groups) {
    for (const item of group.items) {
      const href = pathOnly(item.href);
      const matches = pathname === href || pathname.startsWith(`${href}/`);

      if (!matches) continue;

      // EN UZUN EŞLEŞME KAZANIR: /muhasebe/fisler/yeni hem "Muhasebe
      // Fişleri" hem "Yeni Muhasebe Fişi" ile eşleşiyor; kullanıcının
      // gerçekten durduğu yer daha uzun olanı.
      if (!best || href.length > best.length) {
        best = { group, item, length: href.length };
      }
    }
  }

  return best ? { group: best.group, item: best.item } : null;
}

export type MenuSearchResult = {
  group: MenuGroup;
  item: MenuItem;
};

/**
 * Komut paletinin arama motoru — SAF, ekrandan bağımsız.
 *
 * Aranan metin hem sayfa adında hem bölüm adında aranır: "finans"
 * yazan kullanıcı finans bölümünün tamamını görür. Sonuçlar
 * ARANANLA BAŞLAYANLAR önce gelecek şekilde sıralanır; "kasa" yazınca
 * "Kasa Hesapları" listenin başında olmalı, adının ortasında "kasa"
 * geçen bir sayfa değil.
 */
export function searchMenu(
  query: string,
  groups: MenuGroup[],
  limit = 12,
): MenuSearchResult[] {
  const needle = foldTurkish(query.trim());

  const all = groups.flatMap((group) =>
    group.items.map((item) => ({ group, item })),
  );

  if (needle.length === 0) return all.slice(0, limit);

  const scored: { result: MenuSearchResult; score: number }[] = [];

  for (const entry of all) {
    const label = foldTurkish(entry.item.label);
    const groupLabel = foldTurkish(entry.group.label);

    let score = -1;

    if (label.startsWith(needle)) score = 0;
    else if (label.includes(needle)) score = 1;
    else if (groupLabel.includes(needle)) score = 2;
    else if (foldTurkish(pathOnly(entry.item.href)).includes(needle)) score = 3;

    if (score >= 0) scored.push({ result: entry, score });
  }

  return scored
    .sort((left, right) => left.score - right.score)
    .slice(0, limit)
    .map((entry) => entry.result);
}
