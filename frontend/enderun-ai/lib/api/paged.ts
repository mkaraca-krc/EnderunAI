/**
 * KIRPILMIŞ LİSTE SONUCU — "kaç kayıt geldi" ile "kaç kayıt VAR"
 * birbirinden ayrılır.
 *
 * NEDEN VAR: uçların çoğu büyük tabloları sessiz bir tavanla
 * kırpıyordu ve yalnız diziyi döndürüyordu. Arayüz kırpıldığını
 * bilemediği için gelen kaydı TOPLAM sanıyordu — poz kütüphanesi
 * ekranı 23.531 kayıtlık kütüphane için "Toplam Poz: 100"
 * gösteriyordu.
 *
 * Tavan doğru; tavanın SÖYLENMEMESİ hataydı. Backend karşılığı
 * `Contracts/Core/PagedResult.cs`.
 */
export type Paged<T> = {
  /** Bu istekte dönen kayıtlar (en fazla `take` adet). */
  items: T[];
  /** Süzgeçlere uyan TOPLAM kayıt sayısı — tavandan önce sayılır. */
  total: number;
  /** Bu istekte uygulanan tavan. */
  take: number;
  /** Gösterilmeyen kayıt var mı — ekran bunu uyarıya çevirmeli. */
  hasMore: boolean;
};

/**
 * "1.580 kayıttan 50'si gösteriliyor" cümlesini tek yerden üretir.
 *
 * Her ekranın kendi cümlesini yazması, kırpılmayı söylemeyi UNUTMAYI
 * kolaylaştırıyordu; bu yardımcı onu tek satıra indiriyor.
 */
export function truncationNotice(
  paged: Pick<Paged<unknown>, "items" | "total" | "hasMore">,
  formatCount: (value: number) => string
): string | null {
  if (!paged.hasMore) return null;

  return `${formatCount(paged.total)} kayıttan ${paged.items.length} tanesi gösteriliyor.`;
}
