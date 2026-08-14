"use client";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  accountingAccountService,
  type AccountingAccountListItem,
  type AccountingAccountNature,
  type CreateAccountingAccountRequest,
} from "@/services/accounting-account.service";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

type FormState = {
  companyId: string;
  parentAccountId: string;
  code: string;
  name: string;
  description: string;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode: string;
};

const initialForm: FormState = {
  companyId: "",
  parentAccountId: "",
  code: "",
  name: "",
  description: "",
  nature: 0,
  isPostingAllowed: true,
  requiresProject: false,
  requiresCostCenter: false,
  currencyCode: "TRY",
};

export default function NewAccountingAccountPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [accounts, setAccounts] = useState<AccountingAccountListItem[]>([]);
  const [form, setForm] = useState<FormState>(initialForm);

  const [loadingCompanies, setLoadingCompanies] = useState(true);
  const [loadingAccounts, setLoadingAccounts] = useState(false);
  const [saving, setSaving] = useState(false);

  const [error, setError] = useState("");

  useEffect(() => {
    async function loadCompanies() {
      setLoadingCompanies(true);
      setError("");

      try {
        const result = await companyService.getAll();
        setCompanies(result);

        const firstCompany =
          result.find((company) => company.isActive !== false) ??
          result[0];

        if (firstCompany) {
          setForm((current) => ({
            ...current,
            companyId: firstCompany.id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şirketler alınamadı."
        );
      } finally {
        setLoadingCompanies(false);
      }
    }

    void loadCompanies();
  }, []);

  useEffect(() => {
    async function loadAccounts() {
      if (!form.companyId) {
        setAccounts([]);
        return;
      }

      setLoadingAccounts(true);
      setError("");

      try {
        const result = await accountingAccountService.getAll({
          companyId: form.companyId,
          isActive: true,
        });

        setAccounts(result);
      } catch (err) {
        setAccounts([]);
        setError(
          err instanceof Error
            ? err.message
            : "Üst hesaplar alınamadı."
        );
      } finally {
        setLoadingAccounts(false);
      }
    }

    void loadAccounts();
  }, [form.companyId]);

  const selectedParent = useMemo(
    () =>
      accounts.find(
        (account) => account.id === form.parentAccountId
      ) ?? null,
    [accounts, form.parentAccountId]
  );

  const expectedLevel = selectedParent
    ? selectedParent.level + 1
    : 1;

  async function save(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");

    const payload: CreateAccountingAccountRequest = {
      companyId: form.companyId,
      parentAccountId: form.parentAccountId || null,
      code: form.code.trim(),
      name: form.name.trim(),
      description: form.description.trim() || null,
      nature: form.nature,
      isPostingAllowed: form.isPostingAllowed,
      requiresProject: form.requiresProject,
      requiresCostCenter: form.requiresCostCenter,
      currencyCode:
        form.currencyCode.trim().toUpperCase() || null,
    };

    try {
      const result =
        await accountingAccountService.create(payload);

      router.push(
        `/muhasebe/hesap-plani/${result.id}`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe hesabı oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Muhasebe Hesabı"
      description="Hesap planına ana hesap veya alt hesap ekleyin"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Hesap Tanımı</strong>
          <small>
            Hesap kodu aynı şirket içinde benzersiz olmalıdır.
          </small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe/hesap-plani"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Hesap Planına Dön
          </Link>
        </div>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <form className="erp-form-card" onSubmit={save}>
        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>

            <select
              required
              disabled={loadingCompanies || saving}
              value={form.companyId}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  companyId: event.target.value,
                  parentAccountId: "",
                }))
              }
            >
              <option value="">Şirket seçin</option>

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
            <span>Üst Hesap</span>

            <select
              disabled={
                !form.companyId ||
                loadingAccounts ||
                saving
              }
              value={form.parentAccountId}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  parentAccountId: event.target.value,
                }))
              }
            >
              <option value="">
                Ana hesap olarak oluştur
              </option>

              {accounts.map((account) => (
                <option
                  key={account.id}
                  value={account.id}
                >
                  {account.code} - {account.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Hesap Kodu *</span>

            <input
              required
              maxLength={50}
              value={form.code}
              placeholder="Örn: 100.01"
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  code: event.target.value
                    .toUpperCase()
                    .replace(/\s+/g, ""),
                }))
              }
            />
          </label>

          <label>
            <span>Hesap Adı *</span>

            <input
              required
              maxLength={250}
              value={form.name}
              placeholder="Örn: Merkez Kasa"
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  name: event.target.value,
                }))
              }
            />
          </label>

          <label>
            <span>Hesap Karakteri *</span>

            <select
              value={form.nature}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  nature: Number(
                    event.target.value
                  ) as AccountingAccountNature,
                }))
              }
            >
              <option value={0}>Borç</option>
              <option value={1}>Alacak</option>
              <option value={2}>Borç / Alacak</option>
            </select>
          </label>

          <label>
            <span>Para Birimi</span>

            <input
              maxLength={3}
              value={form.currencyCode}
              placeholder="TRY"
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  currencyCode: event.target.value
                    .toUpperCase()
                    .replace(/[^A-Z]/g, ""),
                }))
              }
            />
          </label>

          <label className="span-2">
            <span>Açıklama</span>

            <textarea
              rows={4}
              maxLength={1000}
              value={form.description}
              placeholder="Hesabın kullanım amacı ve açıklaması"
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  description: event.target.value,
                }))
              }
            />
          </label>
        </div>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(auto-fit, minmax(220px, 1fr))",
            gap: 12,
            marginTop: 16,
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              padding: 14,
              border: "1px solid var(--erp-border)",
              borderRadius: 10,
            }}
          >
            <input
              type="checkbox"
              checked={form.isPostingAllowed}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  isPostingAllowed: event.target.checked,
                }))
              }
            />

            <span>
              <strong>Kayıt Yapılabilir</strong>
              <small>
                Muhasebe fişlerinde bu hesaba kayıt girilebilir.
              </small>
            </span>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              padding: 14,
              border: "1px solid var(--erp-border)",
              borderRadius: 10,
            }}
          >
            <input
              type="checkbox"
              checked={form.requiresProject}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  requiresProject: event.target.checked,
                }))
              }
            />

            <span>
              <strong>Proje Zorunlu</strong>
              <small>
                Fiş satırında proje seçimi zorunlu olur.
              </small>
            </span>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              padding: 14,
              border: "1px solid var(--erp-border)",
              borderRadius: 10,
            }}
          >
            <input
              type="checkbox"
              checked={form.requiresCostCenter}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  requiresCostCenter: event.target.checked,
                }))
              }
            />

            <span>
              <strong>Masraf Merkezi Zorunlu</strong>
              <small>
                Fiş satırında masraf merkezi gerekir.
              </small>
            </span>
          </label>
        </section>

        <section
          className="rw-subtle-panel"
          style={{ marginTop: 16 }}
        >
          <strong>Hesap Yapısı Özeti</strong>

          <div
            style={{
              display: "grid",
              gridTemplateColumns:
                "repeat(auto-fit, minmax(180px, 1fr))",
              gap: 10,
              marginTop: 10,
            }}
          >
            <div>
              <small>Seviye</small>
              <strong style={{ display: "block" }}>
                {expectedLevel}
              </strong>
            </div>

            <div>
              <small>Üst Hesap</small>
              <strong style={{ display: "block" }}>
                {selectedParent
                  ? `${selectedParent.code} - ${selectedParent.name}`
                  : "Ana hesap"}
              </strong>
            </div>

            <div>
              <small>Kullanım</small>
              <strong style={{ display: "block" }}>
                {form.isPostingAllowed
                  ? "Kayıt hesabı"
                  : "Grup hesabı"}
              </strong>
            </div>
          </div>
        </section>

        <div className="erp-actions" style={{ marginTop: 18 }}>
          <button
            type="submit"
            className="erp-primary-button"
            disabled={
              saving ||
              !form.companyId ||
              !form.code.trim() ||
              !form.name.trim()
            }
          >
            {saving
              ? "Kaydediliyor..."
              : "Muhasebe Hesabını Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
