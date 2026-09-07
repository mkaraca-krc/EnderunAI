"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter, usePathname } from "next/navigation";

import MesajPaneli from "./mesaj-paneli";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { apiClient } from "@/lib/api/api-client";
import { useCurrentUser } from "@/lib/use-current-user";

/**
 * MESAJ BALONCUĞU — PANELİN KÖK LAYOUT'TAKİ EVİ (M3/2c-1).
 *
 * ═══ NEDEN KABUKTA DEĞİL, KÖKTE ═══
 *
 * İlk sürümde `erp-shell` içindeydi ve *"panel rota değişiminde
 * sökülmez"* diye yazılmıştı. **YANLIŞTI.** Mehmet taslağın durmadığını
 * tarayıcıdan ölçtü; sebep şuydu:
 *
 * Ortak bir layout kabuğu YOK — `app/layout.tsx` yalnız `{children}`
 * render ediyor ve **her sayfa kendi ERP kabuğunu kuruyor** (173
 * dosya). Rota değişiminde `{children}` konumundaki bileşen TİPİ
 * değişiyor (`GorevlerPage` → `YapilacaklarPage`) ve React alt ağacın
 * tamamını söküyor — kabuk, baloncuk ve panel dahil.
 *
 * Panelin açık kalması yanıltmıştı: açık/kapalı durumu sunucuda saklı
 * ve yeniden okunuyordu. Panel hayatta kalmıyor, **yeniden doğuyordu**.
 *
 * Şimdi kök layout'ta, `{children}`'ın DIŞINDA. Orası rota değişiminde
 * yeniden kurulmuyor.
 *
 * ═══ KABUL ÖLÇÜTÜ BELİRTİ DEĞİL, MEKANİZMA ═══
 *
 * Taslağın durması bir BELİRTİ; asıl hedef panelin SÖKÜLMEMESİ. Taslak
 * yanlışlıkla da düzelebilir (ör. `localStorage`'a yazılsa panel yine
 * her geçişte sökülür ama taslak durur) — o zaman "düzeldi" der ve
 * M3/2c-2'nin canlı bağlantısı yine her ekran değişiminde koparadı.
 *
 * Bu yüzden `monteSayaci` var: bileşenin kaç kez monte olduğunu sayıyor
 * ve sonda ONU ölçüyor. Üç ekran değişiminden sonra 1 kalmalı.
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

/**
 * TERCİH YAZMA GECİKMESİ — HER İKİ ALAN İÇİN AYNI.
 *
 * ═══ ASİMETRİ BİR ARIZA ÜRETTİ, KAYDA GEÇİYOR ═══
 *
 * İlk sürümde panel durumu YALNIZ KAPANIŞTA yazılıyordu ve koda şu
 * gerekçe yazılmıştı: *"açılış, bir sonraki kapanışta zaten
 * kaydedilir."* **Bu cümle hiç kapatılmayan panel için yanlıştı.**
 *
 * Mehmet ölçtü: paneli açık bıraktı, hiç kapatmadı, sayfayı yeniledi
 * — panel kapalı geldi. Açık bir panel "açık" olarak hiç
 * kaydedilmemişti.
 *
 * İki alan artık AYNI kuralla yazılıyor: her değişiklikte, 1 saniye
 * gecikmeyle. Gecikme hızlı aç-kapa'da tek yazma bırakıyor; asimetri
 * ise hiçbir şey kazandırmadan bir durumu kaybediyordu.
 */
const TERCIH_YAZMA_GECIKMESI_MS = 1000;

/**
 * MONTE SAYACI — SONDANIN ÖLÇTÜĞÜ ŞEY.
 *
 * Modül düzeyinde tutuluyor ki bileşen sökülüp yeniden kurulsa bile
 * sayı sıfırlanmasın. `window` üzerinden okunabiliyor: tarayıcıdan
 * doğrulama yapan kişi konsola `__mesajPaneliMonteSayisi` yazıp
 * bakabilir.
 */
let monteSayaci = 0;

/**
 * PANELİN ASLA GÖRÜNMEYECEĞİ TEK İSTİSNA — VE GEREKÇESİ.
 *
 * Ana kural OTURUM kontrolüdür (rota listesi değil): oturum yoksa
 * panel yok. Liste tutmak, unutulacak bir şey daha demektir.
 *
 * `/portal` BU KURALIN TEK İSTİSNASI ve sebebi ayrı: portal sayfaları
 * DIŞARIYA — müşteriye, paydaşa — gösterilmek için var. İçeriden biri
 * o sayfayı açıp ekranını paylaştığında panelde İÇ YAZIŞMALAR
 * görünürdü. İhtimal küçük, bedeli veri sızıntısı.
 *
 * Bu yüzden burada oturum AÇIK olsa bile panel render edilmiyor.
 */
const PANEL_YASAK_ONEK = "/portal";

