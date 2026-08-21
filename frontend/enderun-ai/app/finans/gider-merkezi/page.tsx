"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money, moneyWhole } from "@/lib/format/turkish";
import { Button, ConfirmDialog, Input, Modal, Select } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  financialInstrumentService,
  type CreditCard,
} from "@/services/financial-instrument.service";
import {
  EXPENSE_CENTER_TYPE_VALUE,
  EXPENSE_DOCUMENT_TYPE_VALUE,
  EXPENSE_PAYMENT_METHOD_VALUE,
  expenseService,
  type ExpenseCategory,
  type ExpenseCenter,
  type ExpenseCenterType,
  type ExpenseDuplicateHint,
  type ExpenseEntryList,
  type ExpenseReport,
  type PartnerAccountBalance,
  type RecurringExpenseList,
  type SaveExpenseEntryPayload,
} from "@/services/expense.service";



const dateFormat = new Intl.DateTimeFormat("tr-TR");

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

function monthStart(date: Date) {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));
}

function monthEnd(date: Date) {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0));
}

function iso(date: Date) {
  return date.toISOString().slice(0, 10);
}

/** Merkez anahtarı: tür + kimlik birlikte tekil. */
function centerKey(type: ExpenseCenterType, id: string) {
  return `${type}:${id}`;
}

function parseCenterKey(key: string) {
  const [type, id] = key.split(":");
  return { type: type as ExpenseCenterType, id };
}

const emptyEntryForm = {
  centerKey: "",
  categoryId: "",
  expenseDate: iso(new Date()),
  amount: "",
  description: "",
  paymentMethod: "Bank" as "Bank" | "Cash" | "PartnerAccount" | "CreditCard",
  documentType: "Receipt" as "None" | "Receipt" | "Invoice",
  documentNumber: "",
  partnerAccountId: "",
  creditCardId: "",
};

const emptyTemplateForm = {
  centerKey: "",
  categoryId: "",
  description: "",
  estimatedAmount: "",
  paymentMethod: "Bank" as "Bank" | "Cash",
  startYear: new Date().getUTCFullYear(),
  startMonth: new Date().getUTCMonth() + 1,
  paymentDay: 1,
};

