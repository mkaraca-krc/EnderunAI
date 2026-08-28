import { apiClient } from "@/lib/api/api-client";

/** Plan durumu — tek yönlü ilerler. */
export const OdemePlaniDurumu = {
  Taslak: 0,
  Onayda: 1,
  Onaylandi: 2,
  Uygulandi: 3,
  Kapandi: 4,
} as const;

/**
 * DURUM ETİKETLERİ SIFAT DEĞİL (Kural 37).
 *
 * Başlıkta `Toplam (Onaylandı)` biçiminde, parantez içinde kullanılır.
 * "Onaylandı toplam" gibi bir cümle Türkçede bozuk okunur ve sıfat
 * karşılığı listesi açmak, yeni durum eklendiğinde karşılığını
 * yazmayı unutan biri yüzünden aynı bozukluğu geri getirir.
 */
export const ODEME_PLANI_DURUM_ETIKETLERI: Record<number, string> = {
  0: "Taslak",
  1: "Onayda",
  2: "Onaylandı",
  3: "Uygulandı",
  4: "Kapandı",
};

export const OdemeSatirKarari = {
  Bekliyor: 0,
  Onaylandi: 1,
  Reddedildi: 2,
  Kismi: 3,
} as const;

export const ODEME_KARAR_ETIKETLERI: Record<number, string> = {
  0: "Bekliyor",
  1: "Onaylandı",
  2: "Reddedildi",
  3: "Kısmi",
};

export const OdemeSatirOdemeDurumu = {
  Odenmedi: 0,
  KismenOdendi: 1,
  Odendi: 2,
} as const;

export const ODEME_DURUM_ETIKETLERI: Record<number, string> = {
  0: "Ödenmedi",
  1: "Kısmen ödendi",
  2: "Ödendi",
};

export const OdemeYontemi = { HavaleEft: 0, Cek: 1, Nakit: 2 } as const;

export const ODEME_YONTEM_ETIKETLERI: Record<number, string> = {
  0: "Havale/EFT",
  1: "Çek",
  2: "Nakit",
};

export const BakiyeKaynagi = { Hesaplandi: 0, ElleGirildi: 1 } as const;

export const OdemeKapanisSebebi = {
  ParaYetmedi: 0,
  Ertelendi: 1,
  FaturaGelmedi: 2,
  IptalEdildi: 3,
  Diger: 90,
} as const;

export const KAPANIS_SEBEP_ETIKETLERI: Record<number, string> = {
  0: "Para yetmedi",
  1: "Ertelendi",
  2: "Fatura gelmedi",
  3: "İptal edildi",
  90: "Diğer",
};

export type PlanOzeti = {
  id: string;
  haftaBaslangici: string;
  odemeGunu: string;
  durum: number;
  satirSayisi: number;
  bekleyenSatir: number;
  hazirlayanUserId?: string | null;
  onaylayanUserId?: string | null;
  kapanmaAnUtc?: string | null;
};

export type SatirOzeti = {
  id: string;
  currentAccountId: string;
  cariUnvan?: string | null;
  onerilenTutar: number;
  yontem: number;
  cekVadesi?: string | null;
  oncelik: number;
  cashAccountId?: string | null;
  aciklama?: string | null;
  karar: number;
  onaylananTutar?: number | null;
  odemeDurumu: number;
  odenenTutar: number;
  devirHaftaSayisi: number;

  /**
   * ONAYDAN SONRA DEĞİŞTİ Mİ — KARAR SUNUCUDAN GELİR.
   *
   * Ekran kendi karşılaştırmasını YAZMIYOR. Yazsaydı sunucudaki K2
   * ile zamanla ayrışırdı; çek ekranındaki `canEdit` için de aynı
   * ilke uygulanmıştı.
   */
  onaydanSonraDegisti: boolean;
  degisenAlanlar: string[];

  kapanisSebebi?: number | null;
  kapanisAciklamasi?: string | null;
};

export type PlanDisiOzeti = {
  id: string;
  currentAccountId: string;
  cariUnvan?: string | null;
  tutar: number;
  odemeTarihi: string;
  sebep: string;
};

export type HesapButcesi = {
  cashAccountId: string;
  nakitCikis: number;
  gosterilenBakiye: number;
  fark: number;
  bakiyeKaynagi?: number | null;
};

export type VadeYukumlulugu = { yil: number; ay: number; tutar: number };

