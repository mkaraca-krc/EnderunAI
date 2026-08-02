"use client";

import Link from "next/link";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useParams, useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  accountingVoucherService,
  type AccountingVoucherLineRequest,
  type AccountingVoucherType,
  type UpdateAccountingVoucherRequest,
} from "@/services/accounting-voucher.service";

import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";

import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

type VoucherLineForm = {
  key: string;
  accountingAccountId: string;
  description: string;
  debitAmount: string;
  creditAmount: string;
  currentAccountId: string;
  projectId: string;
  costCenterCode: string;
  documentNumber: string;
  documentDate: string;
  dueDate: string;
};

type VoucherForm = {
  voucherType: AccountingVoucherType;
  voucherDate: string;
  currencyCode: string;
  exchangeRate: string;
  description: string;
  referenceNumber: string;
};

function blankLine(): VoucherLineForm {
  return {
    key: crypto.randomUUID(),
    accountingAccountId: "",
    description: "",
    debitAmount: "",
    creditAmount: "",
    currentAccountId: "",
    projectId: "",
    costCenterCode: "",
    documentNumber: "",
    documentDate: "",
    dueDate: "",
  };
}

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

export default function EditAccountingVoucherPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const id = params.id;

  const [companyId, setCompanyId] = useState("");

  const [form, setForm] = useState<VoucherForm>({
    voucherType: 0,
    voucherDate: "",
    currencyCode: "TRY",
    exchangeRate: "1",
    description: "",
    referenceNumber: "",
  });

  const [lines, setLines] = useState<VoucherLineForm[]>([]);

  const [accounts, setAccounts] = useState<
    AccountingAccountListItem[]
  >([]);

  const [currentAccounts, setCurrentAccounts] = useState<
    CurrentAccountListItem[]
  >([]);

  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const voucher =
          await accountingVoucherService.getById(id);

        if (voucher.status !== 0) {
          throw new Error(
            "Yalnızca taslak muhasebe fişleri düzenlenebilir."
          );
        }

        setCompanyId(voucher.companyId);

        setForm({
          voucherType: voucher.voucherType,
          voucherDate:
            voucher.voucherDate.slice(0, 10),
          currencyCode: voucher.currencyCode,
          exchangeRate: String(voucher.exchangeRate),
          description: voucher.description ?? "",
          referenceNumber:
            voucher.referenceNumber ?? "",
        });

        setLines(
          voucher.lines.map((line) => ({
            key: line.id || crypto.randomUUID(),
            accountingAccountId:
              line.accountingAccountId,
            description: line.description ?? "",
            debitAmount:
              line.debitAmount > 0
                ? String(line.debitAmount)
                : "",
            creditAmount:
              line.creditAmount > 0
                ? String(line.creditAmount)
                : "",
            currentAccountId:
              line.currentAccountId ?? "",
            projectId: line.projectId ?? "",
            costCenterCode:
              line.costCenterCode ?? "",
            documentNumber:
              line.documentNumber ?? "",
            documentDate:
              line.documentDate?.slice(0, 10) ?? "",
            dueDate:
              line.dueDate?.slice(0, 10) ?? "",
          }))
        );

        const [accountResult, currentResult, projectResult] =
          await Promise.all([
            accountingAccountService.getAll({
              companyId: voucher.companyId,
              isActive: true,
            }),
            currentAccountService.getAll(
              voucher.companyId
            ),
            projectService.getAll(voucher.companyId),
          ]);

        setAccounts(
          accountResult.filter(
            (account) => account.isPostingAllowed
          )
        );

        setCurrentAccounts(
          currentResult.filter(
            (account) => account.isActive
          )
        );

        setProjects(projectResult);
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Muhasebe fişi düzenleme bilgileri alınamadı."
        );
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, [id]);

  const totals = useMemo(() => {
    const debit = lines.reduce(
      (sum, line) =>
        sum + (Number(line.debitAmount) || 0),
      0
    );

    const credit = lines.reduce(
      (sum, line) =>
        sum + (Number(line.creditAmount) || 0),
      0
    );

    return {
      debit,
      credit,
      difference: debit - credit,
      balanced:
        debit > 0 &&
        credit > 0 &&
        Math.abs(debit - credit) < 0.005,
    };
  }, [lines]);

  function updateLine(
    key: string,
    field: keyof VoucherLineForm,
    value: string
  ) {
    setLines((current) =>
      current.map((line) =>
        line.key === key
          ? {
              ...line,
              [field]: value,
              ...(field === "debitAmount" &&
              Number(value) > 0
                ? { creditAmount: "" }
                : {}),
              ...(field === "creditAmount" &&
              Number(value) > 0
                ? { debitAmount: "" }
                : {}),
            }
          : line
      )
    );
  }

  function addLine() {
    setLines((current) => [
      ...current,
      blankLine(),
    ]);
  }

  function removeLine(key: string) {
    setLines((current) => {
      if (current.length <= 2) {
        return current;
      }

      return current.filter(
        (line) => line.key !== key
      );
    });
  }

  async function save(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");

    const validLines = lines.filter(
      (line) =>
        line.accountingAccountId &&
        ((Number(line.debitAmount) || 0) > 0 ||
          (Number(line.creditAmount) || 0) > 0)
    );

    if (validLines.length < 2) {
      setError(
        "Muhasebe fişinde en az iki geçerli satır bulunmalıdır."
      );
      setSaving(false);
      return;
    }

    const exchangeRate =
      Number(form.exchangeRate) || 0;

    if (exchangeRate <= 0) {
      setError(
        "Döviz kuru sıfırdan büyük olmalıdır."
      );
      setSaving(false);
      return;
    }

    const requestLines: AccountingVoucherLineRequest[] =
      validLines.map((line) => ({
        accountingAccountId:
          line.accountingAccountId,
        description:
          line.description.trim() || null,
        debitAmount:
          Number(line.debitAmount) || 0,
        creditAmount:
          Number(line.creditAmount) || 0,
        currencyCode:
          form.currencyCode.trim().toUpperCase(),
        exchangeRate,
        currentAccountId:
          line.currentAccountId || null,
        projectId: line.projectId || null,
        costCenterCode:
          line.costCenterCode.trim() || null,
        documentNumber:
          line.documentNumber.trim() || null,
        documentDate:
          line.documentDate || null,
        dueDate: line.dueDate || null,
      }));

    const payload: UpdateAccountingVoucherRequest = {
      voucherType: form.voucherType,
      voucherDate: form.voucherDate,
      currencyCode:
        form.currencyCode.trim().toUpperCase(),
      exchangeRate,
      description:
        form.description.trim() || null,
      referenceNumber:
        form.referenceNumber.trim() || null,
      lines: requestLines,
    };

    try {
      const updated =
        await accountingVoucherService.update(
          id,
          payload
        );

      router.push(
        `/muhasebe/fisler/${updated.id}`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişi güncellenemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <ErpShell
        title="Muhasebe Fişi Düzenle"
        description="Fiş bilgileri yükleniyor"
      >
        <div className="erp-form-card">
          Fiş bilgileri yükleniyor...
        </div>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title="Muhasebe Fişi Düzenle"
      description="Taslak muhasebe fişinin başlık ve satırlarını güncelleyin"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Taslak Fiş Düzenleme</strong>
          <small>
            Kesinleşmiş fişler değiştirilemez.
          </small>
        </div>

        <Link
          href={`/muhasebe/fisler/${id}`}
          className="erp-secondary-button"
          style={{ textDecoration: "none" }}
        >
          Fiş Detayına Dön
        </Link>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <form onSubmit={save}>
        <section className="erp-form-card">
          <div className="erp-form-grid">
            <label>
              <span>Fiş Tipi *</span>

              <select
                value={form.voucherType}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    voucherType: Number(
                      event.target.value
                    ) as AccountingVoucherType,
                  }))
                }
              >
                <option value={0}>Mahsup</option>
                <option value={1}>Tahsil</option>
                <option value={2}>Tediye</option>
                <option value={3}>Açılış</option>
                <option value={4}>Kapanış</option>
              </select>
            </label>

            <label>
              <span>Fiş Tarihi *</span>

              <input
                required
                type="date"
                value={form.voucherDate}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    voucherDate:
                      event.target.value,
                  }))
                }
              />
            </label>

            <label>
              <span>Referans No</span>

              <input
                value={form.referenceNumber}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    referenceNumber:
                      event.target.value,
                  }))
                }
              />
            </label>

            <label>
              <span>Para Birimi *</span>

              <input
                required
                maxLength={3}
                value={form.currencyCode}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    currencyCode:
                      event.target.value
                        .toUpperCase()
                        .replace(/[^A-Z]/g, ""),
                  }))
                }
              />
            </label>

            <label>
              <span>Döviz Kuru *</span>

              <input
                required
                type="number"
                min="0.000001"
                step="0.000001"
                value={form.exchangeRate}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    exchangeRate:
                      event.target.value,
                  }))
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>

              <textarea
                rows={3}
                value={form.description}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    description:
                      event.target.value,
                  }))
                }
              />
            </label>
          </div>
        </section>

        <section
          className="erp-table-card"
          style={{ marginTop: 16 }}
        >
          <div className="erp-toolbar">
            <div>
              <strong>Fiş Satırları</strong>
              <small>{lines.length} satır</small>
            </div>

            <button
              type="button"
              className="erp-secondary-button"
              onClick={addLine}
            >
              + Satır Ekle
            </button>
          </div>

          <div style={{ overflowX: "auto" }}>
            <table
              className="erp-table"
              style={{ minWidth: 1450 }}
            >
              <thead>
                <tr>
                  <th>Sıra</th>
                  <th>Hesap</th>
                  <th>Açıklama</th>
                  <th>Cari</th>
                  <th>Proje</th>
                  <th>Masraf Merkezi</th>
                  <th>Borç</th>
                  <th>Alacak</th>
                  <th>Belge No</th>
                  <th>Belge Tarihi</th>
                  <th>Vade</th>
                  <th></th>
                </tr>
              </thead>

              <tbody>
                {lines.map((line, index) => (
                  <tr key={line.key}>
                    <td>{index + 1}</td>

                    <td>
                      <select
                        required
                        value={
                          line.accountingAccountId
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "accountingAccountId",
                            event.target.value
                          )
                        }
                      >
                        <option value="">
                          Hesap seçin
                        </option>

                        {accounts.map((account) => (
                          <option
                            key={account.id}
                            value={account.id}
                          >
                            {account.code} -{" "}
                            {account.name}
                          </option>
                        ))}
                      </select>
                    </td>

                    <td>
                      <input
                        value={line.description}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "description",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <select
                        value={line.currentAccountId}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "currentAccountId",
                            event.target.value
                          )
                        }
                      >
                        <option value="">
                          Cari yok
                        </option>

                        {currentAccounts.map(
                          (account) => (
                            <option
                              key={account.id}
                              value={account.id}
                            >
                              {account.code} -{" "}
                              {account.title}
                            </option>
                          )
                        )}
                      </select>
                    </td>

                    <td>
                      <select
                        value={line.projectId}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "projectId",
                            event.target.value
                          )
                        }
                      >
                        <option value="">
                          Proje yok
                        </option>

                        {projects.map((project) => (
                          <option
                            key={project.id}
                            value={project.id}
                          >
                            {project.code} -{" "}
                            {project.name}
                          </option>
                        ))}
                      </select>
                    </td>

                    <td>
                      <input
                        value={line.costCenterCode}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "costCenterCode",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        value={line.debitAmount}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "debitAmount",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        value={line.creditAmount}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "creditAmount",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <input
                        value={line.documentNumber}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "documentNumber",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="date"
                        value={line.documentDate}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "documentDate",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="date"
                        value={line.dueDate}
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            "dueDate",
                            event.target.value
                          )
                        }
                      />
                    </td>

                    <td>
                      <button
                        type="button"
                        className="erp-secondary-button"
                        disabled={lines.length <= 2}
                        onClick={() =>
                          removeLine(line.key)
                        }
                      >
                        Sil
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(auto-fit, minmax(210px, 1fr))",
            gap: 12,
            marginTop: 16,
          }}
        >
          <Summary
            label="Toplam Borç"
            value={money.format(totals.debit)}
          />

          <Summary
            label="Toplam Alacak"
            value={money.format(totals.credit)}
          />

          <Summary
            label="Fark"
            value={money.format(totals.difference)}
          />

          <div className="erp-form-card">
            <small>Fiş Dengesi</small>

            <strong
              style={{
                display: "block",
                marginTop: 8,
                fontSize: 21,
              }}
            >
              {totals.balanced
                ? "Dengeli"
                : "Dengesiz"}
            </strong>

            <span
              className={`erp-status ${
                totals.balanced ? "green" : "red"
              }`}
            >
              {totals.balanced
                ? "Kesinleştirilebilir"
                : "Borç ve alacak eşit değil"}
            </span>
          </div>
        </section>

        <div
          className="erp-actions"
          style={{
            justifyContent: "flex-end",
            marginTop: 18,
          }}
        >
          <button
            type="submit"
            className="erp-primary-button"
            disabled={saving || !companyId}
          >
            {saving
              ? "Kaydediliyor..."
              : "Değişiklikleri Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}

function Summary({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="erp-form-card">
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 8,
          fontSize: 23,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
