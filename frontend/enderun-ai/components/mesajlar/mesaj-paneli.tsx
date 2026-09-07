"use client";

import { useEffect, useRef, useState } from "react";

import {
  canliBaglantiyiBaslat,
  canliMesajDinle,
} from "@/lib/mesajlasma/canli-baglanti";
import { etkinKonusmayiYaz } from "@/lib/mesajlasma/etkin-konusma";

import { useCurrentUser } from "@/lib/use-current-user";
import { useTaslakDeposu } from "@/lib/mesajlasma/taslak-deposu";
import { useRefreshable } from "@/lib/data/use-refreshable";
import {
  MESAJ_ARAMA_EN_AZ_HARF,
  MESAJ_ARAMA_IPUCU,
} from "@/lib/mesajlasma/arama-kurali";
import {
  messagingService,
  type MesajOzeti,
  type KisiOzeti,
} from "@/services/messaging.service";

/**
 * MESAJ PANELİ — TEK BİLEŞEN, İKİ KİP (M3/2c-1).
 *
 * ═══ NEDEN TEK BİLEŞEN ═══
 *
 * `/mesajlar` tam sayfa hâli KALDIRILMADI; panel onun yerine değil,
 * yanına geldi. İkisi ayrı yazılsaydı zamanla ayrışırdı ve AYRIŞAN HER
 * NOKTA, BİRİNİN SINAMADIĞI BİR NOKTADIR — bu kod tabanının en sık
 * hatası tam olarak aynı şeyin ikinci kopyası.
 *
 * Fark tek bir `kip` parametresinde: `panel` tek sütun ve liste ↔
 * konuşma geçişi, `tam-sayfa` iki sütunlu düzen.
 *
 * ═══ DURUM BURADA YAŞAMAZ ═══
 *
 * Panelin AÇIK/KAPALI olması ve son konuşma bu bileşende DEĞİL,
 * kabukta tutuluyor (`mesaj-baloncugu.tsx`). Sebep: bu bileşen panel
 * kapandığında sökülür; durumu burada tutmak, kapanışta kaybetmek
 * demekti.
 *
 * ─────────────────────────────────────────────────────────────────
 * MESAJLAR — ÇALIŞAN EN KÜÇÜK MESAJLAŞMA (TUR 2.4).
 *
 * ÜÇ İŞ: konuşma listesi, mesaj görünümü, gönderme.
 *
 * KAPSAM KİLİDİ — BUNLAR BİLEREK YOK: dosya eki, okundu bilgisi
 * (rozet dışında), grup yönetimi, canlı akış. Sunucuda `MesajHub`
 * hazır ama ön yüzde SignalR bağımlılığı yok; onu eklemek bu paketin
 * kapsamını kırardı ve "yarım çalışan üç şey" bırakırdı.
 *
 * ERİŞİM İKİ KAPIDAN GEÇİYOR VE İKİSİ DE SUNUCUDA: `mesajlar.view` /
 * `mesajlar.send` anahtarı özelliğe, ÜYELİK konuşmaya. Bu ekran
 * hiçbir erişim kararı vermiyor — verse, kapı istemcide olurdu.
 */

/** Sunucu saatini kullanıcının okuyacağı biçime çevirir. */
function saat(zaman: string | null): string {
  if (!zaman) return "";

  const t = new Date(zaman);
  if (Number.isNaN(t.getTime())) return "";

  const bugun = new Date();
  const ayniGun =
    t.getFullYear() === bugun.getFullYear() &&
    t.getMonth() === bugun.getMonth() &&
    t.getDate() === bugun.getDate();

  return ayniGun
    ? t.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })
    : t.toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" });
}

export type MesajPaneliKipi = "panel" | "tam-sayfa";

export interface MesajPaneliOzellikleri {
  kip: MesajPaneliKipi;
  /** Panel kipinde: kullanıcı kapatmak istediğinde. */
  onKapat?: () => void;
  /** Panel kipinde: son açılan konuşma değiştiğinde (gecikmeli kaydedilir). */
  onKonusmaDegisti?: (konusmaId: string | null) => void;
  /** Panel kipinde açılışta seçili gelecek konuşma. */
  baslangicKonusmaId?: string | null;

