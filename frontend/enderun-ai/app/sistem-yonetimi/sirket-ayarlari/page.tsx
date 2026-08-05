"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import PayrollSettingsCard from "@/components/hr/payroll-settings-card";
import { Button, Card, CardContent, Input } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  companySettingsService,
  type CompanySettings,
  type RoleWorkHourWindowItem,
  type RoleWorkHourWindows,
  type UpdateCompanySettingsPayload,
} from "@/services/company-settings.service";
import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";
import {
  financeSettingsService,
  type CompanyFinanceSettings,
} from "@/services/supplier-invoice.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

const DAY_LABELS = [
  "Pazar",
  "Pazartesi",
  "Salı",
  "Çarşamba",
  "Perşembe",
  "Cuma",
  "Cumartesi",
];

type DayDraft = { enabled: boolean; startTime: string; endTime: string };

function toDrafts(windows: RoleWorkHourWindowItem[]): DayDraft[] {
  return Array.from({ length: 7 }, (_, day) => {
    const match = windows.find((w) => w.dayOfWeek === day);
    return match
      ? {
          enabled: true,
          startTime: match.startTime.slice(0, 5),
          endTime: match.endTime.slice(0, 5),
        }
      : { enabled: false, startTime: "08:00", endTime: "19:00" };
  });
}

function RoleWorkHourEditor({
  role,
  onSaved,
}: {
  role: RoleWorkHourWindows;
  onSaved: (message: string) => void;
}) {
  const [drafts, setDrafts] = useState<DayDraft[]>(() => toDrafts(role.windows));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    setDrafts(toDrafts(role.windows));
  }, [role.windows]);

  function updateDay(day: number, patch: Partial<DayDraft>) {
    setDrafts((current) =>
      current.map((item, index) => (index === day ? { ...item, ...patch } : item))
    );
  }

  async function save() {
    setSaving(true);
    setError("");
    try {
      const windows: RoleWorkHourWindowItem[] = drafts
        .map((draft, day) => ({ draft, day }))
        .filter(({ draft }) => draft.enabled)
        .map(({ draft, day }) => ({
          dayOfWeek: day,
          startTime: `${draft.startTime}:00`,
          endTime: `${draft.endTime}:00`,
        }));

      const result = await companySettingsService.updateWorkHourWindows(
        role.id,
        windows
      );
      onSaved(result.message);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 p-4">
      <div className="mb-3 flex items-center justify-between">
        <h4 className="font-semibold text-slate-950">{role.name}</h4>
        <Button type="button" onClick={() => void save()} loading={saving} className="text-xs">
          Kaydet
        </Button>
      </div>
      {error && <p className="mb-2 text-xs text-red-600">{error}</p>}
      <div className="grid gap-2">
        {DAY_LABELS.map((label, day) => {
          const draft = drafts[day];
          return (
            <div key={label} className="flex items-center gap-3 text-sm">
              <label className="flex w-32 items-center gap-2">
                <input
                  type="checkbox"
                  checked={draft.enabled}
                  onChange={(e) => updateDay(day, { enabled: e.target.checked })}
                />
                <span className={draft.enabled ? "text-slate-800" : "text-slate-400"}>
                  {label}
                </span>
              </label>
              <input
                type="time"
                disabled={!draft.enabled}
                value={draft.startTime}
                onChange={(e) => updateDay(day, { startTime: e.target.value })}
                className="rounded-lg border border-slate-300 px-2 py-1.5 text-xs disabled:bg-slate-100 disabled:text-slate-400"
              />
              <span className="text-slate-400">–</span>
              <input
                type="time"
                disabled={!draft.enabled}
                value={draft.endTime}
                onChange={(e) => updateDay(day, { endTime: e.target.value })}
                className="rounded-lg border border-slate-300 px-2 py-1.5 text-xs disabled:bg-slate-100 disabled:text-slate-400"
              />
            </div>
          );
        })}
      </div>
    </div>
  );
}

const emptyForm: UpdateCompanySettingsPayload = {
  name: "",
  tradeName: "",
  taxOffice: "",
  taxNumber: "",
  mersisNumber: "",
  tradeRegistryNumber: "",
  phone: "",
  email: "",
  website: "",
  address: "",
};

