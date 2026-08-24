"use client";

import { useCallback, useEffect, useRef, useState } from "react";

import {
  type CollaborationEntityType,
  type CommentItem,
  collaborationService,
} from "@/services/collaboration.service";
import { dateTime } from "@/lib/format/turkish";
import { Button, ConfirmDialog, EmptyState } from "@/components/ui";

/**
 * ORTAK YORUM DİZİSİ.
 *
 * HİÇBİR MODÜLÜ BİLMEZ. `entityType` + `entityId` alır, gerisi her
 * ekran için aynıdır. Modüle özel bir dal açılırsa ortaklık biter ve
 * her ekran kendi kopyasını taşımaya başlar — bu bileşenin varlık
 * sebebi o dalın hiç açılmamasıdır.
 *
 * `currentUserId` dışarıdan geliyor: bileşen kendi başına oturum
 * sorgusu yapsaydı, aynı sayfada iki kez kullanıldığında iki istek
 * atardı.
 */

/** Düzenleme penceresi — sunucudaki kuralın aynısı (15 dakika). */
const DUZENLEME_PENCERESI_MS = 15 * 60 * 1000;

type Props = {
  entityType: CollaborationEntityType;
  entityId: string;

  /**
   * OKUMA İZNİ — ZORUNLU, VARSAYILANI YOK.
   *
   * `false` ise bileşen HİÇ RENDER EDİLMEZ ve hiçbir istek atmaz.
   * Yorum kapısı üç tipte ekran kapısından DAR: ekranı açabilen ama
   * yorum izni olmayan kullanıcı, 403 ya da boş bir hata kutusu
   * GÖRMEMELİ — olmayan bir bölümün hata vermesi, kullanıcıya
   * bozulmuş bir ekran gösterir.
   *
   * ZORUNLU olmasının sebebi: varsayılanı `true` olsaydı yeni bir
   * ekran bileşeni takarken kararı ATLAMAK mümkün olurdu ve atlandığı
   * kimseye görünmezdi. TypeScript şimdi her takma yerinde açık bir
   * karar istiyor.
   */
  canRead: boolean;

  /** Oturum sahibinin kimliği — "bu yorum benim mi" kararı için. */
  currentUserId?: string | null;

  /**
   * Yorum yazma kapalıysa dizi salt okunur görünür. İzin kararı
   * ekranındır: bileşen izin bilmez.
   */
  canWrite?: boolean;

  /**
   * GÖRÜNÜRLÜK UYARISI — yazan kişi kimin okuyacağını bilsin.
   *
   * Yorum, kaydı görebilen HERKESE açık; bu, yazarken bilinmezse
   * kişi kapalı sandığı bir yere açık bir şey yazar. Uyarı ipucu
   * balonu değil, kutunun ÜSTÜNDE düz metin: balon, yazmadan önce
   * okunmayan tek yerdir.
   *
   * Varsayılan genel; görünürlüğü başka bir modülden MİRASLA gelen
   * tipler kendi metnini geçer. Örnek: çek yorumları `finance.view`
   * ile korunuyor, yani "çekleri görebilen herkes" — "bu kaydı
   * görebilen herkes" cümlesi orada eksik kalırdı.
   */
  gorunurlukNotu?: string;
};

