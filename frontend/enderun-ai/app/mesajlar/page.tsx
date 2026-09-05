"use client";

import { useEffect, useRef, useState } from "react";

import { useCurrentUser } from "@/lib/use-current-user";
import { useRefreshable } from "@/lib/data/use-refreshable";
import {
  messagingService,
  type MesajOzeti,
  type KisiOzeti,
} from "@/services/messaging.service";

/**
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

export default function MesajlarSayfasi() {
  const { user } = useCurrentUser();

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

  const [secili, setSecili] = useState<string | null>(null);
  const [mesajlar, setMesajlar] = useState<MesajOzeti[]>([]);
  const [mesajYukleniyor, setMesajYukleniyor] = useState(false);
  const [gonderiliyor, setGonderiliyor] = useState(false);
  const [hata, setHata] = useState<string | null>(null);

  const [taslak, setTaslak] = useState("");

  const [kisiSorgu, setKisiSorgu] = useState("");
  const [kisiler, setKisiler] = useState<KisiOzeti[]>([]);
  const [kisiAcik, setKisiAcik] = useState(false);

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

    if (q.trim().length < 2) {
      setKisiler([]);
      return;
    }

    try {
      setKisiler(await messagingService.kisiAra(q.trim()));
    } catch {
      setKisiler([]);
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

  return (
    <div className="rw">
      <div className="erp-page-header">
        <h1>Mesajlar</h1>
        <p>Çalışma arkadaşlarınızla birebir yazışma.</p>
      </div>

      {(hata ?? konusmaKaynagi.error) && (
        <div className="erp-alert erp-alert-error">
          {hata ?? konusmaKaynagi.error}
        </div>
      )}

      <div className="mesaj-duzen">
        {/* ── SOL: konuşma listesi ── */}
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
                placeholder="Kişi ara (en az 2 harf)"
                onChange={(e) => void kisiAra(e.target.value)}
              />

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

          {!konusmaKaynagi.loading && konusmalar.length === 0 && (
            <div className="erp-empty-state">
              <p>
                <strong>Henüz konuşmanız yok.</strong> &ldquo;Yeni
                konuşma&rdquo; ile bir çalışma arkadaşınızı seçin.
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

        {/* ── SAĞ: mesaj görünümü ── */}
        <section className="mesaj-govde">
          {!secili && (
            <div className="erp-empty-state">
              <p>Soldan bir konuşma seçin.</p>
            </div>
          )}

          {secili && (
            <>
              <header className="mesaj-baslik">
                <strong>{seciliKonusma?.baslik ?? "Konuşma"}</strong>
              </header>

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
      </div>
    </div>
  );
}
