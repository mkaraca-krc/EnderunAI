"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";
import {
  cashAccountService,
  CASH_ACCOUNT_TYPE_LABELS,
  CashAccountType,
  CashTransactionType,
  type CashAccount,
  type CashAccountStatement,
} from "@/services/cash-account.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { projectService, type ProjectListItem } from "@/services/project.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const today = () => new Date().toISOString().slice(0, 10);

export default function CashAccountsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [accounts, setAccounts] = useState<CashAccount[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [statement, setStatement] = useState<CashAccountStatement | null>(null);

  const [currentAccounts, setCurrentAccounts] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [ledgerAccounts, setLedgerAccounts] = useState<AccountingAccountListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [showAccountForm, setShowAccountForm] = useState(false);
  const [showTransactionForm, setShowTransactionForm] = useState(false);
  const [saving, setSaving] = useState(false);

  const [accountForm, setAccountForm] = useState({
    type: String(CashAccountType.Cash),
    code: "",
    name: "",
    bankName: "",
    iban: "",
    openingBalance: "0",
    accountingAccountId: "",
  });

  const [transactionForm, setTransactionForm] = useState({
    transactionDate: today(),
    transactionType: String(CashTransactionType.Collection),
    amount: "",
    description: "",
    documentNumber: "",
    currentAccountId: "",
    projectId: "",
  });

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);
      setCompanyId((current) => current || result[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
    }
  }, []);

  const loadAccounts = useCallback(async () => {
    if (!companyId) {
      setAccounts([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const result = await cashAccountService.getAll({ companyId });
      setAccounts(result);
      setSelectedId((current) =>
        current && result.some((x) => x.id === current) ? current : result[0]?.id ?? ""
      );
    } catch (err) {
      setAccounts([]);
      setError(err instanceof Error ? err.message : "Kasa/banka hesapları alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  const loadStatement = useCallback(async () => {
    if (!selectedId) {
      setStatement(null);
      return;
    }

    try {
      setStatement(await cashAccountService.getStatement(selectedId));
    } catch (err) {
      setStatement(null);
      setError(err instanceof Error ? err.message : "Hesap ekstresi alınamadı.");
    }
  }, [selectedId]);

  const loadLookups = useCallback(async () => {
    if (!companyId) return;

    try {
      const [carilerResult, projectsResult, ledgerResult] = await Promise.all([
        currentAccountService.getAll(companyId),
        projectService.getAll(companyId),
        accountingAccountService.getAll({ companyId, isActive: true }),
      ]);

      setCurrentAccounts(carilerResult);
      setProjects(projectsResult);
      // Kasa/banka hesabı yalnızca 100 ve 102 altına bağlanır.
      setLedgerAccounts(
        ledgerResult.filter(
          (x) =>
            x.isPostingAllowed &&
            (x.code.startsWith("100") || x.code.startsWith("102"))
        )
      );
    } catch {
      // Yardımcı listeler alınamazsa ana ekran çalışmaya devam eder.
    }
  }, [companyId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadAccounts();
  }, [loadAccounts]);

  useEffect(() => {
    void loadStatement();
  }, [loadStatement]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  const totals = useMemo(
    () => ({
      cash: accounts
        .filter((x) => x.type === CashAccountType.Cash)
        .reduce((sum, x) => sum + x.balance, 0),
      bank: accounts
        .filter((x) => x.type === CashAccountType.Bank)
        .reduce((sum, x) => sum + x.balance, 0),
    }),
    [accounts]
  );

  const selectedAccount = accounts.find((x) => x.id === selectedId) ?? null;

  async function submitAccount(event: React.FormEvent) {
    event.preventDefault();
    if (!companyId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await cashAccountService.create({
        companyId,
        type: Number(accountForm.type),
        code: accountForm.code.trim(),
        name: accountForm.name.trim(),
        bankName: accountForm.bankName.trim() || null,
        iban: accountForm.iban.trim() || null,
        currencyCode: "TRY",
        openingBalance: Number(accountForm.openingBalance) || 0,
        accountingAccountId: accountForm.accountingAccountId,
      });

      setNotice("Kasa/banka hesabı oluşturuldu.");
      setShowAccountForm(false);
      setAccountForm({
        type: String(CashAccountType.Cash),
        code: "",
        name: "",
        bankName: "",
        iban: "",
        openingBalance: "0",
        accountingAccountId: "",
      });
      await loadAccounts();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hesap oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  async function submitTransaction(event: React.FormEvent) {
    event.preventDefault();
    if (!selectedId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const type = Number(transactionForm.transactionType);

      await cashAccountService.createTransaction(selectedId, {
        transactionDate: transactionForm.transactionDate,
        transactionType: type,
        direction: type === CashTransactionType.Collection ? 0 : 1,
        amount: Number(transactionForm.amount),
        currencyCode: selectedAccount?.currencyCode ?? "TRY",
        description: transactionForm.description.trim(),
        documentNumber: transactionForm.documentNumber.trim() || null,
        currentAccountId: transactionForm.currentAccountId || null,
        projectId: transactionForm.projectId || null,
      });

      setNotice("Hareket kaydedildi ve muhasebe fişi üretildi.");
      setShowTransactionForm(false);
      setTransactionForm({
        transactionDate: today(),
        transactionType: String(CashTransactionType.Collection),
        amount: "",
        description: "",
        documentNumber: "",
        currentAccountId: "",
        projectId: "",
      });
      await Promise.all([loadAccounts(), loadStatement()]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hareket kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Kasa / Banka"
      description="Kasa ve banka hesapları, hareketleri ve otomatik muhasebe fişleri"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{accounts.length} hesap</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Kasa: {money.format(totals.cash)} · Banka: {money.format(totals.bank)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setShowAccountForm((value) => !value)}
          >
            + Yeni Hesap
          </button>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {showAccountForm && (
        <div className="erp-table-card" style={{ marginBottom: "16px" }}>
          <div className="erp-table-header">
            <h2>Yeni Kasa / Banka Hesabı</h2>
          </div>

          <form onSubmit={submitAccount} style={{ padding: "16px", display: "grid", gap: "12px" }}>
            <div className="erp-form-grid">
              <label>
                Tür
                <select
                  value={accountForm.type}
                  onChange={(e) => setAccountForm({ ...accountForm, type: e.target.value })}
                >
                  <option value={String(CashAccountType.Cash)}>Kasa</option>
                  <option value={String(CashAccountType.Bank)}>Banka</option>
                </select>
              </label>

              <label>
                Kod
                <input
                  required
                  value={accountForm.code}
                  onChange={(e) => setAccountForm({ ...accountForm, code: e.target.value })}
                  placeholder="KASA-001"
                />
              </label>

              <label>
                Ad
                <input
                  required
                  value={accountForm.name}
                  onChange={(e) => setAccountForm({ ...accountForm, name: e.target.value })}
                  placeholder="Merkez Kasa"
                />
              </label>

              <label>
                Muhasebe hesabı (100 / 102)
                <select
                  required
                  value={accountForm.accountingAccountId}
                  onChange={(e) =>
                    setAccountForm({ ...accountForm, accountingAccountId: e.target.value })
                  }
                >
                  <option value="">Seçin...</option>
                  {ledgerAccounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.code} — {account.name}
                    </option>
                  ))}
                </select>
              </label>

              {accountForm.type === String(CashAccountType.Bank) && (
                <>
                  <label>
                    Banka
                    <input
                      value={accountForm.bankName}
                      onChange={(e) =>
                        setAccountForm({ ...accountForm, bankName: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    IBAN
                    <input
                      value={accountForm.iban}
                      onChange={(e) => setAccountForm({ ...accountForm, iban: e.target.value })}
                    />
                  </label>
                </>
              )}

              <label>
                Açılış bakiyesi
                <input
                  type="number"
                  step="0.01"
                  value={accountForm.openingBalance}
                  onChange={(e) =>
                    setAccountForm({ ...accountForm, openingBalance: e.target.value })
                  }
                />
              </label>
            </div>

            <div style={{ display: "flex", gap: "8px" }}>
              <button type="submit" className="erp-primary-button" disabled={saving}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setShowAccountForm(false)}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </div>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Hesaplar</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Hesaplar yükleniyor...</div>
        ) : accounts.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Kasa/banka hesabı yok</strong>
            <p>İlk kasa ya da banka hesabını oluşturarak başlayın.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Ad</th>
                  <th>Tür</th>
                  <th>Muhasebe Hesabı</th>
                  <th style={{ textAlign: "right" }}>Giren</th>
                  <th style={{ textAlign: "right" }}>Çıkan</th>
                  <th style={{ textAlign: "right" }}>Bakiye</th>
                </tr>
              </thead>
              <tbody>
                {accounts.map((account) => (
                  <tr
                    key={account.id}
                    onClick={() => setSelectedId(account.id)}
                    style={{
                      cursor: "pointer",
                      fontWeight: account.id === selectedId ? 600 : undefined,
                    }}
                  >
                    <td>{account.code}</td>
                    <td>
                      {account.name}
                      {account.bankName && <small>{account.bankName}</small>}
                    </td>
                    <td>
                      <span className={`erp-status ${account.type === 1 ? "blue" : "gray"}`}>
                        {CASH_ACCOUNT_TYPE_LABELS[account.type] ?? account.typeName}
                      </span>
                    </td>
                    <td>
                      {account.accountingAccountCode}
                      <small>{account.accountingAccountName}</small>
                    </td>
                    <td style={{ textAlign: "right" }}>{money.format(account.totalIn)}</td>
                    <td style={{ textAlign: "right" }}>{money.format(account.totalOut)}</td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(account.balance)}</strong>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedAccount && (
        <div className="erp-table-card" style={{ marginTop: "16px" }}>
          <div className="erp-table-header">
            <h2>
              {selectedAccount.code} — {selectedAccount.name} hareketleri
            </h2>

            <button
              type="button"
              className="erp-primary-button"
              onClick={() => setShowTransactionForm((value) => !value)}
            >
              + Tahsilat / Ödeme
            </button>
          </div>

          {showTransactionForm && (
            <form
              onSubmit={submitTransaction}
              style={{ padding: "16px", display: "grid", gap: "12px" }}
            >
              <div className="erp-form-grid">
                <label>
                  Tarih
                  <input
                    type="date"
                    required
                    value={transactionForm.transactionDate}
                    onChange={(e) =>
                      setTransactionForm({
                        ...transactionForm,
                        transactionDate: e.target.value,
                      })
                    }
                  />
                </label>

                <label>
                  İşlem
                  <select
                    value={transactionForm.transactionType}
                    onChange={(e) =>
                      setTransactionForm({
                        ...transactionForm,
                        transactionType: e.target.value,
                      })
                    }
                  >
                    <option value={String(CashTransactionType.Collection)}>
                      Tahsilat (para girişi)
                    </option>
                    <option value={String(CashTransactionType.Payment)}>
                      Ödeme (para çıkışı)
                    </option>
                  </select>
                </label>

                <label>
                  Tutar
                  <input
                    type="number"
                    step="0.01"
                    min="0.01"
                    required
                    value={transactionForm.amount}
                    onChange={(e) =>
                      setTransactionForm({ ...transactionForm, amount: e.target.value })
                    }
                  />
                </label>

                <label>
                  Cari
                  <select
                    required
                    value={transactionForm.currentAccountId}
                    onChange={(e) =>
                      setTransactionForm({
                        ...transactionForm,
                        currentAccountId: e.target.value,
                      })
                    }
                  >
                    <option value="">Seçin...</option>
                    {currentAccounts.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.code} — {item.title}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Proje (opsiyonel)
                  <select
                    value={transactionForm.projectId}
                    onChange={(e) =>
                      setTransactionForm({ ...transactionForm, projectId: e.target.value })
                    }
                  >
                    <option value="">—</option>
                    {projects.map((project) => (
                      <option key={project.id} value={project.id}>
                        {project.code} — {project.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Belge no
                  <input
                    value={transactionForm.documentNumber}
                    onChange={(e) =>
                      setTransactionForm({
                        ...transactionForm,
                        documentNumber: e.target.value,
                      })
                    }
                  />
                </label>

                <label style={{ gridColumn: "1 / -1" }}>
                  Açıklama
                  <input
                    required
                    value={transactionForm.description}
                    onChange={(e) =>
                      setTransactionForm({ ...transactionForm, description: e.target.value })
                    }
                  />
                </label>
              </div>

              <div style={{ display: "flex", gap: "8px" }}>
                <button type="submit" className="erp-primary-button" disabled={saving}>
                  {saving ? "Kaydediliyor..." : "Kaydet"}
                </button>
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => setShowTransactionForm(false)}
                >
                  Vazgeç
                </button>
              </div>
            </form>
          )}

          {!statement || statement.transactions.length === 0 ? (
            <div className="erp-empty-state">
              <strong>Hareket yok</strong>
              <p>Bu hesapta henüz kayıtlı bir hareket bulunmuyor.</p>
            </div>
          ) : (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Tarih</th>
                    <th>İşlem</th>
                    <th>Açıklama</th>
                    <th>Cari</th>
                    <th>Fiş</th>
                    <th style={{ textAlign: "right" }}>Giren</th>
                    <th style={{ textAlign: "right" }}>Çıkan</th>
                    <th style={{ textAlign: "right" }}>Bakiye</th>
                  </tr>
                </thead>
                <tbody>
                  {statement.transactions.map((row) => (
                    <tr key={row.id}>
                      <td>{dateFormat.format(new Date(row.transactionDate))}</td>
                      <td>{row.transactionTypeName}</td>
                      <td>
                        {row.description}
                        {row.documentNumber && <small>{row.documentNumber}</small>}
                      </td>
                      <td>{row.currentAccountTitle ?? "—"}</td>
                      <td>{row.accountingVoucherNumber ?? "—"}</td>
                      <td style={{ textAlign: "right" }}>
                        {row.direction === 0 ? money.format(row.amount) : "—"}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {row.direction === 1 ? money.format(row.amount) : "—"}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {money.format(row.runningBalance)}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={5}>
                      <strong>Dönem toplamı</strong>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(statement.totalIn)}</strong>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(statement.totalOut)}</strong>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(statement.closingBalance)}</strong>
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </div>
      )}
    </ErpShell>
  );
}
