"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money } from "@/lib/format/turkish";
import { Button, ConfirmDialog, Input, Modal, Select } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { expenseService, type PartnerAccountBalance } from "@/services/expense.service";
import {
  BANK_LOAN_STATUS_LABEL,
  BANK_LOAN_STATUS_VALUE,
  CREDIT_CARD_OWNERSHIP_VALUE,
  barterService,
  financialInstrumentService,
  type BankLoan,
  type BankLoanInstallment,
  type CreditCard,
  type CreditCardStatement,
} from "@/services/financial-instrument.service";


const dateFormat = new Intl.DateTimeFormat("tr-TR");

function iso(date: Date) {
  return date.toISOString().slice(0, 10);
}

const emptyLoanForm = {
  name: "",
  contractNumber: "",
  principalAmount: "",
  monthlyInterestRate: "",
  installmentCount: "12",
  drawdownDate: iso(new Date()),
  firstInstallmentDate: iso(new Date(Date.now() + 30 * 86_400_000)),
};

const emptyCardForm = {
  name: "",
  bankName: "",
  lastFourDigits: "",
  ownership: "Company" as "Company" | "Personal",
  partnerAccountId: "",
  statementDay: "1",
  dueDay: "10",
};

export default function FinancialInstrumentsPage() {
  const { has, loading: permissionsLoading } = usePermissions();

  const canEdit = has("finance.edit");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [view, setView] = useState<"loans" | "cards">("loans");

  const [loans, setLoans] = useState<BankLoan[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [statements, setStatements] = useState<CreditCardStatement[]>([]);
  const [partners, setPartners] = useState<PartnerAccountBalance[]>([]);

  const [openLoanId, setOpenLoanId] = useState<string | null>(null);
  const [installments, setInstallments] = useState<BankLoanInstallment[]>([]);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [loanModalOpen, setLoanModalOpen] = useState(false);
  const [loanForm, setLoanForm] = useState(emptyLoanForm);

  const [cardModalOpen, setCardModalOpen] = useState(false);
  const [cardForm, setCardForm] = useState(emptyCardForm);

  const [editInstallment, setEditInstallment] =
    useState<BankLoanInstallment | null>(null);
  const [installmentForm, setInstallmentForm] = useState({
    principalAmount: "",
    interestAmount: "",
    dueDate: "",
    isPaid: false,
  });

  const [cancelLoan, setCancelLoan] = useState<BankLoan | null>(null);

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
      const [loanList, cardList, statementList] = await Promise.all([
        financialInstrumentService.listLoans(companyId),
        financialInstrumentService.listCards(companyId),
        financialInstrumentService.listStatements(companyId),
      ]);

      setLoans(loanList);
      setCards(cardList);
      setStatements(statementList);

      // Şahıs carisi extra_payment.view istiyor; yetki yoksa uç 403
      // döner ve şahıs kartı seçeneği boş kalır — sayfanın geri
      // kalanı çalışmaya devam etmeli.
      try {
        setPartners(await expenseService.listPartners(companyId));
      } catch {
        setPartners([]);
      }
    } catch (loadError) {
      setError(
        loadError instanceof Error
          ? loadError.message
          : "Finansal araçlar alınamadı.",
      );
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const openInstallments = useCallback(async (loanId: string) => {
    setOpenLoanId(loanId);

    try {
      setInstallments(await financialInstrumentService.listInstallments(loanId));
    } catch {
      setInstallments([]);
    }
  }, []);

  async function saveLoan() {
    setBusy(true);
    setError("");

    try {
      await financialInstrumentService.createLoan({
        companyId,
        name: loanForm.name,
        contractNumber: loanForm.contractNumber || null,
        principalAmount: Number(loanForm.principalAmount.replace(",", ".")) || 0,
        monthlyInterestRate:
          Number(loanForm.monthlyInterestRate.replace(",", ".")) || 0,
        installmentCount: Number(loanForm.installmentCount) || 0,
        drawdownDate: loanForm.drawdownDate,
        firstInstallmentDate: loanForm.firstInstallmentDate,
      });

      setLoanModalOpen(false);
      setLoanForm(emptyLoanForm);
      setNotice("Kredi tanımlandı; taksit planı otomatik üretildi.");
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Kredi kaydedilemedi.");
    } finally {
      setBusy(false);
    }
  }

  async function saveCard() {
    setBusy(true);
    setError("");

    try {
      await financialInstrumentService.createCard({
        companyId,
        name: cardForm.name,
        bankName: cardForm.bankName || null,
        lastFourDigits: cardForm.lastFourDigits || null,
        ownership: CREDIT_CARD_OWNERSHIP_VALUE[cardForm.ownership],
        partnerAccountId:
          cardForm.ownership === "Personal" ? cardForm.partnerAccountId || null : null,
        statementDay: Number(cardForm.statementDay) || 1,
        dueDay: Number(cardForm.dueDay) || 1,
        isActive: true,
      });

      setCardModalOpen(false);
      setCardForm(emptyCardForm);
      setNotice("Kart tanımlandı.");
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Kart kaydedilemedi.");
    } finally {
      setBusy(false);
    }
  }

  async function saveInstallment() {
    if (!editInstallment) return;

    setBusy(true);
    setError("");

    try {
      await financialInstrumentService.updateInstallment(editInstallment.id, {
        principalAmount:
          Number(installmentForm.principalAmount.replace(",", ".")) || 0,
        interestAmount: Number(installmentForm.interestAmount.replace(",", ".")) || 0,
        dueDate: installmentForm.dueDate,
        isPaid: installmentForm.isPaid,
        paidDate: installmentForm.isPaid ? installmentForm.dueDate : null,
      });

      setEditInstallment(null);
      setNotice("Taksit güncellendi.");

      if (openLoanId) await openInstallments(openLoanId);
      await load();
    } catch (saveError) {
      setError(
        saveError instanceof Error ? saveError.message : "Taksit güncellenemedi.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function setDrawn(loan: BankLoan) {
    setBusy(true);
    setError("");

    try {
      await financialInstrumentService.updateLoanStatus(
        loan.id,
        BANK_LOAN_STATUS_VALUE.Active,
        true,
      );

      setNotice("Kredi çekildi olarak işaretlendi; nakit akışta tekrar giriş yazılmaz.");
      await load();
    } catch (statusError) {
      setError(
        statusError instanceof Error ? statusError.message : "Durum değişmedi.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function confirmCancelLoan() {
    if (!cancelLoan) return;

    setBusy(true);
    setError("");

    try {
      await financialInstrumentService.updateLoanStatus(
        cancelLoan.id,
        BANK_LOAN_STATUS_VALUE.Cancelled,
      );

      setCancelLoan(null);
      setNotice("Kredi iptal edildi; nakit akışta ne çekiliş ne taksit sayılır.");
      await load();
    } catch (cancelError) {
      setError(
        cancelError instanceof Error ? cancelError.message : "Kredi iptal edilemedi.",
      );
    } finally {
      setBusy(false);
    }
  }

  if (permissionsLoading) {
    return (
      <ErpShell design="redwood" title="Finansal Araçlar">
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

        <div className="p-6 text-sm text-slate-500">Yükleniyor…</div>
      </ErpShell>
    );
  }

  if (!has("finance.view")) {
    return (
      <ErpShell design="redwood" title="Finansal Araçlar">
        <div className="p-6">
          <div className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
            Bu ekran <strong>finance.view</strong> yetkisi istiyor.
          </div>
        </div>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      design="redwood"
      title="Finansal Araçlar"
      description="Banka kredileri ve kredi kartları — ikisi de nakit akış takvimine besleniyor."
    >
      <div className="space-y-6 p-6">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <label className="text-xs text-slate-600">
            Şirket
            <Select
              value={companyId}
              onChange={(event) => setCompanyId(event.target.value)}
              className="mt-1 w-56"
              options={companies.map((company) => ({
                value: company.id,
                label: company.name,
              }))}
            />
          </label>

          {canEdit ? (
            <div className="flex gap-2">
              <Button type="button" onClick={() => setLoanModalOpen(true)}>
                Kredi tanımla
              </Button>
              <Button
                type="button"
                variant="secondary"
                onClick={() => setCardModalOpen(true)}
              >
                Kart tanımla
              </Button>
            </div>
          ) : null}
        </header>

        <div className="flex gap-2">
          {(
            [
              ["loans", "Banka Kredileri"],
              ["cards", "Kredi Kartları"],
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

        {loading ? (
          <div className="text-sm text-slate-500">Yükleniyor…</div>
        ) : view === "loans" ? (
          <LoansView
            loans={loans}
            canEdit={canEdit}
            openLoanId={openLoanId}
            installments={installments}
            onToggle={(id) =>
              openLoanId === id ? setOpenLoanId(null) : void openInstallments(id)
            }
            onDrawn={(loan) => void setDrawn(loan)}
            onCancel={(loan) => setCancelLoan(loan)}
            onEditInstallment={(installment) => {
              setEditInstallment(installment);
              setInstallmentForm({
                principalAmount: String(installment.principalAmount),
                interestAmount: String(installment.interestAmount),
                dueDate: installment.dueDate.slice(0, 10),
                isPaid: installment.isPaid,
              });
            }}
          />
        ) : (
          <CardsView cards={cards} statements={statements} />
        )}
      </div>

      {/* --- Kredi tanımlama --- */}
      <Modal
        open={loanModalOpen}
        onClose={() => setLoanModalOpen(false)}
        title="Kredi tanımla"
        description="Taksit planı kaydedince otomatik üretilir; sonra tek tek düzeltebilirsiniz."
        busy={busy}
      >
        <div className="space-y-3">
          <label className="block text-xs text-slate-600">
            Kredi adı
            <Input
              value={loanForm.name}
              onChange={(event) =>
                setLoanForm({ ...loanForm, name: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Ör. İşletme kredisi — Ziraat"
            />
          </label>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Sözleşme no
              <Input
                value={loanForm.contractNumber}
                onChange={(event) =>
                  setLoanForm({ ...loanForm, contractNumber: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Anapara
              <Input
                value={loanForm.principalAmount}
                onChange={(event) =>
                  setLoanForm({ ...loanForm, principalAmount: event.target.value })
                }
                className="mt-1 w-full"
                placeholder="0,00"
              />
            </label>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Aylık faiz (%)
              <Input
                value={loanForm.monthlyInterestRate}
                onChange={(event) =>
                  setLoanForm({
                    ...loanForm,
                    monthlyInterestRate: event.target.value,
                  })
                }
                className="mt-1 w-full"
                placeholder="3,79"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Taksit sayısı
              <Input
                type="number"
                min={1}
                max={600}
                value={loanForm.installmentCount}
                onChange={(event) =>
                  setLoanForm({ ...loanForm, installmentCount: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Çekiliş tarihi
              <Input
                type="date"
                value={loanForm.drawdownDate}
                onChange={(event) =>
                  setLoanForm({ ...loanForm, drawdownDate: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              İlk taksit
              <Input
                type="date"
                value={loanForm.firstInstallmentDate}
                onChange={(event) =>
                  setLoanForm({
                    ...loanForm,
                    firstInstallmentDate: event.target.value,
                  })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <p className="text-[11px] text-slate-500">
            Çekiliş nakit akışta GİRİŞ, taksitler ÇIKIŞ olarak görünür. Kredi
            çekildi olarak işaretlenince giriş bir daha sayılmaz — para zaten
            hesapta.
          </p>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setLoanModalOpen(false)}
              disabled={busy}
            >
              Vazgeç
            </Button>
            <Button type="button" onClick={() => void saveLoan()} disabled={busy}>
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>

      {/* --- Kart tanımlama --- */}
      <Modal
        open={cardModalOpen}
        onClose={() => setCardModalOpen(false)}
        title="Kredi kartı tanımla"
        busy={busy}
      >
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Kart adı
              <Input
                value={cardForm.name}
                onChange={(event) =>
                  setCardForm({ ...cardForm, name: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Banka
              <Input
                value={cardForm.bankName}
                onChange={(event) =>
                  setCardForm({ ...cardForm, bankName: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <div className="grid grid-cols-3 gap-3">
            <label className="block text-xs text-slate-600">
              Son 4 hane
              <Input
                value={cardForm.lastFourDigits}
                maxLength={4}
                onChange={(event) =>
                  setCardForm({ ...cardForm, lastFourDigits: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Kesim günü
              <Input
                type="number"
                min={1}
                max={31}
                value={cardForm.statementDay}
                onChange={(event) =>
                  setCardForm({ ...cardForm, statementDay: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Son ödeme günü
              <Input
                type="number"
                min={1}
                max={31}
                value={cardForm.dueDay}
                onChange={(event) =>
                  setCardForm({ ...cardForm, dueDay: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <label className="block text-xs text-slate-600">
            Kart sahibi
            <Select
              value={cardForm.ownership}
              onChange={(event) =>
                setCardForm({
                  ...cardForm,
                  ownership: event.target.value as "Company" | "Personal",
                })
              }
              className="mt-1 w-full"
              options={[
                { value: "Company", label: "Şirket kartı" },
                { value: "Personal", label: "Şahıs kartı" },
              ]}
            />
          </label>

          {cardForm.ownership === "Personal" ? (
            <label className="block text-xs text-slate-600">
              Kartın sahibi kişi
              <Select
                value={cardForm.partnerAccountId}
                onChange={(event) =>
                  setCardForm({ ...cardForm, partnerAccountId: event.target.value })
                }
                className="mt-1 w-full"
                placeholder="Seçiniz"
                options={partners.map((partner) => ({
                  value: partner.id,
                  label: partner.fullName,
                }))}
              />
              <span className="mt-1 block text-[11px] text-slate-400">
                Şahıs kartıyla yapılan şirket harcaması bu kişinin carisine
                yazılır; şirketin nakdi çıkmaz, ekstreyi kişi öder.
              </span>
            </label>
          ) : null}

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setCardModalOpen(false)}
              disabled={busy}
            >
              Vazgeç
            </Button>
            <Button type="button" onClick={() => void saveCard()} disabled={busy}>
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>

      {/* --- Taksit düzeltme --- */}
      <Modal
        open={editInstallment !== null}
        onClose={() => setEditInstallment(null)}
        title={`${editInstallment?.number ?? ""}. taksiti düzelt`}
        description="Bankanın uyguladığı yuvarlama ya da komisyon hesaptan farklı olabilir."
        busy={busy}
      >
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Anapara
              <Input
                value={installmentForm.principalAmount}
                onChange={(event) =>
                  setInstallmentForm({
                    ...installmentForm,
                    principalAmount: event.target.value,
                  })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Faiz
              <Input
                value={installmentForm.interestAmount}
                onChange={(event) =>
                  setInstallmentForm({
                    ...installmentForm,
                    interestAmount: event.target.value,
                  })
                }
                className="mt-1 w-full"
              />
            </label>
          </div>

          <label className="block text-xs text-slate-600">
            Vade
            <Input
              type="date"
              value={installmentForm.dueDate}
              onChange={(event) =>
                setInstallmentForm({
                  ...installmentForm,
                  dueDate: event.target.value,
                })
              }
              className="mt-1 w-full"
            />
          </label>

          <label className="flex items-center gap-2 text-xs text-slate-700">
            <input
              type="checkbox"
              checked={installmentForm.isPaid}
              onChange={(event) =>
                setInstallmentForm({
                  ...installmentForm,
                  isPaid: event.target.checked,
                })
              }
            />
            Ödendi — ödenen taksit nakit akışta gelecek çıkış olarak sayılmaz
          </label>

          <p className="text-[11px] text-slate-500">
            Faiz gider merkezine finansman gideri olarak düşer; anapara gider
            değildir, borcun kapanmasıdır.
          </p>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setEditInstallment(null)}
              disabled={busy}
            >
              Vazgeç
            </Button>
            <Button
              type="button"
              onClick={() => void saveInstallment()}
              disabled={busy}
            >
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        key={cancelLoan?.id ?? "none"}
        open={cancelLoan !== null}
        title="Krediyi iptal et"
        description={
          `"${cancelLoan?.name ?? ""}" iptal edilecek. Nakit akışta ne çekiliş ` +
          "ne de taksitler sayılır."
        }
        confirmLabel="Krediyi İptal Et"
        requireReason
        reasonLabel="İptal gerekçesi"
        busy={busy}
        onCancel={() => setCancelLoan(null)}
        onConfirm={() => void confirmCancelLoan()}
      />
    </ErpShell>
  );
}

function LoansView({
  loans,
  canEdit,
  openLoanId,
  installments,
  onToggle,
  onDrawn,
  onCancel,
  onEditInstallment,
}: {
  loans: BankLoan[];
  canEdit: boolean;
  openLoanId: string | null;
  installments: BankLoanInstallment[];
  onToggle: (id: string) => void;
  onDrawn: (loan: BankLoan) => void;
  onCancel: (loan: BankLoan) => void;
  onEditInstallment: (installment: BankLoanInstallment) => void;
}) {
  if (loans.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 p-6 text-sm text-slate-500">
        Tanımlı kredi yok. Kredi tanımlarsanız çekilişi nakit akışta giriş,
        taksitleri çıkış olarak görünür.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {loans.map((loan) => (
        <div key={loan.id} className="rounded-lg border border-slate-200">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4">
            <div>
              <p className="font-medium text-slate-900">
                {loan.name}
                <span
                  className={
                    loan.status === "Cancelled"
                      ? "ml-2 rounded bg-rose-100 px-1.5 py-0.5 text-[11px] text-rose-800"
                      : "ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-700"
                  }
                >
                  {BANK_LOAN_STATUS_LABEL[loan.status]}
                </span>
                {loan.isDrawn ? (
                  <span className="ml-2 rounded bg-emerald-100 px-1.5 py-0.5 text-[11px] text-emerald-800">
                    çekildi
                  </span>
                ) : null}
              </p>
              <p className="mt-1 text-xs text-slate-500">
                {money(loan.principalAmount)} · aylık %
                {loan.monthlyInterestRate} · {loan.installmentCount} taksit ·
                çekiliş {dateFormat.format(new Date(loan.drawdownDate))}
              </p>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <div className="text-right">
                <p className="text-[11px] uppercase text-slate-400">Kalan anapara</p>
                <p className="font-medium tabular-nums text-slate-900">
                  {money(loan.remainingPrincipal)}
                </p>
              </div>

              <button
                type="button"
                onClick={() => onToggle(loan.id)}
                className="text-xs text-slate-700 hover:underline"
              >
                {openLoanId === loan.id ? "Planı gizle" : "Taksit planı"}
              </button>

              {canEdit && loan.status !== "Cancelled" ? (
                <>
                  {!loan.isDrawn ? (
                    <button
                      type="button"
                      onClick={() => onDrawn(loan)}
                      className="text-xs text-emerald-700 hover:underline"
                    >
                      Çekildi işaretle
                    </button>
                  ) : null}
                  <button
                    type="button"
                    onClick={() => onCancel(loan)}
                    className="text-xs text-rose-600 hover:underline"
                  >
                    İptal
                  </button>
                </>
              ) : null}
            </div>
          </div>

          {openLoanId === loan.id ? (
            <div className="border-t border-slate-100">
              <DataTable
                rows={installments}
                columns={installmentColumns(canEdit, onEditInstallment)}
                rowKey={(row) => row.id}
                title="Kredi Taksit Planı"
                /*
                 * Açılan kredi değişince sayfa 1'e döner. Sekme
                 * değişiminde alt bileşen zaten yeniden bağlanıyor;
                 * asıl gereken yer burası — 60 taksitlik bir planın
                 * 3. sayfasındayken başka krediyi açmak, kullanıcıyı
                 * yeni planın ortasında bırakırdı.
                 */
                resetKey={loan.id}
              />
            </div>
          ) : null}
        </div>
      ))}
    </div>
  );
}

/*
 * TAKSİT SÜTUNLARI FONKSİYON: yetki ve düzenleme işleyicisi PARAMETRE
 * olarak geliyor. Modül düzeyinde sabit bir dizi olsaydı işleyiciyi
 * kapanışa almak gerekirdi ve o da bayat kapanış demekti — düğme eski
 * durumu görüp yanlış taksit üzerinde çalışabilirdi (F4b desen kararı).
 */
function installmentColumns(
  canEdit: boolean,
  onEditInstallment: (installment: BankLoanInstallment) => void
): DataTableColumn<BankLoanInstallment>[] {
  const columns: DataTableColumn<BankLoanInstallment>[] = [
    { key: "no", header: "#", value: (row) => row.number },
    {
      key: "vade",
      header: "Vade",
      value: (row) => dateFormat.format(new Date(row.dueDate)),
    },
    {
      key: "anapara",
      header: "Anapara",
      numeric: true,
      value: (row) => money(row.principalAmount),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.principalAmount, 0)),
    },
    {
      key: "faiz",
      header: "Faiz",
      numeric: true,
      value: (row) => money(row.interestAmount),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.interestAmount, 0)),
    },
    {
      key: "taksit",
      header: "Taksit",
      numeric: true,
      value: (row) => money(row.totalAmount),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.totalAmount, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => (row.isPaid ? "ödendi" : "bekliyor"),
      render: (row) =>
        row.isPaid ? (
          <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[11px] text-emerald-800">
            ödendi
          </span>
        ) : (
          <span className="text-xs text-slate-500">bekliyor</span>
        ),
    },
  ];

  if (canEdit) {
    columns.push({
      key: "duzelt",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          onClick={() => onEditInstallment(row)}
          className="text-xs text-slate-700 hover:underline"
        >
          Düzelt
        </button>
      ),
    });
  }

  return columns;
}

function CardsView({
  cards,
  statements,
}: {
  cards: CreditCard[];
  statements: CreditCardStatement[];
}) {
  const cardColumns: DataTableColumn<CreditCard>[] = [
    {
      key: "kart",
      header: "Kart",
      value: (row) =>
        row.lastFourDigits ? `${row.name} ···· ${row.lastFourDigits}` : row.name,
      render: (row) => (
        <>
          {row.name}
          {row.lastFourDigits ? (
            <span className="ml-1 text-xs text-slate-400">
              ···· {row.lastFourDigits}
            </span>
          ) : null}
        </>
      ),
    },
    { key: "banka", header: "Banka", value: (row) => row.bankName ?? "—" },
    {
      key: "sahibi",
      header: "Sahibi",
      value: (row) =>
        row.ownership === "Personal"
          ? `şahıs · ${row.partnerName ?? "—"}`
          : "Şirket",
      render: (row) =>
        row.ownership === "Personal" ? (
          <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[11px] text-amber-800">
            şahıs · {row.partnerName ?? "—"}
          </span>
        ) : (
          "Şirket"
        ),
    },
    {
      key: "kesim",
      header: "Kesim / Son ödeme",
      value: (row) => `Ayın ${row.statementDay}'i / ${row.dueDay}'i`,
    },
  ];

  const statementColumns: DataTableColumn<CreditCardStatement>[] = [
    { key: "kart", header: "Kart", value: (row) => row.cardName },
    {
      key: "donem",
      header: "Dönem",
      value: (row) =>
        `${dateFormat.format(new Date(row.periodStart))} – ` +
        `${dateFormat.format(new Date(row.periodEnd))} (${row.itemCount} harcama)`,
      render: (row) => (
        <>
          {dateFormat.format(new Date(row.periodStart))} –{" "}
          {dateFormat.format(new Date(row.periodEnd))}
          <span className="ml-1 text-xs text-slate-400">
            ({row.itemCount} harcama)
          </span>
        </>
      ),
    },
    {
      key: "vade",
      header: "Son ödeme",
      value: (row) => dateFormat.format(new Date(row.dueDate)),
    },
    {
      key: "borc",
      header: "Dönem borcu",
      numeric: true,
      value: (row) => money(row.amount),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.amount, 0)),
    },
    {
      key: "nakit",
      header: "Nakit etkisi",
      /*
       * ŞAHIS KARTI ŞİRKET NAKDİNİ ÇIKARMAZ. Dışa aktarmada da bu ayrım
       * görünmeli: nakit akış tahminine giren ile girmeyen aynı sütunda
       * duruyor ve karışırsa tahmin şişer.
       */
      value: (row) =>
        row.producesCashOutflow
          ? "nakit akışta çıkış"
          : "şahıs ödüyor — şirket nakdi çıkmaz",
      render: (row) =>
        row.producesCashOutflow ? (
          <span className="text-xs text-slate-600">nakit akışta çıkış</span>
        ) : (
          <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[11px] text-amber-800">
            şahıs ödüyor — şirket nakdi çıkmaz
          </span>
        ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="overflow-x-auto rounded-lg border border-slate-200">
        <h2 className="border-b border-slate-100 px-3 py-2 text-sm font-medium text-slate-800">
          Kartlar
        </h2>

        {cards.length === 0 ? (
          <p className="p-4 text-sm text-slate-500">
            Tanımlı kart yok. Kart tanımlarsanız harcamalar Gider Merkezi&apos;nden
            girilir, nakit çıkışı ekstre gününde görünür.
          </p>
        ) : (
          <DataTable
            rows={cards}
            columns={cardColumns}
            rowKey={(row) => row.id}
            title="Kredi Kartları"
          />
        )}
      </div>

      <div className="overflow-x-auto rounded-lg border border-slate-200">
        <h2 className="border-b border-slate-100 px-3 py-2 text-sm font-medium text-slate-800">
          Ekstreler
        </h2>

        {statements.length === 0 ? (
          <p className="p-4 text-sm text-slate-500">
            Bu dönemde kart harcaması yok.
          </p>
        ) : (
          <DataTable
            rows={statements}
            columns={statementColumns}
            rowKey={(row) => `${row.creditCardId}-${row.periodEnd}`}
            title="Kredi Kartı Ekstreleri"
          />
        )}
      </div>
    </div>
  );
}
