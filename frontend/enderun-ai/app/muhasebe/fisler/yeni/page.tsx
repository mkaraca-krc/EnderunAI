"use client";

import Link from "next/link";
import { SearchableSelect } from "@/components/ui";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";

import {
  accountingVoucherService,
  type AccountingVoucherLineRequest,
  type AccountingVoucherType,
  type CreateAccountingVoucherRequest,
} from "@/services/accounting-voucher.service";

import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

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
  companyId: string;
  voucherType: AccountingVoucherType;
  voucherDate: string;
  currencyCode: string;
  exchangeRate: string;
  description: string;
  referenceNumber: string;
};

const today = new Date().toISOString().slice(0, 10);

const initialForm: VoucherForm = {
  companyId: "",
  voucherType: 0,
  voucherDate: today,
  currencyCode: "TRY",
  exchangeRate: "1",
  description: "",
  referenceNumber: "",
};

function createBlankLine(): VoucherLineForm {
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

export default function NewAccountingVoucherPage() {
  const router = useRouter();

  const [form, setForm] =
    useState<VoucherForm>(initialForm);

  const [lines, setLines] = useState<VoucherLineForm[]>([
    createBlankLine(),
    createBlankLine(),
  ]);

  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [accounts, setAccounts] = useState<
    AccountingAccountListItem[]
  >([]);

  const [currentAccounts, setCurrentAccounts] = useState<
    CurrentAccountListItem[]
  >([]);

  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [loadingReferences, setLoadingReferences] =
    useState(false);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    async function loadCompanies() {
      try {
        const result = await companyService.getAll();
        setCompanies(result);

        const company =
          result.find(
            (item) => item.isActive !== false
          ) ?? result[0];

        if (company) {
          setForm((current) => ({
            ...current,
            companyId: company.id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şirketler alınamadı."
        );
      }
    }

    void loadCompanies();
  }, []);

  useEffect(() => {
    async function loadReferences() {
      if (!form.companyId) {
        setAccounts([]);
        setCurrentAccounts([]);
        setProjects([]);
        return;
      }

      setLoadingReferences(true);
      setError("");

      try {
        const [accountResult, currentResult, projectResult] =
          await Promise.all([
            accountingAccountService.getAll({
              companyId: form.companyId,
              isActive: true,
            }),
            currentAccountService.getAll(form.companyId),
            projectService.getAll(form.companyId),
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

        setLines([
          createBlankLine(),
          createBlankLine(),
        ]);
      } catch (err) {
        setAccounts([]);
        setCurrentAccounts([]);
        setProjects([]);

        setError(
          err instanceof Error
            ? err.message
            : "Fiş referans bilgileri alınamadı."
        );
      } finally {
        setLoadingReferences(false);
      }
    }

    void loadReferences();
  }, [form.companyId]);

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
      createBlankLine(),
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

    const payload: CreateAccountingVoucherRequest = {
      companyId: form.companyId,
      voucherType: form.voucherType,
      voucherDate: form.voucherDate,
      currencyCode:
        form.currencyCode.trim().toUpperCase(),
      exchangeRate,
      description:
        form.description.trim() || null,
      referenceNumber:
        form.referenceNumber.trim() || null,
      sourceModule: "MANUAL",
      sourceEntityId: null,
      lines: requestLines,
    };

    try {
      const result =
        await accountingVoucherService.create(
          payload
        );

      router.push(
        `/muhasebe/fisler/${result.id}`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe fişi kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  /**
   * Cari seçenekleri TEK YERDE. Fiş satırı TABLO HÜCRESİNDE olduğu için
   * seçicinin listesi sabit konumlu çiziliyor — `.rw .erp-table-wrap`
   * `overflow: auto` taşıyor ve mutlak konumlu bir liste kırpılırdı.
   */
  const cariOptions = useMemo(
    () =>
      currentAccounts.map((account) => ({
        id: account.id,
        code: account.code,
        title: account.title,
        extra: [account.shortName, account.taxNumber],
      })),
    [currentAccounts]
  );

  return (
    <ErpShell
      design="redwood"
      title="Yeni Muhasebe Fişi"
      description="Borç ve alacak satırlarıyla taslak muhasebe fişi oluşturun"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Fiş Bilgileri</strong>
          <small>
            Fiş numarası kayıt sırasında otomatik oluşturulur.
          </small>
        </div>

        <Link
          href="/muhasebe/fisler"
          className="erp-secondary-button"
          style={{ textDecoration: "none" }}
        >
          Fişlere Dön
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
              <span>Şirket *</span>

              <select
                required
                value={form.companyId}
                disabled={saving}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                  }))
                }
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.code} - {company.name}
                  </option>
                ))}
              </select>
            </label>

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
                placeholder="Fatura, dekont veya belge no"
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
                placeholder="Muhasebe fişi açıklaması"
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
              <small>
                {lines.length} satır
              </small>
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
                        disabled={loadingReferences}
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
                      <SearchableSelect
                        value={line.currentAccountId}
                        onChange={(next) =>
                          updateLine(
                            line.key,
                            "currentAccountId",
                            next
                          )
                        }
                        options={cariOptions}
                        emptyLabel="Cari yok"
                      />
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
                        placeholder="Masraf merkezi"
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
            value={money(totals.debit)}
          />

          <Summary
            label="Toplam Alacak"
            value={money(totals.credit)}
          />

          <Summary
            label="Fark"
            value={money(totals.difference)}
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
            disabled={
              saving ||
              loadingReferences ||
              !form.companyId
            }
          >
            {saving
              ? "Kaydediliyor..."
              : "Taslak Fişi Kaydet"}
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