  /**
   * Mesaj sesi susturulmuş mu ve nasıl değiştirilir (MESAJ/3 B3).
   *
   * PANEL SAHİBİ TAŞIYOR, PANEL SAKLAMIYOR: tercih sunucuda ve onu
   * okuyup yazan yer `MesajBaloncugu`. Panel kendi kopyasını
   * tutsaydı, tam sayfa ile panel ayrışır ve iki yüzeyde iki farklı
   * cevap görünürdü — taslakta birebir bu yaşandı.
   *
   * İkisi de verilmezse düğme HİÇ render edilmez; tam sayfa kipinde
   * bugün böyle.
   */
  sesSusturuldu?: boolean;
  onSesiDegistir?: () => void;
  /**
   * Taslak değiştiğinde haber: hangi konuşma, ne yazıldı.
   *
   * TASLAĞIN KENDİSİ ARTIK PROP DEĞİL — paylaşılan `TaslakDeposu`ndan
   * okunuyor. Prop olsaydı `/mesajlar` sayfasının onu geçirmesi
   * gerekirdi ve geçirmediği gün (bugünkü kusur) kullanıcı yazdığını
   * kaybederdi. Bu geri çağrı yalnız KAPATMA UYARISI için duruyor.
   */
  onTaslakDegisti?: (konusmaId: string, metin: string) => void;
}