export default function MesajBaloncugu() {
  const router = useRouter();
  const pathname = usePathname() ?? "/";
  const { user, loading: oturumYukleniyor } = useCurrentUser();

  useEffect(() => {
    monteSayaci += 1;

    if (typeof window !== "undefined") {
      (window as unknown as Record<string, number>).__mesajPaneliMonteSayisi =
        monteSayaci;
    }
  }, []);

  const [acik, setAcik] = useState(false);
  const [sonKonusma, setSonKonusma] = useState<string | null>(null);
  /*
   * TASLAK KONUŞMA BAŞINA TUTULUYOR — TEK GENEL ALAN YANLIŞ OLURDU.
   *
   * Kullanıcı A konuşmasına yazıp B'ye geçip geri döndüğünde A'daki
   * yazdığı durmalı; B'de A'nın metni GÖRÜNMEMELİ. Tek bir genel
   * taslak alanı ikinci şartı bozardı.
   *
   * BURADA TUTULUYOR, PANELDE DEĞİL: panel kapandığında `MesajPaneli`
   * sökülür. Taslak baloncukta durduğu için kapatıp açmak da metni
   * kaybettirmiyor.
   */
  const [taslaklar, setTaslaklar] = useState<Record<string, string>>({});

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

  /** Açık konuşmada yazılmış metin var mı — kapatma uyarısı için. */
  const acikTaslakVar = Object.values(taslaklar).some(
    (x) => x.trim().length > 0
  );

  /*
   * TERCİH YÜKLENMEDEN YAZILMAZ.
   * Yüklenmeden yazılsaydı, varsayılan `false` kullanıcının kayıtlı
   * tercihini ezerdi — kenar çubuğunda aynı korumanın aynısı.
   */
  const tercihYuklendi = useRef(false);
  const yazmaZamani = useRef<ReturnType<typeof setTimeout> | null>(null);
  const bekleyen = useRef<{
    messagePanelOpen?: boolean;
    lastConversationId?: string;
  }>({});

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

  /**
   * Tercihi GECİKMELİ kaydeder. Gönderilmeyen alanlar sunucuda DEĞİŞMEZ.
   *
   * Bekleyen yazımlar birikiyor: art arda "panel açıldı" ve "konuşma
   * seçildi" gelirse tek istekte gidiyorlar.
   */
  const tercihYaz = useCallback(
    (govde: { messagePanelOpen?: boolean; lastConversationId?: string }) => {
      if (!tercihYuklendi.current) return;

      bekleyen.current = { ...bekleyen.current, ...govde };

      if (yazmaZamani.current) clearTimeout(yazmaZamani.current);

      yazmaZamani.current = setTimeout(() => {
        const gonderilecek = bekleyen.current;
        bekleyen.current = {};

        void apiClient("user-preferences", {
          method: "PUT",
          // Menü tercihini taşımıyoruz: uç `null` alanlara DOKUNMUYOR.
          body: { sidebarCollapsed: false, favoritePaths: null, ...gonderilecek },
        }).catch(() => {
          // Kaydedilemeyen bir panel tercihi için kullanıcıyı bölmeyiz.
        });
      }, TERCIH_YAZMA_GECIKMESI_MS);
    },
    []
  );

  /** Uyarısız kapatma — soruya "evet" dendikten sonra da buraya gelinir. */
  const gercektenKapat = useCallback(() => {
    setKapatmaSorusu(false);
    setAcik(false);
    // TASLAKLAR SİLİNMİYOR: panel kapanınca metin kaybolmuyor, yeniden
    // açınca yerinde duruyor. Kullanıcı "kapat" derken "yazdığımı sil"
    // demiyor.
    tercihYaz({ messagePanelOpen: false });
  }, [tercihYaz]);

  const kapat = useCallback(() => {
    if (acikTaslakVar) {
      setKapatmaSorusu(true);
      return;
    }

    gercektenKapat();
  }, [acikTaslakVar, gercektenKapat]);

  const ac = useCallback(() => {
    // MOBİLDE PANEL AÇILMAZ: dar ekranda tam sayfaya gidilir.
    if (window.innerWidth < DAR_EKRAN_ESIGI) {
      router.push("/mesajlar");
      return;
    }

    setAcik(true);
    tercihYaz({ messagePanelOpen: true });
  }, [router, tercihYaz]);

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

      tercihYaz({ lastConversationId: konusmaId });
    },
    [tercihYaz]
  );

  useEffect(
    () => () => {
      if (yazmaZamani.current) clearTimeout(yazmaZamani.current);
    },
    []
  );

  /*
   * ═══ RENDER KAPISI — ÜÇ AYRI SEBEP, ÜÇÜ DE SESSİZ ═══
   *
   * 1. OTURUM YÜKLENİYOR: hiçbir şey render edilmiyor. Aksi hâlde
   *    giriş ekranında bir an baloncuk görünür ve yanıp söner.
   * 2. OTURUM YOK: panel yok. Rota listesi DEĞİL, oturum kontrolü —
   *    yeni bir oturumsuz rota eklendiğinde kimsenin liste
   *    güncellemesi gerekmesin.
   * 3. /portal: oturum AÇIK olsa bile panel yok (yukarıdaki gerekçe).
   */
  if (oturumYukleniyor) return null;
  if (!user) return null;
  if (pathname.startsWith(PANEL_YASAK_ONEK)) return null;

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
            taslaklar={taslaklar}
            onTaslakDegisti={(konusmaId, metin) =>
              setTaslaklar((mevcut) => ({ ...mevcut, [konusmaId]: metin }))
            }
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
