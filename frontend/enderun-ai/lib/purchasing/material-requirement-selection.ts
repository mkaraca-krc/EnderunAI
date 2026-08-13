import type { ProjectMaterialRequirementLine } from "@/services/project-material-requirement.service";

/**
 * Malzeme ihtiyacı listesinde hangi satır talep edilebilir ve seçilince
 * hangi miktarla gelir.
 *
 * Kural ekranın içine gömülmedi: hem satır onay kutusu, hem "eksikleri
 * seç" düğmesi, hem de gönderilecek miktar aynı kuralı kullanıyor.
 * Üç yerde ayrı yazılsaydı biri değişip diğerleri unutulur, kullanıcı
 * seçemediği bir satırı toplu seçimde seçilmiş görürdü.
 *
 * MİKTARLAR BURADA HESAPLANMAZ: eksik sunucudan gelir. Ekran yeniden
 * hesaplasaydı sunucuyla ayrışır ve kullanıcı talep ederken başka,
 * kayıtta başka miktar görürdü.
 */
export function isSelectable(line: ProjectMaterialRequirementLine) {
  return line.canRequest && line.shortageQuantity > 0;
}

/** Seçilebilir satırların tamamı, miktarları kalan eksik kadar. */
export function defaultSelection(
  lines: ProjectMaterialRequirementLine[],
): Record<string, string> {
  const selection: Record<string, string> = {};

  for (const line of lines) {
    if (isSelectable(line) && line.inventoryItemId) {
      selection[line.inventoryItemId] = String(line.shortageQuantity);
    }
  }

  return selection;
}

/**
 * Gönderilecek satırlar. Sayıya çevrilemeyen ya da sıfır/negatif giriş
 * SIFIR olarak gider; sunucu bunu "kalan eksiğin tamamı" diye yorumlar
 * ve miktarı yine kendi hesapladığı eksikle sınırlar.
 */
export function toRequestLines(
  selection: Record<string, string>,
): { inventoryItemId: string; quantity: number }[] {
  return Object.entries(selection).map(([inventoryItemId, quantity]) => {
    const parsed = Number(String(quantity).replace(",", "."));

    return {
      inventoryItemId,
      quantity: Number.isFinite(parsed) && parsed > 0 ? parsed : 0,
    };
  });
}
