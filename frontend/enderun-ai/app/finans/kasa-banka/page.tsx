"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, Drawer } from "@/components/ui";
import { date as formatDate, money } from "@/lib/format/turkish";
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

/*
 * Sayı ve tarih biçimi paylaşılan `lib/format/turkish`'ten geliyor.
 * Bu ekran kendi Intl biçimleyicisini kuruyordu ve para birimi simgesi
 * BAŞA geliyordu ("₺1.250,00"); sağa hizalı sütunda öne gelen simge
 * basamakları kaydırıyor, iki satırın rakamları hizalanmıyordu.
 */
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
      design="redwood"
    >
      <div className="erp-toolbar">
        <label className="rw-inline-field">
          <span>Şirket</span>
          <select
            value={companyId}
            onChange={(e) => setCompanyId(e.target.value)}
            aria-label="Şirket seç"
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>
        </label>

        <div className="erp-actions">
          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setShowAccountForm(true)}
          >
            + Yeni Hesap
          </button>
        </div>
      
        <Button variant="secondary" disabled={loading} onClick={() => void loadStatement()}>Yenile</Button>
      </div>

      {/*
        TOPLAM NAKİT AYRI BİR KART: kasa ve banka toplamları küçük
        yazıyla yan yanaydı, ikisinin toplamı hiç yazmıyordu — şirketin
        elindeki nakit, ekrandaki iki sayıyı kafadan toplayarak
        bulunuyordu.
      */}
      <div className="rw-stats">
        <div className="erp-stat-card">
          <span className="erp-stat-label">Kasa</span>
          <strong className="rw-num">{money(totals.cash)}</strong>
          <small>
            {accounts.filter((x) => x.type === CashAccountType.Cash).length} hesap
          </small>
        </div>

        <div className="erp-stat-card">
          <span className="erp-stat-label">Banka</span>
          <strong className="rw-num">{money(totals.bank)}</strong>
          <small>
            {accounts.filter((x) => x.type === CashAccountType.Bank).length} hesap
          </small>
        </div>

        <div className="erp-stat-card rw-stat-accent">
          <span className="erp-stat-label">Toplam nakit</span>
          <strong className="rw-num">{money(totals.cash + totals.bank)}</strong>
          <small>{accounts.length} hesabın güncel bakiyesi</small>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {/*
        Hesap açma formu artık listenin üstünü kaplamıyor: açıldığında
        tablo aşağı kayıyor ve kullanıcı hangi hesapların var olduğunu
        göremeden yeni kod uydurmak zorunda kalıyordu. Panelde liste
        arkada görünür kalıyor.
      */}
      <Drawer
        open={showAccountForm}
        title="Yeni Kasa / Banka Hesabı"
        description="Hesap bir muhasebe hesabına (100 kasa / 102 banka) bağlanır; hareketler oraya işlenir."
        onClose={() => setShowAccountForm(false)}
        busy={saving}
        size="lg"
        footer={
          <div className="flex justify-end gap-3">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setShowAccountForm(false)}
              disabled={saving}
            >
              Vazgeç
            </Button>

            <Button type="submit" form="kasa-hesap-formu" loading={saving}>
              Kaydet
            </Button>
          </div>
        }
      >
        <form id="kasa-hesap-formu" onSubmit={submitAccount}>
          <div className="erp-form-grid">
            <label>
              <span>Tür</span>
              <select
                value={accountForm.type}
                onChange={(e) => setAccountForm({ ...accountForm, type: e.target.value })}
              >
                <option value={String(CashAccountType.Cash)}>Kasa</option>
                <option value={String(CashAccountType.Bank)}>Banka</option>
              </select>
            </label>

            <label>
              <span>Kod</span>
              <input
                required
                value={accountForm.code}
                onChange={(e) => setAccountForm({ ...accountForm, code: e.target.value })}
                placeholder="KASA-001"
              />
            </label>

            <label>
              <span>Ad</span>
              <input
                required
                value={accountForm.name}
                onChange={(e) => setAccountForm({ ...accountForm, name: e.target.value })}
                placeholder="Merkez Kasa"
              />
            </label>

            <label>
              <span>Muhasebe hesabı (100 / 102)</span>
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
                  <span>Banka</span>
                  <input
                    value={accountForm.bankName}
                    onChange={(e) =>
                      setAccountForm({ ...accountForm, bankName: e.target.value })
                    }
                  />
                </label>

                <label>
                  <span>IBAN</span>
                  <input
                    value={accountForm.iban}
                    onChange={(e) => setAccountForm({ ...accountForm, iban: e.target.value })}
                  />
                </label>
              </>
            )}

            <label>
              <span>Açılış bakiyesi</span>
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
        </form>
      </Drawer>

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Hesaplar</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Hesaplar yükleniyor...</div>
        ) : accounts.length === 0 ? (
          <div className="erp-empty-state">
            <div className="erp-empty-icon">₺</div>
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
                  <th className="num">Giren</th>
                  <th className="num">Çıkan</th>
                  <th className="num">Bakiye</th>
                </tr>
              </thead>
              <tbody>
                {accounts.map((account) => (
                  /*
                    SATIR SEÇİLEBİLİR VE KLAVYEYLE ULAŞILABİLİR:
                    yalnızca onClick vardı; klavyeyle gezen kullanıcı
                    hiçbir hesabı seçemiyor, dolayısıyla ekstreyi hiç
                    göremiyordu. Seçili satır artık kalın yazıyla değil,
                    marka rengi şeritle işaretleniyor — kalınlık iki
                    satır arasında fark edilmiyordu.
                  */
                  <tr
                    key={account.id}
                    tabIndex={0}
                    aria-current={account.id === selectedId}
                    className={`rw-selectable ${
                      account.id === selectedId ? "selected" : ""
                    }`}
                    onClick={() => setSelectedId(account.id)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        setSelectedId(account.id);
                      }
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
                    <td className="num">{money(account.totalIn)}</td>
                    <td className="num">{money(account.totalOut)}</td>
                    <td className="num">
                      <strong>{money(account.balance)}</strong>
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
              onClick={() => setShowTransactionForm(true)}
            >
              + Tahsilat / Ödeme
            </button>
          </div>

          <Drawer
            open={showTransactionForm}
            title="Tahsilat / Ödeme"
            description={`${selectedAccount.code} — ${selectedAccount.name}. Kayıt muhasebe fişini de üretir.`}
            onClose={() => setShowTransactionForm(false)}
            busy={saving}
            size="lg"
            footer={
              <div className="flex justify-end gap-3">
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setShowTransactionForm(false)}
                  disabled={saving}
                >
                  Vazgeç
                </Button>

                <Button type="submit" form="kasa-hareket-formu" loading={saving}>
                  Kaydet
                </Button>
              </div>
            }
          >
            <form id="kasa-hareket-formu" onSubmit={submitTransaction}>
              <div className="erp-form-grid">
                <label>
                  <span>Tarih</span>
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
                  <span>İşlem</span>
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
                  <span>Tutar</span>
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
                  <span>Cari</span>
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
                  <span>Proje (opsiyonel)</span>
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
                  <span>Belge no</span>
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
            </form>
          </Drawer>

          {!statement || statement.transactions.length === 0 ? (
            <div className="erp-empty-state">
              <div className="erp-empty-icon">⇄</div>
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
                    <th className="num">Giren</th>
                    <th className="num">Çıkan</th>
                    <th className="num">Bakiye</th>
                  </tr>
                </thead>
                <tbody>
                  {statement.transactions.map((row) => (
                    <tr key={row.id}>
                      <td>{formatDate(row.transactionDate)}</td>
                      <td>{row.transactionTypeName}</td>
                      <td>
                        {row.description}
                        {row.documentNumber && <small>{row.documentNumber}</small>}
                      </td>
                      <td>{row.currentAccountTitle ?? "—"}</td>
                      <td>{row.accountingVoucherNumber ?? "—"}</td>
                      <td className="num">
                        {row.direction === 0 ? money(row.amount) : "—"}
                      </td>
                      <td className="num">
                        {row.direction === 1 ? money(row.amount) : "—"}
                      </td>
                      <td className="num">
                        {money(row.runningBalance)}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={5}>
                      <strong>Dönem toplamı</strong>
                    </td>
                    <td className="num">
                      <strong>{money(statement.totalIn)}</strong>
                    </td>
                    <td className="num">
                      <strong>{money(statement.totalOut)}</strong>
                    </td>
                    <td className="num">
                      <strong>{money(statement.closingBalance)}</strong>
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