/**
 * K6 — İKİ AYRI SAYI, TOPLANMAZ.
 *
 * `hesapBazindaNakit` bu cuma ÇIKACAK parayı, `gelecekYukumlulukler`
 * bu cuma YARATILAN çek borcunu taşır. Tek sayıya toplanırsa hafta
 * olduğundan pahalı görünür ve gerçek nakit ihtiyacı kaybolur.
 */
export type ButceOzeti = {
  hesapBazindaNakit: HesapButcesi[];
  gelecekYukumlulukler: VadeYukumlulugu[];
};

export type PlanDetayi = {
  id: string;
  haftaBaslangici: string;
  odemeGunu: string;
  durum: number;
  hazirlayanUserId?: string | null;
  onaylayanUserId?: string | null;
  satirlar: SatirOzeti[];
  gecenHaftaninPlanDisi: PlanDisiOzeti[];
  butce: ButceOzeti;
};

export type SatirIstegi = {
  currentAccountId: string;
  tutar: number;
  yontem: number;
  cekVadesi?: string | null;
  oncelik: number;
  cashAccountId?: string | null;
  aciklama?: string | null;
};

/*
 * YOL SABİT YAZILIYOR, ÖNEK DEĞİŞKENİ YOK.
 *
 * Önce `const KOK = "odeme-planlari"` vardı ve her çağrı
 * `${KOK}/...` biçimindeydi. Uç bekçisi (tests/endpoint-guard)
 * çağrıyı backend rotalarına karşı çözemiyor: hesaplanmış önek
 * gördüğünde yolu doğrulayamıyor ve çağrı "doğrulanamayan"
 * sayısına giriyor. O sayı bir cırcır ve bu paket onu
 * yükseltmiyor. Değişken yalnız SEGMENT olarak kullanılıyor.
 */

/**
 * `apiClient` bir sınıf DEĞİL, tek bir fonksiyon: yol + seçenekler
 * alır. Yolun başına `/api/` konmaz — istemci onu kendisi ekliyor
 * (`/api/backend/...`). Bunu varsaymak yerine mevcut kullanımdan
 * ölçtüm; `cheques/${id}` biçimi zaten böyle.
 */
export const odemePlaniService = {
  listele: (companyId: string) =>
    apiClient<PlanOzeti[]>(`odeme-planlari?companyId=${companyId}`),

  detay: (id: string) => apiClient<PlanDetayi>(`odeme-planlari/${id}`),

  taslakOlustur: (companyId: string, hafta: string) =>
    apiClient<{ id: string }>(`odeme-planlari/taslak`, {
      method: "POST",
      body: { companyId, hafta },
    }),

  onayaSun: (id: string) =>
    apiClient<void>(`odeme-planlari/${id}/onaya-sun`, { method: "POST", body: {} }),

  satirEkle: (planId: string, istek: SatirIstegi) =>
    apiClient<{ id: string }>(`odeme-planlari/${planId}/satirlar`, {
      method: "POST",
      body: istek,
    }),

  satirGuncelle: (satirId: string, istek: SatirIstegi) =>
    apiClient<void>(`odeme-planlari/satirlar/${satirId}`, {
      method: "PUT",
      body: istek,
    }),

  satirSil: (satirId: string) =>
    apiClient<void>(`odeme-planlari/satirlar/${satirId}`, { method: "DELETE" }),

  /** K1 — satır satır karar. Yalnız GM. */
  satirKarar: (
    satirId: string,
    istek: {
      karar: number;
      onaylananTutar?: number | null;
      cekVadesi?: string | null;
      oncelik?: number | null;
    },
  ) =>
    apiClient<void>(`odeme-planlari/satirlar/${satirId}/karar`, {
      method: "POST",
      body: istek,
    }),

  satirOdeme: (satirId: string, odenenTutar: number) =>
    apiClient<void>(`odeme-planlari/satirlar/${satirId}/odeme`, {
      method: "POST",
      body: { odenenTutar },
    }),

  /** B1/B2 — tutar verilirse elle girilmiş, verilmezse hesaplanır. */
  bakiyeYaz: (
    planId: string,
    cashAccountId: string,
    elleGirilenTutar?: number | null,
  ) =>
    apiClient<{ tutar: number; kaynak: number }>(`odeme-planlari/${planId}/bakiye`, {
      method: "POST",
      body: { cashAccountId, elleGirilenTutar },
    }),

  butce: (id: string) => apiClient<ButceOzeti>(`odeme-planlari/${id}/butce`),

  kapat: (id: string) =>
    apiClient<void>(`odeme-planlari/${id}/kapat`, { method: "POST", body: {} }),
};
