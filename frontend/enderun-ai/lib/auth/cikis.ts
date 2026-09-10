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

/**
 * SW/1 — OTURUM KAPANINCA TARAYICI ÖNBELLEĞİ BOŞALTILIR.
 *
 * SW artık hiçbir şeyi önbelleğe almıyor ve etkinleşirken eskileri
 * siliyor; bu, KENDİ başına yeterli olmalı. Çıkıştaki temizlik ikinci
 * kat: SW güncellemesi henüz inmemiş bir cihazda (eski SW hâlâ etkin)
 * ortak tablette bir sonraki kullanıcıya veri kalmasın.
 *
 * Temizlik `finally` içinde: çıkış isteği ağ hatasıyla düşse de önbellek
 * boşaltılır. Temizliğin kendisi başarısız olursa ÇIKIŞ DURMAZ.
 */
export async function onbellekleriBosalt(): Promise<void> {
  if (typeof caches === "undefined") return;
  try {
    const adlar = await caches.keys();
    await Promise.all(adlar.map((ad) => caches.delete(ad)));
  } catch {
    // Çıkış, temizlik başarısız diye durmaz.
  }
}

export async function cikisIstegi(sebep: CikisSebebi): Promise<Response> {
  try {
    return await fetch("/api/auth/logout", {
      method: "POST",
      cache: "no-store",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason: sebep }),
    });
  } finally {
    await onbellekleriBosalt();
  }
}
