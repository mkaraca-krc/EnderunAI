"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ApiError } from "@/lib/api/api-client";
import {
  DeviationImpact,
  progressTrackingService,
  type ProgressTracking,
  type TrackingItem,
} from "@/services/progress-tracking.service";

const money = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const quantity = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "Metraj takibi yüklenemedi.";
}

/** Sapmanın anlamı → renk ve etiket. */
function impactStyle(impact: number): {
  className: string;
  label: string;
  hint: string;
} {
  switch (impact) {
    case DeviationImpact.Opportunity:
      return {
        className: "green",
        label: "İlave iş fırsatı",
        hint: "Birim fiyatlı: yapılan iş kadar ödenir, hakedişe eklenebilir",
      };
    case DeviationImpact.ProfitErosion:
      return {
        className: "red",
        label: "Kâr erozyonu",
        hint: "Anahtar teslim: bedel sabit, bu tutar doğrudan kârdan gider",
      };
    case DeviationImpact.Saving:
      return { className: "green", label: "Tasarruf", hint: "Keşfin altında kalındı" };
    case DeviationImpact.Information:
      return { className: "gray", label: "Bilgi", hint: "Hakediş de o kadar az olur" };
    case DeviationImpact.Undetermined:
      return {
        className: "gray",
        label: "Yorumlanmadı",
        hint: "Sözleşme tipi belirlenmemiş",
      };
    default:
      return { className: "gray", label: "-", hint: "" };
  }
}

/**
 * Keşif vs Gerçekleşen ekranı.
 *
 * Aynı sapma birim fiyatlı işte fırsat, anahtar teslimde zarardır —
 * renk kodu bu ayrımdan çıkar ve sözleşme tipi seçilmeden hiçbir yorum
 * yapılmaz.
 */
