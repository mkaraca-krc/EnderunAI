"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";

import MesajPaneli from "./mesaj-paneli";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { apiClient } from "@/lib/api/api-client";

/**
 * MESAJ BALONCUĞU — PANELİN KABUKTAKİ EVİ (M3/2c-1).
 *
 * ═══ NEDEN KABUKTA, SAYFA BİLEŞENİNDE DEĞİL ═══
 *
 * Bu bileşen `erp-shell` içinde, `{children}`'ın DIŞINDA duruyor.
 * Sayfa bileşenine konsaydı her rota değişiminde SÖKÜLÜP yeniden
 * kurulurdu: açık konuşma kapanır, yazılmış taslak gider, ileride
 * eklenecek SignalR bağlantısı kopardı.
 *
 * `HizirBubble` aynı yerde ve aynı sebeple duruyor.
 *
 * Bunu `mesaj-paneli-kabukta.test.tsx` tutuyor: sahte bir kabukta
 * `children` değiştirilip panelin ayakta ve taslağın yerinde kaldığı
 * sınanıyor. Panel `children` içine taşınırsa o test kırmızı yanar.
 *
 * ═══ MOBİLDE PANEL YOK ═══
 *
 * 900px altında yan panel kullanılamaz; düğme `/mesajlar` tam sayfaya
 * götürür. Eşik ÖLÇÜLEREK seçildi: `.mesaj-duzen` zaten 900px'te tek
 * sütuna geçiyor. Yeni bir eşik, aynı ekranın iki farklı noktada
 * kırılması demekti.
 */

/** Panelin kullanılamayacağı genişlik. `globals.css` ile aynı eşik. */
const DAR_EKRAN_ESIGI = 900;

/** Son konuşma yazımı bu kadar beklenir; hızlı geçişlerde son seçim yazılır. */
const KONUSMA_YAZMA_GECIKMESI_MS = 1000;

