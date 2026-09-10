/**
 * ÇIKIŞ — TEK ÇAĞRI YERİ, TEK SEBEP KÜMESİ (GÜNLÜK/1).
 *
 * ═══ NEDEN AYRI MODÜL ═══
 *
 * İki çağıran var: kullanıcının "Çıkış" düğmesi ve mesai izleyicisi.
 * İkisi de aynı ucu çağırıyor ve aynı sebep kümesini kullanmak
 * zorunda — sunucu tarafındaki küme ile ayrışırsa günlükte
 * "bilinmiyor" birikir ve teşhis değeri kaybolur.
 *
 * ═══ VE BİR KAPI BUNU ZORLADI ═══
 *
 * `tests/api-client-govde-sozlesmesi.test.ts`, ortak API istemcisini
 * KULLANAN bir dosyada `body: JSON.stringify(...)` görürse kırmızı
 * yanıyor: istemci gövdeyi zaten çeviriyor ve çift çevrim sunucuya
 * NESNE yerine METİN gönderip 400 üretiyor (2026-09-06'da canlıda
 * ölçüldü).
 *
 * `work-hour-session-watcher.tsx` hem o istemciyi kullanıyor hem de
 * doğrudan `fetch` çağırıyordu. Kod güvenliydi ama dosya iki kalıbı
 * karıştırıyordu; kapı bunu haklı olarak reddetti. Çizgiyi gevşetmek
 * yerine kalıp ayrıldı — ve ayrılınca tekrar da ortadan kalktı.
 *
 * ═══ BU DOSYA NEDEN İSTEMCİYİ ADIYLA ANMIYOR ═══
 *
 * ÖLÇÜLDÜ: kapının süzgeci `kod.includes(...)` ile DÜZ METİN arıyor ve
 * YORUMLARI DA SAYIYOR. İlk yazımda açıklama istemcinin adını anıyordu
 * ve bu dosya — onu hiç kullanmadığı hâlde — "kullanıcı" sayılıp
 * kırmızı yandı. Süzgecin bu özelliği kusur değil, ölçülmüş bir
 * sınırdır; kapıyı değiştirmek yerine metin ona göre yazıldı.
 */
export type CikisSebebi = "kullanici-dugmesi" | "mesai-izleyicisi";

export function cikisIstegi(sebep: CikisSebebi): Promise<Response> {
  return fetch("/api/auth/logout", {
    method: "POST",
    cache: "no-store",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason: sebep }),
  });
}
