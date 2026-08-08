"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  PURCHASE_RETURN_STATUS,
  purchaseReturnService,
  type PurchaseReturnListItem,
} from "@/services/goods-receipt.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function number(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 2,
  }).format(value);
}

function statusClass(status: number) {
  if (status === PURCHASE_RETURN_STATUS.Completed) return "erp-status green";
  if (status === PURCHASE_RETURN_STATUS.Cancelled) return "erp-status gray";
  if (status === PURCHASE_RETURN_STATUS.Sent) return "erp-status blue";
  return "erp-status orange";
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/**
 * Alış iadeleri — mal kabulde reddedilen/hasarlı miktar için otomatik
 * doğan belgeler.
 *
 * Varsayılan görünüm BEKLEYENler: tedarikçiyle kapanmamış iade,
 * kimsenin bakmadığı sürece sessizce büyüyen borçtur. Kapanmışlar
 * ayrı sekmede duruyor.
 */
export default function PurchaseReturnsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [items, setItems] = useState<PurchaseReturnListItem[]>([]);

  const [openOnly, setOpenOnly] = useState(true);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setItems(
        await purchaseReturnService.getAll({
          companyId,
          openOnly: openOnly || undefined,
        })
      );
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId, openOnly]);

  useEffect(() => {
    void (async () => {
      try {
        const rows = await companyService.getAll();
        setCompanies(rows);

        const first = rows.find((x) => x.isActive !== false) ?? rows[0];
        if (first) setCompanyId((current) => current || first.id);
      } catch (err) {
        setError(messageOf(err));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  async function advance(row: PurchaseReturnListItem, status: number) {
    let note: string | null = null;

    if (status === PURCHASE_RETURN_STATUS.Cancelled) {
      note = (window.prompt("İptal gerekçesi (zorunlu):") ?? "").trim();

      if (!note) {
        setError(
          "İptal gerekçesi zorunludur; reddedilmiş mal sessizce kaybolmamalı."
        );
        return;
      }
    }

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await purchaseReturnService.advance(row.id, status, note);
      setNotice(result.message);
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  const openCount = items.filter(
    (x) =>
      x.status === PURCHASE_RETURN_STATUS.Draft ||
      x.status === PURCHASE_RETURN_STATUS.Sent
  ).length;

  const openAmount = items
    .filter(
      (x) =>
        x.status === PURCHASE_RETURN_STATUS.Draft ||
        x.status === PURCHASE_RETURN_STATUS.Sent
    )
    .reduce((sum, x) => sum + x.totalAmount, 0);

  return (
    <ErpShell
      title="Alış İadeleri"
      description="Mal kabulde reddedilen ve hasarlı gelen malın tedarikçiye iadesi"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-page-toolbar">
        <select
          value={companyId}
          onChange={(event) => setCompanyId(event.target.value)}
        >
          {companies.map((company) => (
            <option key={company.id} value={company.id}>
              {company.code} · {company.name}
            </option>
          ))}
        </select>

        <button
          type="button"
          className={openOnly ? "erp-primary-button" : "erp-secondary-button"}
          onClick={() => setOpenOnly(true)}
        >
          Bekleyen İadeler
        </button>

        <button
          type="button"
          className={openOnly ? "erp-secondary-button" : "erp-primary-button"}
          onClick={() => setOpenOnly(false)}
        >
          Tümü
        </button>

        <Link className="erp-secondary-button" href="/depo-stok/mal-kabul">
          Mal Kabul
        </Link>
      </div>

      <div className="erp-quick-grid">
        <div className="erp-panel">
          <small style={{ display: "block" }}>Bekleyen İade</small>
          <strong style={{ fontSize: 22 }}>{openCount}</strong>
          <small style={{ display: "block" }}>
            tedarikçiyle kapanmamış belge
          </small>
        </div>

        <div className="erp-panel">
          <small style={{ display: "block" }}>Bekleyen Tutar</small>
          <strong style={{ fontSize: 22 }}>{money(openAmount)}</strong>
          <small style={{ display: "block" }}>alım fiyatı üzerinden</small>
        </div>
      </div>

      <section className="erp-table-card" style={{ marginTop: 16 }}>
        <div className="erp-table-header">
          <h2>{openOnly ? "Bekleyen İadeler" : "Tüm İadeler"}</h2>
          <small>{items.length} kayıt</small>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <strong>
              {openOnly
                ? "Bekleyen iade yok"
                : "Bu şirkette alış iadesi bulunmuyor"}
            </strong>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>İade No</th>
                  <th>Tedarikçi</th>
                  <th>Mal Kabul</th>
                  <th>Proje</th>
                  <th>Miktar</th>
                  <th>Tutar</th>
                  <th>Durum</th>
                  <th>İşlem</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <strong>{row.returnNumber}</strong>
                      <small style={{ display: "block" }}>
                        {dateFormat.format(new Date(row.returnDate))}
                      </small>
                    </td>
                    <td>{row.supplierName}</td>
                    <td>
                      <Link href={`/depo-stok/mal-kabul/${row.goodsReceiptId}`}>
                        {row.receiptNumber}
                      </Link>
                      <small style={{ display: "block" }}>
                        sipariş {row.orderNumber}
                      </small>
                    </td>
                    <td>
                      {row.projectCode}
                      <small style={{ display: "block" }}>
                        {row.projectName}
                      </small>
                    </td>
                    <td>
                      {number(row.totalQuantity)}
                      <small style={{ display: "block" }}>
                        {row.itemCount} kalem
                      </small>
                    </td>
                    <td>{money(row.totalAmount, row.currencyCode)}</td>
                    <td>
                      <span className={statusClass(row.status)}>
                        {row.statusName}
                      </span>
                    </td>
                    <td>
                      <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                        <Link
                          className="erp-secondary-button"
                          href={`/depo-stok/iadeler/${row.id}`}
                        >
                          Aç
                        </Link>

                        {row.status === PURCHASE_RETURN_STATUS.Draft && (
                          <button
                            type="button"
                            className="erp-primary-button"
                            disabled={busy}
                            onClick={() =>
                              advance(row, PURCHASE_RETURN_STATUS.Sent)
                            }
                          >
                            Tedarikçiye Gönderildi
                          </button>
                        )}

                        {row.status === PURCHASE_RETURN_STATUS.Sent && (
                          <button
                            type="button"
                            className="erp-primary-button"
                            disabled={busy}
                            onClick={() =>
                              advance(row, PURCHASE_RETURN_STATUS.Completed)
                            }
                          >
                            Kapat
                          </button>
                        )}

                        {(row.status === PURCHASE_RETURN_STATUS.Draft ||
                          row.status === PURCHASE_RETURN_STATUS.Sent) && (
                          <button
                            type="button"
                            className="erp-secondary-button"
                            disabled={busy}
                            onClick={() =>
                              advance(row, PURCHASE_RETURN_STATUS.Cancelled)
                            }
                          >
                            İptal
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ErpShell>
  );
}
