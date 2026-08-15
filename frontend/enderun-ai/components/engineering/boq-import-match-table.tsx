"use client";

import { useState } from "react";

import { customPositionService } from "@/services/engineering-position.service";
import { unitPrice } from "@/lib/format/turkish";
import type {
  BoqImportMatchDecision,
  BoqImportPreviewItem,
} from "@/services/project-boq.service";

type Props = {
  companyId: string;
  items: BoqImportPreviewItem[];
  /** Kullanıcının el ile verdiği kararlar; dokunulmayan satır yok. */
  decisions: Record<number, string | null>;
  onChange: (rowNumber: number, positionId: string | null) => void;
  disabled?: boolean;
};

const disciplines = [
  { value: 0, label: "Elektrik" },
  { value: 1, label: "Orta Gerilim" },
  { value: 2, label: "Zayıf Akım" },
  { value: 3, label: "Veri Merkezi" },
  { value: 4, label: "Fiber" },
  { value: 5, label: "Mekanik" },
  { value: 6, label: "İnşaat" },
  { value: 99, label: "Diğer" },
];


/**
 * Aktarım kararlarını gönderilebilir listeye çevirir.
 *
 * Yalnızca kullanıcının dokunduğu satırlar gönderilir; dokunulmayan
 * satırlarda uç YALNIZCA kesin eşleşmeyi uygular. Böylece ekranda
 * "eşleşti" yazan satır aktarımda da bağlanır, belirsiz satır ise
 * sessizce bağlanmaz.
 */
export function toDecisions(
  decisions: Record<number, string | null>
): BoqImportMatchDecision[] {
  return Object.entries(decisions).map(([rowNumber, positionId]) => ({
    rowNumber: Number(rowNumber),
    positionId,
  }));
}

/**
 * Excel önizlemesinde satır-poz eşleştirme.
 *
 * Üç seçenek: adaydan seç, özel poz aç ya da atla. Sistem yalnızca
 * tartışmasız durumda kendi seçer; birbirine yakın iki aday arasından
 * sistemin seçmesi, yanlış pozla fiyatlanmış bir icmal üretir ve bunu
 * sonradan fark etmek çok zordur.
 */
