"use client";

/**
 * MESAJ SESİ — GELEN MESAJDA, BİR KEZ, SESSİZ DÜŞEREK.
 *
 * ═══ NEDEN MODÜL DÜZEYİNDE ═══
 *
 * Ses tek bir `Audio` nesnesi üzerinden çalıyor. Bileşen durumunda
 * tutulsaydı her yeniden çizimde yeni nesne doğar, kilit açma
 * (unlock) kaybolur ve tarayıcı sesi engellerdi.
 *
 * ═══ OTOMATİK OYNATMA POLİTİKASI (B5) ═══
 *
 * Tarayıcılar, kullanıcı sayfayla ETKİLEŞMEDEN ses çalmayı engeller.
 * `play()` reddedilen bir söz (rejected promise) döndürür. Bu bir
 * ARIZA DEĞİL, tarayıcının normal davranışı:
 *
 *   · konsola hata basılmaz  (her mesajda gürültü olurdu)
 *   · kullanıcıya modal gösterilmez
 *   · istisna FIRLATILMAZ
 *
 * Sessizce geçiliyor ve MESAJIN KENDİSİ normal işliyor. Ses bir
 * hatırlatma; yokluğu mesajlaşmayı kullanılamaz yapmaz.
 *
 * İlk kullanıcı etkileşiminde `kilidiAc()` sesi SESSİZ olarak bir kez
 * çalıp durduruyor; tarayıcı o andan sonra izin veriyor.
 *
 * ═══ SUSTURMA ARALIĞI (B6) ═══
 *
 * Aynı anda beş mesaj gelirse beş kez çalmaz. Son çalma zamanı
 * modülde tutuluyor ve 3 saniyeden yakın istekler DÜŞÜRÜLÜYOR —
 * kuyruğa alınmıyor, çünkü gecikmiş bir bildirim sesi olayla
 * ilgisini kaybediyor.
 */

export const SES_YOLU = "/sesler/mesaj.wav";

/** Yerleşik ses seviyesi. B4: 0,4'ü geçmez. */
export const SES_SEVIYESI = 0.35;

/** Susturma aralığı (B6: en az 3 saniye). */
export const SUSTURMA_ARALIGI_MS = 3000;

declare global {
  interface Window {
    /** Testin ve tarayıcı denetiminin okuduğu sayaç. */
    __mesajSesiCalmaSayisi?: number;
    __mesajSesiSonSebep?: string;
  }
}

let ses: HTMLAudioElement | null = null;
let sonCalma = 0;
let kilitAcildi = false;

function sesiKur(): HTMLAudioElement | null {
  if (typeof Audio === "undefined") return null;

  if (!ses) {
    ses = new Audio(SES_YOLU);
    ses.volume = SES_SEVIYESI;
    // Ön yükleme: ilk mesajda indirme beklenmesin.
    ses.preload = "auto";
  }

  return ses;
}

function isaretle(sebep: string) {
  if (typeof window === "undefined") return;
  window.__mesajSesiSonSebep = sebep;
}

/**
 * İLK KULLANICI ETKİLEŞİMİNDE ÇAĞRILIR.
 *
 * Sesi SESSİZ çalıp hemen durduruyor — tarayıcı "kullanıcı etkileşimi
 * oldu" saysın diye. Başarısız olursa hiçbir şey yapmıyor; bir sonraki
 * gerçek çalma denemesi yine deneyecek.
 */
export function kilidiAc(): void {
  if (kilitAcildi) return;

  const parca = sesiKur();
  if (!parca) return;

  kilitAcildi = true;

  const eskiSes = parca.volume;
  parca.volume = 0;

  void parca
    .play()
    .then(() => {
      parca.pause();
      parca.currentTime = 0;
      parca.volume = eskiSes;
      isaretle("kilit-acildi");
    })
    .catch(() => {
      parca.volume = eskiSes;
      // SESSİZ: kilit açılamadıysa da akış devam eder.
      isaretle("kilit-acilamadi");
    });
}

/**
 * Sesi çalar. Engellenirse, susturulmuşsa ya da aralık dolmamışsa
 * SESSİZCE hiçbir şey yapmaz.
 */
export function sesCal(susturulmus: boolean): void {
  if (typeof window === "undefined") return;

  if (susturulmus) {
    isaretle("susturulmus");
    return;
  }

  const simdi = Date.now();
  if (simdi - sonCalma < SUSTURMA_ARALIGI_MS) {
    isaretle("aralik-dolmadi");
    return;
  }

  const parca = sesiKur();
  if (!parca) {
    isaretle("ses-yok");
    return;
  }

  sonCalma = simdi;

  try {
    parca.currentTime = 0;
    void parca.play().then(
      () => {
        window.__mesajSesiCalmaSayisi =
          (window.__mesajSesiCalmaSayisi ?? 0) + 1;
        isaretle("caldi");
      },
      () => {
        // ENGELLENDİ — konsola basılmıyor, fırlatılmıyor (B5).
        isaretle("engellendi");
      }
    );
  } catch {
    // `currentTime` bazı tarayıcılarda kaynak yüklenmeden fırlatır.
    isaretle("hata");
  }
}

/** Testlerin ve oturum kapanışının kullandığı sıfırlama. */
export function sesDurumunuSifirla(): void {
  sonCalma = 0;
  kilitAcildi = false;
}