export function CommentThread({
  entityType,
  entityId,
  currentUserId,
  canRead,
  canWrite = true,
  gorunurlukNotu = "Bu yorumu, kaydı görebilen herkes görür.",
}: Props) {
  const [items, setItems] = useState<CommentItem[]>([]);
  const [cursor, setCursor] = useState<{ createdAtUtc: string; id: string } | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [taslak, setTaslak] = useState("");
  const [gonderiliyor, setGonderiliyor] = useState(false);

  const [duzenlenen, setDuzenlenen] = useState<string | null>(null);
  const [duzenlemeMetni, setDuzenlemeMetni] = useState("");

  const [gizlenecek, setGizlenecek] = useState<CommentItem | null>(null);
  const [gizleniyor, setGizleniyor] = useState(false);

  /**
   * ŞİMDİKİ ZAMAN DURUM OLARAK TUTULUYOR.
   *
   * Düzenleme penceresi zamanla KAPANIR. `Date.now()` doğrudan
   * render içinde okunsaydı, pencere dolduğunda düğme ekranda
   * kalmaya devam ederdi (React yeniden çizmez) ve kullanıcı
   * tıklayıp uçtan hata yerdi. Dakikada bir tazeleniyor.
   */
  const [simdi, setSimdi] = useState(() => Date.now());

  useEffect(() => {
    const zamanlayici = window.setInterval(() => setSimdi(Date.now()), 60_000);
    return () => window.clearInterval(zamanlayici);
  }, []);

  const iptalRef = useRef(0);

  // İZİN YOKSA HİÇBİR ŞEY YOK — istek de yok, kutu da yok.
  const okuyabilir = canRead;

  const yukle = useCallback(
    async (devam = false) => {
      const tur = ++iptalRef.current;

      if (!devam) {
        setLoading(true);
        setError(null);
      }

      try {
        const sayfa = await collaborationService.listComments(
          entityType,
          entityId,
          devam ? cursor : null
        );

        // YARIŞ KORUMASI: kullanıcı hızlıca başka kayda geçerse eski
        // isteğin yanıtı yeni listeyi ezmesin.
        if (tur !== iptalRef.current) return;

        setItems((eski) => (devam ? [...eski, ...sayfa.items] : sayfa.items));
        setCursor(sayfa.nextCursor);
        setHasMore(sayfa.hasMore);
      } catch (hata) {
        if (tur !== iptalRef.current) return;
        setError(hata instanceof Error ? hata.message : "Yorumlar yüklenemedi.");
      } finally {
        if (tur === iptalRef.current) setLoading(false);
      }
    },
    [entityType, entityId, cursor]
  );

  // Kayıt değişince liste sıfırdan yüklenir.
  useEffect(() => {
    setItems([]);
    setCursor(null);
    setHasMore(false);
    if (okuyabilir) void yukle(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityType, entityId, okuyabilir]);

  async function gonder() {
    const metin = taslak.trim();
    if (!metin || gonderiliyor) return;

    setGonderiliyor(true);
    setError(null);

    try {
      const yeni = await collaborationService.addComment(entityType, entityId, metin);

      // Listeyi yeniden çekmiyoruz: uç yeni yorumu ADIYLA döndürüyor,
      // yeniden çekmek imleci de sıfırlardı.
      setItems((eski) => [yeni, ...eski]);
      setTaslak("");
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Yorum yazılamadı.");
    } finally {
      setGonderiliyor(false);
    }
  }

  async function duzenlemeyiKaydet(id: string) {
    const metin = duzenlemeMetni.trim();
    if (!metin) return;

    try {
      const guncel = await collaborationService.editComment(id, metin);
      setItems((eski) => eski.map((x) => (x.id === id ? guncel : x)));
      setDuzenlenen(null);
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Yorum düzenlenemedi.");
    }
  }

  async function gizle() {
    if (!gizlenecek) return;

    setGizleniyor(true);

    try {
      const guncel = await collaborationService.hideComment(gizlenecek.id);
      setItems((eski) => eski.map((x) => (x.id === gizlenecek.id ? guncel : x)));
      setGizlenecek(null);
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Yorum gizlenemedi.");
    } finally {
      setGizleniyor(false);
    }
  }

  function duzenlenebilir(item: CommentItem) {
    if (item.isHidden) return false;
    if (!currentUserId || item.createdByUserId !== currentUserId) return false;

    return simdi - new Date(item.createdAtUtc).getTime() < DUZENLEME_PENCERESI_MS;
  }

  if (!okuyabilir) return null;

  return (
    <section className="erp-panel" aria-label="Yorumlar">
      <header className="erp-panel-header">
        <h2>Yorumlar</h2>
      </header>

      {canWrite && (
        <div className="erp-comment-compose">
          <p className="erp-comment-visibility">{gorunurlukNotu}</p>
          <textarea
            value={taslak}
            onChange={(e) => setTaslak(e.target.value)}
            placeholder="Yorum yazın…"
            rows={3}
            aria-label="Yeni yorum"
          />
          <Button
            variant="primary"
            disabled={!taslak.trim() || gonderiliyor}
            onClick={() => void gonder()}
          >
            {gonderiliyor ? "Gönderiliyor…" : "Yorum Ekle"}
          </Button>
        </div>
      )}

      {error && (
        <p className="erp-status red" role="alert">
          {error}
        </p>
      )}

      {loading && items.length === 0 && <p>Yorumlar yükleniyor…</p>}

      {!loading && items.length === 0 && !error && (
        <EmptyState
          title="Henüz yorum yok"
          description={
            canWrite
              ? "İlk yorumu siz yazın — kayıtla ilgili kararlar burada kalsın."
              : "Bu kayda henüz kimse yorum yazmadı."
          }
        />
      )}

      <ul className="erp-comment-list">
        {items.map((item) => (
          <li key={item.id} className="erp-comment">
            <div className="erp-comment-head">
              <strong>{item.createdByName}</strong>
              <span>{dateTime(item.createdAtUtc)}</span>
              {item.editCount > 0 && !item.isHidden && (
                <span className="erp-status">düzenlendi</span>
              )}
            </div>

            {item.isHidden ? (
              /*
               * GİZLENEN YORUM SİLİNMİŞ GİBİ GÖRÜNMEZ — YERİ DURUR.
               * Cevap verilmiş bir cümle konuşmadan çıkarılırsa kalan
               * cevaplar anlamsızlaşır.
               */
              <p className="erp-comment-hidden">
                Bu yorum {item.hiddenByName ?? "yazarı"} tarafından gizlendi.
              </p>
            ) : duzenlenen === item.id ? (
              <div className="erp-comment-compose">
                <textarea
                  value={duzenlemeMetni}
                  onChange={(e) => setDuzenlemeMetni(e.target.value)}
                  rows={3}
                  aria-label="Yorumu düzenle"
                />
                <div className="erp-comment-actions">
                  <Button
                    variant="primary"
                    disabled={!duzenlemeMetni.trim()}
                    onClick={() => void duzenlemeyiKaydet(item.id)}
                  >
                    Kaydet
                  </Button>
                  <Button variant="secondary" onClick={() => setDuzenlenen(null)}>
                    Vazgeç
                  </Button>
                </div>
              </div>
            ) : (
              <p className="erp-comment-body">{item.body}</p>
            )}

            {duzenlenebilir(item) && duzenlenen !== item.id && (
              <div className="erp-comment-actions">
                <Button
                  variant="secondary"
                  onClick={() => {
                    setDuzenlenen(item.id);
                    setDuzenlemeMetni(item.body ?? "");
                  }}
                >
                  Düzenle
                </Button>
                <Button variant="secondary" onClick={() => setGizlenecek(item)}>
                  Gizle
                </Button>
              </div>
            )}
          </li>
        ))}
      </ul>

      {hasMore && (
        <Button variant="secondary" disabled={loading} onClick={() => void yukle(true)}>
          Daha eski yorumlar
        </Button>
      )}

      <ConfirmDialog
        open={gizlenecek !== null}
        title="Yorumu gizle"
        description="Yorum silinmez, gizlenir: yeri konuşmada kalır ve kimin gizlediği görünür."
        confirmLabel="Yorumu Gizle"
        busy={gizleniyor}
        onCancel={() => setGizlenecek(null)}
        onConfirm={() => void gizle()}
      />
    </section>
  );
}
