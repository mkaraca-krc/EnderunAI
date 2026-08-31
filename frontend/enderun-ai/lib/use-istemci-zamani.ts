import { useSyncExternalStore } from "react";

/*
 * İSTEMCİYE ÖZGÜ ZAMAN — TEK KAYNAK.
 *
 * NEDEN VAR: `new Date()` çizim sırasında sunucuda (statik önçizimde:
 * DERLEME ANI) ve istemcide (kullanıcının açtığı an) farklı sonuç
 * verir. Aradaki fark her yüklemede hidrasyon uyuşmazlığı üretir —
 * `data-table.tsx` bunu 11 gün boyunca 26 ekranda yaptı (Kural 71).
 *
 * NEDEN `useEffect` + `setState` DEĞİL: ilk düzeltmem o desendeydi ve
 * lint cırcırını 154'ten 159'a çıkardı. Cırcır haklıydı; desen bu iş
 * için yanlış araç. `useSyncExternalStore` tam olarak bunun için var:
 * sunucu anlık görüntüsü ile istemci anlık görüntüsü AYRI verilir,
 * React geçişi kendi yönetir ve uyuşmazlık doğmaz.
 *
 * NEDEN `lib/` ALTINDA: belirsizlik tek bir gözden geçirilmiş yerde
 * yaşasın. `tests/cizimde-belirsiz-deger.test.ts` `app/` ve
 * `components/` altını tarıyor; ekranlar `new Date()` yazmak yerine bu
 * kancaları çağırır ve muhafız ekranlarda kalan her kaçağı görür.
 */

/** Hiçbir zaman değişmeyen bir dış kaynak: abonelik boş. */
const aboneOl = () => () => {};

/**
 * Sunucuda `false`, istemcide bağlanma sonrası `true`.
 *
 * Hidrasyon geçişinde de `false` döner — bu yüzden sunucu HTML'i ile
 * ilk istemci çizimi AYNI olur ve uyuşmazlık imkânsızdır.
 */
export function useIstemcideMi(): boolean {
  return useSyncExternalStore(
    aboneOl,
    () => true,
    () => false,
  );
}

/** İstemcide "şu an", sunucuda `null`. */
export function useIstemciTarihi(): Date | null {
  return useIstemcideMi() ? new Date() : null;
}

/** İstemcide bugünün ISO tarihi (`YYYY-AA-GG`), sunucuda `null`. */
export function useIstemciGunu(): string | null {
  const tarih = useIstemciTarihi();
  return tarih ? tarih.toISOString().slice(0, 10) : null;
}

/** İstemcide içinde bulunulan yıl, sunucuda `null`. */
export function useIstemciYili(): number | null {
  const tarih = useIstemciTarihi();
  return tarih ? tarih.getFullYear() : null;
}
