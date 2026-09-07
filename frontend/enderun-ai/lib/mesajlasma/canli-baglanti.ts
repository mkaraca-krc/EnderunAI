"use client";

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type ILogger,
} from "@microsoft/signalr";

import type { MesajOzeti } from "@/services/messaging.service";

/**
 * CANLI MESAJ BAĞLANTISI — TEK BAĞLANTI, MODÜL DÜZEYİNDE.
 *
 * ═══ NEDEN BİLEŞENİN İÇİNDE DEĞİL ═══
 *
 * Bağlantı bir React durumu olsaydı her yeniden çizimde yeniden
 * kurulma riski taşırdı ve çıkış düğmesi ona ulaşamazdı. Çıkışın
 * bağlantıyı KAPATMASI şart: soket, jetonu bittiğinde sunucu
 * tarafından da düşürülüyor (`CloseOnAuthenticationExpiration`)
 * ama çıkış jetonu bitirmez — kullanıcı çıktıktan sonra açık kalan
 * soket, aynı makineyi kullanan bir sonraki kişiye mesaj düşürürdü.
 *
 * ═══ TAŞIMA ADI DIŞARIDAN OKUNABİLİR ═══
 *
 * `window.__mesajCanliTasima`. Sebep: WebSocket kurulamazsa SignalR
 * SESSİZCE LongPolling'e düşer ve ekran çalışıyor görünür — her
 * mesaj için saniyede bir HTTP isteği. "Çalışıyor" ile "doğru
 * çalışıyor" dışarıdan aynı görünmesin diye seçilen taşımanın adı
 * yazılıyor.
 *
 * Ad, kütüphanenin kendi günlük satırından okunuyor
 * (`Selecting transport 'X'.`) — sınıf adından DEĞİL: üretim yapısı
 * küçültülürken sınıf adları bozulur ve okunan değer anlamsızlaşırdı.
 */

declare global {
  interface Window {
    __mesajCanliTasima?: string;
    __mesajCanliDurum?: string;
  }
}

export const CANLI_YOL = "/api/hubs/mesaj";
export const OLAY_ADI = "MesajGeldi";

type Dinleyici = (mesaj: MesajOzeti) => void;

let baglanti: HubConnection | null = null;
let kurulum: Promise<HubConnection | null> | null = null;
const dinleyiciler = new Set<Dinleyici>();

function durumYaz(durum: string) {
  if (typeof window !== "undefined") window.__mesajCanliDurum = durum;
}

function tasimaYaz(ad: string) {
  if (typeof window !== "undefined") window.__mesajCanliTasima = ad;
}

/**
 * Kütüphanenin günlüğünü dinleyip seçilen taşımanın adını yakalar.
 *
 * `Selecting transport 'WebSockets'.` satırı Debug düzeyinde geliyor;
 * bu yüzden günlük düzeyi Debug'a çekildi. Satırlar konsola
 * BASILMIYOR — yalnız taşıma adı ayıklanıyor.
 */
function tasimaDinleyicisi(): ILogger {
  return {
    log(seviye: LogLevel, mesaj: string) {
      const eslesme = /Selecting transport '([^']+)'/.exec(mesaj);
      if (eslesme) tasimaYaz(eslesme[1]);

      if (seviye >= LogLevel.Error) durumYaz("hata");
    },
  };
}

export function canliBaglantiDurumu(): HubConnectionState | "yok" {
  return baglanti?.state ?? "yok";
}

/**
 * Bağlantıyı kurar (zaten varsa aynısını döndürür).
 *
 * ÇEREZLE KİMLİK: jeton URL'e konmuyor — sorgu dizesindeki jeton
 * erişim kaydına, tarayıcı geçmişine ve vekil sunucu kayıtlarına
 * düşerdi. Sunucu tarafında çerez okuma YALNIZ `/api/hubs` yolunda
 * açık (Program.cs, `OnMessageReceived`).
 */
export function canliBaglantiyiBaslat(): Promise<HubConnection | null> {
  if (typeof window === "undefined") return Promise.resolve(null);
  if (baglanti && baglanti.state === HubConnectionState.Connected)
    return Promise.resolve(baglanti);
  if (kurulum) return kurulum;

  const yeni = new HubConnectionBuilder()
    .withUrl(CANLI_YOL, { withCredentials: true })
    // GERİ DÖNÜŞ ARALIKLARI ARTAN: sunucu yeniden başlarken
    // (safe-deploy) her açık sekme aynı anda saniyede bir vurursa
    // ayağa kalkan servisi ikinci kez düşürürüz.
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(tasimaDinleyicisi())
    .build();

  yeni.on(OLAY_ADI, (mesaj: MesajOzeti) => {
    for (const dinleyici of dinleyiciler) {
      try {
        dinleyici(mesaj);
      } catch {
        // Bir dinleyicinin hatası diğerlerini düşürmez.
      }
    }
  });

  yeni.onreconnecting(() => durumYaz("yeniden-baglaniyor"));
  yeni.onreconnected(() => durumYaz("bagli"));
  yeni.onclose(() => durumYaz("kapali"));

  baglanti = yeni;
  durumYaz("baglaniyor");

  kurulum = yeni
    .start()
    .then(() => {
      durumYaz("bagli");
      return yeni;
    })
    .catch(() => {
      // SESSİZ DÜŞÜŞ DEĞİL: durum `window`'a yazılıyor ve ekran
      // REST yenilemesiyle çalışmaya devam ediyor. Canlı akış bir
      // HIZLANDIRMA; yokluğunda mesajlaşma kullanılamaz hale gelmez.
      durumYaz("kurulamadi");
      baglanti = null;
      return null;
    })
    .finally(() => {
      kurulum = null;
    });

  return kurulum;
}

/** Yeni mesaj dinleyicisi ekler; çözücü fonksiyon döndürür. */
export function canliMesajDinle(dinleyici: Dinleyici): () => void {
  dinleyiciler.add(dinleyici);
  return () => {
    dinleyiciler.delete(dinleyici);
  };
}

/**
 * ÇIKIŞTA ÇAĞRILIR. Bağlantıyı kapatır ve dinleyicileri boşaltır.
 *
 * `stop()` beklenmiyor: çıkış akışı `router.replace` ile ilerliyor
 * ve kapanışı beklemek çıkışı yavaşlatırdı. Kapanış başarısız olsa
 * bile sunucu tarafında jeton çerezi silindiği için yeniden
 * bağlanma denemesi kimlik doğrulayamaz.
 */
export function canliBaglantiyiKapat(): void {
  dinleyiciler.clear();

  const acik = baglanti;
  baglanti = null;
  kurulum = null;
  durumYaz("kapali");

  void acik?.stop().catch(() => undefined);
}