export default function BoqImportMatchTable({
  companyId,
  items,
  decisions,
  onChange,
  disabled,
}: Props) {
  const [customRow, setCustomRow] = useState<number | null>(null);
  const [customName, setCustomName] = useState("");
  const [customUnit, setCustomUnit] = useState("");
  const [customPrice, setCustomPrice] = useState("");
  const [customDiscipline, setCustomDiscipline] = useState(0);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState("");

  // Açılan poz kütüphaneye yazıldı ama önizleme yeniden okunmuyor;
  // satırın yeni bağını burada tutuyoruz.
  const [createdLabels, setCreatedLabels] = useState<Record<number, string>>({});

  function openCustomForm(item: BoqImportPreviewItem) {
    setCustomRow(item.rowNumber);
    setCustomName(item.description);
    setCustomUnit(item.unit);
    setCustomPrice(item.unitPrice ? String(item.unitPrice) : "");
    setCustomDiscipline(0);
    setError("");
  }

  async function createCustom(rowNumber: number) {
    if (!customName.trim()) {
      setError("Poz adı boş olamaz.");
      return;
    }

    setCreating(true);
    setError("");

    try {
      const parsedPrice = customPrice.trim()
        ? Number(customPrice.replace(",", "."))
        : null;

      if (parsedPrice !== null && Number.isNaN(parsedPrice)) {
        setError("Birim fiyat sayı olmalı.");
        return;
      }

      const created = await customPositionService.create({
        companyId,
        name: customName.trim(),
        unit: customUnit.trim() || null,
        discipline: customDiscipline,
        unitPrice: parsedPrice,
        year: parsedPrice !== null ? new Date().getFullYear() : null,
      });

      setCreatedLabels((current) => ({
        ...current,
        [rowNumber]: `${created.code} — ${created.name}`,
      }));

      onChange(rowNumber, created.id);
      setCustomRow(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Özel poz açılamadı.");
    } finally {
      setCreating(false);
    }
  }

  function currentValue(item: BoqImportPreviewItem) {
    const decided = decisions[item.rowNumber];

    if (decided !== undefined) return decided ?? "";

    // Karar verilmemişse ekranda görünen varsayılan: kesin eşleşme.
    if (item.match?.isCertain && item.match.candidates.length > 0)
      return item.match.candidates[0].positionId;

    return "";
  }

  return (
    <div className="erp-table-wrap">
      <table className="erp-table">
        <thead>
          <tr>
            <th>Satır</th>
            <th>Kalem</th>
            <th style={{ textAlign: "right" }}>Birim fiyat</th>
            <th>Poz eşleşmesi</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => {
            const candidates = item.match?.candidates ?? [];
            const selected = currentValue(item);
            const isSkipped = decisions[item.rowNumber] === null;

            return (
              <tr key={item.rowNumber}>
                <td>{item.rowNumber}</td>
                <td>
                  <strong>{item.positionCode || "—"}</strong>
                  <small style={{ display: "block" }}>{item.description}</small>
                </td>
                <td style={{ textAlign: "right" }}>
                  {unitPrice(item.unitPrice)}
                  <small style={{ display: "block" }}>{item.unit}</small>
                </td>
                <td>
                  {createdLabels[item.rowNumber] ? (
                    <small style={{ color: "var(--color-semantic-success)" }}>
                      Özel poz açıldı: {createdLabels[item.rowNumber]}
                    </small>
                  ) : candidates.length === 0 ? (
                    <small style={{ color: "var(--erp-muted)" }}>
                      Kütüphanede karşılık bulunamadı.
                    </small>
                  ) : (
                    <>
                      <select
                        value={selected}
                        disabled={disabled}
                        onChange={(event) =>
                          onChange(
                            item.rowNumber,
                            event.target.value || null
                          )
                        }
                      >
                        <option value="">Atla — poza bağlama</option>
                        {candidates.map((candidate) => (
                          <option
                            key={candidate.positionId}
                            value={candidate.positionId}
                          >
                            {candidate.code} — {candidate.name.slice(0, 60)}
                            {candidate.unitPrice != null
                              ? ` (${unitPrice(candidate.unitPrice)} TL)`
                              : ""}
                          </option>
                        ))}
                      </select>

                      <small style={{ display: "block" }}>
                        {isSkipped
                          ? "Bu satır poza bağlanmayacak."
                          : item.match?.isCertain
                            ? item.match.certaintyReason ??
                              "Eşleşme kesin, otomatik bağlanacak."
                            : (item.match?.certaintyReason ??
                              "Aday var, seçim sizde.")}
                      </small>
                    </>
                  )}

                  {!createdLabels[item.rowNumber] && (
                    <button
                      type="button"
                      className="erp-secondary-button"
                      disabled={disabled || creating}
                      style={{ marginTop: 4 }}
                      onClick={() => openCustomForm(item)}
                    >
                      Özel poz yap
                    </button>
                  )}

                  {customRow === item.rowNumber && (
                    <div
                      style={{
                        marginTop: 6,
                        padding: 8,
                        border: "1px solid var(--erp-border)",
                        borderRadius: 4,
                        display: "grid",
                        gap: 6,
                      }}
                    >
                      <input
                        value={customName}
                        placeholder="Poz adı"
                        onChange={(event) => setCustomName(event.target.value)}
                      />
                      <div style={{ display: "flex", gap: 6 }}>
                        <input
                          value={customUnit}
                          placeholder="Birim"
                          style={{ width: 90 }}
                          onChange={(event) => setCustomUnit(event.target.value)}
                        />
                        <input
                          value={customPrice}
                          placeholder="Birim fiyat"
                          style={{ width: 120 }}
                          onChange={(event) =>
                            setCustomPrice(event.target.value)
                          }
                        />
                        <select
                          value={customDiscipline}
                          onChange={(event) =>
                            setCustomDiscipline(Number(event.target.value))
                          }
                        >
                          {disciplines.map((discipline) => (
                            <option
                              key={discipline.value}
                              value={discipline.value}
                            >
                              {discipline.label}
                            </option>
                          ))}
                        </select>
                      </div>

                      <small>
                        Girilen fiyat &quot;Şirket&quot; fiyatı olarak
                        kaydedilir; resmi kitap fiyatı gibi görünmez.
                      </small>

                      {error && (
                        <small style={{ color: "var(--color-semantic-danger)" }}>{error}</small>
                      )}

                      <div style={{ display: "flex", gap: 6 }}>
                        <button
                          type="button"
                          className="erp-primary-button"
                          disabled={creating}
                          onClick={() => void createCustom(item.rowNumber)}
                        >
                          {creating ? "Açılıyor..." : "Kütüphaneye ekle"}
                        </button>
                        <button
                          type="button"
                          className="erp-secondary-button"
                          disabled={creating}
                          onClick={() => setCustomRow(null)}
                        >
                          Vazgeç
                        </button>
                      </div>
                    </div>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