export default function CompanySettingsPage() {
  const [company, setCompany] = useState<CompanySettings | null>(null);
  const [form, setForm] = useState<UpdateCompanySettingsPayload>(emptyForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [uploadingLogo, setUploadingLogo] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [newBank, setNewBank] = useState({
    bankName: "",
    iban: "",
    accountHolder: "",
  });
  const [addingBank, setAddingBank] = useState(false);

  const [workHourRoles, setWorkHourRoles] = useState<RoleWorkHourWindows[]>([]);
  const [workHourLoading, setWorkHourLoading] = useState(true);
  const [workHourError, setWorkHourError] = useState("");

  const [financeSettings, setFinanceSettings] = useState<CompanyFinanceSettings | null>(null);
  const [financeAccounts, setFinanceAccounts] = useState<AccountingAccountListItem[]>([]);
  const [financeLoading, setFinanceLoading] = useState(true);
  const [financeSaving, setFinanceSaving] = useState(false);
  const [financeError, setFinanceError] = useState("");

  const [testEmailAddress, setTestEmailAddress] = useState("");
  const [sendingTestEmail, setSendingTestEmail] = useState(false);
  const [testEmailResult, setTestEmailResult] = useState<{
    ok: boolean;
    message: string;
  } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await companySettingsService.get();
      setCompany(result);
      setForm({
        name: result.name,
        tradeName: result.tradeName ?? "",
        taxOffice: result.taxOffice ?? "",
        taxNumber: result.taxNumber ?? "",
        mersisNumber: result.mersisNumber ?? "",
        tradeRegistryNumber: result.tradeRegistryNumber ?? "",
        phone: result.phone ?? "",
        email: result.email ?? "",
        website: result.website ?? "",
        address: result.address ?? "",
      });
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  const loadWorkHourWindows = useCallback(async () => {
    setWorkHourLoading(true);
    setWorkHourError("");
    try {
      const roles = await companySettingsService.getWorkHourWindows();
      setWorkHourRoles(roles);
    } catch (requestError) {
      setWorkHourError(getErrorMessage(requestError));
    } finally {
      setWorkHourLoading(false);
    }
  }, []);

  const loadFinanceSettings = useCallback(async () => {
    setFinanceLoading(true);
    setFinanceError("");
    try {
      const [settings, accounts] = await Promise.all([
        financeSettingsService.get(),
        accountingAccountService.getAll({ isActive: true }),
      ]);
      setFinanceSettings(settings);
      // Yalnız fiş kesilebilen (grup olmayan) hesaplar seçilebilir.
      setFinanceAccounts(accounts.filter((account) => account.isPostingAllowed));
    } catch (requestError) {
      setFinanceError(getErrorMessage(requestError));
    } finally {
      setFinanceLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    void loadWorkHourWindows();
    void loadFinanceSettings();
  }, [load, loadWorkHourWindows, loadFinanceSettings]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");

    try {
      const result = await companySettingsService.update(form);
      setCompany(result.company);
      setNotice(result.message);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  async function handleLogoChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;

    setUploadingLogo(true);
    setError("");

    try {
      await companySettingsService.uploadLogo(file);
      setNotice("Logo güncellendi.");
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setUploadingLogo(false);
    }
  }

  async function submitBankAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!newBank.bankName.trim() || !newBank.iban.trim()) {
      setError("Banka adı ve IBAN zorunludur.");
      return;
    }

    setAddingBank(true);
    setError("");

    try {
      await companySettingsService.addBankAccount({
        bankName: newBank.bankName.trim(),
        iban: newBank.iban.trim(),
        accountHolder: newBank.accountHolder.trim() || undefined,
      });
      setNewBank({ bankName: "", iban: "", accountHolder: "" });
      setNotice("IBAN eklendi.");
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setAddingBank(false);
    }
  }

  async function removeBankAccount(id: string) {
    if (!window.confirm("Bu IBAN kaydını silmek istediğinize emin misiniz?")) {
      return;
    }

    setError("");
    try {
      await companySettingsService.deleteBankAccount(id);
      setNotice("IBAN silindi.");
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    }
  }

  async function saveFinanceSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!financeSettings) return;

    setFinanceSaving(true);
    setFinanceError("");
    try {
      const result = await financeSettingsService.update({
        gmApprovalThresholdTry: financeSettings.gmApprovalThresholdTry,
        threeWayTolerancePercent: financeSettings.threeWayTolerancePercent,
        defaultVatRate: financeSettings.defaultVatRate,
        vatInAccountId: financeSettings.vatInAccountId ?? null,
        vatOutAccountId: financeSettings.vatOutAccountId ?? null,
        salesAccountId: financeSettings.salesAccountId ?? null,
        expenseAccountId: financeSettings.expenseAccountId ?? null,
        payablesAccountId: financeSettings.payablesAccountId ?? null,
        receivablesAccountId: financeSettings.receivablesAccountId ?? null,
        factoringExpenseAccountId: financeSettings.factoringExpenseAccountId ?? null,
        deductionAccountId: financeSettings.deductionAccountId ?? null,
      });
      setFinanceSettings(result.settings);
      setNotice(result.message);
    } catch (requestError) {
      setFinanceError(getErrorMessage(requestError));
    } finally {
      setFinanceSaving(false);
    }
  }

  async function sendTestEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!testEmailAddress.trim()) return;

    setSendingTestEmail(true);
    setTestEmailResult(null);
    try {
      const result = await companySettingsService.sendTestEmail(
        testEmailAddress.trim()
      );
      setTestEmailResult({ ok: true, message: result.message });
    } catch (requestError) {
      setTestEmailResult({ ok: false, message: getErrorMessage(requestError) });
    } finally {
      setSendingTestEmail(false);
    }
  }

  return (
    <ErpShell
      title="Şirket Ayarları"
      description="Kurumsal kimlik: unvan, vergi bilgileri, IBAN listesi ve logo"
    >
      <div className="space-y-6">
        {error && (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        {loading ? (
          <div className="rounded-xl border border-slate-200 bg-white py-16 text-center text-sm text-slate-500">
            Şirket bilgileri yükleniyor...
          </div>
        ) : (
          <div className="grid gap-6 lg:grid-cols-[280px_1fr]">
            <Card>
              <CardContent className="flex flex-col items-center gap-4 p-6">
                <div className="flex h-32 w-32 items-center justify-center overflow-hidden rounded-2xl border border-slate-200 bg-slate-50">
                  {company?.logoUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={company.logoUrl}
                      alt="Şirket logosu"
                      className="h-full w-full object-contain"
                    />
                  ) : (
                    <span className="text-xs text-slate-400">Logo yok</span>
                  )}
                </div>
                <label className="w-full">
                  <span className="sr-only">Logo yükle</span>
                  <input
                    type="file"
                    accept="image/png,image/jpeg,image/webp"
                    onChange={handleLogoChange}
                    disabled={uploadingLogo}
                    className="block w-full text-xs text-slate-500 file:mr-3 file:rounded-lg file:border-0 file:bg-brand-700 file:px-3 file:py-2 file:text-xs file:font-medium file:text-white"
                  />
                </label>
                <p className="text-center text-xs text-slate-400">
                  PDF, e-posta şablonu ve işveren portalı bu logoyu kullanır.
                </p>
              </CardContent>
            </Card>

            <Card>
              <CardContent className="p-6">
                <form onSubmit={submit} className="space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Input
                      label="Unvan"
                      required
                      value={form.name}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, name: e.target.value }))
                      }
                    />
                    <Input
                      label="Ticari Unvan (kısa)"
                      value={form.tradeName ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, tradeName: e.target.value }))
                      }
                    />
                    <Input
                      label="Vergi Dairesi"
                      value={form.taxOffice ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, taxOffice: e.target.value }))
                      }
                    />
                    <Input
                      label="Vergi Kimlik No (VKN)"
                      value={form.taxNumber ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, taxNumber: e.target.value }))
                      }
                    />
                    <Input
                      label="Mersis No"
                      value={form.mersisNumber ?? ""}
                      placeholder="Henüz girilmedi"
                      onChange={(e) =>
                        setForm((c) => ({ ...c, mersisNumber: e.target.value }))
                      }
                    />
                    <Input
                      label="Ticaret Sicil No"
                      value={form.tradeRegistryNumber ?? ""}
                      placeholder="Henüz girilmedi"
                      onChange={(e) =>
                        setForm((c) => ({
                          ...c,
                          tradeRegistryNumber: e.target.value,
                        }))
                      }
                    />
                    <Input
                      label="Telefon"
                      value={form.phone ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, phone: e.target.value }))
                      }
                    />
                    <Input
                      label="E-posta"
                      type="email"
                      value={form.email ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, email: e.target.value }))
                      }
                    />
                    <Input
                      label="Web Sitesi"
                      value={form.website ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, website: e.target.value }))
                      }
                    />
                  </div>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">
                      Adres
                    </span>
                    <textarea
                      className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                      rows={2}
                      value={form.address ?? ""}
                      onChange={(e) =>
                        setForm((c) => ({ ...c, address: e.target.value }))
                      }
                    />
                  </label>
                  <div className="flex justify-end">
                    <Button type="submit" loading={saving}>
                      Kaydet
                    </Button>
                  </div>
                </form>
              </CardContent>
            </Card>

            <Card className="lg:col-span-2">
              <CardContent className="p-6">
                <h3 className="mb-1 font-semibold text-slate-950">
                  Banka Hesapları (IBAN)
                </h3>
                <p className="mb-4 text-sm text-slate-500">
                  Sipariş belgeleri ve e-posta şablonlarında gösterilir.
                </p>

                {(company?.bankAccounts.length ?? 0) > 0 && (
                  <div className="mb-4 grid gap-2">
                    {company!.bankAccounts.map((account) => (
                      <div
                        key={account.id}
                        className="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm"
                      >
                        <div>
                          <strong>{account.bankName}</strong>
                          <span className="ml-2 font-mono text-slate-600">
                            {account.iban}
                          </span>
                          {account.accountHolder && (
                            <span className="ml-2 text-xs text-slate-400">
                              ({account.accountHolder})
                            </span>
                          )}
                        </div>
                        <button
                          type="button"
                          className="text-xs text-red-600 hover:underline"
                          onClick={() => void removeBankAccount(account.id)}
                        >
                          Sil
                        </button>
                      </div>
                    ))}
                  </div>
                )}

                <form
                  onSubmit={submitBankAccount}
                  className="grid gap-3 sm:grid-cols-[1fr_1.4fr_1fr_auto]"
                >
                  <Input
                    placeholder="Banka adı"
                    value={newBank.bankName}
                    onChange={(e) =>
                      setNewBank((c) => ({ ...c, bankName: e.target.value }))
                    }
                  />
                  <Input
                    placeholder="TR.. IBAN"
                    value={newBank.iban}
                    onChange={(e) =>
                      setNewBank((c) => ({ ...c, iban: e.target.value }))
                    }
                  />
                  <Input
                    placeholder="Hesap sahibi (ops.)"
                    value={newBank.accountHolder}
                    onChange={(e) =>
                      setNewBank((c) => ({
                        ...c,
                        accountHolder: e.target.value,
                      }))
                    }
                  />
                  <Button type="submit" loading={addingBank}>
                    + Ekle
                  </Button>
                </form>
              </CardContent>
            </Card>

            <Card className="lg:col-span-2">
              <CardContent className="p-6">
                <h3 className="mb-1 font-semibold text-slate-950">
                  Finans / Muhasebe Ayarları
                </h3>
                <p className="mb-4 text-sm text-slate-500">
                  Otomatik muhasebe fişlerinde kullanılacak varsayılan hesaplar,
                  Genel Müdür onay tutar eşiği ve 3 yönlü kontrol toleransı.
                  Listede yalnızca fiş kesilebilen (grup olmayan) hesaplar yer alır.
                </p>

                {financeError && (
                  <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {financeError}
                  </div>
                )}

                {financeLoading ? (
                  <div className="py-10 text-center text-sm text-slate-500">
                    Finans ayarları yükleniyor...
                  </div>
                ) : !financeSettings ? (
                  <div className="py-10 text-center text-sm text-slate-500">
                    Finans ayarları okunamadı.
                  </div>
                ) : (
                  <form onSubmit={saveFinanceSettings} className="space-y-4">
                    <div className="grid gap-4 sm:grid-cols-3">
                      <Input
                        label="GM Onay Eşiği (TL)"
                        type="number"
                        min={0}
                        step={1000}
                        value={String(financeSettings.gmApprovalThresholdTry)}
                        onChange={(e) =>
                          setFinanceSettings((current) =>
                            current
                              ? { ...current, gmApprovalThresholdTry: Number(e.target.value) }
                              : current
                          )
                        }
                      />
                      <Input
                        label="3 Yönlü Tolerans (%)"
                        type="number"
                        min={0}
                        max={100}
                        step={0.1}
                        value={String(financeSettings.threeWayTolerancePercent)}
                        onChange={(e) =>
                          setFinanceSettings((current) =>
                            current
                              ? { ...current, threeWayTolerancePercent: Number(e.target.value) }
                              : current
                          )
                        }
                      />
                      <Input
                        label="Varsayılan KDV (%)"
                        type="number"
                        min={0}
                        max={100}
                        step={1}
                        value={String(financeSettings.defaultVatRate)}
                        onChange={(e) =>
                          setFinanceSettings((current) =>
                            current
                              ? { ...current, defaultVatRate: Number(e.target.value) }
                              : current
                          )
                        }
                      />
                    </div>

                    <div className="grid gap-4 sm:grid-cols-2">
                      {(
                        [
                          ["expenseAccountId", "Maliyet Hesabı (740)"],
                          ["vatInAccountId", "İndirilecek KDV (191)"],
                          ["payablesAccountId", "Satıcılar (320)"],
                          ["receivablesAccountId", "Alıcılar (120)"],
                          ["salesAccountId", "Yurtiçi Satışlar (600)"],
                          ["vatOutAccountId", "Hesaplanan KDV (391)"],
                          ["factoringExpenseAccountId", "Finansman Gideri (780)"],
                          ["deductionAccountId", "Hakediş Kesintileri (126)"],
                        ] as const
                      ).map(([field, label]) => (
                        <label key={field} className="block">
                          <span className="mb-1.5 block text-sm font-medium text-slate-700">
                            {label}
                          </span>
                          <select
                            className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                            value={financeSettings[field] ?? ""}
                            onChange={(e) =>
                              setFinanceSettings((current) =>
                                current
                                  ? { ...current, [field]: e.target.value || null }
                                  : current
                              )
                            }
                          >
                            <option value="">Seçilmedi</option>
                            {financeAccounts.map((account) => (
                              <option key={account.id} value={account.id}>
                                {account.code} — {account.name}
                              </option>
                            ))}
                          </select>
                        </label>
                      ))}
                    </div>

                    <div className="flex justify-end">
                      <Button type="submit" loading={financeSaving}>
                        Finans Ayarlarını Kaydet
                      </Button>
                    </div>
                  </form>
                )}
              </CardContent>
            </Card>

            <Card className="lg:col-span-2">
              <CardContent className="p-6">
                <h3 className="mb-1 font-semibold text-slate-950">
                  E-posta Entegrasyonu Testi
                </h3>
                <p className="mb-4 text-sm text-slate-500">
                  Brevo API üzerinden gönderim gerçekten çalışıyor mu diye
                  kendinize test e-postası gönderin.
                </p>

                {testEmailResult && (
                  <div
                    className={`mb-4 rounded-xl border px-4 py-3 text-sm ${
                      testEmailResult.ok
                        ? "border-emerald-200 bg-emerald-50 text-emerald-700"
                        : "border-red-200 bg-red-50 text-red-700"
                    }`}
                  >
                    {testEmailResult.message}
                  </div>
                )}

                <form
                  onSubmit={sendTestEmail}
                  className="flex flex-col gap-3 sm:flex-row"
                >
                  <Input
                    type="email"
                    placeholder="test@enderunenerji.com.tr"
                    value={testEmailAddress}
                    onChange={(e) => setTestEmailAddress(e.target.value)}
                    required
                    className="flex-1"
                  />
                  <Button type="submit" loading={sendingTestEmail}>
                    Test E-postası Gönder
                  </Button>
                </form>
              </CardContent>
            </Card>

            <PayrollSettingsCard companyId={company?.id ?? null} />

            <Card className="lg:col-span-2">
              <CardContent className="p-6">
                <h3 className="mb-1 font-semibold text-slate-950">
                  Mesai Saati Pencereleri
                </h3>
                <p className="mb-4 text-sm text-slate-500">
                  Rol bazlı erişim penceresi — Admin ve Genel Müdür rolleri her
                  zaman açıktır ve burada listelenmez. Bir günün onayı kapalıysa
                  o rol o gün sisteme giremez.
                </p>

                {workHourError && (
                  <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {workHourError}
                  </div>
                )}

                {workHourLoading ? (
                  <div className="py-10 text-center text-sm text-slate-500">
                    Mesai pencereleri yükleniyor...
                  </div>
                ) : (
                  <div className="grid gap-4 lg:grid-cols-2">
                    {workHourRoles.map((role) => (
                      <RoleWorkHourEditor
                        key={role.id}
                        role={role}
                        onSaved={(message) => {
                          setNotice(message);
                          void loadWorkHourWindows();
                        }}
                      />
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
