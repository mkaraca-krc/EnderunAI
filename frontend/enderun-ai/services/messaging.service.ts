import { apiClient } from "@/lib/api/api-client";

/**
 * MESAJLAŞMA SERVİSİ — sunucu sözleşmesinin ön yüzdeki karşılığı.
 *
 * ALAN ADLARI TAHMİN EDİLMEDİ, OKUNDU. İlk taslakta `ogeler`,
 * `karsiTarafAdi`, `adSoyad`, `benimMi` gibi makul görünen adlar
 * yazılmıştı; sunucudaki kayıtlar okununca dördü de YANLIŞ çıktı.
 * Kaynak: `EnderunAI.Api/Services/Messaging/MesajlasmaService.cs`.
 *
 * KAPSAM KİLİDİ (TUR 2.4): dosya eki, okundu bilgisi ve grup yönetimi
 * BU PAKETTE YOK. Çalışan en küçük mesajlaşma: konuşma listesi, mesaj
 * görünümü, gönderme.
 *
 * CANLI AKIŞ (SignalR) DA YOK — bilerek. Sunucuda `MesajHub` hazır ama
 * ön yüzde `@microsoft/signalr` bağımlılığı yok; onu bu pakette
 * eklemek kapsam kilidini kırardı. Ekran açılışta, konuşma
 * seçildiğinde ve gönderimden sonra yeniliyor. Canlı akış ayrı ve
 * küçük bir iş.
 */

/** Sunucunun sayfalama zarfı. Düz dizi DEĞİL — `kayitlar` alanı var. */
export type SayfaSonucu<T> = {
  kayitlar: T[];
  sonrakiVar: boolean;
};

export type KonusmaOzeti = {
  id: string;
  companyId: string;
  baslik: string;
  karsiTarafUserId: string | null;
  sonMesajZamani: string | null;
  sonMesajOnizleme: string | null;
  okunmamisSayisi: number;
};

/**
 * DİKKAT: `benimMi` DİYE BİR ALAN YOK. Mesajın kime ait olduğu
 * `gonderenUserId` ile oturumdaki kullanıcının kimliği
 * karşılaştırılarak bulunur. Sunucunun söylemediği bir şeyi
 * söylüyormuş gibi tiplemek, ekranı sessizce yanlış hizalardı.
 */
export type MesajOzeti = {
  id: string;
  konusmaId: string;
  gonderenUserId: string;
  gonderenAd: string;
  govde: string;
  gonderimZamani: string;
  duzenlendi: boolean;
};

export type KisiOzeti = {
  userId: string;
  ad: string;
  unvan: string | null;
};

function sorgu(parametreler: Record<string, string | number | undefined>) {
  const p = new URLSearchParams();

  for (const [ad, deger] of Object.entries(parametreler)) {
    if (deger !== undefined && deger !== "") {
      p.set(ad, String(deger));
    }
  }

  const s = p.toString();
  return s ? `?${s}` : "";
}

export const messagingService = {
  konusmalar(limit = 30) {
    return apiClient<SayfaSonucu<KonusmaOzeti>>(
      `mesajlar/konusmalar${sorgu({ limit })}`
    );
  },

  mesajlar(konusmaId: string, limit = 50) {
    return apiClient<SayfaSonucu<MesajOzeti>>(
      `mesajlar/konusmalar/${konusmaId}/mesajlar${sorgu({ limit })}`
    );
  },

  gonder(konusmaId: string, govde: string) {
    return apiClient<MesajOzeti>(
      `mesajlar/konusmalar/${konusmaId}/mesajlar`,
      { method: "POST", body: JSON.stringify({ govde }) }
    );
  },

  birebirAc(karsiUserId: string) {
    return apiClient<KonusmaOzeti>("mesajlar/konusmalar/birebir", {
      method: "POST",
      body: JSON.stringify({ karsiUserId }),
    });
  },

  kisiAra(q: string) {
    return apiClient<KisiOzeti[]>(`mesajlar/kisiler${sorgu({ q })}`);
  },

  okundu(konusmaId: string) {
    return apiClient<{ message: string }>(
      `mesajlar/konusmalar/${konusmaId}/okundu`,
      { method: "POST" }
    );
  },
};
