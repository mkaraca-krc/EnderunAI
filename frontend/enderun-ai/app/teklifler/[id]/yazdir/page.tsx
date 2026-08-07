"use client";

import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import { offerService, type OfferPrintData } from "@/services/offer.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const quantityFormat = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});

function money(value: number, currency: string) {
  try {
    return new Intl.NumberFormat("tr-TR", {
      style: "currency",
      currency,
    }).format(value);
  } catch {
    return `${new Intl.NumberFormat("tr-TR", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value)} ${currency}`;
  }
}

/**
 * Teklifin antetli çıktısı.
 *
 * ErpShell KULLANILMIYOR: menü ve kabuk kağıda basılır, antet
 * kayardı. Sayfa doğrudan yazdırılabilir bir belge olarak kuruluyor
 * ve ekrandaki tek etkileşim yazdırma düğmesi — o da @media print
 * ile gizleniyor.
 */
export default function OfferPrintPage() {
  const params = useParams<{ id: string }>();
  const offerId = params.id;

  const [data, setData] = useState<OfferPrintData | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await offerService.getPrintData(offerId);
        if (!cancelled) setData(result);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Teklif alınamadı.");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [offerId]);

  if (error) {
    return <main style={{ padding: 32 }}>{error}</main>;
  }

  if (!data) {
    return <main style={{ padding: 32 }}>Teklif yükleniyor...</main>;
  }

  const currency = data.currency || "TRY";

  return (
    <main className="offer-print">
      <style>{`
        .offer-print {
          max-width: 210mm;
          margin: 0 auto;
          padding: 24px;
          background: #fff;
          color: #111;
          font-size: 12px;
          line-height: 1.5;
        }
        .offer-print table { width: 100%; border-collapse: collapse; }
        .offer-print th, .offer-print td {
          border: 1px solid #999;
          padding: 6px 8px;
          text-align: left;
          vertical-align: top;
        }
        .offer-print th { background: #f2f2f2; }
        .offer-print td.num, .offer-print th.num {
          text-align: right;
          font-variant-numeric: tabular-nums;
        }
        .offer-print .letterhead {
          display: flex;
          justify-content: space-between;
          gap: 24px;
          border-bottom: 2px solid #111;
          padding-bottom: 12px;
          margin-bottom: 16px;
        }
        .offer-print .meta { display: flex; gap: 32px; margin-bottom: 16px; }
        .offer-print .totals { margin-top: 12px; width: 60mm; margin-left: auto; }
        .offer-print .print-actions { margin-bottom: 16px; }
        @media print {
          .offer-print { padding: 0; max-width: none; }
          .offer-print .print-actions { display: none; }
        }
      `}</style>

      <div className="print-actions">
        <button type="button" onClick={() => window.print()}>
          Yazdır
        </button>
      </div>

      <header className="letterhead">
        <div>
          <strong style={{ fontSize: 16 }}>{data.company.name}</strong>
          {data.company.address && <div>{data.company.address}</div>}
          <div>
            {[data.company.phone, data.company.email]
              .filter(Boolean)
              .join(" · ")}
          </div>
          {(data.company.taxOffice || data.company.taxNumber) && (
            <div>
              {[data.company.taxOffice, data.company.taxNumber]
                .filter(Boolean)
                .join(" V.D. ")}
            </div>
          )}
        </div>

        <div style={{ textAlign: "right" }}>
          <strong style={{ fontSize: 16 }}>TEKLİF</strong>
          <div>No: {data.offerNumber}</div>
          <div>Tarih: {dateFormat.format(new Date(data.offerDate))}</div>
          {data.validUntil && (
            <div>
              Geçerlilik: {dateFormat.format(new Date(data.validUntil))}
            </div>
          )}
        </div>
      </header>

      <h1 style={{ fontSize: 14, margin: "0 0 8px" }}>{data.title}</h1>

      {(data.projectCode || data.projectName) && (
        <div className="meta">
          <div>
            <strong>Proje:</strong> {data.projectCode} {data.projectName}
          </div>
        </div>
      )}

      {data.description && <p>{data.description}</p>}

      <table>
        <thead>
          <tr>
            <th style={{ width: "8mm" }}>#</th>
            <th style={{ width: "22mm" }}>Poz</th>
            <th>Açıklama</th>
            <th style={{ width: "14mm" }}>Birim</th>
            <th className="num" style={{ width: "20mm" }}>
              Miktar
            </th>
            <th className="num" style={{ width: "24mm" }}>
              Birim Fiyat
            </th>
            <th className="num" style={{ width: "28mm" }}>
              Tutar
            </th>
          </tr>
        </thead>
        <tbody>
          {data.items.map((item) => (
            <tr key={item.lineNumber}>
              <td>{item.lineNumber}</td>
              <td>{item.positionNumber ?? "—"}</td>
              <td>
                {item.description}
                {/* Malzeme/montaj ayrımı girilmişse çıktıda da görünmeli:
                    işveren çoğu zaman bu kırılımı ister. */}
                {item.laborUnitPrice > 0 && (
                  <div style={{ fontSize: 10, color: "#555" }}>
                    Malzeme {money(item.materialUnitPrice, currency)} · Montaj{" "}
                    {money(item.laborUnitPrice, currency)}
                    {item.overheadUnitPrice > 0 &&
                      ` · GG ${money(item.overheadUnitPrice, currency)}`}
                  </div>
                )}
              </td>
              <td>{item.unit}</td>
              <td className="num">{quantityFormat.format(item.quantity)}</td>
              <td className="num">{money(item.unitSalesPrice, currency)}</td>
              <td className="num">{money(item.salesTotal, currency)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <table className="totals">
        <tbody>
          <tr>
            <td>
              <strong>Genel Toplam</strong>
            </td>
            <td className="num">
              <strong>{money(data.grandTotal, currency)}</strong>
            </td>
          </tr>
        </tbody>
      </table>

      {data.notes && (
        <section style={{ marginTop: 16 }}>
          <strong>Notlar</strong>
          <p style={{ whiteSpace: "pre-wrap" }}>{data.notes}</p>
        </section>
      )}
    </main>
  );
}
