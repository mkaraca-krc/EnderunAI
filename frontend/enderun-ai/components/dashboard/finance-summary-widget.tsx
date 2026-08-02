import Link from "next/link";

import type { FinanceDashboard } from "@/services/finance-dashboard.service";

type FinanceSummaryWidgetProps = {
  finance: FinanceDashboard | null;
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

function barWidth(value: number, maximum: number) {
  if (maximum <= 0 || value === 0) {
    return 4;
  }

  return Math.max(
    4,
    Math.min(100, (Math.abs(value) / maximum) * 100)
  );
}

export default function FinanceSummaryWidget({
  finance,
}: FinanceSummaryWidgetProps) {
  const rows = [
    {
      label: "Kasa Bakiyesi",
      value: finance?.cashBalance ?? 0,
      tone: "positive",
    },
    {
      label: "Banka Bakiyesi",
      value: finance?.bankBalance ?? 0,
      tone: "positive",
    },
    {
      label: "Cari Alacak",
      value: finance?.receivables ?? 0,
      tone: "positive",
    },
    {
      label: "Cari Borç",
      value: finance?.payables ?? 0,
      tone: "warning",
    },
    {
      label: "Bugünkü Tahsilat",
      value: finance?.todayCollections ?? 0,
      tone: "positive",
    },
    {
      label: "Bugünkü Ödeme",
      value: finance?.todayPayments ?? 0,
      tone: "warning",
    },
    {
      label: "Net Nakit Değişimi",
      value: finance?.netCashChange ?? 0,
      tone:
        (finance?.netCashChange ?? 0) >= 0
          ? "positive"
          : "critical",
    },
  ];

  const maximum = Math.max(
    1,
    ...rows.map((row) => Math.abs(row.value))
  );

  return (
    <section className="erp-panel dashboard-finance-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Finans Görünümü</h2>
          <p>
            {finance
              ? `${finance.companyName} · Güncel finans özeti`
              : "Kasa, banka ve cari hesap özeti"}
          </p>
        </div>

        <Link href="/finans">Finans Merkezi</Link>
      </div>

      {!finance ? (
        <div className="erp-empty-state">
          Finans verileri yükleniyor veya henüz kayıt bulunmuyor.
        </div>
      ) : (
        <>
          <div className="dashboard-summary-list">
            {rows.map((row) => (
              <div
                className="dashboard-summary-row"
                key={row.label}
              >
                <div className="dashboard-summary-heading">
                  <span>{row.label}</span>
                  <strong>{money.format(row.value)}</strong>
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
              </div>
            ))}
          </div>

          <div className="mt-5 grid gap-3 border-t border-white/10 pt-5 sm:grid-cols-3">
            <div>
              <span className="text-xs text-slate-400">
                Hazır Değerler
              </span>
              <strong className="mt-1 block">
                {money.format(finance.totalLiquidAssets)}
              </strong>
            </div>

            <div>
              <span className="text-xs text-slate-400">
                Dönem Geliri
              </span>
              <strong className="mt-1 block">
                {money.format(finance.periodRevenue)}
              </strong>
            </div>

            <div>
              <span className="text-xs text-slate-400">
                Net Kâr / Zarar
              </span>
              <strong className="mt-1 block">
                {money.format(
                  finance.netProfit > 0
                    ? finance.netProfit
                    : -finance.netLoss
                )}
              </strong>
            </div>
          </div>
        </>
      )}
    </section>
  );
}
