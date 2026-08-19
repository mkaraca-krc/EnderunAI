"use client";

import Link from "next/link";
import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { Button } from "@/components/ui";

import {
  accountingVoucherService,
  type AccountingVoucherListItem,
  type AccountingVoucherStatus,
  type AccountingVoucherType,
} from "@/services/accounting-voucher.service";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

const date = new Intl.DateTimeFormat("tr-TR");

const typeLabels: Record<AccountingVoucherType, string> = {
  0: "Mahsup",
  1: "Tahsil",
  2: "Tediye",
  3: "Açılış",
  4: "Kapanış",
};

const statusLabels: Record<AccountingVoucherStatus, string> = {
  0: "Taslak",
  1: "Kesinleşti",
  2: "İptal",
};

const statusClasses: Record<AccountingVoucherStatus, string> = {
  0: "yellow",
  1: "green",
  2: "red",
};

const columns: DataTableColumn<AccountingVoucherListItem>[] = [
  {
    key: "no",
    header: "Fiş No",
    value: (item) => item.voucherNumber,
    render: (item) => (
      <>
        <strong>{item.voucherNumber}</strong>
        <small>{item.referenceNumber ?? "—"}</small>
      </>
    ),
  },
  {
    key: "tarih",
    header: "Tarih",
    value: (item) => date.format(new Date(item.voucherDate)),
  },
  { key: "tip", header: "Tip", value: (item) => typeLabels[item.voucherType] },
  {
    key: "aciklama",
    header: "Açıklama",
    value: (item) => item.description ?? "—",
  },
  { key: "satir", header: "Satır", numeric: true, value: (item) => item.lineCount },
  {
    key: "borc",
    header: "Borç",
    numeric: true,
    value: (item) => item.totalDebit,
    render: (item) => money(item.totalDebit),
  },
  {
    key: "alacak",
    header: "Alacak",
    numeric: true,
    value: (item) => item.totalCredit,
    render: (item) => money(item.totalCredit),
  },
  {
    key: "durum",
    header: "Durum",
    value: (item) => statusLabels[item.status],
    render: (item) => (
      <span className={`erp-status ${statusClasses[item.status]}`}>
        {statusLabels[item.status]}
      </span>
    ),
  },
  {
    key: "islem",
    header: "İşlem",
    value: () => "",
    render: (item) => (
      <Link
        href={`/muhasebe/fisler/${item.id}`}
        className="erp-secondary-button"
        style={{ textDecoration: "none" }}
      >
        Detay
      </Link>
    ),
  },
];

export default function AccountingVouchersPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [items, setItems] = useState<AccountingVoucherListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [status, setStatus] = useState("");
  const [voucherType, setVoucherType] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);

      setCompanyId(
        result.find((item) => item.isActive !== false)?.id ??
          result[0]?.id ??
          ""
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Şirketler alınamadı."
      );
    }
  }, []);

  const loadItems = useCallback(async () => {
    if (!companyId) {
      setItems([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const result = await accountingVoucherService.getAll({
        companyId,
        status: status === "" ? undefined : Number(status),
        voucherType:
          voucherType === "" ? undefined : Number(voucherType),
        search,
      });

      setItems(result);
    } catch (err) {
      setItems([]);
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişleri alınamadı."
      );
    } finally {
      setLoading(false);
    }
  }, [companyId, search, status, voucherType]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadItems();
  }, [loadItems]);

  const summary = useMemo(() => {
    return {
      count: items.length,
      debit: items.reduce(
        (sum, item) => sum + item.totalDebit,
        0
      ),
      credit: items.reduce(
        (sum, item) => sum + item.totalCredit,
        0
      ),
      draft: items.filter((item) => item.status === 0).length,
    };
  }, [items]);

  return (
    <ErpShell
      design="redwood"
      title="Muhasebe Fişleri"
      description="Mahsup, tahsil, tediye, açılış ve kapanış fişleri"
    >
      <div className="erp-toolbar">
        <div>
          <strong>{summary.count} fiş</strong>
          <small>{summary.draft} taslak kayıt</small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Muhasebe Merkezi
          </Link>

          <Link
            href="/muhasebe/fisler/yeni"
            className="erp-primary-button"
            style={{ textDecoration: "none" }}
          >
            + Yeni Fiş
          </Link>
        </div>
      
        <Button variant="secondary" disabled={loading} onClick={() => void loadItems()}>Yenile</Button>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <section className="erp-form-card">
        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>
            <select
              value={companyId}
              onChange={(event) =>
                setCompanyId(event.target.value)
              }
            >
              <option value="">Şirket seçin</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.code} - {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Fiş Tipi</span>
            <select
              value={voucherType}
              onChange={(event) =>
                setVoucherType(event.target.value)
              }
            >
              <option value="">Tümü</option>
              <option value="0">Mahsup</option>
              <option value="1">Tahsil</option>
              <option value="2">Tediye</option>
              <option value="3">Açılış</option>
              <option value="4">Kapanış</option>
            </select>
          </label>

          <label>
            <span>Durum</span>
            <select
              value={status}
              onChange={(event) =>
                setStatus(event.target.value)
              }
            >
              <option value="">Tümü</option>
              <option value="0">Taslak</option>
              <option value="1">Kesinleşti</option>
              <option value="2">İptal</option>
            </select>
          </label>

          <label>
            <span>Fiş Ara</span>
            <input
              value={search}
              placeholder="Fiş no, açıklama veya referans"
              onChange={(event) =>
                setSearch(event.target.value)
              }
            />
          </label>
        </div>
      </section>

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(200px, 1fr))",
          gap: 12,
          margin: "16px 0",
        }}
      >
        <Summary label="Toplam Fiş" value={summary.count} />
        <Summary
          label="Toplam Borç"
          value={money(summary.debit)}
        />
        <Summary
          label="Toplam Alacak"
          value={money(summary.credit)}
        />
        <Summary
          label="Denge"
          value={money(
            summary.debit - summary.credit
          )}
        />
      </section>

      <div className="erp-table-card">
        <DataTable
          rows={items}
          columns={columns}
          rowKey={(item) => item.id}
          loading={loading}
          title="Muhasebe Fişleri"
          emptyText="Muhasebe fişi bulunmuyor."
          resetKey={`${companyId}|${status}|${voucherType}|${search}`}
        />
      </div>
    </ErpShell>
  );
}

function Summary({
  label,
  value,
}: {
  label: string;
  value: string | number;
}) {
  return (
    <div className="erp-form-card">
      <small>{label}</small>
      <strong
        style={{
          display: "block",
          marginTop: 8,
          fontSize: 24,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