export default function MesajPaneli({
  kip,
  onKapat,
  onKonusmaDegisti,
  baslangicKonusmaId,
  onTaslakDegisti,
  sesSusturuldu,
  onSesiDegistir,
}: MesajPaneliOzellikleri) {
  const { user } = useCurrentUser();
  const panelKipi = kip === "panel";

  /*
   * KONUŞMA LİSTESİ ORTAK KANCADAN — KENDİ `load()`'UMU YAZMADIM.
   *
   * `useRefreshable` uygulamanın tek veri tazeleme mekanizması: ilk
   * yükleme, hata, elle yenileme ve mutasyon sonrası tazeleme onda.
   * Kendi efektimi yazsaydım 127'nci `load()` olurdu ve
   * `react-hooks/set-state-in-effect` çırasını da 2 ihlal ileri
   * iterdi. Çizgiyi kaydırmak sorunu gizlerdi.
   */
  const konusmaKaynagi = useRefreshable(() => messagingService.konusmalar());

  const konusmalar = konusmaKaynagi.data?.kayitlar ?? [];

  const [secili, setSecili] = useState<string | null>(
    baslangicKonusmaId ?? null
  );
  const [mesajlar, setMesajlar] = useState<MesajOzeti[]>([]);
  const [mesajYukleniyor, setMesajYukleniyor] = useState(false);
  const [gonderiliyor, setGonderiliyor] = useState(false);
  const [hata, setHata] = useState<string | null>(null);

  const [yerelTaslak, setYerelTaslak] = useState("");

  /*
   * TASLAK PAYLAŞILAN DEPODAN — İKİ YÜZEY AYNI KAYNAĞI OKUYOR.
   *
   * Panel ve `/mesajlar` tam sayfası aynı `TaslakDeposu`nu kullanıyor.
   * Önce panelde tutuluyordu ve tam sayfa onu görmüyordu: panelde
   * yazıp `/mesajlar`'a giden yazdığını KAYBEDİYORDU. Dar pencerede ✉
   * düğmesi doğrudan `/mesajlar`'a götürdüğü için bu, telefondan
   * girenlerin tamamının yaşadığı yoldu.
   *
   * Depo yoksa (sağlayıcısız render) yerel duruma düşüyor — patlamak
   * kullanıcıya mesajlaşmayı tamamen kaybettirirdi.
   */
  const depo = useTaslakDeposu();

  const taslak = depo
    ? (secili ? (depo.taslaklar[secili] ?? "") : "")
    : yerelTaslak;

  function setTaslak(deger: string) {
    if (depo) {
      if (secili) {
        depo.taslakYaz(secili, deger);
        onTaslakDegisti?.(secili, deger);
      }
      return;
    }

    setYerelTaslak(deger);
  }

  const [kisiSorgu, setKisiSorgu] = useState("");
  const [kisiler, setKisiler] = useState<KisiOzeti[]>([]);
  const [kisiAcik, setKisiAcik] = useState(false);

  /*
   * ARAMANIN NEDEN SONUÇ VERMEDİĞİ HER ZAMAN SÖYLENİR.
   *
   * `kisiHatasi` sunucunun mesajını taşır; `kisiArandi` ise "arama
   * gerçekten koştu mu" bilgisini. İkisi olmadan boş bir liste üç
   * ayrı durumu aynı gösteriyordu: henüz yazmadın, harf yetmedi,
   * eşleşen yok. Üçünün cevabı farklı.
   */
  const [kisiHatasi, setKisiHatasi] = useState<string | null>(null);
  const [kisiArandi, setKisiArandi] = useState(false);

  const dip = useRef<HTMLDivElement | null>(null);

  /*
   * MESAJLAR EFEKTLE DEĞİL, TIKLAMAYLA YÜKLENİYOR.
   *
   * `secili` değiştiğinde çalışan bir efekt yazmak en kolay yoldu ama
   * o efektin gövdesi setState'e iniyor. Mesajları AÇAN EYLEMİN
   * kendisinde yüklemek hem kuralı çözüyor hem de doğrusu: veri,
   * durum değiştiği için değil, kullanıcı istediği için geliyor.
   */
  async function konusmaSec(konusmaId: string) {
    setSecili(konusmaId);
    // SES KARARI BUNU OKUYOR: ekranda açık olan konuşmaya gelen
    // mesaj ses çalmaz (B2) — kullanıcı zaten bakıyor.
    etkinKonusmayiYaz(konusmaId);
    onKonusmaDegisti?.(konusmaId);
    setMesajYukleniyor(true);

    try {
      const yanit = await messagingService.mesajlar(konusmaId);

      /*
       * SUNUCU EN YENİDEN ESKİYE SAYFALIYOR (imleç `CreatedAtUtc`
       * azalan). Ekranda konuşma ESKİDEN YENİYE okunur, o yüzden
       * burada ters çevriliyor. Sunucunun sırasını değiştirmek
       * sayfalamayı bozardı.
       */
      setMesajlar([...(yanit.kayitlar ?? [])].reverse());
      setHata(null);
    } catch (err) {
      setHata(err instanceof Error ? err.message : "Mesajlar yüklenemedi.");
    } finally {
      setMesajYukleniyor(false);
    }

    /*
     * OKUNDU İŞARETİ SESSİZCE DÜŞEBİLİR — VE DÜŞMESİ KABUL.
     *
     * Rozetin bir saniye geç güncellenmesi, kullanıcıya hata
     * göstermekten iyidir. Ama hatayı yutup hiçbir yere yazmamak da
     * olmaz: konsola düşüyor.
     */
    void messagingService
      .okundu(konusmaId)
      .then(() => konusmaKaynagi.refresh())
      .catch((err) => console.warn("Okundu işaretlenemedi:", err));
  }

  /*
   * TEK EFEKT: yeni mesaj gelince akışın dibine kaydır.
   * setState ÇAĞIRMIYOR — dış sisteme (DOM) yazıyor, efektin asıl işi.
   */
  useEffect(() => {
    dip.current?.scrollIntoView({ block: "end" });
  }, [mesajlar]);

  /*
   * ═══ CANLI AKIŞ ═══
   *
   * `secili`yi bağımlılık listesine koymak, her konuşma değişiminde
   * aboneliği söküp yeniden kurardı. Onun yerine seçili konuşma bir
   * ref'te tutuluyor ve abonelik BİR KEZ kuruluyor.
   */
  const seciliRef = useRef<string | null>(secili);

  useEffect(() => {
    seciliRef.current = secili;
    etkinKonusmayiYaz(secili);
  }, [secili]);

  /*
   * BİLEŞEN GİDERSE ETKİN KONUŞMA DA GİDER.
   *
   * Panel kapandığında ya da `/mesajlar`tan çıkıldığında kayıt
   * kalsaydı, kullanıcı artık bakmadığı bir konuşma için ses
   * duymazdı — sessizlik, hatanın en zor fark edilen türü.
   */
  useEffect(
    () => () => {
      etkinKonusmayiYaz(null);
    },
    []
  );

  useEffect(() => {
    void canliBaglantiyiBaslat();

    return canliMesajDinle((gelen) => {
      /*
       * LİSTE HER DURUMDA TAZELENİYOR: sıralama, önizleme ve
       * okunmamış sayısı sunucuda değişti. Seçili olmayan bir
       * konuşmaya gelen mesaj da listede görünmeli.
       */
      void konusmaKaynagi.refresh();

      if (gelen.konusmaId !== seciliRef.current) return;

      setMesajlar((mevcut) => {
        /*
         * KENDİ GÖNDERDİĞİMİZ MESAJ İKİ KEZ GELİYOR: bir kez
         * `gonder()` yanıtından, bir kez yayından (sunucu gönderene
         * de yayınlıyor — başka sekmesi açık olabilir). Kimliğe göre
         * eleniyor; zamana göre elemek saat farkında bozulurdu.
         */
        if (mevcut.some((x) => x.id === gelen.id)) return mevcut;
        return [...mevcut, gelen];
      });

      // Açık konuşmaya gelen mesaj okunmuş sayılır.
      void messagingService
        .okundu(gelen.konusmaId)
        .then(() => konusmaKaynagi.refresh())
        .catch((err) => console.warn("Okundu işaretlenemedi:", err));
    });
    // Abonelik BİR KEZ kurulur. `konusmaKaynagi.refresh` bağımlılığa
    // konsaydı her tazelemede soket dinleyicisi sökülüp yeniden
    // takılırdı; kaçan mesaj tam o aralıkta düşerdi.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function gonder() {
    const govde = taslak.trim();
    if (!govde || !secili || gonderiliyor) return;

    setGonderiliyor(true);
    try {
      const mesaj = await messagingService.gonder(secili, govde);
      setMesajlar((mevcut) => [...mevcut, mesaj]);
      setTaslak("");
      setHata(null);

      // Liste sırası ve önizleme sunucuda değişti; yeniden okunuyor.
      void konusmaKaynagi.refresh();
    } catch (err) {
      setHata(err instanceof Error ? err.message : "Mesaj gönderilemedi.");
    } finally {
      setGonderiliyor(false);
    }
  }

  async function kisiAra(q: string) {
    setKisiSorgu(q);

    const sorgu = q.trim();

    /*
     * ASGARİ HARF TEK KAYNAKTAN.
     *
     * Burada "2" yazıyordu, sunucu 3 istiyordu. Bir harflik fark,
     * kullanıcıya hiçbir açıklaması olmayan boş bir liste olarak
     * görünüyordu. Sayı artık `MESAJ_ARAMA_EN_AZ_HARF`'ten geliyor ve
     * sunucudakiyle aynı kaldığını bir test tutuyor.
     */
    if (sorgu.length < MESAJ_ARAMA_EN_AZ_HARF) {
      setKisiler([]);
      setKisiArandi(false);
      setKisiHatasi(null);
      return;
    }

    try {
      setKisiler(await messagingService.kisiAra(sorgu));
      setKisiHatasi(null);
      setKisiArandi(true);
    } catch (err) {
      /*
       * SUNUCUNUN MESAJI YUTULMUYOR.
       *
       * Uç anlamlı bir cümle dönüyor ("Arama için en az 3 harf
       * yazın…"). Eski hâlde `catch {}` onu düşürüyor ve kullanıcı
       * neden sonuç almadığını göremiyordu.
       */
      setKisiler([]);
      setKisiArandi(true);
      setKisiHatasi(
        err instanceof Error ? err.message : "Kişi araması başarısız oldu."
      );
    }
  }

  async function birebirAc(kisi: KisiOzeti) {
    try {
      const konusma = await messagingService.birebirAc(kisi.userId);
      setKisiAcik(false);
      setKisiSorgu("");
      setKisiler([]);
      await konusmaKaynagi.refresh();
      await konusmaSec(konusma.id);
    } catch (err) {
      setHata(err instanceof Error ? err.message : "Konuşma açılamadı.");
    }
  }

  const seciliKonusma = konusmalar.find((x) => x.id === secili) ?? null;

  /*
   * PANEL KİPİNDE TEK SÜTUN: dar bir kutuda iki sütun okunmaz.
   * Konuşma seçiliyken liste gizlenir, "geri" ile dönülür. Tam sayfa
   * kipinde ikisi yan yana durur — bugünkü davranış aynen korunuyor.
   */
  const listeGorunsun = !panelKipi || !secili;
  const govdeGorunsun = !panelKipi || !!secili;

  /*
   * TAM SAYFA KİPİNDE BAŞLIK YOK — KABUK ZATEN YAZIYOR.
   *
   * Burada `erp-page-header` içinde "Mesajlar" + "Çalışma
   * arkadaşlarınızla birebir yazışma." duruyordu; `ErpShell` de aynı
   * başlığı aynı metinle render ediyor. Ekranda BİREBİR AYNI başlık
   * iki kez görünüyordu (tarayıcıda ölçüldü, MESAJ/3).
   *
   * Sadece görüntü sorunu değildi: ikinci başlık 78 px yükseklik
   * yiyordu ve o 78 px, composer'ı görünen alanın dışına iten
   * bütçenin parçasıydı.
   */
  return (
    <div className={panelKipi ? "rw mesaj-panel-govde" : "rw"}>
      {panelKipi ? (
        <div className="mesaj-panel-baslik">
          {secili && (
            <button
              type="button"
              className="mesaj-panel-geri"
              onClick={() => setSecili(null)}
              aria-label="Konuşma listesine dön"
            >
              ←
            </button>
          )}
          <strong>{secili ? seciliKonusma?.baslik ?? "Konuşma" : "Mesajlar"}</strong>
          {onSesiDegistir && (
            <button
              type="button"
              className="mesaj-panel-ses"
              onClick={onSesiDegistir}
              aria-pressed={!sesSusturuldu}
              aria-label={
                sesSusturuldu
                  ? "Mesaj sesini aç"
                  : "Mesaj sesini kapat"
              }
              title={
                sesSusturuldu
                  ? "Mesaj sesi kapalı — açmak için tıklayın"
                  : "Mesaj sesi açık — kapatmak için tıklayın"
              }
            >
              {sesSusturuldu ? "🔇" : "🔊"}
            </button>
          )}

          <a className="mesaj-panel-tamsayfa" href="/mesajlar" title="Tam sayfada aç">
            ⤢
          </a>
          <button
            type="button"
            className="mesaj-panel-kapat"
            onClick={() => onKapat?.()}
            aria-label="Mesaj panelini kapat"
          >
            ✕
          </button>
        </div>
      ) : null}

      {(hata ?? konusmaKaynagi.error) && (
        <div className="erp-alert erp-alert-error">
          {hata ?? konusmaKaynagi.error}
        </div>
      )}

      <div className={panelKipi ? "mesaj-duzen mesaj-duzen-panel" : "mesaj-duzen"}>
        {/* ── SOL: konuşma listesi ── */}
        {listeGorunsun && (
        <aside className="mesaj-liste">
          <button
            type="button"
            className="erp-btn"
            onClick={() => setKisiAcik((a) => !a)}
          >
            {kisiAcik ? "Vazgeç" : "Yeni konuşma"}
          </button>

          {kisiAcik && (
            <div className="mesaj-kisi-arama">
              <input
                type="search"
                value={kisiSorgu}
                placeholder={MESAJ_ARAMA_IPUCU}
                onChange={(e) => void kisiAra(e.target.value)}
              />

              {kisiHatasi && (
                <div className="erp-alert erp-alert-error">{kisiHatasi}</div>
              )}

              {/* SESSİZ BOŞ LİSTE YOK: aramanın koştuğu ama kimseyi
                  bulamadığı durum, hiç aranmamış durumdan ayrılır. */}
              {!kisiHatasi && kisiArandi && kisiler.length === 0 && (
                <div className="erp-empty-state">
                  <p>
                    <strong>Eşleşen kişi bulunamadı.</strong> Ad, soyad ya da
                    kullanıcı adının bir parçasını yazmayı deneyin.
                  </p>
                </div>
              )}

              {kisiler.map((kisi) => (
                <button
                  key={kisi.userId}
                  type="button"
                  className="mesaj-kisi"
                  onClick={() => void birebirAc(kisi)}
                >
                  <strong>{kisi.ad}</strong>
                  {kisi.unvan && <small>{kisi.unvan}</small>}
                </button>
              ))}
            </div>
          )}

          {konusmaKaynagi.loading && <div className="erp-alert">Yükleniyor…</div>}

          {/*
            BOŞ DURUM SEBEBİNİ SÖYLER.
            2026-09-06'da rehber HERKES için boştu ve ekran bunu hiç
            söylemiyordu: sebep arama değil, veriydi — 13 kullanıcının
            hiçbirinde personel bağı yoktu ve uç yapısı gereği kimseyi
            döndüremiyordu. "Sessizlik" o gün üç ayrı arızayı aynı
            gösterdi. Ekran artık ne olduğunu ve ne yapılacağını yazar.
          */}
          {!konusmaKaynagi.loading && konusmalar.length === 0 && (
            <div className="erp-empty-state">
              <p>
                <strong>Henüz konuşmanız yok.</strong> &ldquo;Yeni
                konuşma&rdquo; ile bir çalışma arkadaşınızı seçin.
              </p>
              <p>
                Rehberde kimseyi bulamıyorsanız, o kişinin kullanıcı
                hesabının bir <strong>personel kaydına bağlı</strong> ve
                hesabının <strong>etkin</strong> olması gerekir. Bağ yoksa
                kişi rehberde görünmez; yöneticinize bildirin.
              </p>
            </div>
          )}

          {konusmalar.map((k) => (
            <button
              key={k.id}
              type="button"
              className={
                k.id === secili ? "mesaj-satir mesaj-satir-secili" : "mesaj-satir"
              }
              onClick={() => void konusmaSec(k.id)}
            >
              <span className="mesaj-satir-ust">
                <strong>{k.baslik}</strong>
                <small>{saat(k.sonMesajZamani)}</small>
              </span>
              <span className="mesaj-satir-alt">
                <span>{k.sonMesajOnizleme ?? "—"}</span>
                {k.okunmamisSayisi > 0 && (
                  <em className="mesaj-rozet">{k.okunmamisSayisi}</em>
                )}
              </span>
            </button>
          ))}
        </aside>
        )}

        {/* ── SAĞ: mesaj görünümü ── */}
        {govdeGorunsun && (
        <section className="mesaj-govde">
          {!secili && (
            <div className="erp-empty-state">
              <p>Soldan bir konuşma seçin.</p>
            </div>
          )}

          {secili && (
            <>
              {!panelKipi && (
                <header className="mesaj-baslik">
                  <strong>{seciliKonusma?.baslik ?? "Konuşma"}</strong>
                </header>
              )}

              <div className="mesaj-akis">
                {mesajYukleniyor && <div className="erp-alert">Yükleniyor…</div>}

                {!mesajYukleniyor && mesajlar.length === 0 && (
                  <p className="mesaj-bos">
                    Bu konuşmada henüz mesaj yok. İlkini siz yazın.
                  </p>
                )}

                {mesajlar.map((m) => {
                  /*
                   * "BENİM Mİ" SUNUCUDAN GELMİYOR — HESAPLANIYOR.
                   * `MesajOzeti` böyle bir alan taşımıyor; hizalama
                   * gönderen kimliğinin oturumdaki kullanıcıyla
                   * karşılaştırılmasıyla bulunuyor.
                   */
                  const benim = !!user?.id && m.gonderenUserId === user.id;

                  return (
                    <div
                      key={m.id}
                      className={benim ? "mesaj mesaj-benim" : "mesaj"}
                    >
                      {!benim && <small>{m.gonderenAd}</small>}
                      <p>{m.govde}</p>
                      <time>{saat(m.gonderimZamani)}</time>
                    </div>
                  );
                })}

                <div ref={dip} />
              </div>

              <form
                className="mesaj-yaz"
                onSubmit={(e) => {
                  e.preventDefault();
                  void gonder();
                }}
              >
                <input
                  type="text"
                  value={taslak}
                  maxLength={4000}
                  placeholder="Mesaj yazın…"
                  onChange={(e) => setTaslak(e.target.value)}
                />
                <button
                  type="submit"
                  className="erp-btn erp-btn-primary"
                  disabled={gonderiliyor || taslak.trim().length === 0}
                >
                  {gonderiliyor ? "Gönderiliyor…" : "Gönder"}
                </button>
              </form>
            </>
          )}
        </section>
        )}
      </div>
    </div>
  );
}
