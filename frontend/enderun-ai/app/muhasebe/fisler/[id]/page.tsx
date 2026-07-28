"use client";

import Link from "next/link";
import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useParams } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  accountingVoucherService,
  type AccountingVoucherDetail,
  type AccountingVoucherStatus,
  type AccountingVoucherType,
} from "@/services/accounting-voucher.service";

const typeLabels: Record<AccountingVoucherType, string> = {
  0: "Mahsup",
  1: "Tahsil",
  2: "Tediye",
  3: "Açılış",
  4: "Kapanış",
};

const statusLabels: Record<
  AccountingVoucherStatus,
  string
> = {
  0: "Taslak",
  1: "Kesinleşti",
  2: "İptal",
};

const statusClasses: Record<
  AccountingVoucherStatus,
  string
> = {
  0: "yellow",
  1: "green",
  2: "red",
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const number = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const date = new Intl.DateTimeFormat("tr-TR");

export default function AccountingVoucherDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;

  const [item, setItem] =
    useState<AccountingVoucherDetail | null>(null);

  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const result =
        await accountingVoucherService.getById(id);

      setItem(result);
    } catch (err) {
      setItem(null);

      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişi alınamadı."
      );
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  const difference = useMemo(() => {
    if (!item) {
      return 0;
    }

    return item.totalDebit - item.totalCredit;
  }, [item]);

  async function postVoucher() {
    if (!item) {
      return;
    }

    const confirmed = window.confirm(
      `${item.voucherNumber} numaralı muhasebe fişi kesinleştirilsin mi?\n\nKesinleşen fiş artık düzenlenemez.`
    );

    if (!confirmed) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      const result =
        await accountingVoucherService.post(item.id);

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişi kesinleştirilemedi."
      );
    } finally {
      setWorking(false);
    }
  }

  async function cancelVoucher() {
    if (!item) {
      return;
    }

    const reason = window.prompt(
      "Muhasebe fişi iptal gerekçesini yazın:"
    );

    if (!reason?.trim()) {
      return;
    }

    const confirmed = window.confirm(
      `${item.voucherNumber} numaralı muhasebe fişi iptal edilsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      const result =
        await accountingVoucherService.cancel(
          item.id,
          reason.trim()
        );

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişi iptal edilemedi."
      );
    } finally {
      setWorking(false);
    }
  }

  if (loading) {
    return (
      <ErpShell
        title="Muhasebe Fişi"
        description="Fiş bilgileri yükleniyor"
      >
        <div className="erp-form-card">
          Muhasebe fişi yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell
        title="Muhasebe Fişi"
        description="Kayıt bulunamadı"
      >
        {error && (
          <div className="erp-alert error">
            {error}
          </div>
        )}

        <Link
          href="/muhasebe/fisler"
          className="erp-secondary-button"
          style={{ textDecoration: "none" }}
        >
          Muhasebe Fişlerine Dön
        </Link>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title={item.voucherNumber}
      description="Muhasebe fişi detay ve işlem ekranı"
    >
      <div className="erp-toolbar">
        <div>
          <strong>{item.voucherNumber}</strong>

          <small>
            {typeLabels[item.voucherType]} ·{" "}
            {date.format(
              new Date(item.voucherDate)
            )}
          </small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe/fisler"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Fişlere Dön
          </Link>

          {item.status === 0 && (
            <Link
              href={`/muhasebe/fisler/${item.id}/duzenle`}
              className="erp-secondary-button"
              style={{ textDecoration: "none" }}
            >
              Düzenle
            </Link>
          )}

          {item.status === 0 && (
            <button
              type="button"
              className="erp-primary-button"
              disabled={working}
              onClick={() =>
                void postVoucher()
              }
            >
              {working
                ? "İşleniyor..."
                : "Kesinleştir"}
            </button>
          )}

          {item.status !== 2 && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={working}
              onClick={() =>
                void cancelVoucher()
              }
            >
              Fişi İptal Et
            </button>
          )}
        </div>
      </div>

      {message && (
        <div className="erp-alert success">
          {message}
        </div>
      )}

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(190px, 1fr))",
          gap: 12,
          marginBottom: 16,
        }}
      >
        <Summary
          label="Fiş Tipi"
          value={typeLabels[item.voucherType]}
        />

        <Summary
          label="Durum"
          value={statusLabels[item.status]}
          statusClass={statusClasses[item.status]}
        />

        <Summary
          label="Mali Dönem"
          value={`${item.fiscalYear} / ${item.fiscalPeriod}`}
        />

        <Summary
          label="Satır Sayısı"
          value={String(item.lines.length)}
        />
      </section>

      <section className="erp-form-card">
        <div className="erp-form-grid">
          <Info
            label="Fiş Numarası"
            value={item.voucherNumber}
          />

          <Info
            label="Fiş Tarihi"
            value={date.format(
              new Date(item.voucherDate)
            )}
          />

          <Info
            label="Para Birimi"
            value={item.currencyCode}
          />

          <Info
            label="Döviz Kuru"
            value={number.format(item.exchangeRate)}
          />

          <Info
            label="Referans Numarası"
            value={item.referenceNumber ?? "—"}
          />

          <Info
            label="Kaynak Modül"
            value={item.sourceModule ?? "Manuel"}
          />

          <div className="span-2">
            <small>Açıklama</small>

            <strong
              style={{
                display: "block",
                marginTop: 5,
              }}
            >
              {item.description ?? "—"}
            </strong>
          </div>
        </div>
      </section>

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(210px, 1fr))",
          gap: 12,
          margin: "16px 0",
        }}
      >
        <Summary
          label="Toplam Borç"
          value={money.format(item.totalDebit)}
        />

        <Summary
          label="Toplam Alacak"
          value={money.format(item.totalCredit)}
        />

        <Summary
          label="Fark"
          value={money.format(difference)}
        />

        <Summary
          label="Denge"
          value={
            Math.abs(difference) < 0.005
              ? "Dengeli"
              : "Dengesiz"
          }
          statusClass={
            Math.abs(difference) < 0.005
              ? "green"
              : "red"
          }
        />
      </section>

      <section className="erp-table-card">
        <div className="erp-toolbar">
          <div>
            <strong>Fiş Satırları</strong>
            <small>
              {item.lines.length} kayıt
            </small>
          </div>
        </div>

        <div style={{ overflowX: "auto" }}>
          <table
            className="erp-table"
            style={{ minWidth: 1250 }}
          >
            <thead>
              <tr>
                <th>Sıra</th>
                <th>Hesap</th>
                <th>Açıklama</th>
                <th>Cari</th>
                <th>Proje</th>
                <th>Hiyerarşi</th>
                <th>Masraf Merkezi</th>
                <th>Belge No</th>
                <th>Belge Tarihi</th>
                <th>Vade</th>
                <th>Borç</th>
                <th>Alacak</th>
              </tr>
            </thead>

            <tbody>
              {item.lines.map((line) => (
                <tr key={line.id}>
                  <td>{line.lineNumber}</td>

                  <td>
                    <strong>
                      {line.accountCode}
                    </strong>

                    <small>
                      {line.accountName}
                    </small>
                  </td>

                  <td>
                    {line.description ?? "—"}
                  </td>

                  <td>
                    {line.currentAccountTitle ??
                      "—"}
                  </td>

                  <td>
                    {line.projectCode
                      ? `${line.projectCode} - ${line.projectName}`
                      : "—"}
                  </td>

                  <td>
                    {line.projectHierarchyNodeCode
                      ? `${line.projectHierarchyNodeCode} - ${line.projectHierarchyNodeName}`
                      : "—"}
                  </td>

                  <td>
                    {line.costCenterCode ??
                      "—"}
                  </td>

                  <td>
                    {line.documentNumber ??
                      "—"}
                  </td>

                  <td>
                    {line.documentDate
                      ? date.format(
                          new Date(
                            line.documentDate
                          )
                        )
                      : "—"}
                  </td>

                  <td>
                    {line.dueDate
                      ? date.format(
                          new Date(line.dueDate)
                        )
                      : "—"}
                  </td>

                  <td>
                    {money.format(
                      line.debitAmountLocal
                    )}
                  </td>

                  <td>
                    {money.format(
                      line.creditAmountLocal
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {item.status === 1 && (
        <section
          className="erp-form-card"
          style={{ marginTop: 16 }}
        >
          <strong>
            Kesinleştirme Bilgisi
          </strong>

          <div
            style={{
              marginTop: 10,
            }}
          >
            <Info
              label="Kesinleşme Tarihi"
              value={
                item.postedAtUtc
                  ? new Date(
                      item.postedAtUtc
                    ).toLocaleString("tr-TR")
                  : "—"
              }
            />
          </div>
        </section>
      )}

      {item.status === 2 && (
        <section
          className="erp-form-card"
          style={{ marginTop: 16 }}
        >
          <strong>İptal Bilgisi</strong>

          <div
            style={{
              display: "grid",
              gridTemplateColumns:
                "repeat(auto-fit, minmax(220px, 1fr))",
              gap: 12,
              marginTop: 10,
            }}
          >
            <Info
              label="İptal Tarihi"
              value={
                item.cancelledAtUtc
                  ? new Date(
                      item.cancelledAtUtc
                    ).toLocaleString("tr-TR")
                  : "—"
              }
            />

            <Info
              label="İptal Gerekçesi"
              value={
                item.cancellationReason ??
                "—"
              }
            />
          </div>
        </section>
      )}
    </ErpShell>
  );
}

function Summary({
  label,
  value,
  statusClass,
}: {
  label: string;
  value: string;
  statusClass?: string;
}) {
  return (
    <div className="erp-form-card">
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 8,
          fontSize: 22,
        }}
      >
        {value}
      </strong>

      {statusClass && (
        <span
          className={`erp-status ${statusClass}`}
          style={{ marginTop: 8 }}
        >
          {value}
        </span>
      )}
    </div>
  );
}

function Info({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div>
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 5,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