export default function MesajBaloncugu() {
  const router = useRouter();

  const [acik, setAcik] = useState(false);
  const [sonKonusma, setSonKonusma] = useState<string | null>(null);
  const [taslakVar, setTaslakVar] = useState(false);

  /*
   * KAPATMA UYARISI — TARAYICI DİYALOĞU DEĞİL.
   *
   * İlk yazımda `window.confirm` kullandım ve `native-dialogs.test.ts`
   * yayını durdurdu. Kapı haklıydı: tarayıcı diyaloğu
   * biçimlendirilemiyor, hatayı içinde gösteremiyor ve projenin geri
   * kalanıyla aynı görünmüyor. Projenin kendi `ConfirmDialog`'u var.
   *
   * GEREKÇE ALANI YOK: burada geri alınamaz bir iş yapılmıyor,
   * yalnız yazılmamış bir taslak atılıyor. Gerekçe istemek, çay
   * siparişine izin anahtarı koymak gibi olurdu.
   */
  const [kapatmaSorusu, setKapatmaSorusu] = useState(false);

  /*
   * TERCİH YÜKLENMEDEN YAZILMAZ.
   * Yüklenmeden yazılsaydı, varsayılan `false` kullanıcının kayıtlı
   * tercihini ezerdi — kenar çubuğunda aynı korumanın aynısı.
   */
  const tercihYuklendi = useRef(false);
  const yazmaZamani = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    let etkin = true;

    void apiClient<{
      messagePanelOpen: boolean;
      lastConversationId: string | null;
    }>("user-preferences")
      .then((tercih) => {
        if (!etkin) return;
        setAcik(tercih.messagePanelOpen ?? false);
        setSonKonusma(tercih.lastConversationId ?? null);
        tercihYuklendi.current = true;
      })
      .catch(() => {
        // Tercih okunamazsa panel kapalı başlar ve YAZMA KAPALI kalır:
        // varsayılan, kullanıcının kaydını ezmesin.
        if (etkin) tercihYuklendi.current = false;
      });

    return () => {
      etkin = false;
    };
  }, []);

  /** Tercihi kaydeder. Gönderilmeyen alanlar sunucuda DEĞİŞMEZ. */
  const tercihYaz = useCallback(
    (govde: { messagePanelOpen?: boolean; lastConversationId?: string }) => {
      if (!tercihYuklendi.current) return;

      void apiClient("user-preferences", {
        method: "PUT",
        // Menü tercihini taşımıyoruz: uç `null` alanlara DOKUNMUYOR.
        body: { sidebarCollapsed: false, favoritePaths: null, ...govde },
      }).catch(() => {
        // Kaydedilemeyen bir panel tercihi için kullanıcıyı bölmeyiz.
      });
    },
    []
  );

  /*
   * PANEL DURUMU YALNIZ KAPANIŞTA YAZILIR.
   * Her aç-kapa'da yazsaydık "açtım hemen kapattım" iki yazma
   * üretirdi. Açılış, bir sonraki kapanışta zaten kaydedilir.
   */
  /** Uyarısız kapatma — soruya "evet" dendikten sonra da buraya gelinir. */
  const gercektenKapat = useCallback(() => {
    setKapatmaSorusu(false);
    setAcik(false);
    setTaslakVar(false);
    tercihYaz({ messagePanelOpen: false });
  }, [tercihYaz]);

  const kapat = useCallback(() => {
    if (taslakVar) {
      setKapatmaSorusu(true);
      return;
    }

    gercektenKapat();
  }, [taslakVar, gercektenKapat]);

  const ac = useCallback(() => {
    // MOBİLDE PANEL AÇILMAZ: dar ekranda tam sayfaya gidilir.
    if (window.innerWidth < DAR_EKRAN_ESIGI) {
      router.push("/mesajlar");
      return;
    }

    setAcik(true);
  }, [router]);

  /* ESC İLE KAPANIR — taslak uyarısı `kapat` içinde. */
  useEffect(() => {
    if (!acik) return;

    function tusa(olay: KeyboardEvent) {
      if (olay.key === "Escape") kapat();
    }

    window.addEventListener("keydown", tusa);
    return () => window.removeEventListener("keydown", tusa);
  }, [acik, kapat]);

  /* SON KONUŞMA GECİKMELİ YAZILIR. */
  const konusmaDegisti = useCallback(
    (konusmaId: string | null) => {
      setSonKonusma(konusmaId);

      if (!konusmaId) return;

      if (yazmaZamani.current) clearTimeout(yazmaZamani.current);

      yazmaZamani.current = setTimeout(() => {
        tercihYaz({ lastConversationId: konusmaId });
      }, KONUSMA_YAZMA_GECIKMESI_MS);
    },
    [tercihYaz]
  );

  useEffect(
    () => () => {
      if (yazmaZamani.current) clearTimeout(yazmaZamani.current);
    },
    []
  );

  return (
    <>
      {/*
        MODAL DEĞİL: arka planda örtü yok, altındaki ekran tıklanabilir
        kalıyor. Panelin varlık sebebi zaten "yazarken çalışabilmek".
      */}
      {acik && (
        <div className="mesaj-panel" role="complementary" aria-label="Mesajlar">
          <MesajPaneli
            kip="panel"
            onKapat={kapat}
            onKonusmaDegisti={konusmaDegisti}
            baslangicKonusmaId={sonKonusma}
            onTaslakDegisti={setTaslakVar}
          />
        </div>
      )}

      <ConfirmDialog
        open={kapatmaSorusu}
        title="Yazdığınız mesaj gönderilmedi"
        description="Paneli kapatırsanız yazdığınız metin kaybolur."
        confirmLabel="Yine de kapat"
        onCancel={() => setKapatmaSorusu(false)}
        onConfirm={gercektenKapat}
      />

      <button
        type="button"
        onClick={() => (acik ? kapat() : ac())}
        aria-label={acik ? "Mesajları kapat" : "Mesajlar"}
        className="mesaj-baloncuk"
      >
        {acik ? "✕" : "✉"}
      </button>
    </>
  );
}
