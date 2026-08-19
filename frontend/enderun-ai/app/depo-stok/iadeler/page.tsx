"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { Button, ConfirmDialog } from "@/components/ui";
import { amount, currencyMoney } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  PURCHASE_RETURN_STATUS,
  purchaseReturnService,
  type PurchaseReturnListItem,
} from "@/services/goods-receipt.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function money(value: number, currency = "TRY") {
  return currencyMoney(value, currency);
}

function number(value: number) {
  return amount(value);
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
  /**
   * Düğme -> uç -> izin (PurchaseReturnsController):
   *   POST purchase-returns/{id}/durum -> purchasing-receipts.edit
   *
   * ÜÇ DÜĞME DE (gönderildi / kapat / iptal) AYNI UCA gidiyor: durum
   * ilerletme tek uçta toplanmış. "İptal yıkıcıdır, delete olmalı"
   * kuralı burada uygulanamaz — uç ayrım yapmıyor ve
   * purchasing-receipts.delete'e bağlamak "gizli ama izinli" üretirdi.
   * Ayrım isteniyorsa önce uç bölünmeli (bkz. TEMIZLIK-TARAMASI.md).
   */
  const actions = useModuleActions("purchasing-receipts");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [items, setItems] = useState<PurchaseReturnListItem[]>([]);

  const [openOnly, setOpenOnly] = useState(true);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [pending, setPending] = useState<{
    row: PurchaseReturnListItem;
    status: number;
  } | null>(null);

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

  async function advance(
    row: PurchaseReturnListItem,
    status: number,
    note: string,
  ) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await purchaseReturnService.advance(
        row.id,
        status,
        note.trim() || null,
      );
      setPending(null);
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


  /* Eylem sütunu duruma ve yetkiye bağlı; dosyaya yazılmaz. */
  const columns = useMemo<DataTableColumn<PurchaseReturnListItem>[]>(
    () => [
      {
        key: "no",
        header: "İade No",
        value: (row) => row.returnNumber,
        render: (row) => (
          <>
            <strong>{row.returnNumber}</strong>
            <small style={{ display: "block" }}>
              {dateFormat.format(new Date(row.returnDate))}
            </small>
          </>
        ),
      },
      { key: "tedarikci", header: "Tedarikçi", value: (row) => row.supplierName },
      {
        key: "malkabul",
        header: "Mal Kabul",
        value: (row) => `${row.receiptNumber} (sipariş ${row.orderNumber})`,
        render: (row) => (
          <>
            <Link href={`/depo-stok/mal-kabul/${row.goodsReceiptId}`}>
              {row.receiptNumber}
            </Link>
            <small style={{ display: "block" }}>sipariş {row.orderNumber}</small>
          </>
        ),
      },
      {
        key: "proje",
        header: "Proje",
        value: (row) => `${row.projectCode} ${row.projectName}`.trim(),
        render: (row) => (
          <>
            {row.projectCode}
            <small style={{ display: "block" }}>{row.projectName}</small>
          </>
        ),
      },
      {
        key: "miktar",
        header: "Miktar",
        numeric: true,
        value: (row) => row.totalQuantity,
        render: (row) => (
          <>
            {number(row.totalQuantity)}
            <small style={{ display: "block" }}>{row.itemCount} kalem</small>
          </>
        ),
      },
      {
        key: "tutar",
        header: "Tutar",
        numeric: true,
        value: (row) => row.totalAmount,
        render: (row) => money(row.totalAmount, row.currencyCode),
      },
      {
        key: "durum",
        header: "Durum",
        value: (row) => row.statusName,
        render: (row) => (
          <span className={statusClass(row.status)}>{row.statusName}</span>
        ),
      },
      {
        key: "islem",
        header: "İşlem",
        value: () => "",
        render: (row) => (
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <Link
              className="erp-secondary-button"
              href={`/depo-stok/iadeler/${row.id}`}
            >
              Aç
            </Link>

            {row.status === PURCHASE_RETURN_STATUS.Draft &&
              actions.can("edit") && (
                <button
                  type="button"
                  className="erp-primary-button"
                  disabled={busy}
                  onClick={() =>
                    setPending({ row, status: PURCHASE_RETURN_STATUS.Sent })
                  }
                >
                  Tedarikçiye Gönderildi
                </button>
              )}

            {row.status === PURCHASE_RETURN_STATUS.Sent &&
              actions.can("edit") && (
                <button
                  type="button"
                  className="erp-primary-button"
                  disabled={busy}
                  onClick={() =>
                    setPending({ row, status: PURCHASE_RETURN_STATUS.Completed })
                  }
                >
                  Kapat
                </button>
              )}

            {(row.status === PURCHASE_RETURN_STATUS.Draft ||
              row.status === PURCHASE_RETURN_STATUS.Sent) &&
              actions.can("edit") && (
                <button
                  type="button"
                  className="erp-secondary-button"
                  disabled={busy}
                  onClick={() =>
                    setPending({ row, status: PURCHASE_RETURN_STATUS.Cancelled })
                  }
                >
                  İptal
                </button>
              )}
          </div>
        ),
      },
    ],
    [actions, busy]
  );


  return (
    <ErpShell
      design="redwood"
      title="Alış İadeleri"
      description="Mal kabulde reddedilen ve hasarlı gelen malın tedarikçiye iadesi"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

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
            <DataTable
              rows={items}
              columns={columns}
              rowKey={(row) => row.id}
              title="Satın Alma İadeleri"
              emptyText="İade kaydı bulunmuyor."
            />
          </div>
        )}
      </section>
      {/*
        Eskiden yalnızca İPTAL gerekçe soruyordu; "Gönderildi" ve
        "Tamamlandı" tek tıkla, onaysız işleniyordu. Üçü de tedarikçiyle
        yapılan mutabakatı değiştiriyor, üçü de onay istiyor.
      */}
      {pending && (
        <ConfirmDialog
          key={`${pending.row.id}-${pending.status}`}
          open
          title={
            pending.status === PURCHASE_RETURN_STATUS.Cancelled
              ? "İade iptal edilsin mi?"
              : pending.status === PURCHASE_RETURN_STATUS.Sent
                ? "İade gönderildi olarak işaretlensin mi?"
                : "İade tamamlandı olarak kapatılsın mı?"
          }
          description={
            pending.status === PURCHASE_RETURN_STATUS.Cancelled
              ? "Reddedilmiş mal sessizce kaybolmamalı: iptal gerekçesi zorunlu."
              : `${pending.row.returnNumber} numaralı iade için bu adım kaydedilir.`
          }
          confirmLabel={
            pending.status === PURCHASE_RETURN_STATUS.Cancelled
              ? "İptal Et"
              : "Onayla"
          }
          requireReason={pending.status === PURCHASE_RETURN_STATUS.Cancelled}
          showReason
          busy={busy}
          onCancel={() => setPending(null)}
          onConfirm={(reason) =>
            void advance(pending.row, pending.status, reason)
          }
        />
      )}

    </ErpShell>
  );
}