export default function ExpenseCentrePage() {
  const { has, loading: permissionsLoading } = usePermissions();

  const canManage = has("expense.manage");
  const canSeeCash = has("extra_payment.view");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [from, setFrom] = useState(iso(monthStart(new Date())));
  const [to, setTo] = useState(iso(monthEnd(new Date())));

  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [centers, setCenters] = useState<ExpenseCenter[]>([]);
  const [report, setReport] = useState<ExpenseReport | null>(null);
  const [entries, setEntries] = useState<ExpenseEntryList | null>(null);
  const [recurring, setRecurring] = useState<RecurringExpenseList | null>(null);
  const [partners, setPartners] = useState<PartnerAccountBalance[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);

  const [view, setView] = useState<
    "report" | "entries" | "recurring" | "partners"
  >("report");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  const [entryModalOpen, setEntryModalOpen] = useState(false);
  const [entryForm, setEntryForm] = useState(emptyEntryForm);
  const [duplicates, setDuplicates] = useState<ExpenseDuplicateHint[]>([]);

  const [templateModalOpen, setTemplateModalOpen] = useState(false);
  const [templateForm, setTemplateForm] = useState(emptyTemplateForm);

  const [confirmPeriod, setConfirmPeriod] = useState<{
    templateId: string;
    year: number;
    month: number;
    description: string;
    estimated: number;
  } | null>(null);
  const [actualAmount, setActualAmount] = useState("");

  const [deleteTarget, setDeleteTarget] = useState<{ id: string; label: string } | null>(
    null,
  );

  /** Elle giriş listesinde otomatik kategoriler GÖRÜNMEZ. */
  const manualCategories = useMemo(
    () => categories.filter((x) => !x.isAutomaticOnly),
    [categories],
  );

  useEffect(() => {
    void (async () => {
      try {
        const list = await companyService.getAll();
        setCompanies(list);
        if (list.length > 0) setCompanyId(list[0].id);
      } catch {
        setError("Şirket listesi alınamadı.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      const period = new Date(from);

      const [categoryList, centerList, reportData, entryData, recurringData] =
        await Promise.all([
          expenseService.listCategories(companyId),
          expenseService.listCenters(companyId),
          expenseService.getReport(companyId, from, to),
          expenseService.listEntries({ companyId, from, to }),
          expenseService.listRecurring(
            companyId,
            period.getUTCFullYear(),
            period.getUTCMonth() + 1,
          ),
        ]);

      setCategories(categoryList);
      setCenters(centerList);

      // Şahıs carisi extra_payment.view istiyor; yetki yoksa uç 403
      // döner ve sekme boş kalır — sayfanın geri kalanı çalışmaya
      // devam etmeli.
      try {
        setPartners(await expenseService.listPartners(companyId));
      } catch {
        setPartners([]);
      }

      // Kart listesi finance.view istiyor; gider merkezini görüp
      // finansı görmeyen kullanıcıda kart seçeneği boş kalır.
      try {
        setCards(await financialInstrumentService.listCards(companyId));
      } catch {
        setCards([]);
      }

      setReport(reportData);
      setEntries(entryData);
      setRecurring(recurringData);
    } catch (loadError) {
      setError(
        loadError instanceof Error ? loadError.message : "Gider verileri alınamadı.",
      );
    } finally {
      setLoading(false);
    }
  }, [companyId, from, to]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  function entryPayload(): SaveExpenseEntryPayload {
    const center = parseCenterKey(entryForm.centerKey);

    return {
      companyId,
      centerType: EXPENSE_CENTER_TYPE_VALUE[center.type],
      centerId: center.id,
      expenseCategoryId: entryForm.categoryId,
      expenseDate: entryForm.expenseDate,
      amount: Number(entryForm.amount.replace(",", ".")) || 0,
      description: entryForm.description,
      paymentMethod: EXPENSE_PAYMENT_METHOD_VALUE[entryForm.paymentMethod],
      documentType: EXPENSE_DOCUMENT_TYPE_VALUE[entryForm.documentType],
      documentNumber: entryForm.documentNumber || null,
      supplierCurrentAccountId: null,
      partnerAccountId:
        entryForm.paymentMethod === "PartnerAccount"
          ? entryForm.partnerAccountId || null
          : null,
      creditCardId:
        entryForm.paymentMethod === "CreditCard"
          ? entryForm.creditCardId || null
          : null,
    };
  }

  /**
   * R4: kaydetmeden önce benzer kayıt uyarısı. UYARI, ENGEL DEĞİL —
   * iki ayrı yakıt fişi meşrudur; kullanıcı görüp yine kaydedebilir.
   */
  async function checkDuplicates() {
    if (!entryForm.centerKey || !entryForm.categoryId || !entryForm.amount) {
      setDuplicates([]);
      return;
    }

    try {
      setDuplicates(await expenseService.findDuplicates(entryPayload()));
    } catch {
      setDuplicates([]);
    }
  }

  async function saveEntry() {
    setBusy(true);
    setError("");

    try {
      await expenseService.createEntry(entryPayload());
      setEntryModalOpen(false);
      setEntryForm(emptyEntryForm);
      setDuplicates([]);
      setNotice("Gider kaydı eklendi.");
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Gider kaydedilemedi.");
    } finally {
      setBusy(false);
    }
  }

  async function saveTemplate() {
    setBusy(true);
    setError("");

    try {
      const center = parseCenterKey(templateForm.centerKey);

      await expenseService.createRecurring({
        companyId,
        centerType: EXPENSE_CENTER_TYPE_VALUE[center.type],
        centerId: center.id,
        expenseCategoryId: templateForm.categoryId,
        description: templateForm.description,
        estimatedAmount:
          Number(templateForm.estimatedAmount.replace(",", ".")) || 0,
        paymentMethod: EXPENSE_PAYMENT_METHOD_VALUE[templateForm.paymentMethod],
        supplierCurrentAccountId: null,
        startYear: templateForm.startYear,
        startMonth: templateForm.startMonth,
        endYear: null,
        endMonth: null,
        paymentDay: templateForm.paymentDay,
      });

      setTemplateModalOpen(false);
      setTemplateForm(emptyTemplateForm);
      setNotice("Tekrarlayan gider tanımlandı.");
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Şablon kaydedilemedi.");
    } finally {
      setBusy(false);
    }
  }

  async function confirmPeriodActual() {
    if (!confirmPeriod) return;

    setBusy(true);
    setError("");

    try {
      await expenseService.confirmRecurringPeriod(confirmPeriod.templateId, {
        year: confirmPeriod.year,
        month: confirmPeriod.month,
        actualAmount: Number(actualAmount.replace(",", ".")) || 0,
        documentType: EXPENSE_DOCUMENT_TYPE_VALUE.Invoice,
        documentNumber: null,
      });

      setConfirmPeriod(null);
      setActualAmount("");
      setNotice("Dönem kesinleşti; tahmini yerine gerçekleşen sayılıyor.");
      await load();
    } catch (confirmError) {
      setError(
        confirmError instanceof Error ? confirmError.message : "Dönem kesinleşmedi.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function deleteEntry() {
    if (!deleteTarget) return;

    setBusy(true);
    setError("");

    try {
      await expenseService.deleteEntry(deleteTarget.id);
      setDeleteTarget(null);
      setNotice("Gider kaydı silindi.");
      await load();
    } catch (deleteError) {
      setError(
        deleteError instanceof Error ? deleteError.message : "Gider silinemedi.",
      );
    } finally {
      setBusy(false);
    }
  }

  if (permissionsLoading) {
    return (
      <ErpShell design="redwood" title="Gider Merkezi">
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

        <div className="p-6 text-sm text-slate-500">Yükleniyor…</div>
      </ErpShell>
    );
  }

  if (!has("expense.view")) {
    return (
      <ErpShell design="redwood" title="Gider Merkezi">
        <div className="p-6">
          <div className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
            Gider merkezi raporu için <strong>expense.view</strong> yetkisi
            gerekiyor.
          </div>
        </div>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      design="redwood"
      title="Gider Merkezi"
      description="Merkez ve kategori kırılımında giderler — ofise ne harcadık, şantiyeye ne harcadık."
    >
      <div className="space-y-6 p-6">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="text-xs text-slate-600">
              Şirket
              <Select
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
                className="mt-1 w-52"
                options={companies.map((company) => ({
                  value: company.id,
                  label: company.name,
                }))}
              />
            </label>

            <label className="text-xs text-slate-600">
              Başlangıç
              <Input
                type="date"
                value={from}
                onChange={(event) => setFrom(event.target.value)}
                className="mt-1"
              />
            </label>

            <label className="text-xs text-slate-600">
              Bitiş
              <Input
                type="date"
                value={to}
                onChange={(event) => setTo(event.target.value)}
                className="mt-1"
              />
            </label>
          </div>
        </header>

        <div className="flex flex-wrap gap-2">
          {(
            [
              ["report", "Rapor"],
              ["entries", "Gider Kayıtları"],
              ["recurring", "Tekrarlayan"],
              ["partners", "Şahıs Carisi"],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              onClick={() => setView(key)}
              className={
                view === key
                  ? "rounded-md bg-brand-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-600"
                  : "rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-50"
              }
            >
              {label}
            </button>
          ))}

          {canManage ? (
            <div className="ml-auto flex gap-2">
              <Button
                type="button"
                onClick={() => {
                  setEntryForm(emptyEntryForm);
                  setDuplicates([]);
                  setEntryModalOpen(true);
                }}
              >
                Gider ekle
              </Button>
              <Button
                type="button"
                variant="secondary"
                onClick={() => {
                  setTemplateForm(emptyTemplateForm);
                  setTemplateModalOpen(true);
                }}
              >
                Tekrarlayan tanımla
              </Button>
            </div>
          ) : null}
        </div>

        {error ? (
          <div className="rounded-lg border border-rose-300 bg-rose-50 p-3 text-sm text-rose-800">
            {error}
          </div>
        ) : null}

        {notice ? (
          <div className="rounded-lg border border-emerald-300 bg-emerald-50 p-3 text-sm text-emerald-800">
            {notice}
          </div>
        ) : null}

        {/* Gizlenen kalem uyarısı: tutar taşımıyor, yalnızca eksik
            bakıldığını söylüyor. */}
        {report?.hiddenNote ? (
          <div className="rounded-lg border border-slate-300 bg-slate-50 p-3 text-sm text-slate-700">
            {report.hiddenNote} ({report.hiddenCount} kalem)
          </div>
        ) : null}

        {report?.notes?.length ? (
          <ul className="space-y-1 rounded-lg border border-sky-200 bg-sky-50 p-3 text-xs text-sky-900">
            {report.notes.map((note) => (
              <li key={note}>• {note}</li>
            ))}
          </ul>
        ) : null}

        {loading ? (
          <div className="text-sm text-slate-500">Yükleniyor…</div>
        ) : view === "report" ? (
          <ReportView report={report} periodKey={`${from}|${to}`} />
        ) : view === "entries" ? (
          <EntriesView
            data={entries}
            canManage={canManage}
            onDelete={(id, label) => setDeleteTarget({ id, label })}
            periodKey={`${from}|${to}`}
          />
        ) : view === "partners" ? (
          <PartnersView data={partners} canSeeCash={canSeeCash} />
        ) : (
          <RecurringView
            data={recurring}
            canManage={canManage}
            onConfirm={(period) => {
              setConfirmPeriod(period);
              setActualAmount(String(period.estimated));
            }}
            periodKey={`${from}|${to}`}
          />
        )}
      </div>

      {/* --- Gider ekleme --- */}
      <Modal
        open={entryModalOpen}
        onClose={() => setEntryModalOpen(false)}
        title="Gider ekle"
        busy={busy}
      >
        <div className="space-y-3">
          <label className="block text-xs text-slate-600">
            Gider merkezi
            <Select
              value={entryForm.centerKey}
              onChange={(event) =>
                setEntryForm({ ...entryForm, centerKey: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Seçiniz"
              options={centers.map((center) => ({
                value: centerKey(center.type, center.id),
                label: center.name,
              }))}
            />
          </label>

          <label className="block text-xs text-slate-600">
            Kategori
            <Select
              value={entryForm.categoryId}
              onChange={(event) =>
                setEntryForm({ ...entryForm, categoryId: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Seçiniz"
              options={manualCategories.map((category) => ({
                value: category.id,
                label: category.name,
              }))}
            />
            <span className="mt-1 block text-[11px] text-slate-400">
              Malzeme, işçilik, taşeron ve yol listede yok: bu kalemler satın
              alma, puantaj ve görevlendirmeden otomatik geliyor.
            </span>
          </label>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Tarih
              <Input
                type="date"
                value={entryForm.expenseDate}
                onChange={(event) =>
                  setEntryForm({ ...entryForm, expenseDate: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Tutar
              <Input
                value={entryForm.amount}
                onChange={(event) =>
                  setEntryForm({ ...entryForm, amount: event.target.value })
                }
                onBlur={() => void checkDuplicates()}
                placeholder="0,00"
                className="mt-1 w-full"
              />
            </label>
          </div>

          <label className="block text-xs text-slate-600">
            Açıklama
            <Input
              value={entryForm.description}
              onChange={(event) =>
                setEntryForm({ ...entryForm, description: event.target.value })
              }
              className="mt-1 w-full"
            />
          </label>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Ödeme şekli
              {/* Elden seçeneği yalnız yetkiliye: yetkisiz kullanıcı
                  zaten uçta 403 alır, burada da gösterilmiyor. */}
              <Select
                value={entryForm.paymentMethod}
                onChange={(event) =>
                  setEntryForm({
                    ...entryForm,
                    paymentMethod: event.target.value as
                      | "Bank"
                      | "Cash"
                      | "PartnerAccount"
                      | "CreditCard",
                  })
                }
                className="mt-1 w-full"
                options={
                  canSeeCash
                    ? [
                        { value: "Bank", label: "Banka" },
                        { value: "CreditCard", label: "Kredi kartı" },
                        { value: "Cash", label: "Elden" },
                        {
                          value: "PartnerAccount",
                          label: "Faturasız — şahıs carisinden mahsup",
                        },
                      ]
                    : [
                        { value: "Bank", label: "Banka" },
                        { value: "CreditCard", label: "Kredi kartı" },
                      ]
                }
              />
            </label>

            <label className="block text-xs text-slate-600">
              Belge
              <Select
                value={entryForm.documentType}
                onChange={(event) =>
                  setEntryForm({
                    ...entryForm,
                    documentType: event.target.value as "None" | "Receipt" | "Invoice",
                  })
                }
                className="mt-1 w-full"
                options={[
                  { value: "Receipt", label: "Fiş" },
                  { value: "Invoice", label: "Fatura" },
                  { value: "None", label: "Belgesiz" },
                ]}
              />
            </label>
          </div>

          {entryForm.paymentMethod === "CreditCard" ? (
            <label className="block text-xs text-slate-600">
              Kart
              <Select
                value={entryForm.creditCardId}
                onChange={(event) =>
                  setEntryForm({ ...entryForm, creditCardId: event.target.value })
                }
                className="mt-1 w-full"
                placeholder="Seçiniz"
                options={cards.map((card) => ({
                  value: card.id,
                  label:
                    card.ownership === "Personal"
                      ? `${card.name} (şahıs — ${card.partnerName ?? "?"})`
                      : card.name,
                }))}
              />
              <span className="mt-1 block text-[11px] text-slate-400">
                Gider bugün sayılır, nakit ekstrenin son ödeme gününde çıkar.
                Şahıs kartında şirket nakdi hiç çıkmaz; harcama kart sahibinin
                carisine yazılır.
              </span>
            </label>
          ) : null}

          {entryForm.paymentMethod === "PartnerAccount" ? (
            <label className="block text-xs text-slate-600">
              Mahsup edilecek kişi
              <Select
                value={entryForm.partnerAccountId}
                onChange={(event) =>
                  setEntryForm({
                    ...entryForm,
                    partnerAccountId: event.target.value,
                  })
                }
                className="mt-1 w-full"
                placeholder="Seçiniz"
                options={partners.map((partner) => ({
                  value: partner.id,
                  label: partner.fullName,
                }))}
              />
              <span className="mt-1 block text-[11px] text-slate-400">
                Bu gider kişinin borcundan düşer. Şirket nakdini tekrar
                etkilemez — para avans olarak zaten çıktı.
              </span>
            </label>
          ) : null}

          <label className="block text-xs text-slate-600">
            Belge no
            <Input
              value={entryForm.documentNumber}
              onChange={(event) =>
                setEntryForm({ ...entryForm, documentNumber: event.target.value })
              }
              className="mt-1 w-full"
            />
          </label>

          {duplicates.length > 0 ? (
            <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900">
              <p className="font-medium">Bu gider zaten girilmiş olabilir:</p>
              <ul className="mt-1 space-y-0.5">
                {duplicates.map((hint) => (
                  <li key={hint.id}>
                    {dateFormat.format(new Date(hint.expenseDate))} ·{" "}
                    {money(hint.amount)} · {hint.description}
                  </li>
                ))}
              </ul>
              <p className="mt-1 text-[11px]">
                Yine de kaydedebilirsiniz — iki ayrı ödeme olabilir.
              </p>
            </div>
          ) : null}

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setEntryModalOpen(false)}
              disabled={busy}
            >
              Vazgeç
            </Button>
            <Button type="button" onClick={() => void saveEntry()} disabled={busy}>
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>

      {/* --- Tekrarlayan tanımlama --- */}
      <Modal
        open={templateModalOpen}
        onClose={() => setTemplateModalOpen(false)}
        title="Tekrarlayan gider tanımla"
        busy={busy}
      >
        <div className="space-y-3">
          <label className="block text-xs text-slate-600">
            Gider merkezi
            <Select
              value={templateForm.centerKey}
              onChange={(event) =>
                setTemplateForm({ ...templateForm, centerKey: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Seçiniz"
              options={centers.map((center) => ({
                value: centerKey(center.type, center.id),
                label: center.name,
              }))}
            />
          </label>

          <label className="block text-xs text-slate-600">
            Kategori
            <Select
              value={templateForm.categoryId}
              onChange={(event) =>
                setTemplateForm({ ...templateForm, categoryId: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Seçiniz"
              options={manualCategories.map((category) => ({
                value: category.id,
                label: category.name,
              }))}
            />
          </label>

          <label className="block text-xs text-slate-600">
            Açıklama
            <Input
              value={templateForm.description}
              onChange={(event) =>
                setTemplateForm({ ...templateForm, description: event.target.value })
              }
              className="mt-1 w-full"
            />
          </label>

          <div className="grid grid-cols-3 gap-3">
            <label className="block text-xs text-slate-600">
              Aylık tahmini
              <Input
                value={templateForm.estimatedAmount}
                onChange={(event) =>
                  setTemplateForm({
                    ...templateForm,
                    estimatedAmount: event.target.value,
                  })
                }
                placeholder="0,00"
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Başlangıç ayı
              <Select
                value={String(templateForm.startMonth)}
                onChange={(event) =>
                  setTemplateForm({
                    ...templateForm,
                    startMonth: Number(event.target.value),
                  })
                }
                className="mt-1 w-full"
                options={MONTHS.map((name, index) => ({
                  value: String(index + 1),
                  label: name,
                }))}
              />
            </label>

            <label className="block text-xs text-slate-600">
              Ödeme günü
              <Input
                type="number"
                min={1}
                max={31}
                value={templateForm.paymentDay}
                onChange={(event) =>
                  setTemplateForm({
                    ...templateForm,
                    paymentDay: Number(event.target.value),
                  })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <label className="block text-xs text-slate-600">
            Ödeme şekli
            <Select
              value={templateForm.paymentMethod}
              onChange={(event) =>
                setTemplateForm({
                  ...templateForm,
                  paymentMethod: event.target.value as "Bank" | "Cash",
                })
              }
              className="mt-1 w-full"
              options={
                canSeeCash
                  ? [
                      { value: "Bank", label: "Banka" },
                      { value: "Cash", label: "Elden" },
                    ]
                  : [{ value: "Bank", label: "Banka" }]
              }
            />
          </label>

          <p className="text-[11px] text-slate-500">
            Şablon her ay TAHMİNİ tutarla akar; ay gelince gerçekleşeni
            girdiğinizde o ayın tahminisi düşer ve yerine gerçek tutar geçer.
          </p>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setTemplateModalOpen(false)}
              disabled={busy}
            >
              Vazgeç
            </Button>
            <Button type="button" onClick={() => void saveTemplate()} disabled={busy}>
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>

      {/* --- Dönem kesinleştirme --- */}
      <Modal
        open={confirmPeriod !== null}
        onClose={() => setConfirmPeriod(null)}
        title="Gerçekleşen tutarı gir"
        busy={busy}
      >
        {confirmPeriod ? (
          <div className="space-y-3">
            <p className="text-sm text-slate-700">
              <strong>{confirmPeriod.description}</strong> —{" "}
              {MONTHS[confirmPeriod.month - 1]} {confirmPeriod.year}
            </p>
            <p className="text-xs text-slate-500">
              Tahmini: {money(confirmPeriod.estimated)}. Gerçekleşen
              girildiğinde bu ayın tahminisi düşer; ikisi birden sayılmaz.
            </p>

            <label className="block text-xs text-slate-600">
              Gerçekleşen tutar
              <Input
                value={actualAmount}
                onChange={(event) => setActualAmount(event.target.value)}
                className="mt-1 w-full"
              />
            </label>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="secondary"
                onClick={() => setConfirmPeriod(null)}
                disabled={busy}
              >
                Vazgeç
              </Button>
              <Button
                type="button"
                onClick={() => void confirmPeriodActual()}
                disabled={busy}
              >
                Kesinleştir
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>

      <ConfirmDialog
        key={deleteTarget?.id ?? "none"}
        open={deleteTarget !== null}
        title="Gider kaydını sil"
        description={`"${deleteTarget?.label ?? ""}" kaydı silinecek.`}
        confirmLabel="Sil"
        busy={busy}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={() => void deleteEntry()}
      />
    </ErpShell>
  );
}

/*
 * SÜTUN TANIMLARI (F4i).
 *
 * Yetki ve işleyici gerektiren tablolarda sütunlar FONKSİYON: modül
 * düzeyinde sabit dizi olsaydı işleyici kapanışa alınır ve bayat
 * kapanış düğmeyi yanlış kayıt üzerinde çalıştırabilirdi (F4b kararı).
 */
const reportColumns: DataTableColumn<ExpenseReport["rows"][number]>[] = [
  { key: "merkez", header: "Merkez", value: (row) => row.centerName },
  { key: "kategori", header: "Kategori", value: (row) => row.categoryName },
  {
    key: "kaynak",
    header: "Kaynak",
    value: (row) =>
      row.source +
      (row.isEstimated ? " · tahmini" : "") +
      (!row.isEditableHere ? " · otomatik" : ""),
    render: (row) => (
      <>
        <span className="text-slate-600">{row.source}</span>
        {row.isEstimated ? (
          <span className="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-[11px] text-amber-800">
            tahmini
          </span>
        ) : null}
        {/* Otomatik kalem: kaynağından düzeltilir. */}
        {!row.isEditableHere ? (
          <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-600">
            otomatik
          </span>
        ) : null}
      </>
    ),
  },
  {
    key: "tutar",
    header: "Tutar",
    numeric: true,
    value: (row) => money(row.amount),
    footer: (rows) => money(rows.reduce((sum, row) => sum + row.amount, 0)),
  },
];

function totalsColumns(
  total: number
): DataTableColumn<{ key: string; label: string; amount: number }>[] {
  return [
    { key: "kalem", header: "Kalem", value: (row) => row.label },
    {
      key: "pay",
      header: "Pay",
      numeric: true,
      value: (row) => `%${total > 0 ? Math.round((row.amount / total) * 100) : 0}`,
    },
    {
      key: "tutar",
      header: "Tutar",
      numeric: true,
      value: (row) => moneyWhole(row.amount),
      footer: (rows) => moneyWhole(rows.reduce((sum, row) => sum + row.amount, 0)),
    },
  ];
}

function entryColumns(
  canManage: boolean,
  onDelete: (id: string, label: string) => void,
  hiddenCount: number
): DataTableColumn<ExpenseEntryList["items"][number]>[] {
  const columns: DataTableColumn<ExpenseEntryList["items"][number]>[] = [
    {
      key: "tarih",
      header: "Tarih",
      value: (row) => dateFormat.format(new Date(row.expenseDate)),
    },
    { key: "merkez", header: "Merkez", value: (row) => row.centerName },
    { key: "kategori", header: "Kategori", value: (row) => row.categoryName },
    {
      key: "aciklama",
      header: "Açıklama",
      value: (row) => row.description + (row.isRecurring ? " · tekrarlayan" : ""),
      render: (row) => (
        <>
          {row.description}
          {row.isRecurring ? (
            <span className="ml-2 rounded bg-sky-100 px-1.5 py-0.5 text-[11px] text-sky-800">
              tekrarlayan
            </span>
          ) : null}
        </>
      ),
    },
    {
      key: "odeme",
      header: "Ödeme",
      value: (row) =>
        row.paymentMethod === "Cash"
          ? "elden"
          : row.paymentMethod === "PartnerAccount"
            ? `faturasız · ${row.partnerName ?? "şahıs"}`
            : row.paymentMethod === "CreditCard"
              ? `kart · ${row.cardName ?? "—"}`
              : "Banka",
      render: (row) =>
        row.paymentMethod === "Cash" ? (
          <span className="rounded bg-violet-100 px-1.5 py-0.5 text-[11px] text-violet-800">
            elden
          </span>
        ) : row.paymentMethod === "PartnerAccount" ? (
          <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[11px] text-amber-800">
            faturasız · {row.partnerName ?? "şahıs"}
          </span>
        ) : row.paymentMethod === "CreditCard" ? (
          <span className="rounded bg-sky-100 px-1.5 py-0.5 text-[11px] text-sky-800">
            kart · {row.cardName ?? "—"}
          </span>
        ) : (
          "Banka"
        ),
    },
    {
      key: "tutar",
      header: "Tutar",
      numeric: true,
      value: (row) => money(row.amount),
      /*
       * GİZLENEN KALEM VARSA TOPLAM BUNU SÖYLER. Sessizce eksik bir
       * toplam göstermek, tam olarak bu programın kovaladığı hata.
       */
      footer: (rows) =>
        `${money(rows.reduce((sum, row) => sum + row.amount, 0))}` +
        (hiddenCount > 0 ? " (yalnız görünen kalemler)" : ""),
    },
  ];

  if (canManage) {
    columns.push({
      key: "sil",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          onClick={() => onDelete(row.id, row.description)}
          className="text-xs text-rose-600 hover:underline"
        >
          Sil
        </button>
      ),
    });
  }

  return columns;
}

function ReportView({
  report,
  periodKey,
}: {
  report: ExpenseReport | null;
  /** Tarih aralığı değişince sayfa 1'e döner. */
  periodKey: string;
}) {
  if (!report || report.rows.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 p-6 text-sm text-slate-500">
        Bu dönemde gider yok.
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="rounded-lg border border-slate-200 bg-white p-4">
        <p className="text-xs uppercase tracking-wide text-slate-500">
          Dönem toplamı
        </p>
        <p className="mt-1 text-2xl font-semibold text-slate-900">
          {money(report.total)}
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <TotalsTable
          title="Merkeze göre"
          rows={report.centerTotals.map((x) => ({
            key: `${x.centerType}:${x.centerId}`,
            label: x.centerName,
            amount: x.amount,
          }))}
          total={report.total}
        />

        <TotalsTable
          title="Kategoriye göre"
          rows={report.categoryTotals.map((x) => ({
            key: x.categoryCode,
            label: x.categoryName,
            amount: x.amount,
          }))}
          total={report.total}
        />
      </div>

      <div className="overflow-x-auto rounded-lg border border-slate-200">
        <DataTable
          rows={report.rows}
          columns={reportColumns}
          rowKey={(row) =>
            `${row.centerId}-${row.categoryCode}-${row.source}-${row.amount}`
          }
          title="Gider Merkezi Raporu"
          resetKey={periodKey}
        />
      </div>
    </div>
  );
}

function TotalsTable({
  title,
  rows,
  total,
}: {
  title: string;
  rows: { key: string; label: string; amount: number }[];
  total: number;
}) {
  return (
    <div className="rounded-lg border border-slate-200">
      <h2 className="border-b border-slate-100 px-3 py-2 text-sm font-medium text-slate-800">
        {title}
      </h2>
      <DataTable
        rows={rows}
        columns={totalsColumns(total)}
        rowKey={(row) => row.key}
        title={title}
      />
    </div>
  );
}

function EntriesView({
  data,
  canManage,
  onDelete,
  periodKey,
}: {
  data: ExpenseEntryList | null;
  canManage: boolean;
  onDelete: (id: string, label: string) => void;
  periodKey: string;
}) {
  if (!data || data.items.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 p-6 text-sm text-slate-500">
        Bu dönemde elle girilmiş gider yok.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200">
      <DataTable
        rows={data.items}
        columns={entryColumns(canManage, onDelete, data.hiddenCount)}
        rowKey={(row) => row.id}
        title="Elle Girilen Giderler"
        resetKey={periodKey}
      />
    </div>
  );
}

function templateColumns(
  canManage: boolean,
  periods: RecurringExpenseList["periods"],
  onConfirm: (input: {
    templateId: string;
    year: number;
    month: number;
    description: string;
    estimated: number;
  }) => void
): DataTableColumn<RecurringExpenseList["templates"][number]>[] {
  const periodOf = (templateId: string) =>
    periods?.find((x) => x.templateId === templateId);

  const columns: DataTableColumn<RecurringExpenseList["templates"][number]>[] = [
    {
      key: "aciklama",
      header: "Açıklama",
      value: (row) => row.description + (row.isStopped ? " · durduruldu" : ""),
      render: (row) => (
        <>
          {row.description}
          {row.isStopped ? (
            <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-600">
              durduruldu
            </span>
          ) : null}
        </>
      ),
    },
    { key: "merkez", header: "Merkez", value: (row) => row.centerName },
    { key: "kategori", header: "Kategori", value: (row) => row.categoryName },
    {
      key: "tahmini",
      header: "Aylık tahmini",
      numeric: true,
      value: (row) => money(row.estimatedAmount),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.estimatedAmount, 0)),
    },
    {
      key: "donem",
      header: "Seçili dönem",
      value: (row) => {
        const period = periodOf(row.id);

        if (!period) return "kapsam dışı";

        return period.isConfirmed
          ? `kesinleşti · ${money(period.actualAmount ?? 0)}`
          : "gerçekleşen bekleniyor";
      },
      render: (row) => {
        const period = periodOf(row.id);

        if (!period)
          return <span className="text-xs text-slate-400">kapsam dışı</span>;

        return period.isConfirmed ? (
          <span className="text-xs text-emerald-700">
            kesinleşti · {money(period.actualAmount ?? 0)}
          </span>
        ) : (
          <span className="text-xs text-amber-700">gerçekleşen bekleniyor</span>
        );
      },
    },
  ];

  if (canManage) {
    columns.push({
      key: "gir",
      header: "",
      value: () => "",
      render: (row) => {
        const period = periodOf(row.id);

        if (!period || period.isConfirmed) return null;

        return (
          <button
            type="button"
            onClick={() =>
              onConfirm({
                templateId: row.id,
                year: period.year,
                month: period.month,
                description: row.description,
                estimated: period.estimatedAmount,
              })
            }
            className="text-xs text-slate-700 hover:underline"
          >
            Gerçekleşeni gir
          </button>
        );
      },
    });
  }

  return columns;
}

const partnerColumns: DataTableColumn<PartnerAccountBalance>[] = [
  { key: "kisi", header: "Kişi", value: (row) => row.fullName },
  { key: "unvan", header: "Ünvan", value: (row) => row.title ?? "—" },
  {
    key: "avans",
    header: "Verilen (avans)",
    numeric: true,
    value: (row) => money(row.advanceTotal),
    footer: (rows) => money(rows.reduce((sum, row) => sum + row.advanceTotal, 0)),
  },
  {
    key: "mahsup",
    header: "Mahsup (faturasız gider)",
    numeric: true,
    value: (row) => money(row.settlementTotal),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.settlementTotal, 0)),
  },
  {
    key: "geri",
    header: "Geri ödeme",
    numeric: true,
    value: (row) => money(row.repaymentTotal),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.repaymentTotal, 0)),
  },
  {
    key: "bakiye",
    header: "Bakiye",
    numeric: true,
    value: (row) => money(row.balance),
    /*
     * Pozitif bakiye kişinin ŞİRKETE OLAN BORCU — anlamsal renk
     * tokendan geliyor, ham hex değil.
     */
    render: (row) => (
      <span className={row.balance > 0 ? "rw-value-danger" : ""}>
        {money(row.balance)}
      </span>
    ),
    footer: (rows) => money(rows.reduce((sum, row) => sum + row.balance, 0)),
  },
];

function RecurringView({
  data,
  canManage,
  onConfirm,
  periodKey,
}: {
  data: RecurringExpenseList | null;
  canManage: boolean;
  /** Tarih aralığı değişince sayfa 1'e döner. */
  periodKey: string;
  onConfirm: (period: {
    templateId: string;
    year: number;
    month: number;
    description: string;
    estimated: number;
  }) => void;
}) {
  if (!data || data.templates.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 p-6 text-sm text-slate-500">
        Tanımlı tekrarlayan gider yok. Kira, elektrik ve internet gibi düzenli
        giderleri buradan tanımlarsanız nakit akış takvimine de düşerler.
      </div>
    );
  }

  const periods = data.periods ?? [];

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200">
      <DataTable
        rows={data.templates}
        columns={templateColumns(canManage, periods, onConfirm)}
        rowKey={(row) => row.id}
        title="Tekrarlayan Giderler"
        resetKey={periodKey}
      />
    </div>
  );
}

function PartnersView({
  data,
  canSeeCash,
}: {
  data: PartnerAccountBalance[];
  canSeeCash: boolean;
}) {
  if (!canSeeCash) {
    return (
      <div className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
        Şahıs carisi faturasız kalemler taşıyor; görmek için{" "}
        <strong>extra_payment.view</strong> yetkisi gerekiyor.
      </div>
    );
  }

  if (data.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 p-6 text-sm text-slate-500">
        Tanımlı şahıs carisi yok. Şirketten bir kişiye para çıkıyor ve o
        para faturasız giderlerle kapanıyorsa buradan takip edilir.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200">
      <DataTable
        rows={data}
        columns={partnerColumns}
        rowKey={(row) => row.id}
        title="Ortak Cari Hesapları"
      />
      <p className="border-t border-slate-100 px-3 py-2 text-[11px] text-slate-500">
        Bakiye = verilen − (mahsup + geri ödeme). Pozitif bakiye, kişinin
        şirkete olan borcudur.
      </p>
    </div>
  );
}
