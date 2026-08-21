"use client";

import { useCallback, useEffect, useRef, useState } from "react";

import { Button } from "@/components/ui";
import {
  inventoryService,
  type InventoryItemPhoto,
} from "@/services/inventory.service";

/**
 * STOK KARTI GÖRSEL GALERİSİ.
 *
 * SERBEST kartlarda (dekoratif aydınlatma, özel imalat) ürün tarifle
 * anlatılamaz: montaj öncesi/sonrası, detay ve ölçü krokisi ayrı
 * görsellerdir. Biri KAPAK olarak işaretlenir; listede ve etikette o
 * görünür.
 *
 * KAPAK GÜVENCESİNİ SUNUCU VERİYOR: ilk yüklenen kendiliğinden kapak
 * olur, kapak silinince sıradaki devralır. Ekran bu kuralı yeniden
 * uygulamıyor — iki yerde yazılsaydı biri değişince diğeri geride
 * kalırdı; yükleme/silme sonrası listeyi yeniden okuyor.
 */
export function InventoryPhotoGallery({
  itemId,
  canEdit,
}: {
  itemId: string;
  canEdit: boolean;
}) {
  const [photos, setPhotos] = useState<InventoryItemPhoto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setPhotos(await inventoryService.getPhotos(itemId));
      setError("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Görseller alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [itemId]);

  useEffect(() => {
    void load();
  }, [load]);

  async function upload(file: File) {
    setBusy(true);
    setError("");

    try {
      await inventoryService.addPhoto(itemId, file);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Görsel yüklenemedi.");
    } finally {
      setBusy(false);
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  async function run(action: () => Promise<unknown>, fallback: string) {
    setBusy(true);
    setError("");

    try {
      await action();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : fallback);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="erp-table-card erp-mt">
      <div className="erp-table-header">
        <h2>Görseller</h2>
        {canEdit && (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => fileInput.current?.click()}
          >
            {busy ? "Yükleniyor…" : "Görsel Ekle"}
          </Button>
        )}
      </div>

      {error && <p className="erp-form-error">{error}</p>}

      <input
        ref={fileInput}
        type="file"
        accept="image/*"
        style={{ display: "none" }}
        onChange={(event) => {
          const file = event.target.files?.[0];
          if (file) void upload(file);
        }}
      />

      {loading ? (
        <p>Yükleniyor…</p>
      ) : photos.length === 0 ? (
        <div className="erp-empty-state">
          <p>
            Bu kartta görsel yok. Dekoratif ve özel imalat ürünlerde görsel,
            malzemenin ne olduğunu tarifin anlatamadığı kadar iyi anlatır.
          </p>
        </div>
      ) : (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(180px, 1fr))",
            gap: "1rem",
            padding: "1rem",
          }}
        >
          {photos.map((photo) => (
            <figure key={photo.id} style={{ margin: 0 }}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={inventoryService.photoUrl(photo.id)}
                alt={photo.caption ?? photo.originalName}
                style={{
                  width: "100%",
                  aspectRatio: "4 / 3",
                  objectFit: "cover",
                  borderRadius: "0.5rem",
                  /*
                   * KAPAK ÇERÇEVESİ TOKENDAN. Ham hex yazılsaydı marka
                   * rengi değiştiğinde bu hücre geride kalır, koyu temada
                   * da yanlış anlam taşırdı. İlk yazımda olmayan bir
                   * değişken adı uydurulmuştu; gerçek tokenlar `--erp-*`
                   * ve sözleşme testi bunu yakaladı.
                   */
                  border: photo.isCover
                    ? "2px solid var(--erp-accent)"
                    : "1px solid var(--erp-border)",
                }}
              />

              <figcaption style={{ marginTop: "0.5rem", fontSize: "0.8rem" }}>
                <div>{photo.caption ?? photo.originalName}</div>

                {photo.isCover ? (
                  <strong>Kapak</strong>
                ) : (
                  canEdit && (
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() =>
                        void run(
                          () => inventoryService.setCoverPhoto(photo.id),
                          "Kapak değiştirilemedi."
                        )
                      }
                    >
                      Kapak yap
                    </Button>
                  )
                )}

                {canEdit && (
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() =>
                      void run(
                        () => inventoryService.deletePhoto(photo.id),
                        "Görsel silinemedi."
                      )
                    }
                  >
                    Sil
                  </Button>
                )}
              </figcaption>
            </figure>
          ))}
        </div>
      )}
    </div>
  );
}
