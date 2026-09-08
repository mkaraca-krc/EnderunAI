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
export type MesajEkiOzeti = {
  id: string;
  mesajId: string;
  ad: string;
  contentType: string;
  boyutBayt: number;
};

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
      { method: "POST", body: { govde } }
    );
  },

  birebirAc(karsiUserId: string) {
    return apiClient<KonusmaOzeti>("mesajlar/konusmalar/birebir", {
      method: "POST",
      body: { karsiUserId },
    });
  },

  kisiAra(q: string) {
    return apiClient<KisiOzeti[]>(`mesajlar/kisiler${sorgu({ q })}`);
  },

  /**
   * Mesaja dosya ekler — İLERLEME İLE.
   *
   * ═══ NEDEN fetch DEĞİL XMLHttpRequest ═══
   *
   * `fetch` YÜKLEME ilerlemesini vermiyor (indirme için
   * `response.body` var, yükleme için karşılığı yok). C10 "yükleme
   * sırasında ilerleme görünür" diyor; 20 MB'lık bir dosyada
   * ilerlemesiz bekleme, donmuş ekrandan ayırt edilemez.
   *
   * Bu, `apiClient`ın yanında ikinci bir yol açmak demek ve bunu
   * bilerek yapıyorum — sebebi burada yazılı. `apiClient`ın FormData
   * desteği yine de eklendi: ilerleme GEREKMEYEN çağrılar oradan
   * geçsin, iki yol tek sebeple ayrışsın.
   */
  ekle(
    mesajId: string,
    dosyalar: File[],
    ilerleme?: (yuzde: number) => void
  ): Promise<MesajEkiOzeti[]> {
    const govde = new FormData();
    for (const d of dosyalar) govde.append("dosyalar", d, d.name);

    return new Promise<MesajEkiOzeti[]>((coz, red) => {
      const istek = new XMLHttpRequest();
      istek.open("POST", `/api/backend/mesajlar/mesajlar/${mesajId}/ekler`);

      istek.upload.onprogress = (olay) => {
        if (olay.lengthComputable && ilerleme) {
          ilerleme(Math.round((olay.loaded / olay.total) * 100));
        }
      };

      istek.onload = () => {
        if (istek.status >= 200 && istek.status < 300) {
          try {
            coz(JSON.parse(istek.responseText) as MesajEkiOzeti[]);
          } catch {
            red(new Error("Sunucu yanıtı okunamadı."));
          }
          return;
        }

        /*
         * SUNUCUNUN SEBEBİ YUTULMUYOR.
         *
         * Uç "dosyanın içeriği uzantısıyla uyuşmuyor" gibi anlamlı
         * cümleler dönüyor. Genel bir "yükleme başarısız" metni
         * kullanıcıyı aynı dosyayı tekrar denemeye iterdi.
         */
        let mesaj = "Dosya yüklenemedi.";
        try {
          const govde = JSON.parse(istek.responseText) as { message?: string };
          if (govde?.message) mesaj = govde.message;
        } catch {
          // Yanıt JSON değilse varsayılan metin kalır.
        }
        red(new Error(mesaj));
      };

      istek.onerror = () => red(new Error("Sunucuya ulaşılamadı."));
      istek.send(govde);
    });
  },

  /** Verilen mesajların eklerini getirir. */
  ekleriGetir(mesajIdleri: string[]) {
    if (mesajIdleri.length === 0) return Promise.resolve([] as MesajEkiOzeti[]);

    const q = mesajIdleri.map((x) => `mesajId=${encodeURIComponent(x)}`).join("&");
    return apiClient<MesajEkiOzeti[]>(`mesajlar/ekler?${q}`);
  },

  /** İndirme adresi — yetki kontrolü sunucuda, statik yol YOK. */
  ekIndirmeYolu(ekId: string) {
    return `/api/backend/mesajlar/ekler/${ekId}/indir`;
  },

  okundu(konusmaId: string) {
    return apiClient<{ message: string }>(
      `mesajlar/konusmalar/${konusmaId}/okundu`,
      { method: "POST" }
    );
  },
};
