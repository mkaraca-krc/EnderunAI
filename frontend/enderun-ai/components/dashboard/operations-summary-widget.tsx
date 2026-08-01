import Link from "next/link";

type OperationsSummaryWidgetProps = {
  openPurchaseRequests: number;
  openRfqs: number;
  openOrders: number;
  criticalStock: number;
};

type Tone =
  | "positive"
  | "warning"
  | "critical"
  | "neutral";

type OperationRow = {
  label: string;
  value: number;
  href: string;
  tone: Tone;
};

function barWidth(value: number, maximum: number) {
  if (value <= 0 || maximum <= 0) {
    return 4;
  }

  return Math.max(
    8,
    Math.min(100, (value / maximum) * 100)
  );
}

export default function OperationsSummaryWidget({
  openPurchaseRequests,
  openRfqs,
  openOrders,
  criticalStock,
}: OperationsSummaryWidgetProps) {
  const rows: OperationRow[] = [
    {
      label: "Açık Satın Alma Talebi",
      value: openPurchaseRequests,
      href: "/satin-alma",
      tone:
        openPurchaseRequests > 0
          ? "warning"
          : "positive",
    },
    {
      label: "Devam Eden RFQ",
      value: openRfqs,
      href: "/satin-alma/rfq",
      tone:
        openRfqs > 0
          ? "neutral"
          : "positive",
    },
    {
      label: "Açık Sipariş",
      value: openOrders,
      href: "/satin-alma/siparis",
      tone:
        openOrders > 0
          ? "neutral"
          : "positive",
    },
    {
      label: "Kritik Stok",
      value: criticalStock,
      href: "/depo-stok",
      tone:
        criticalStock > 0
          ? "critical"
          : "positive",
    },
  ];

  const maximum = Math.max(
    1,
    ...rows.map((row) => row.value)
  );

  return (
    <section className="erp-panel dashboard-operations-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Operasyon Görünümü</h2>
          <p>Satın alma ve depo süreç özeti</p>
        </div>

        <Link href="/satin-alma/raporlar">
          Detaylı Rapor
        </Link>
      </div>

      <div className="dashboard-summary-list">
        {rows.map((row) => (
          <Link
            className="dashboard-summary-row dashboard-summary-link"
            href={row.href}
            key={row.label}
          >
            <div className="dashboard-summary-heading">
              <span>{row.label}</span>
              <strong>{row.value}</strong>
            </div>

            <div className="dashboard-summary-track">
              <span
                className={`dashboard-summary-bar ${row.tone}`}
                style={{
                  width: `${barWidth(
                    row.value,
                    maximum
                  )}%`,
                }}
              />
            </div>
          </Link>
        ))}
      </div>
    </section>
  );
}