export default function MetrajTakipPage() {
  const params = useParams<{ id: string }>();

  const [data, setData] = useState<ProgressTracking | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      setData(await progressTrackingService.get(params.id));
    } catch (loadError) {
      setError(getErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <ErpShell title="Metraj Takip" description="">
        <div className="erp-loading">Keşif–gerçekleşen karşılaştırması hazırlanıyor...</div>
      </ErpShell>
    );
  }

  if (!data) {
    return (
      <ErpShell title="Metraj Takip" description="">
        <div className="erp-alert error">{error || "Proje bulunamadı."}</div>
      </ErpShell>
    );
  }

  const profit = data.profitEstimate;

  return (
    <ErpShell
      title={`Metraj Takip — ${data.projectCode}`}
      description={`${data.projectName} · ${data.contractTypeName}`}
    >
      <div className="erp-toolbar">
        <div>
          <strong>Keşif vs Gerçekleşen</strong>
          <small>Sözleşme metrajı kaynağı: {data.baselineSource}</small>
        </div>

        <Link href={`/projeler/${data.projectId}`}>Proje Kartına Dön</Link>
      </div>

      {data.warnings.map((warning) => (
        <div
          key={warning}
          className={`erp-alert ${data.erosionAlarm ? "error" : ""}`}
        >
          {warning}
        </div>
      ))}

      {/* --- ÖZET --- */}
      <div className="erp-form-grid" style={{ marginTop: 18 }}>
        <Stat
          label="Sözleşme Tutarı (metraj)"
          value={money.format(data.totals.contractAmount)}
        />
        <Stat
          label="Gerçekleşen"
          value={money.format(data.totals.realizedAmount)}
          hint={`Fiziksel gerçekleşme %${quantity.format(
            data.totals.physicalCompletionRate
          )}`}
        />
        <Stat
          label="Keşif Üstü"
          value={money.format(data.totals.overrunAmount)}
          tone={data.totals.overrunAmount > 0 ? "warn" : undefined}
        />
        <Stat
          label="Keşif Altı"
          value={money.format(data.totals.underrunAmount)}
        />
        <Stat
          label="Net Sapma"
          value={money.format(data.totals.netDeviationAmount)}
          tone={data.erosionAlarm ? "bad" : undefined}
          hint={`${data.totals.warningItemCount} kalem eşiği aştı`}
        />
      </div>

      {/* --- KÂR TAHMİNİ --- */}
      <div className="erp-form-card" style={{ marginTop: 18, padding: 22 }}>
        <h2 style={{ marginBottom: 10 }}>Güncel Tahmini Kâr</h2>

        {profit.isReliable ? (
          <div style={{ maxWidth: 520 }}>
            <Row label="Sözleşme bedeli" value={money.format(profit.contractAmount)} />
            <Row label="Fiili maliyet" value={money.format(profit.actualCost)} />
            <Row
              label={`Fiziksel gerçekleşme`}
              value={`%${quantity.format(profit.physicalCompletionRate)}`}
            />
            <Row
              label="Tahmini toplam maliyet"
              value={money.format(profit.estimatedTotalCost)}
            />
            <div style={{ borderTop: "2px solid #0f2f38", marginTop: 8, paddingTop: 8 }}>
              <Row
                label="TAHMİNİ KÂR"
                value={`${money.format(profit.estimatedProfit)}  (%${quantity.format(
                  profit.estimatedProfitRate
                )})`}
                bold
              />
            </div>
            <p style={{ marginTop: 10, fontSize: 12, color: "#5f6874" }}>
              Tahmin, fiili maliyetin gerçekleşme oranına bölünmesiyle
              bulunur; gerçekleşme arttıkça isabet artar.
            </p>
          </div>
        ) : (
          <p style={{ fontSize: 13, color: "#5f6874" }}>{profit.unreliableReason}</p>
        )}
      </div>

      {/* --- KALEM TABLOSU --- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>Kalem Bazında Karşılaştırma</h2>
          <p>{data.totals.itemCount} kalem</p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Bölüm</th>
                <th>Poz</th>
                <th>Açıklama</th>
                <th>Br.</th>
                <th className="tabular">Keşif</th>
                <th className="tabular">Gerçekleşen</th>
                <th className="tabular">Kalan</th>
                <th className="tabular">Fark</th>
                <th className="tabular">Fark %</th>
                <th className="tabular">Tutar Etkisi</th>
                <th className="tabular">Stok Sarfı</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && (
                <tr>
                  <td colSpan={12}>Karşılaştırılacak kalem yok.</td>
                </tr>
              )}

              {data.items.map((item) => (
                <ItemRow key={item.positionCode} item={item} />
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </ErpShell>
  );
}

function ItemRow({ item }: { item: TrackingItem }) {
  const style = impactStyle(item.impact);

  return (
    <tr>
      <td>{item.sectionName ?? "-"}</td>
      <td>{item.positionCode}</td>
      <td>
        {item.description}
        {item.exceedsWarningThreshold && (
          <small style={{ color: "#b3261e" }}>
            Keşfin %110&apos;unu aştı
          </small>
        )}
      </td>
      <td>{item.unit}</td>
      <td className="tabular">{quantity.format(item.contractQuantity)}</td>
      <td className="tabular">
        <strong>{quantity.format(item.realizedQuantity)}</strong>
      </td>
      <td className="tabular">{quantity.format(item.remainingQuantity)}</td>
      <td className="tabular">
        {item.deviationQuantity > 0 ? "+" : ""}
        {quantity.format(item.deviationQuantity)}
      </td>
      <td className="tabular">
        {item.contractQuantity > 0
          ? `${item.deviationRate > 0 ? "+" : ""}${quantity.format(item.deviationRate)}`
          : "-"}
      </td>
      <td className="tabular">
        <strong>
          {item.deviationAmount > 0 ? "+" : ""}
          {money.format(item.deviationAmount)}
        </strong>
      </td>
      <td className="tabular">
        {item.issuedStockQuantity !== null && item.issuedStockQuantity !== undefined
          ? quantity.format(item.issuedStockQuantity)
          : "-"}
      </td>
      <td>
        <span className={`erp-status ${style.className}`}>{style.label}</span>
        {style.hint && <small>{style.hint}</small>}
      </td>
    </tr>
  );
}

function Stat({
  label,
  value,
  hint,
  tone,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: "warn" | "bad";
}) {
  const color = tone === "bad" ? "#b3261e" : tone === "warn" ? "#8a6d00" : undefined;

  return (
    <div>
      <span>{label}</span>
      <div style={{ marginTop: 6, fontSize: 20, fontWeight: 700, color }}>
        {value}
      </div>
      {hint && (
        <div style={{ marginTop: 2, fontSize: 12, color: "#5f6874" }}>{hint}</div>
      )}
    </div>
  );
}

function Row({
  label,
  value,
  bold,
}: {
  label: string;
  value: string;
  bold?: boolean;
}) {
  return (
    <div
      style={{
        display: "flex",
        justifyContent: "space-between",
        padding: "4px 0",
        fontWeight: bold ? 700 : 400,
      }}
    >
      <span>{label}</span>
      <span className="tabular">{value}</span>
    </div>
  );
}
