import Link from "next/link";

import type { FinanceDashboard } from "@/services/finance-dashboard.service";
import { moneyWhole } from "@/lib/format/turkish";

type FinanceSummaryWidgetProps = {
  finance: FinanceDashboard | null;
};


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
  const unavailable = finance?.unavailableFields ?? [];

  const rows = [
    {
      label: "Kasa Bakiyesi",
      value: finance?.cashBalance ?? 0,
      tone: "positive",
      field: "cashBalance",
    },
    {
      label: "Banka Bakiyesi",
      value: finance?.bankBalance ?? 0,
      tone: "positive",
      field: "bankBalance",
    },
    {
      label: "Cari Alacak",
      value: finance?.receivables ?? 0,
      tone: "positive",
      field: "receivables",
    },
    {
      label: "Cari Borç",
      value: finance?.payables ?? 0,
      tone: "warning",
      field: "payables",
    },
    {
      label: "Bugünkü Tahsilat",
      value: finance?.todayCollections ?? 0,
      tone: "positive",
      field: "todayCollections",
    },
    {
      label: "Bugünkü Ödeme",
      value: finance?.todayPayments ?? 0,
      tone: "warning",
      field: "todayPayments",
    },
    {
      label: "Net Nakit Değişimi",
      value: finance?.netCashChange ?? 0,
      tone:
        (finance?.netCashChange ?? 0) >= 0
          ? "positive"
          : "critical",
      field: "netCashChange",
    },
  ].map((row) => ({
    ...row,
    isPending: unavailable.includes(row.field),
  }));

  const maximum = Math.max(
    1,
    ...rows
      .filter((row) => !row.isPending)
      .map((row) => Math.abs(row.value))
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
                className={`dashboard-summary-row${
                  row.isPending ? " is-pending" : ""
                }`}
                key={row.label}
              >
                <div className="dashboard-summary-heading">
                  <span>{row.label}</span>
                  <strong>
                    {row.isPending
                      ? "Veri henüz yok"
                      : moneyWhole(row.value)}
                  </strong>
                </div>

                <div className="dashboard-summary-track">
                  <span
                    className={`dashboard-summary-bar ${
                      row.isPending ? "pending" : row.tone
                    }`}
                    style={{
                      width: row.isPending
                        ? "100%"
                        : `${barWidth(row.value, maximum)}%`,
                    }}
                  />
                </div>
              </div>
            ))}
          </div>

          {finance.unavailableFieldsMessage && (
            <p className="mt-3 text-xs text-slate-400">
              {finance.unavailableFieldsMessage}
            </p>
          )}

          <div className="mt-5 grid gap-3 border-t border-white/10 pt-5 sm:grid-cols-2 xl:grid-cols-4">
            <div>
              <span className="text-xs text-slate-400">
                Hazır Değerler
              </span>
              <strong className="mt-1 block">
                {unavailable.includes("totalLiquidAssets")
                  ? "Veri henüz yok"
                  : moneyWhole(finance.totalLiquidAssets)}
              </strong>
            </div>

            <div>
              <span className="text-xs text-slate-400">
                Dönem Geliri
              </span>
              <strong className="mt-1 block">
                {moneyWhole(finance.periodRevenue)}
              </strong>
            </div>

            <div>
              <span className="text-xs text-slate-400">
                Dönem Gideri
              </span>
              <strong className="mt-1 block">
                {moneyWhole(finance.periodExpense)}
              </strong>

              {/* KIRILIM: dönem gideri proje + merkez/şube toplamıdır.
                  Yalnız toplam gösterilseydi merkez giderinin dahil
                  olduğu görünmez, rakam yine "eksik mi" diye
                  sorgulanırdı. */}
              <span className="mt-1 block text-xs text-slate-400">
                Proje {moneyWhole(finance.projectExpense)} · Merkez{" "}
                {moneyWhole(finance.centralExpense)}
              </span>
            </div>

            <div>
              <span className="text-xs text-slate-400">
                Net Kâr / Zarar
              </span>
              <strong className="mt-1 block">
                {moneyWhole(
                  finance.netProfit > 0
                    ? finance.netProfit
                    : -finance.netLoss
                )}
              </strong>

              {/* Kredi faizi dönem giderine DAHİL DEĞİL; gerçekleşen
                  ama faaliyet gideri değil. Sıfırsa satır hiç
                  çıkmıyor — sıfır göstermek boş gürültü olurdu. */}
              {finance.financingExpense > 0 && (
                <span className="mt-1 block text-xs text-slate-400">
                  Finansman gideri {moneyWhole(finance.financingExpense)}{" "}
                  (dönem giderine dahil değil)
                </span>
              )}
            </div>
          </div>
        </>
      )}
    </section>
  );
}
