"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  currentAccountService,
  type CurrentAccountStatement,
} from "@/services/current-account.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Bakiye yönü: pozitif borç (bize borçlu), negatif alacak (biz borçluyuz). */
function balanceLabel(value: number) {
  if (value === 0) return "Kapalı";
  return value > 0 ? "Borç" : "Alacak";
}

const SOURCE_LABELS: Record<string, string> = {
  SupplierInvoice: "Tedarikçi Faturası",
  ProgressPayment: "Hakediş",
};

export default function CurrentAccountStatementPage() {
  const params = useParams<{ id: string }>();
  const accountId = params.id;

  const [statement, setStatement] = useState<CurrentAccountStatement | null>(null);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      setStatement(
        await currentAccountService.getStatement(accountId, {
          startDate: startDate || undefined,
          endDate: endDate || undefined,
        })
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Ekstre alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [accountId, startDate, endDate]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <ErpShell
      title={
        statement
          ? `Cari Ekstresi — ${statement.currentAccount.title}`
          : "Cari Ekstresi"
      }
      description="Muhasebe defterinden (kesinleşmiş fişler) hesaplanır"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-page-toolbar">
        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", alignItems: "flex-end" }}>
          <label>
            <span style={{ display: "block", fontSize: "11px" }}>Başlangıç</span>
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
            />
          </label>
          <label>
            <span style={{ display: "block", fontSize: "11px" }}>Bitiş</span>
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
            />
          </label>
          {(startDate || endDate) && (
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => {
                setStartDate("");
                setEndDate("");
              }}
            >
              Temizle
            </button>
          )}
        </div>

        <Link className="erp-secondary-button" href="/cariler">
          Cari Kartlara Dön
        </Link>
      </div>

      {loading ? (
        <div className="erp-panel erp-loading">Ekstre yükleniyor...</div>
      ) : !statement ? (
        <div className="erp-panel erp-empty-state">
          <strong>Cari bulunamadı</strong>
        </div>
      ) : (
        <>
          <div className="erp-quick-grid">
            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Devir Bakiyesi</small>
              <strong>{money.format(statement.openingBalance)}</strong>
            </div>
            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Dönem Borç</small>
              <strong>{money.format(statement.periodDebit)}</strong>
            </div>
            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Dönem Alacak</small>
              <strong>{money.format(statement.periodCredit)}</strong>
            </div>
            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Kapanış Bakiyesi</small>
              <strong>
                {money.format(Math.abs(statement.closingBalance))}{" "}
                <span className="erp-status blue">
                  {balanceLabel(statement.closingBalance)}
                </span>
              </strong>
            </div>
          </div>

          <div className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>Hareketler</h2>
              <small>{statement.lineCount} kayıt</small>
            </div>

            {statement.lines.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Bu dönemde hareket yok</strong>
                <p>
                  Cari hareketleri, tedarikçi faturası ve hakediş
                  kesinleştikçe muhasebe fişleri üzerinden otomatik oluşur.
                </p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tarih</th>
                      <th>Fiş No</th>
                      <th>Kaynak</th>
                      <th>Hesap</th>
                      <th>Açıklama</th>
                      <th>Belge</th>
                      <th>Borç</th>
                      <th>Alacak</th>
                      <th>Bakiye</th>
                    </tr>
                  </thead>
                  <tbody>
                    {statement.lines.map((line) => (
                      <tr key={line.id}>
                        <td>{dateFormat.format(new Date(line.voucherDate))}</td>
                        <td>{line.voucherNumber}</td>
                        <td>
                          {line.sourceModule
                            ? (SOURCE_LABELS[line.sourceModule] ?? line.sourceModule)
                            : "Manuel"}
                        </td>
                        <td>
                          {line.accountCode}
                          <small>{line.accountName}</small>
                        </td>
                        <td>
                          {line.description || "—"}
                          {line.projectCode && <small>{line.projectCode}</small>}
                        </td>
                        <td>{line.documentNumber || "—"}</td>
                        <td>{line.debit ? money.format(line.debit) : "—"}</td>
                        <td>{line.credit ? money.format(line.credit) : "—"}</td>
                        <td>
                          <strong>
                            {money.format(Math.abs(line.runningBalance))}
                          </strong>
                          <small>{balanceLabel(line.runningBalance)}</small>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </ErpShell>
  );
}
