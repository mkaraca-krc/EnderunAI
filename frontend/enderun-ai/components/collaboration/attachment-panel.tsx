"use client";

import { useCallback, useEffect, useRef, useState } from "react";

import {
  type AttachmentItem,
  type CollaborationEntityType,
  collaborationService,
  dosyaBoyutu,
} from "@/services/collaboration.service";
import { dateTime } from "@/lib/format/turkish";
import { Button, EmptyState } from "@/components/ui";

/**
 * ORTAK EK DOSYA BLOĞU.
 *
 * Yorum dizisiyle aynı kural: modül bilmez, `entityType` +
 * `entityId` ile çalışır.
 *
 * MOBİLDE KAMERA DOĞRUDAN AÇILIR. Sahadaki kişi için "dosya seç"
 * akışı, çekilmiş bir fotoğrafı galeriden bulmak demek; `capture`
 * ile kamera doğrudan açılıyor. Ayrı bir düğme, çünkü galeriden
 * seçmek de gerekiyor — tek düğme ikisinden birini imkânsız kılardı.
 */

type Props = {
  entityType: CollaborationEntityType;
  entityId: string;

  /** Yükleme kapalıysa blok salt okunur. İzin kararı ekranındır. */
  canUpload?: boolean;
};

export function AttachmentPanel({ entityType, entityId, canUpload = true }: Props) {
  const [items, setItems] = useState<AttachmentItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [yukleniyor, setYukleniyor] = useState(false);

  const dosyaRef = useRef<HTMLInputElement>(null);
  const kameraRef = useRef<HTMLInputElement>(null);

  const yukle = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      setItems(await collaborationService.listAttachments(entityType, entityId));
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Ek dosyalar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [entityType, entityId]);

  useEffect(() => {
    void yukle();
  }, [yukle]);

  async function gonder(dosya: File | undefined | null) {
    if (!dosya || yukleniyor) return;

    setYukleniyor(true);
    setError(null);

    try {
      const yeni = await collaborationService.uploadAttachment(
        entityType,
        entityId,
        dosya
      );

      setItems((eski) => [yeni, ...eski]);
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Dosya yüklenemedi.");
    } finally {
      setYukleniyor(false);

      // Aynı dosya ikinci kez seçilebilsin: input değeri
      // temizlenmezse tarayıcı "change" olayını tetiklemez.
      if (dosyaRef.current) dosyaRef.current.value = "";
      if (kameraRef.current) kameraRef.current.value = "";
    }
  }

  return (
    <section className="erp-panel" aria-label="Ek dosyalar">
      <header className="erp-panel-header">
        <h2>Ek Dosyalar</h2>
      </header>

      {canUpload && (
        <div className="erp-attachment-actions">
          <input
            ref={dosyaRef}
            type="file"
            hidden
            onChange={(e) => void gonder(e.target.files?.[0])}
          />
          <input
            ref={kameraRef}
            type="file"
            accept="image/*"
            capture="environment"
            hidden
            onChange={(e) => void gonder(e.target.files?.[0])}
          />

          <Button
            variant="secondary"
            disabled={yukleniyor}
            onClick={() => dosyaRef.current?.click()}
          >
            {yukleniyor ? "Yükleniyor…" : "Dosya Ekle"}
          </Button>

          <Button
            variant="secondary"
            disabled={yukleniyor}
            onClick={() => kameraRef.current?.click()}
            className="erp-only-mobile"
          >
            Fotoğraf Çek
          </Button>
        </div>
      )}

      {error && (
        <p className="erp-status red" role="alert">
          {error}
        </p>
      )}

      {loading && items.length === 0 && <p>Ek dosyalar yükleniyor…</p>}

      {!loading && items.length === 0 && !error && (
        <EmptyState
          title="Ek dosya yok"
          description={
            canUpload
              ? "Sahadan fotoğraf, sözleşme ya da ölçüm belgesi ekleyebilirsiniz."
              : "Bu kayda henüz dosya eklenmedi."
          }
        />
      )}

      <ul className="erp-attachment-list">
        {items.map((item) => (
          <li key={item.id} className="erp-attachment">
            <div className="erp-attachment-info">
              <a href={item.downloadUrl} download>
                {item.originalName}
              </a>
              <span>
                {dosyaBoyutu(item.sizeBytes)} · {item.uploadedByName} ·{" "}
                {dateTime(item.createdAtUtc)}
              </span>
            </div>

            {!item.isBrowserViewable && (
              /*
               * TARAYICI GÖSTEREMEYEN TÜR AÇIKÇA SÖYLENİR.
               *
               * iPhone'un varsayılanı HEIC ve Chrome/Firefox onu
               * gösteremiyor. Bunu yazmasaydık kullanıcı bozuk resim
               * simgesi görür ve dosyanın bozuk yüklendiğini sanırdı.
               */
              <span className="erp-status">tarayıcıda açılmaz, indirin</span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
