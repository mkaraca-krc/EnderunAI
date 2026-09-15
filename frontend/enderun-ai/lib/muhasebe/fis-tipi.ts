import type { AccountingVoucherType } from "@/services/accounting-voucher.service";

/**
 * MUHASEBE FİŞ TİPİ — TEK KAYNAK (2026-09-16).
 *
 * ═══ NEDEN VAR ═══
 *
 * Aynı eşleme ALTI yerde kopyalanmıştı ve üç ayrı biçimde:
 *
 *   Record<AccountingVoucherType, string> : fisler/page.tsx
 *                                           fisler/[id]/page.tsx
 *   Record<number, string> (TÜR DENETİMİ KAYIP)
 *                                         : yevmiye/page.tsx
 *                                           buyuk-defter/page.tsx
 *   elle <option> listesi (YAZMA YOLU)    : fisler/yeni/page.tsx
 *                                           fisler/[id]/duzenle/page.tsx
 *
 * Son ikisi en tehlikelisiydi: onlar OKUMA değil YAZMA yolundaydı —
 * kullanıcı fişi orada oluşturuyor. Yanlış etiketli bir fiş tipi,
 * yanlış etiketli bir malzemeden pahalıya gelir: fiş defterin kendisine
 * yazılır ve mali müşavire öyle gider.
 *
 * ═══ TÜR DENETİMİ BİLEREK DAR ═══
 *
 * `Record<AccountingVoucherType, string>` yazıldı, `Record<number, ...>`
 * DEĞİL. Arka uca yeni bir tip eklenip (`AccountingVoucherType` 0|1|2|3|4
 * genişletilince) buraya etiket eklenmezse DERLEME DÜŞER. `number` ile
 * yazılsaydı sessizce `undefined` basardı — ekranda boş bir hücre,
 * defterde adsız bir fiş.
 *
 * Arka uç kaynağı: `backend/EnderunAI.Api/Models/Accounting/AccountingVoucher.cs`
 *   Journal=0 · Collection=1 · Payment=2 * Opening=3 · Closing=4
 */
export const FIS_TIPI_ETIKETLERI: Record<AccountingVoucherType, string> = {
  0: "Mahsup",
  1: "Tahsil",
  2: "Tediye",
  3: "Açılış",
  4: "Kapanış",
};

/** Seçim kutuları için sıralı liste — sıra enum değeriyle aynı. */
export const FIS_TIPI_SECENEKLERI: ReadonlyArray<{
  deger: AccountingVoucherType;
  etiket: string;
}> = (Object.keys(FIS_TIPI_ETIKETLERI) as unknown[] as string[])
  .map((k) => Number(k) as AccountingVoucherType)
  .sort((a, b) => a - b)
  .map((deger) => ({ deger, etiket: FIS_TIPI_ETIKETLERI[deger] }));

/**
 * Bilinmeyen bir değer gelirse SESSİZ KALMAZ.
 *
 * Arka uç bir gün 5 gönderirse ekran boş hücre değil, görünür bir
 * işaret basmalı — "bilmiyorum" demek, yanlış bilmekten iyidir ama
 * sessiz kalmak ikisinden de kötüdür.
 */
export function fisTipiEtiketi(tip: number): string {
  const etiket = FIS_TIPI_ETIKETLERI[tip as AccountingVoucherType];
  return etiket ?? `bilinmeyen tip (${tip})`;
}
