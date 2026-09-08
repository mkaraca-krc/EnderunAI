"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter, usePathname } from "next/navigation";

import MesajPaneli from "./mesaj-paneli";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { apiClient } from "@/lib/api/api-client";
import {
  canliBaglantiyiBaslat,
  canliMesajDinle,
} from "@/lib/mesajlasma/canli-baglanti";
import { konusmayaBakiliyor } from "@/lib/mesajlasma/etkin-konusma";
import { kilidiAc, sesCal } from "@/lib/mesajlasma/mesaj-sesi";
import { useCurrentUser } from "@/lib/use-current-user";
import { messagingService } from "@/services/messaging.service";
import { PANEL_DAR_EKRAN_ESIGI } from "@/lib/mesajlasma/panel-esigi";
import { useTaslakDeposu } from "@/lib/mesajlasma/taslak-deposu";

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
   * TASLAK ARTIK BURADA DEĞİL, PAYLAŞILAN DEPODA.
   *
   * Panelde tutulduğunda `/mesajlar` tam sayfası onu görmüyordu ve
   * dar pencerede giden kullanıcı yazdığını kaybediyordu. Depo kök
   * layout'ta; baloncuk yalnız OKUYOR (kapatma uyarısı için).
   */
  const taslakDeposu = useTaslakDeposu();

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
   * ═══ OKUNMAMIŞ SAYISI SUNUCUDAN OKUNUR, SAYILMAZ ═══
   *
   * Gelen yayınları yerel bir sayaçla toplamak kolaydı ve YANLIŞ
   * olurdu: başka bir sekmede okunan mesaj bu sekmenin sayacından
   * düşmez, kaçırılan yayın hiç eklenmez, yeniden bağlanma
   * aralığında gelenler kaybolur. Sayı her seferinde konuşma
   * listesinden toplanıyor — sunucu ne diyorsa o.
   */
  const [okunmamis, setOkunmamis] = useState(0);

  /*
   * SES SUSTURULMUŞ MU (B3).
   *
   * Alan "susturuldu" saklıyor, "açık" değil: kaydı olmayan
   * kullanıcıda `false` = susturulmamış = ses AÇIK. Varsayılan
   * AÇIK istendiği için tersini saklamak, kayıt yokluğunu sessizliğe
   * çevirirdi.
   */
  const [sesSusturuldu, setSesSusturuldu] = useState(false);

  const okunmamisTazele = useCallback(() => {
    void messagingService
      .konusmalar()
      .then((sayfa) =>
        setOkunmamis(
          (sayfa.kayitlar ?? []).reduce(
            (toplam, konusma) => toplam + (konusma.okunmamisSayisi ?? 0),
            0
          )
        )
      )
      .catch(() => {
        // Rozet için kullanıcıyı bölmeyiz; bir sonraki olayda tazelenir.
      });
  }, []);

  /** Açık konuşmada yazılmış metin var mı — kapatma uyarısı için. */
  const acikTaslakVar = Object.values(taslakDeposu?.taslaklar ?? {}).some(
    (x) => x.trim().length > 0
  );

  /*
   * TERCİH YÜKLENMEDEN YAZILMAZ.
   * Yüklenmeden yazılsaydı, varsayılan `false` kullanıcının kayıtlı
   * tercihini ezerdi — kenar çubuğunda aynı korumanın aynısı.
   */
  const tercihYuklendi = useRef(false);

  /*
   * ═══ KULLANICI DOKUNDUYSA TERCİH YANITI ONU EZMEZ ═══
   *
   * ÖLÇÜLEN YARIŞ (rig, 2026-09-08): kullanıcı tercih yanıtı gelmeden
   * baloncuğa tıklıyor → `setAcik(true)` çalışıyor, panel açılıyor.
   * Ama `tercihYaz` o anda `tercihYuklendi.current` false olduğu için
   * YAZMIYOR. Saniyeler sonra tercih GET'i dönüyor ve
   * `setAcik(tercih.messagePanelOpen ?? false)` ile paneli KAPATIYOR.
   *
   * Rig ölçümü: panel açıldı, 3 saniye açık kaldı, 20 saniyelik
   * pencerede kendiliğinden kapandı. DOM'da `.mesaj-panel` yok,
   * baloncuk ✉ (kapalı).
   *
   * PENCEREYİ BEN GENİŞLETTİM: GİRİŞ-DÖNGÜ/1'de tercih efektini
   * `[]`den `[user, oturumYukleniyor]`e çevirdim; tercihler artık
   * oturum çözülene kadar bekliyor. Önce mount anında geliyorlardı.
   *
   * KURAL: sunucudan gelen tercih, KULLANICININ O ARADA VERDİĞİ
   * KARARI ezemez. Kullanıcı dokunduysa tercih yalnız yazılır,
   * okunmaz.
   */
  const kullaniciDokundu = useRef(false);
  const yazmaZamani = useRef<ReturnType<typeof setTimeout> | null>(null);
  const bekleyen = useRef<{
    messagePanelOpen?: boolean;
    lastConversationId?: string;
    messageSoundMuted?: boolean;
  }>({});

  useEffect(() => {
    let etkin = true;

    /*
     * OTURUM YOKKEN İSTEK ATILMIYOR (GİRİŞ-DÖNGÜ/1).
     *
     * Aşağıdaki `return null` üçlüsü (oturum yükleniyor / kullanıcı yok
     * / portal) yalnız ARAYÜZÜ gizliyordu; `useEffect` render'ın
     * dönüşünden bağımsız koşar. Yani baloncuk giriş ekranında
     * görünmüyordu ama `user-preferences` isteğini yine de atıyordu
     * ve 401 alıyordu.
     *
     * ASIL DÖNGÜ KIRICI `api-client` içinde (401'de giriş ekranındayken
     * yönlendirme yok). Buradaki koruma onun yerine geçmiyor,
     * gereksiz isteği en baştan engelliyor: giriş ekranında dakikada
     * yüzlerce 401 üretmenin hiçbir faydası yok.
     *
     * ═══ AMA ÖLÇÜM BUNUN DAHA DEĞERLİ OLDUĞUNU GÖSTERDİ ═══
     *
     * 2026-09-08, canlı: yayın sırasında arka uç 503 verirken giriş
     * ekranı 10 saniye açık tutuldu.
     *   sayfanın kendi attığı istek : 0
     *   belge yüklemesi             : 1
     * Bir gün önce aynı koşulda 10 saniyede 862 istek vardı.
     *
     * Sayfa 503'ü ZARİFÇE KARŞILAMADI — 503'e yol açan çağrıyı HİÇ
     * YAPMADI. Fark önemli: iyi karşılama kodu bir gün bozulabilir,
     * hiç yapılmayan çağrı bozulamaz. YAPILMAYAN ÇAĞRININ HATASINDA
     * DÖNGÜ OLAMAZ.
     *
     * Yani bu koruma "gereksiz istek engelleme"den ibaret değil;
     * arka uç TÜMÜYLE düştüğünde de giriş ekranını ayakta tutan şey.
     * `api-client` yalnız 401'de yönlendirmiyor; 5xx bir başka yola
     * girseydi burası son savunma olurdu.
     */
    if (oturumYukleniyor || !user) return;

    void apiClient<{
      messagePanelOpen: boolean;
      lastConversationId: string | null;
      messageSoundMuted: boolean;
    }>("user-preferences")
      .then((tercih) => {
        if (!etkin) return;

        // YAZMA KAPISI HER HÂLDE AÇILIYOR: tercih artık okundu, bundan
        // sonraki değişiklikler sunucuya gidebilir.
        tercihYuklendi.current = true;

        // Kullanıcı bu arada dokunduysa DURUM EZİLMEZ (yarış).
        if (kullaniciDokundu.current) return;

        setAcik(tercih.messagePanelOpen ?? false);
        setSonKonusma(tercih.lastConversationId ?? null);
        setSesSusturuldu(tercih.messageSoundMuted ?? false);
      })
      .catch(() => {
        // Tercih okunamazsa panel kapalı başlar ve YAZMA KAPALI kalır:
        // varsayılan, kullanıcının kaydını ezmesin.
        if (etkin) tercihYuklendi.current = false;
      });

    return () => {
      etkin = false;
    };
    /*
     * BAĞIMLILIK: `user` ve `oturumYukleniyor`.
     *
     * Dizi `[]` kalsaydı, yukarıdaki oturum koruması bir REGRESYON
     * üretirdi: oturum asenkron çözülüyor, ilk render'da `user` null
     * oluyor. Effect bir kez koşup çıkacak ve kullanıcı giriş yaptıktan
     * sonra tercihleri BİR DAHA hiç okunmayacaktı — panel her açılışta
     * kapalı ve ses ayarı varsayılan gelirdi.
     *
     * Yani koruma eklerken bağımlılığı da eklemek zorunlu; biri
     * ötekisiz yanlış.
     */
  }, [user, oturumYukleniyor]);

  /**
   * Tercihi GECİKMELİ kaydeder. Gönderilmeyen alanlar sunucuda DEĞİŞMEZ.
   *
   * Bekleyen yazımlar birikiyor: art arda "panel açıldı" ve "konuşma
   * seçildi" gelirse tek istekte gidiyorlar.
   */
  const tercihYaz = useCallback(
    (govde: {
      messagePanelOpen?: boolean;
      lastConversationId?: string;
      messageSoundMuted?: boolean;
    }) => {
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

  /** Ses tercihini değiştirir ve kaydeder. */
  const sesiDegistir = useCallback(() => {
    setSesSusturuldu((onceki) => {
      const yeni = !onceki;
      tercihYaz({ messageSoundMuted: yeni });
      // AÇARKEN KİLİDİ DE AÇ: bu bir kullanıcı etkileşimi, tarayıcı
      // tam bu anda izin veriyor. Sonraki mesajda denemek geç olurdu.
      if (!yeni) kilidiAc();
      return yeni;
    });
  }, [tercihYaz]);

  /** Uyarısız kapatma — soruya "evet" dendikten sonra da buraya gelinir. */
  const gercektenKapat = useCallback(() => {
    kullaniciDokundu.current = true;
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
    kullaniciDokundu.current = true;

    // MOBİLDE PANEL AÇILMAZ: dar ekranda tam sayfaya gidilir.
    if (window.innerWidth < PANEL_DAR_EKRAN_ESIGI) {
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
   * ═══ CANLI BAĞLANTI BALONCUKTA KURULUYOR, PANELDE DEĞİL ═══
   *
   * Panel yalnız AÇIKKEN monte; bağlantı orada kurulsaydı panel
   * kapalıyken hiç mesaj gelmez ve rozet hiç artmazdı — yani rozetin
   * tek işe yaradığı durumda çalışmazdı. Baloncuk kök layout'ta ve
   * oturum boyunca monte kalıyor.
   *
   * `canliBaglantiyiBaslat` ÇAĞRILDIĞINDA bağlantı zaten varsa
   * aynısını döndürüyor: panel de çağırıyor, ikinci soket açılmıyor.
   */
  /*
   * SES KARARI TEK YERDE — GELEN MESAJDA, KENDİ GÖNDERİMİNDE ASLA.
   *
   * Üç şart, üçü de ölçülebilir:
   *   1. Mesaj BAŞKASINDAN geliyor. Sunucu yayını GÖNDERENE DE
   *      yolluyor (başka sekmesi açık olabilir); kendi yazdığında
   *      ses duymak rahatsız edici ve yanıltıcı olurdu (B1).
   *   2. Kullanıcı O KONUŞMAYA BAKMIYOR. "Bakıyor" = konuşma açık
   *      VE sekme görünür. Başka sekmedeyken ses ÇALAR (B2).
   *   3. Ses susturulmamış (B3).
   *
   * Karar burada, çünkü baloncuk oturum boyunca monte kalan tek
   * yer. Panelde olsaydı panel kapalıyken hiç ses çalmazdı — yani
   * sesin en gerekli olduğu durumda çalışmazdı.
   */
  const sesSusturulduRef = useRef(sesSusturuldu);

  useEffect(() => {
    sesSusturulduRef.current = sesSusturuldu;
  }, [sesSusturuldu]);

  useEffect(() => {
    if (!user) return;

    void canliBaglantiyiBaslat();
    okunmamisTazele();

    return canliMesajDinle((gelen) => {
      okunmamisTazele();

      if (gelen.gonderenUserId === user.id) return;
      if (konusmayaBakiliyor(gelen.konusmaId)) return;

      sesCal(sesSusturulduRef.current);
    });
  }, [user, okunmamisTazele]);

  /*
   * OTOMATİK OYNATMA KİLİDİ İLK ETKİLEŞİMDE AÇILIR (B5).
   *
   * Tarayıcı, kullanıcı sayfayla etkileşmeden ses çalmayı engelliyor.
   * Dinleyiciler `once: true` ile bir kez koşuyor ve kendilerini
   * söküyor — kalıcı bir dinleyici her tıklamada iş yapardı.
   */
  useEffect(() => {
    if (!user) return;

    const ac = () => kilidiAc();
    const olaylar: (keyof WindowEventMap)[] = ["pointerdown", "keydown"];

    for (const olay of olaylar)
      window.addEventListener(olay, ac, { once: true, passive: true });

    return () => {
      for (const olay of olaylar) window.removeEventListener(olay, ac);
    };
  }, [user]);

  /*
   * SEKME BAŞLIĞI — ARKA PLANDAKİ SEKMEDE TEK GÖRÜNÜR İŞARET.
   *
   * Özgün başlık bir ref'te saklanıyor: `document.title`'ı her
   * seferinde okuyup önek eklemek "(2) (1) Enderun AI" üretirdi.
   */
  const ozgunBaslik = useRef<string | null>(null);

  useEffect(() => {
    if (typeof document === "undefined") return;

    ozgunBaslik.current ??= document.title;
    const taban = ozgunBaslik.current;

    document.title = okunmamis > 0 ? `(${okunmamis}) ${taban}` : taban;

    return () => {
      if (ozgunBaslik.current) document.title = ozgunBaslik.current;
    };
  }, [okunmamis]);

  /* Panel açılınca rozet tazelenir: içeride okunanlar düşsün. */
  useEffect(() => {
    if (acik) okunmamisTazele();
  }, [acik, okunmamisTazele]);

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
            sesSusturuldu={sesSusturuldu}
            onSesiDegistir={sesiDegistir}
            onKapat={kapat}
            onKonusmaDegisti={konusmaDegisti}
            baslangicKonusmaId={sonKonusma}
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

        {/*
          ROZET YALNIZ PANEL KAPALIYKEN: panel açıkken sayı zaten
          konuşma listesinde konuşma konuşma görünüyor ve düğmenin
          üstünde ikinci bir sayı kafa karıştırırdı.
        */}
        {!acik && okunmamis > 0 && (
          <span
            className="mesaj-baloncuk-rozet"
            aria-label={`${okunmamis} okunmamış mesaj`}
          >
            {okunmamis > 99 ? "99+" : okunmamis}
          </span>
        )}
      </button>
    </>
  );
}
