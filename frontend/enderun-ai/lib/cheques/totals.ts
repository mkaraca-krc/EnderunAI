import { ChequeStatus, type ChequeListItem } from "@/services/cheque.service";

/**
 * Çek defteri ekranındaki toplamlar.
 *
 * TEK YERDE, ÇÜNKÜ AYRIŞMIŞLARDI: üst satırdaki "Listelenen toplam"
 * ile ay alt toplamları ayrı ayrı hesaplanıyordu ve iki kuralları
 * farklıydı — üst toplam iptal edilmiş çekleri sayıyor, ay toplamları
 * saymıyordu. Aynı ekranda birbirini tutmayan iki rakam vardı.
 *
 * İki kural burada bir kez yazılı:
 *   1. İPTAL EDİLEN ÇEK TOPLAMA GİRMEZ. Mali etkileri ters kayıtla
 *      geri alındı; ne giriş ne çıkış sayılır. Satır listede KALIR
 *      (denetim izi) ama tutara ve adede girmez.
 *   2. DEFTER DEĞERİ (amountTry) toplanır, ham tutar değil. Farklı
 *      para birimlerindeki ham tutarları toplamak (10.000 USD +
 *      5.000 TRY = 15.000) anlamsız bir sayı üretir.
 *
 * Özet kartları (portföyde / verilen açık) backend'den geliyor ve
 * aynı kuralı zaten uyguluyor (`Status != Voided`); bu modül onların
 * ekran tarafındaki karşılığı.
 */

const MONTH_NAMES = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

/** İptal edilen çek hiçbir toplama girmez. */
export function countsTowardTotals(item: ChequeListItem) {
  return item.status !== ChequeStatus.Voided;
}

/** Toplanabilir değer: keşide kurundaki TL karşılığı. */
export function bookValue(item: ChequeListItem) {
  return item.amountTry;
}

export interface ChequeMonthGroup {
  key: string;
  label: string;
  /** Yalnızca toplama giren çeklerin defter değeri. */
  total: number;
  /** Toplama giren çek sayısı — iptaller hariç. */
  count: number;
  /** İptaller DAHİL bütün satırlar; denetim izi ekranda kalır. */
  rows: ChequeListItem[];
}

export interface ChequeTotals {
  /** Üst satırdaki toplam. Ay toplamlarının toplamına EŞİTTİR. */
  listTotal: number;
  groups: ChequeMonthGroup[];
}

/**
 * Listeyi vade ayına göre gruplar ve toplamları çıkarır.
 *
 * Uç zaten vadeye göre sıralı döndürdüğü için ayrı bir özet ucuna
 * gerek yok. `listTotal` grupların toplamından türetiliyor — ayrı
 * bir `reduce` yazılsaydı ikisi yine ayrışabilirdi.
 */
/**
 * ÇEKİN AY ANAHTARI — gruplamanın TEK tanımı.
 *
 * Dışa açıldı çünkü ekran da aynı anahtara ihtiyaç duyuyor (tablo
 * bileşenine grup anahtarı olarak geçiyor). İkinci bir `slice(0, 7)`
 * yazılsaydı iki tanım zamanla ayrışır ve ekrandaki gruplama ile
 * toplamların dayandığı gruplama farklı olurdu.
 */
export function chequeMonthKey(item: Pick<ChequeListItem, "dueDate">): string {
  return item.dueDate.slice(0, 7);
}

export function summarizeCheques(items: ChequeListItem[]): ChequeTotals {
  const groups = new Map<string, ChequeMonthGroup>();

  for (const item of items) {
    const key = chequeMonthKey(item);

    let group = groups.get(key);

    if (!group) {
      const [year, month] = key.split("-");

      group = {
        key,
        label: `${MONTH_NAMES[Number(month) - 1]} ${year}`,
        total: 0,
        count: 0,
        rows: [],
      };

      groups.set(key, group);
    }

    group.rows.push(item);

    if (!countsTowardTotals(item)) continue;

    group.count += 1;
    group.total += bookValue(item);
  }

  const list = [...groups.values()];

  return {
    // Üst toplam grupların toplamı: iki sayının ayrışması artık
    // yapısal olarak imkânsız.
    listTotal: list.reduce((sum, group) => sum + group.total, 0),
    groups: list,
  };
}
