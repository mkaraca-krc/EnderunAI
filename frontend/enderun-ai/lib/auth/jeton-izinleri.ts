/**
 * JETONDAKİ İZİNLERİN TEK YORUMLAYICISI — ÖN YÜZ (JETON/1 · Ş1).
 *
 * Arka uçtaki karşılığı `Security/JetonIzinKodlamasi.cs`. İki çalışma
 * ortamı olduğu için iki dosya var; ama HER TARAFTA TEK YER.
 *
 * ÜÇ KODLAMA:
 *   all_permissions              → kataloğun TAMAMI
 *   all_permissions + not_permissions → tamamı EKSİ listelenenler
 *   permissions                  → yalnız listelenenler
 *
 * BAŞKA HİÇBİR YER BU ALAN ADLARINI OKUMAZ. `middleware.ts` dahil
 * hepsi buradan geçer; `tests/jeton-kodlamasi-tek-yer.test.ts` bunu
 * tarıyor.
 *
 * ───────────────────────────────────────────────────────────────
 * EN TEHLİKELİ HATA: BAYRAĞI TEK BAŞINA OKUMAK
 * ───────────────────────────────────────────────────────────────
 *
 * ANLAMAYAN OKUYUCU KAPALI TARAFA DÜŞER. Üretici tümleyen
 * kodlamasında `all_permissions` GÖNDERMEZ; `not_permissions`ı
 * bilmeyen bir okuyucu ne bayrak ne liste görür, izin kümesi BOŞ
 * kalır ve kullanıcı ekrana giremez. Eksik yetki GÖRÜNÜR ve
 * düzeltilir; fazla yetki görünmez ve zararlıdır. Bu, yayın geri
 * alındığında eski ön yüzün 12 saat yaşayan yeni jetonlarla
 * karşılaşacağı pencerede önem taşıyor.
 *
 * `all_permissions` gördüğünde "her şeye izinli" demek, yanındaki
 * `not_permissions` listesini GÖRMEMEK demektir — kullanıcıya
 * olmayan bir yetki verilir. Eski kod tam olarak böyle yazılmıştı
 * (bayrak ikili bir dünyada doğruydu). Tümleyen eklendiği an o okuma
 * biçimi sessizce yanlışa döndü. Bu yüzden okuma tek kapıdan geçiyor.
 */

export type JetonErisimi = {
  /** Rol adları — yalnız görüntüleme için; yetki kararı ROL ADINDAN VERİLMEZ. */
  roller: Set<string>;
  /** Açıkça verilmiş izinler (yalnız `permissions` kodlamasında dolu). */
  izinler: Set<string>;
  /** Kataloğun tamamı verilmiş mi. */
  hepsi: boolean;
  /** `hepsi` iken hariç tutulanlar. */
  haric: Set<string>;
};

function degerler(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  return typeof value === "string" ? [value] : [];
}

/**
 * JWT gövdesini çözer. Bozuk jetonda BOŞ erişim döner — çözülemeyen
 * bir jetonla "her şeye izinli" saymak, imzasız bir metnin yetki
 * vermesi olurdu.
 */
export function jetonErisimi(token: string): JetonErisimi {
  try {
    const part = token.split(".")[1];
    const base64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    const payload = JSON.parse(atob(padded)) as Record<string, unknown>;

    const roller = [
      ...degerler(payload.roles),
      ...degerler(payload.role),
      ...degerler(
        payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"],
      ),
    ];

    const haric = new Set(degerler(payload.not_permissions));

    /*
     * TÜMLEYEN VARSA BAYRAK ARANMAZ.
     *
     * Üretici, tümleyen kodlamasında `all_permissions` GÖNDERMİYOR:
     * bu bilerek böyle, anlamayan bir okuyucu kapalı tarafa düşsün
     * diye. Burada yine de tümleyene ÖNCE bakılıyor — bir gün ikisi
     * birden gelirse bayrağı önce okuyan kod tümleyeni yok sayıp
     * fazla yetki verirdi.
     */
    const hepsi =
      haric.size > 0 ||
      payload.all_permissions === true ||
      payload.all_permissions === "true";

    return {
      roller: new Set(roller),
      izinler: new Set([
        ...degerler(payload.permissions),
        ...degerler(payload.permission),
      ]),
      hepsi,
      haric,
    };
  } catch {
    return {
      roller: new Set<string>(),
      izinler: new Set<string>(),
      hepsi: false,
      haric: new Set<string>(),
    };
  }
}

/**
 * TEK KARAR NOKTASI: bu izin var mı?
 *
 * Kodlamanın hangisi olduğu çağıranı ilgilendirmiyor — bilmesi
 * gerekseydi kodlama üç yere daha yayılırdı.
 */
export function izinVarMi(erisim: JetonErisimi, izin: string): boolean {
  if (erisim.hepsi) return !erisim.haric.has(izin);
  return erisim.izinler.has(izin);
}
