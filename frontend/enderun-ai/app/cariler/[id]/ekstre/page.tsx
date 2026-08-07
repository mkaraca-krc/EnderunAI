"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  currentAccountService,
  type CurrentAccountStatement,
  type CurrentAccountValuation,
} from "@/services/current-account.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const rateFormat = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
});

/**
 * Döviz tutarını kendi para biriminde biçimler. Bilinmeyen bir kod
 * gelirse Intl hata fırlatır; o durumda sayı + kod olarak yazılır,
 * ekran boş kalmaz.
 */
const currencyFormatters = new Map<string, Intl.NumberFormat>();

function formatCurrency(value: number, code: string) {
  if (code === "TRY") return money.format(value);

  let formatter = currencyFormatters.get(code);

  if (!formatter) {
    try {
      formatter = new Intl.NumberFormat("tr-TR", {
        style: "currency",
        currency: code,
      });
    } catch {
      formatter = new Intl.NumberFormat("tr-TR", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
    }
    currencyFormatters.set(code, formatter);
  }

  const text = formatter.format(value);

  return text.includes(code) || formatter.resolvedOptions().style === "currency"
    ? text
    : `${text} ${code}`;
}

/** Bakiye yönü: pozitif borç (bize borçlu), negatif alacak (biz borçluyuz). */
function balanceLabel(value: number) {
  if (value === 0) return "Kapalı";
  return value > 0 ? "Borç" : "Alacak";
}

function todayIso() {
  return new Date().toISOString().slice(0, 10);
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
  const [currency, setCurrency] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [currencyOptions, setCurrencyOptions] = useState<string[]>([]);

  const [valuation, setValuation] = useState<CurrentAccountValuation | null>(null);
  const [valuationDate, setValuationDate] = useState(todayIso);
  const [valuationError, setValuationError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await currentAccountService.getStatement(accountId, {
        startDate: startDate || undefined,
        endDate: endDate || undefined,
        currency: currency || undefined,
      });

      setStatement(result);

      // Filtre listesi yalnızca FİLTRESİZ ekstreden kurulur: tek dövize
      // indikten sonra özetten kurarsak diğer para birimleri listeden
      // düşer ve kullanıcı "Tümü"ne geri dönemez.
      if (!currency) {
        setCurrencyOptions(
          (result.currencySummary ?? []).map((x) => x.currencyCode)
        );
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Ekstre alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [accountId, startDate, endDate, currency]);

  useEffect(() => {
    void load();
  }, [load]);

  const hasForeignCurrency = statement?.hasForeignCurrency ?? false;

  // Değerleme yalnızca dövizli caride anlamlı; TL cariye boşuna
  // istek atmıyoruz.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      if (!hasForeignCurrency) {
        if (!cancelled) {
          setValuation(null);
          setValuationError("");
        }
        return;
      }

      try {
        const result = await currentAccountService.getCurrencyValuation(
          accountId,
          valuationDate || undefined
        );
        if (!cancelled) {
          setValuation(result);
          setValuationError("");
        }
      } catch (err) {
        if (!cancelled) {
          setValuation(null);
          setValuationError(
            err instanceof Error ? err.message : "Değerleme alınamadı."
          );
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [accountId, hasForeignCurrency, valuationDate]);

  const summaries = useMemo(
    () => statement?.currencySummary ?? [],
    [statement]
  );

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
          {currencyOptions.length > 1 && (
            <label>
              <span style={{ display: "block", fontSize: "11px" }}>Para Birimi</span>
              <select
                value={currency}
                onChange={(e) => setCurrency(e.target.value)}
              >
                <option value="">Tümü</option>
                {currencyOptions.map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </select>
            </label>
          )}
          {(startDate || endDate || currency) && (
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => {
                setStartDate("");
                setEndDate("");
                setCurrency("");
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

          {hasForeignCurrency && summaries.length > 0 && (
            <div className="erp-table-card" style={{ marginTop: 16 }}>
              <div className="erp-table-header">
                <h2>Para Birimi Bazında Bakiye</h2>
                <small>
                  TL sütunu defter değeridir: her hareket kendi günündeki
                  kurla çevrilmiştir
                </small>
              </div>
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Para Birimi</th>
                      <th>Devir</th>
                      <th>Dönem Borç</th>
                      <th>Dönem Alacak</th>
                      <th>Kapanış</th>
                      <th>Kapanış (TL defter)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summaries.map((row) => (
                      <tr key={row.currencyCode}>
                        <td>
                          <strong>{row.currencyCode}</strong>
                        </td>
                        <td>
                          {formatCurrency(row.openingBalance, row.currencyCode)}
                        </td>
                        <td>
                          {formatCurrency(row.periodDebit, row.currencyCode)}
                        </td>
                        <td>
                          {formatCurrency(row.periodCredit, row.currencyCode)}
                        </td>
                        <td>
                          <strong>
                            {formatCurrency(
                              Math.abs(row.closingBalance),
                              row.currencyCode
                            )}
                          </strong>
                          <small>{balanceLabel(row.closingBalance)}</small>
                        </td>
                        <td>{money.format(row.closingBalanceLocal)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {hasForeignCurrency && (
            <div className="erp-table-card" style={{ marginTop: 16 }}>
              <div className="erp-table-header">
                <h2>Kur Değerlemesi</h2>
                <label style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ fontSize: "11px" }}>Değerleme tarihi</span>
                  <input
                    type="date"
                    value={valuationDate}
                    onChange={(e) => setValuationDate(e.target.value)}
                  />
                </label>
              </div>

              {valuationError ? (
                <div className="erp-alert error">{valuationError}</div>
              ) : !valuation ? (
                <div className="erp-loading">Değerleme hesaplanıyor...</div>
              ) : valuation.currencies.length === 0 ? (
                <div className="erp-empty-state">
                  <strong>Değerlenecek döviz bakiyesi yok</strong>
                </div>
              ) : (
                <>
                  <div className="erp-table-wrap">
                    <table className="erp-table">
                      <thead>
                        <tr>
                          <th>Para Birimi</th>
                          <th>Bakiye</th>
                          <th>Defter Değeri (TL)</th>
                          <th>Kur</th>
                          <th>Değerlenmiş (TL)</th>
                          <th>Fark</th>
                        </tr>
                      </thead>
                      <tbody>
                        {valuation.currencies.map((row) => (
                          <tr key={row.currencyCode}>
                            <td>
                              <strong>{row.currencyCode}</strong>
                            </td>
                            <td>
                              {formatCurrency(row.balance, row.currencyCode)}
                            </td>
                            <td>{money.format(row.bookValueLocal)}</td>
                            <td>
                              {row.rateAvailable && row.valuationRate != null ? (
                                <>
                                  {rateFormat.format(row.valuationRate)}
                                  <small>{row.rateSource}</small>
                                </>
                              ) : (
                                <span className="erp-status red">Kur yok</span>
                              )}
                            </td>
                            <td>
                              {row.valuedLocal != null
                                ? money.format(row.valuedLocal)
                                : "—"}
                            </td>
                            <td>
                              {row.difference != null ? (
                                <strong>{money.format(row.difference)}</strong>
                              ) : (
                                <small>{row.message}</small>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  <div style={{ padding: "12px 16px" }}>
                    <strong>
                      Toplam kur farkı: {money.format(valuation.totalDifference)}
                    </strong>
                    {valuation.hasMissingRate && (
                      <div className="erp-alert warning" style={{ marginTop: 8 }}>
                        Kuru bulunamayan döviz var; toplam eksiktir. Kur
                        arşivi tamamlanana kadar bu rakam tam değildir.
                      </div>
                    )}
                    <p style={{ marginTop: 8, fontSize: 12 }}>
                      Bu fark gerçekleşmemiş kur farkıdır; defteri
                      değiştirmez ve muhasebe fişi kesilmez.
                    </p>
                  </div>
                </>
              )}
            </div>
          )}

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
                      {hasForeignCurrency && <th>Döviz</th>}
                      <th>Borç</th>
                      <th>Alacak</th>
                      <th>Bakiye</th>
                    </tr>
                  </thead>
                  <tbody>
                    {statement.lines.map((line) => {
                      const code = line.currencyCode ?? "TRY";
                      const isForeign = code !== "TRY";

                      return (
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
                          {hasForeignCurrency && (
                            <td>
                              {isForeign ? (
                                <>
                                  {formatCurrency(
                                    (line.debitOriginal ?? 0) -
                                      (line.creditOriginal ?? 0),
                                    code
                                  )}
                                  <small>
                                    kur {rateFormat.format(line.exchangeRate ?? 1)}
                                    {" · bakiye "}
                                    {formatCurrency(
                                      line.runningBalanceOriginal ?? 0,
                                      code
                                    )}
                                  </small>
                                </>
                              ) : (
                                "—"
                              )}
                            </td>
                          )}
                          <td>{line.debit ? money.format(line.debit) : "—"}</td>
                          <td>{line.credit ? money.format(line.credit) : "—"}</td>
                          <td>
                            <strong>
                              {money.format(Math.abs(line.runningBalance))}
                            </strong>
                            <small>{balanceLabel(line.runningBalance)}</small>
                          </td>
                        </tr>
                      );
                    })}
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
