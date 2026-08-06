"use client";

import { useEffect, useRef, useState } from "react";

import {
  engineeringPositionService,
  positionPriceService,
  type EngineeringPositionListItem,
} from "@/services/engineering-position.service";

/** Seçilen pozun kaleme doldurulacak bilgileri. */
export type PickedPosition = {
  id: string;
  code: string;
  officialCode?: string | null;
  name: string;
  unit: string;
  category?: string | null;
  institution?: string | null;
  /** Kütüphaneden gelen birim fiyat; bulunamadıysa null. */
  unitPrice?: number | null;
  materialPrice?: number | null;
  laborPrice?: number | null;
  priceExplanation?: string | null;
};

type Props = {
  value?: string | null;
  /** Seçili pozun kısa gösterimi (kod — tanım). */
  label?: string | null;
  year?: number;
  disabled?: boolean;
  onPick: (position: PickedPosition | null) => void;
};

const money = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Poz kütüphanesinden arayarak seçim.
 *
 * Kütüphane 23 binin üzerinde poz taşıyor; hepsini bir açılır listeye
 * doldurmak tarayıcıyı kilitler. Bu yüzden arama SUNUCUDA yapılıyor ve
 * yalnızca eşleşen ilk kayıtlar getiriliyor.
 *
 * Seçim yapılınca birim fiyat kütüphaneden çekilip kaleme doldurulur;
 * fiyat bulunamazsa alan boş bırakılır ve gerekçesi yazılır — sıfır
 * fiyat doldurmak sessiz bir hata olurdu.
 */
export default function PositionPicker({
  value,
  label,
  year,
  disabled,
  onPick,
}: Props) {
  const [open, setOpen] = useState(false);
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<EngineeringPositionListItem[]>([]);
  const [error, setError] = useState("");

  // Aramayı her tuşta değil, yazma durunca çalıştır.
  const debounced = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!open) return;

    const query = term.trim();

    if (debounced.current) clearTimeout(debounced.current);

    // Arama bir dış sisteme gidiyor: efekt gövdesi yalnızca zamanlayıcı
    // kurar, bütün durum güncellemeleri await'ten sonra yapılır.
    debounced.current = setTimeout(() => {
      void (async () => {
        if (query.length < 2) {
          setResults([]);
          setError("");
          return;
        }

        try {
          const rows = await engineeringPositionService.getAll({
            search: query,
            take: 40,
          });

          setResults(rows);
          setError("");
        } catch (err) {
          setResults([]);
          setError(err instanceof Error ? err.message : "Poz aranamadı.");
        }
      })();
    }, 250);

    return () => {
      if (debounced.current) clearTimeout(debounced.current);
    };
  }, [term, open]);

  async function choose(position: EngineeringPositionListItem) {
    setOpen(false);
    setTerm("");
    setResults([]);

    let unitPrice: number | null = null;
    let materialPrice: number | null = null;
    let laborPrice: number | null = null;
    let explanation: string | null = null;

    try {
      const resolution = await positionPriceService.resolve(position.id, year);

      unitPrice = resolution.found ? resolution.unitPrice ?? null : null;
      materialPrice = resolution.materialPrice ?? null;
      laborPrice = resolution.laborPrice ?? null;
      explanation = resolution.explanation;
    } catch {
      // Fiyat alınamazsa poz yine seçilir; fiyat elle girilir.
      explanation = "Birim fiyat alınamadı, elle girin.";
    }

    onPick({
      id: position.id,
      code: position.code,
      officialCode: position.officialCode,
      name: position.name,
      unit: position.unit,
      category: position.category,
      institution: position.officialInstitution,
      unitPrice,
      materialPrice,
      laborPrice,
      priceExplanation: explanation,
    });
  }

  return (
    <div style={{ position: "relative" }}>
      {!open ? (
        <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
          <button
            type="button"
            className="erp-secondary-button"
            disabled={disabled}
            onClick={() => setOpen(true)}
          >
            {value ? "Değiştir" : "Poz ara"}
          </button>

          <small>{label || "Poz seçilmedi"}</small>

          {value && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={disabled}
              onClick={() => onPick(null)}
              title="Poz bağını kaldır"
            >
              ×
            </button>
          )}
        </div>
      ) : (
        <div>
          <input
            className="erp-input"
            autoFocus
            value={term}
            placeholder="Poz no veya tanım (en az 2 harf)"
            onChange={(event) => setTerm(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") setOpen(false);
            }}
          />

          {error && <small className="erp-text-danger">{error}</small>}

          {term.trim().length >= 2 && results.length === 0 && (
            <small>
              Eşleşen poz yok. Kütüphanede karşılığı yoksa özel poz
              tanımlayabilirsiniz.
            </small>
          )}

          {results.length > 0 && (
            <ul
              style={{
                position: "absolute",
                zIndex: 20,
                background: "var(--erp-surface, #fff)",
                border: "1px solid #d8dee6",
                borderRadius: 6,
                maxHeight: 260,
                overflowY: "auto",
                width: 460,
                margin: 0,
                padding: 0,
                listStyle: "none",
              }}
            >
              {results.map((position) => (
                <li key={position.id}>
                  <button
                    type="button"
                    style={{
                      display: "block",
                      width: "100%",
                      textAlign: "left",
                      padding: "6px 10px",
                      border: "none",
                      background: "transparent",
                      cursor: "pointer",
                    }}
                    onClick={() => void choose(position)}
                  >
                    <strong>{position.officialCode || position.code}</strong>{" "}
                    {position.name.length > 70
                      ? `${position.name.slice(0, 70)}...`
                      : position.name}
                    <small style={{ display: "block" }}>
                      {position.unit}
                      {position.officialInstitution
                        ? ` · ${position.officialInstitution}`
                        : ""}
                    </small>
                  </button>
                </li>
              ))}
            </ul>
          )}

          <button
            type="button"
            className="erp-secondary-button"
            style={{ marginTop: 6 }}
            onClick={() => setOpen(false)}
          >
            Vazgeç
          </button>
        </div>
      )}
    </div>
  );
}

/** Referans fiyatı okunur biçime çevirir. */
export function formatReferencePrice(value?: number | null) {
  return value == null ? "—" : money.format(value);
}
