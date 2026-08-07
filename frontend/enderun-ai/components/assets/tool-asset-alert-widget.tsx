"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { apiClient } from "@/lib/api/api-client";

type ToolAssetAlertSummary = {
  warrantyExpiringCount: number;
  inServiceCount: number;
  overdueReturnCount: number;
  frequentFailureCount: number;
};

/**
 * Dashboard demirbaş uyarı kartı.
 *
 * Kaynak, Hızır brifingiyle AYNI servistir; iki ekranın farklı sayı
 * göstermesi kullanıcıyı hangisine güveneceği konusunda tereddüde
 * düşürürdü.
 *
 * Uyarı yoksa kart sessizdir — "her şey yolunda" satırı dashboard'u
 * gereksiz doldurur.
 */
export default function ToolAssetAlertWidget() {
  const [summary, setSummary] = useState<ToolAssetAlertSummary | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await apiClient<ToolAssetAlertSummary>("tool-assets/alerts");
        if (!cancelled) setSummary(result);
      } catch {
        // Yetki yoksa kart hiç çizilmez.
        if (!cancelled) setUnavailable(true);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  if (unavailable || !summary) return null;

  const rows: [string, number, string][] = [
    ["Serviste bekleyen", summary.inServiceCount, "/demirbas/servis"],
    ["İadesi geciken zimmet", summary.overdueReturnCount, "/insan-kaynaklari/zimmetler"],
    ["Garantisi bitiyor", summary.warrantyExpiringCount, "/demirbas"],
    ["Sık arızalanan", summary.frequentFailureCount, "/demirbas"],
  ];

  const active = rows.filter(([, count]) => count > 0);

  if (active.length === 0) return null;

  return (
    <article className="erp-panel">
      <header
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "baseline",
          marginBottom: 8,
        }}
      >
        <h3 style={{ margin: 0 }}>Demirbaş</h3>
        <Link href="/demirbas" style={{ fontSize: 12 }}>
          Ayrıntı
        </Link>
      </header>

      <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13 }}>
        {active.map(([label, count, href]) => (
          <li key={label} style={{ marginBottom: 4 }}>
            <Link href={href}>
              {label}: <strong>{count}</strong>
            </Link>
          </li>
        ))}
      </ul>
    </article>
  );
}
