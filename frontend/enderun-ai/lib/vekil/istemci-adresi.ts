/**
 * VEKİL/1 — İSTEMCİ ADRESİNİ ARKA UCA TAŞI. TEK KAYNAK.
 *
 * ═══ NEDEN VAR (2026-09-15, ölçüldü) ═══
 *
 * Next.js vekil rotaları arka uca giden isteğe YENİ bir `Headers`
 * kuruyor ve yalnız birkaç başlığı kopyalıyordu. `X-Forwarded-For`
 * kopyalananlar arasında DEĞİLDİ. Sonuç: arka uç, vekilden geçen her
 * isteği `127.0.0.1` sanıyordu.
 *
 * İKİ SOMUT ZARAR ÖLÇÜLDÜ:
 *
 *   (a) Parola değiştirme kısıtı HERKES İÇİN TEK ANAHTARDA. Bir
 *       kullanıcının 5 başarısız denemesi, TÜM kullanıcıların parola
 *       değiştirmesini 15 dakika kilitliyordu. Canlıda sondayla
 *       tetiklenip gözlendi.
 *
 *   (b) Denetim kaydının IP sütunu YALAN SÖYLÜYORDU: `Created` 742 ve
 *       `Updated` 525 satırının hepsinde `127.0.0.1`. GÜNLÜK/1 ile
 *       yeni açtığımız kaydın bir alanı, doğduğu gün anlamsızdı.
 *
 * ═══ EN ÖNEMLİ KURAL: AYNEN İLET, EKLEME YAPMA ═══
 *
 * Arka uç (`AuthController.ResolveClientIp`) zincirin SON elemanını
 * okur. Bu güvenlidir çünkü nginx `$proxy_add_x_forwarded_for` ile
 * gerçek eşi zincirin SONUNA ekler; istemcinin uydurduğu her şey önde
 * kalır.
 *
 * BU YÜZDEN BURADA ZİNCİRE KENDİ ADRESİMİZİ EKLEMİYORUZ. Eklersek son
 * eleman `127.0.0.1` olur ve 2026-09-15 akşamı yayınladığımız hız
 * sınırı SESSİZCE ÇÖKER — herkes tek kovaya düşer, kimse ısırmaz.
 * Yani "daha doğru görünen" bir `${mevcut}, ${bizimAdres}` satırı,
 * düzeltmenin kendisini iptal eder.
 *
 * ZİNCİRİ AYNEN GEÇİR. Sondaki gerçek adres zaten nginx'in koyduğudur.
 */

/**
 * Gelen istekten istemci adresi zincirini okur.
 *
 * `x-real-ip`e düşülmesinin sebebi: nginx her vekil bloğunda ikisini de
 * kuruyor (ölçüldü: 8 bloğun 8'i). Zincir bir gün düşerse tek adres
 * hâlâ doğru cevabı verir — ve tek elemanlı zincirde "son eleman" o
 * adrestir.
 */
export function istemciAdresZinciri(
  basliklar: Headers
): string | null {
  const zincir = basliklar.get("x-forwarded-for");
  if (zincir && zincir.trim().length > 0) {
    return zincir;
  }

  const tekAdres = basliklar.get("x-real-ip");
  if (tekAdres && tekAdres.trim().length > 0) {
    return tekAdres;
  }

  return null;
}

/**
 * Arka uca gidecek başlıklara istemci adresini işler.
 *
 * Adres okunamıyorsa başlık HİÇ KONULMAZ. Boş bir `X-Forwarded-For`
 * göndermek, arka ucun `RemoteIpAddress`e düşmesini engelleyip elinde
 * boş dizgeyle kalmasına yol açardı — yokluk, boşluktan iyidir.
 */
export function istemciAdresiniIlet(
  gelen: Headers,
  giden: Headers
): void {
  const zincir = istemciAdresZinciri(gelen);
  if (zincir === null) {
    return;
  }

  giden.set("x-forwarded-for", zincir);
}
