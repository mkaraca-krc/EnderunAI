/**
 * SENTETİK DENETİM SATIRI — GİZLEME SÜZGECİ İÇİN TEK KAYNAK.
 *
 * ═══ NEDEN VAR (2026-09-16) ═══
 *
 * Her yayının ısıtma adımı 3-4 sahte başarısız giriş yazıyor
 * (`isitma-yok-*`), ve ölçüm sondaları da satır bırakıyor (`sonda-*`).
 * Yılda yüzlerce sentetik satır demek; gerçek olaylar aralarında
 * kaybolur.
 *
 * ═══ KAYIT DEĞİL EKRAN DEĞİŞTİ ═══
 *
 * Mehmet Bey'in kararı: gürültü **kaydı değiştirerek değil, ekranı
 * değiştirerek** çözülür. Denetim YAZICISINA hiçbir istisna
 * eklenmedi ve eklenmeyecek — sebebi Kural 96'da yazılı: *istisnanın
 * anahtarı saldırganın eline geçer.* `isitma-yok-` önekini yazıcıda
 * atlasaydık, o önekle deneyen herkesin başarısız girişleri kayda hiç
 * düşmezdi.
 *
 * Burada yapılan yalnız GÖRÜNTÜLEME: satırlar duruyor, kullanıcı
 * isterse gizliyor, gizlenen sayı ekranda yazıyor.
 *
 * ═══ VARSAYILAN: HER ŞEY GÖRÜNÜR ═══
 *
 * Süzgeç kapalı doğar. Bir denetim ekranının varsayılanı "eksiksiz"
 * olmalıdır; gizlemeyi kullanıcı bilerek seçer.
 */

/** Sentetik satırların kullanıcı adı önekleri. */
export const SENTETIK_ONEKLER = ["isitma-yok-", "sonda-"] as const;

/**
 * Bu satır bir ısıtma ya da ölçüm sondası tarafından mı üretildi?
 *
 * YALNIZ ÖNEKE BAKAR ve bu bilinçli: gerçek bir kullanıcı adının
 * `sonda-` ile başlaması beklenmez, ama başlarsa satır GİZLENİR —
 * SİLİNMEZ. Gizleme geri alınabilir, silme değil.
 */
export function sentetikSatir(actorUsername: string | null | undefined): boolean {
  if (!actorUsername) return false;
  const ad = actorUsername.toLowerCase();
  return SENTETIK_ONEKLER.some((onek) => ad.startsWith(onek));
}

/** Gizlenecek satır sayısı — ekranda yazılır, sessizce düşürülmez. */
export function sentetikSayisi(
  satirlar: ReadonlyArray<{ actorUsername: string | null }>
): number {
  return satirlar.filter((s) => sentetikSatir(s.actorUsername)).length;
}
