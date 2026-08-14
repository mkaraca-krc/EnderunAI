"use client";

import Link from "next/link";
import ErpShell from "../../../components/erp/erp-shell";

export default function HrReportsRecoveryPage() {
  return (
    <ErpShell
      design="redwood"
      title="İK Rapor Merkezi"
      description="Rapor ekranı bakım çalışması sonrasında yeniden devreye alınacaktır."
    >
      <section
        style={{
          maxWidth: "760px",
          padding: "28px",
          border: "1px solid var(--erp-border)",
          borderRadius: "14px",
          background: "var(--erp-panel)",
          boxShadow: "0 4px 18px rgba(15, 23, 42, 0.06)",
        }}
      >
        <h2
          style={{
            margin: 0,
            color: "var(--erp-text)",
            fontSize: "22px",
          }}
        >
          İK Rapor Merkezi
        </h2>

        <p
          style={{
            marginTop: "12px",
            color: "var(--erp-muted)",
            lineHeight: 1.7,
          }}
        >
          Rapor sayfasındaki teknik düzenleme sürüyor.
          İnsan Kaynakları modülünün diğer ekranlarını kullanmaya
          devam edebilirsiniz.
        </p>

        <div
          style={{
            display: "flex",
            flexWrap: "wrap",
            gap: "10px",
            marginTop: "22px",
          }}
        >
          <Link
            href="/insan-kaynaklari"
            style={{
              padding: "11px 16px",
              borderRadius: "9px",
              background: "var(--erp-primary)",
              color: "var(--color-on-brand)",
              fontWeight: 700,
              textDecoration: "none",
            }}
          >
            İK Merkezine Dön
          </Link>

          <Link
            href="/insan-kaynaklari/onay-merkezi"
            style={{
              padding: "11px 16px",
              border: "1px solid var(--erp-border)",
              borderRadius: "9px",
              color: "var(--erp-muted)",
              fontWeight: 700,
              textDecoration: "none",
            }}
          >
            Onay Merkezine Git
          </Link>
        </div>
      </section>
    </ErpShell>
  );
}
