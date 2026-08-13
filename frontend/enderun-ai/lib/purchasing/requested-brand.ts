/**
 * İSTENEN MARKA — üç durumun TEK yorum yeri.
 *
 * Marka talepte girilir, RFQ'ya ve siparişe taşınır; dolayısıyla en az
 * dört ekranda gösterilir. Her ekran "marka boş mu, muadil kabul mü"
 * sorusunu kendi başına yorumlasaydı ekranlar zamanla ayrışır, aynı
 * kalem bir yerde "zorunlu", başka yerde "farketmez" görünürdü.
 * Kural burada bir kez yazılır.
 *
 * ÜÇ DURUM (backend doğrulamasıyla birebir aynı):
 *   marka dolu + muadil false → ZORUNLU marka
 *   marka dolu + muadil true  → TERCİH, muadil kabul
 *   marka boş  + muadil true  → FARKETMEZ
 *   marka boş  + muadil false → GEÇERSİZ (form kabul etmez)
 */

export type RequestedBrandFields = {
  requestedBrand?: string | null;
  brandIrrelevant?: boolean | null;
};

export type RequestedBrandState =
  | "required"
  | "preferred"
  | "irrelevant";

export function requestedBrandState(
  item: RequestedBrandFields,
): RequestedBrandState {
  const brand = item.requestedBrand?.trim();

  if (!brand) {
    return "irrelevant";
  }

  return item.brandIrrelevant ? "preferred" : "required";
}

/**
 * Ekranda görünecek metin. "Tercih" ile "zorunlu" AYRI yazılır —
 * ikisi de marka içerir ama tedarikçinin hareket alanı farklıdır ve
 * satın almacı bunu tek bakışta ayırt edebilmelidir.
 */
export function requestedBrandLabel(item: RequestedBrandFields): string {
  const brand = item.requestedBrand?.trim();

  switch (requestedBrandState(item)) {
    case "required":
      return `${brand} (zorunlu)`;
    case "preferred":
      return `${brand} (tercih — muadil kabul)`;
    default:
      return "Marka farketmez";
  }
}

export function requestedBrandBadgeVariant(
  item: RequestedBrandFields,
): "warning" | "info" | "default" {
  switch (requestedBrandState(item)) {
    case "required":
      return "warning";
    case "preferred":
      return "info";
    default:
      return "default";
  }
}

/**
 * Formun kalem doğrulaması — backend'deki ValidateRequest kuralının
 * kullanıcıya erken gösterilen hâli. Backend kuralı KALDIRILMAZ:
 * burası yalnız erken uyarıdır, tek yetkili doğrulama sunucudadır.
 */
export function requestedBrandError(
  item: RequestedBrandFields,
): string | null {
  if (!item.requestedBrand?.trim() && !item.brandIrrelevant) {
    return "marka girilmeli ya da \"marka farketmez / muadil kabul\" işaretlenmelidir";
  }

  return null;
}

/**
 * Siparişte teklif edilen marka istenen markadan SAPTI mı.
 *
 * Muadil kabul edilen kalemde sapma zaten beklenen sonuçtur; orada
 * uyarı çıkarmak gürültü olur. Yalnız zorunlu markada anlamlıdır.
 */
export function brandMismatch(
  item: RequestedBrandFields & { brand?: string | null },
): boolean {
  if (requestedBrandState(item) !== "required") {
    return false;
  }

  const supplied = item.brand?.trim();

  if (!supplied) {
    return false;
  }

  // TÜRKÇE KÜÇÜLTME KULLANILMAZ. "SCHNEIDER" Türkçe kurallarla
  // "schneıder" olur (noktasız ı) ve "Schneider" ile eşleşmez —
  // marka adları latin ticari isimlerdir, Türkçe harf kuralı onları
  // bozar. Bu yüzden dilden bağımsız küçültme ile karşılaştırılır.
  return (
    supplied.toLowerCase() !==
    item.requestedBrand!.trim().toLowerCase()
  );
}
