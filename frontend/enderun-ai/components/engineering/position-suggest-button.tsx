"use client";

import { useState } from "react";

import {
  positionMatchService,
  type PositionSuggestion,
} from "@/services/engineering-position.service";
import type { PickedPosition } from "@/components/engineering/position-picker";
import { unitPrice } from "@/lib/format/turkish";

type Props = {
  companyId?: string | null;
  /** Serbest metin iş tanımı — sorgu bu. */
  description?: string | null;
  year?: number;
  disabled?: boolean;
  onPick: (position: PickedPosition) => void;
};


function toPicked(suggestion: PositionSuggestion): PickedPosition {
  return {
    id: suggestion.positionId,
    code: suggestion.code,
    officialCode: suggestion.officialCode,
    name: suggestion.name,
    unit: suggestion.unit,
    category: suggestion.category,
    institution: suggestion.institution,
    unitPrice: suggestion.unitPrice,
    materialPrice: suggestion.materialPrice,
    laborPrice: suggestion.laborPrice,
    priceExplanation: suggestion.priceExplanation,
  };
}

/**
 * Serbest metin iş tanımından poz önerir.
 *
 * KESİN eşleşmede (poz numarası yazılmış ya da bir aday diğerlerinden
 * belirgin biçimde önde) poz doğrudan seçilir. Belirsizse aday listesi
 * açılır ve seçimi kullanıcı yapar — birbirine yakın iki aday arasından
 * sistemin seçmesi, yanlış pozla fiyatlanmış bir keşif üretir ve bunu
 * sonradan fark etmek çok zordur.
 *
 * Hiç aday yoksa kullanıcıya özel poz açması söylenir; zorla eşleştirme
 * yapılmaz.
 */
export default function PositionSuggestButton({
  companyId,
  description,
  year,
  disabled,
  onPick,
}: Props) {
  const [busy, setBusy] = useState(false);
  const [candidates, setCandidates] = useState<PositionSuggestion[] | null>(null);
  const [note, setNote] = useState("");

  async function suggest() {
    const query = (description ?? "").trim();

    if (!companyId) {
      setNote("Önce şirket seçin.");
      return;
    }

    if (query.length < 3) {
      setNote("Önce iş tanımını yazın.");
      return;
    }

    setBusy(true);
    setNote("");
    setCandidates(null);

    try {
      const result = await positionMatchService.suggest(companyId, query, {
        year,
        limit: 6,
      });

      if (result.suggestions.length === 0) {
        setNote(result.explanation);
        return;
      }

      if (result.isCertain) {
        onPick(toPicked(result.suggestions[0]));
        setNote(result.certaintyReason ?? "Eşleşme kesin, poz seçildi.");
        return;
      }

      setCandidates(result.suggestions);
      setNote(result.certaintyReason ?? "Adaylardan birini seçin.");
    } catch (err) {
      setNote(err instanceof Error ? err.message : "Öneri alınamadı.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <button
        type="button"
        className="erp-secondary-button"
        disabled={disabled || busy}
        onClick={() => void suggest()}
      >
        {busy ? "Aranıyor..." : "Poz öner"}
      </button>

      {note && <small style={{ display: "block" }}>{note}</small>}

      {candidates && (
        <ul style={{ margin: "6px 0 0", padding: 0, listStyle: "none" }}>
          {candidates.map((candidate) => (
            <li key={candidate.positionId}>
              <button
                type="button"
                style={{
                  display: "block",
                  width: "100%",
                  textAlign: "left",
                  padding: "4px 6px",
                  border: "1px solid var(--erp-border)",
                  borderRadius: 4,
                  background: "transparent",
                  cursor: "pointer",
                  marginBottom: 4,
                }}
                onClick={() => {
                  onPick(toPicked(candidate));
                  setCandidates(null);
                  setNote("Poz seçildi.");
                }}
              >
                <strong>{candidate.officialCode || candidate.code}</strong>{" "}
                {candidate.name.length > 60
                  ? `${candidate.name.slice(0, 60)}...`
                  : candidate.name}
                <small style={{ display: "block" }}>
                  {candidate.unit}
                  {candidate.institution ? ` · ${candidate.institution}` : ""}
                  {candidate.unitPrice != null
                    ? ` · ${unitPrice(candidate.unitPrice)} TL`
                    : " · fiyat yok"}
                  {candidate.aiReason ? ` · ${candidate.aiReason}` : ""}
                </small>
              </button>
            </li>
          ))}

          <li>
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setCandidates(null)}
            >
              Kapat
            </button>
          </li>
        </ul>
      )}
    </div>
  );
}
