"use client";

import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { currencyMoney } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  cashAccountService,
  type CashAccount,
} from "@/services/cash-account.service";

import {
  CompanyListItem,
  companyService,
} from "@/services/company.service";

import {
  PersonnelListItem,
  personnelService,
} from "@/services/personnel.service";

import {
  CompanyPayrollCalculationResult,
  MarkPayrollPaidRequest,
  PayrollRecord,
  PayrollStatus,
  PayrollSummary,
  hrPayrollService,
} from "@/services/hr-payroll.service";

import {
  PayrollBankAccount,
  PayrollCashAccount,
  payrollPaymentAccountService,
} from "@/services/payroll-payment-account.service";
import { foldTurkish } from "@/lib/search/fold";

const MONTHS = [
  { value: 1, label: "Ocak" },
  { value: 2, label: "Şubat" },
  { value: 3, label: "Mart" },
  { value: 4, label: "Nisan" },
  { value: 5, label: "Mayıs" },
  { value: 6, label: "Haziran" },
  { value: 7, label: "Temmuz" },
  { value: 8, label: "Ağustos" },
  { value: 9, label: "Eylül" },
  { value: 10, label: "Ekim" },
  { value: 11, label: "Kasım" },
  { value: 12, label: "Aralık" },
];

const CURRENT_DATE = new Date();
const CURRENT_YEAR = CURRENT_DATE.getFullYear();
const CURRENT_MONTH = CURRENT_DATE.getMonth() + 1;

const YEARS = Array.from(
  { length: 7 },
  (_, index) => CURRENT_YEAR - 4 + index
);

function formatMoney(
  value: number,
  currencyCode = "TRY"
): string {
  // "MIXED": dönemde birden fazla para birimi var demek; toplam
  // TL cinsinden gösteriliyor. Bu eşleme korunuyor — kod olarak
  // yazılsaydı ekranda "1.250,00 MIXED" çıkardı.
  return currencyMoney(
    value ?? 0,
    currencyCode === "MIXED" ? "TRY" : currencyCode || "TRY"
  );
}

function getErrorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }

  return "İşlem sırasında beklenmeyen bir hata oluştu.";
}

/**
 * Bordro durumunun rozet varyantı.
 *
 * Eskiden burada bir STİL NESNESİ üretiliyordu (zemin, yazı ve
 * kenarlık için üçer ham hex) ve satır içinde yayılıyordu. Rozetin
 * kendisi zaten paylaşılan bir bileşen: `erp-status` yuvarlak köşeyi,
 * dolguyu ve tipografiyi veriyor, Redwood katmanı da renkleri
 * tokendan okuyor. Sınıfa bağlanınca sekiz hex birden kalktı ve
 * koyu tema turunda bu rozet ayrıca ele alınmayacak.
 *
 * Renk sırası ilerlemeyi anlatıyor: taslak gri, hesaplandı sarı
 * (bekliyor), onaylandı mavi (karar verildi), ödendi yeşil (bitti).
 */
function statusVariant(status: PayrollStatus): string {
  switch (status) {
    case PayrollStatus.Paid:
      return "green";

    case PayrollStatus.Approved:
      return "blue";

    case PayrollStatus.Calculated:
      return "yellow";

    default:
      return "gray";
  }
}

function statusLabel(record: PayrollRecord): string {
  if (record.statusName) {
    switch (record.statusName.toLowerCase()) {
      case "draft":
        return "Taslak";
      case "calculated":
        return "Hesaplandı";
      case "approved":
        return "Onaylandı";
      case "paid":
        return "Ödendi";
      default:
        return record.statusName;
    }
  }

  switch (record.status) {
    case PayrollStatus.Calculated:
      return "Hesaplandı";
    case PayrollStatus.Approved:
      return "Onaylandı";
    case PayrollStatus.Paid:
      return "Ödendi";
    default:
      return "Taslak";
  }
}

const panelStyle = {
  background: "var(--erp-panel)",
  border: "1px solid var(--erp-border)",
  borderRadius: "16px",
  boxShadow: "0 8px 24px rgba(15, 23, 42, 0.05)",
};

const inputStyle = {
  width: "100%",
  minHeight: "42px",
  border: "1px solid var(--erp-border)",
  borderRadius: "10px",
  padding: "8px 12px",
  background: "var(--erp-panel)",
  color: "var(--erp-text)",
};

export default function PayrollManagementPage() {
  /*
   * Aksiyon izinleri UÇLARDAN türetildi (HrPayrollController):
   *   POST   payroll/calculate      -> attendance-payroll.create
   *   POST   records/{id}/approve   -> attendance-payroll.approve
   *   POST   records/{id}/paid      -> attendance-payroll.edit   (!)
   *   DELETE records/{id}           -> attendance-payroll.delete
   *
   * (!) "Ödendi" düğmesi EDIT'e bağlı — avans ekranındaki aynı adlı
   * düğme CREATE'e bağlı. İzin ucun kendisinden okundu.
   */
  const actions = useModuleActions("attendance-payroll");

  /*
   * ÖDEME EYLEMİ AYRI KAPIDA — EKRAN KAPISINDAN DAR.
   *
   * Ödeme işaretlemek banka hesabı listesi gerektiriyor ve o liste
   * `bank_account.view` ile korunuyor. Ekranı `payroll.view` ile
   * açan ama bu anahtarı olmayan rol (bugün: Teknik Koordinatör)
   * ödeme düğmesini HİÇ GÖRMEZ — 403 alıp bozuk ekran görmez.
   * M1/7'deki `canRead` deseninin aynısı.
   */
  const bankActions = useModuleActions("bank_account");
  const odemeYapabilir = bankActions.can("view") && !bankActions.loading;

  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [personnel, setPersonnel] = useState<
    PersonnelListItem[]
  >([]);

  const [records, setRecords] = useState<
    PayrollRecord[]
  >([]);

  const [summary, setSummary] = useState<
    PayrollSummary | null
  >(null);

  const [companyId, setCompanyId] =
    useState("");

  const [year, setYear] =
    useState(CURRENT_YEAR);

  const [month, setMonth] =
    useState(CURRENT_MONTH);

  const [statusFilter, setStatusFilter] =
    useState("");

  const [search, setSearch] =
    useState("");

  const [loading, setLoading] =
    useState(false);

  const [calculating, setCalculating] =
    useState(false);

  // Dönem muhasebeleştirme ve ödeme (kayıt bazlı ödemeden ayrı tutulur)
  const [periodBusy, setPeriodBusy] = useState(false);
  const [periodCashAccounts, setPeriodCashAccounts] = useState<CashAccount[]>([]);
  const [periodCashAccountId, setPeriodCashAccountId] = useState("");
  const [periodPaymentDate, setPeriodPaymentDate] = useState(
    new Date().toISOString().slice(0, 10)
  );

  const [message, setMessage] =
    useState("");

  const [error, setError] =
    useState("");

  const [calculationResult, setCalculationResult] =
    useState<CompanyPayrollCalculationResult | null>(
      null
    );

  const [selectedRecord, setSelectedRecord] =
    useState<PayrollRecord | null>(null);

  /**
   * Onay bekleyen bordro işlemi.
   *
   * Dönem işlemleri (hesapla / muhasebeleştir / öde) tüm şirketi
   * birden etkiliyor; kayıt işlemleri (onayla / sil) tek personeli.
   * Ayrımı tipte tutmak, onay metninin hangisinden bahsettiğini
   * karıştırmayı imkânsız kılıyor.
   */
  const [pending, setPending] = useState<
    | { kind: "calculate" }
    | { kind: "post" }
    | { kind: "pay" }
    | { kind: "approve"; record: PayrollRecord }
    | { kind: "delete"; record: PayrollRecord }
    | null
  >(null);

  const [actionRecordId, setActionRecordId] =
    useState<string | null>(null);

  const [
    paymentRecord,
    setPaymentRecord,
  ] = useState<PayrollRecord | null>(null);

  const [
    paymentMethod,
    setPaymentMethod,
  ] = useState<0 | 1>(0);

  const [
    paymentBankAccountId,
    setPaymentBankAccountId,
  ] = useState("");

  const [
    paymentCashAccountId,
    setPaymentCashAccountId,
  ] = useState("");

  const [
    paymentReference,
    setPaymentReference,
  ] = useState("");

  const [
    paymentDate,
    setPaymentDate,
  ] = useState(
    new Date().toISOString().slice(0, 10)
  );

  const [
    paymentBankAccounts,
    setPaymentBankAccounts,
  ] = useState<PayrollBankAccount[]>([]);

  const [
    paymentCashAccounts,
    setPaymentCashAccounts,
  ] = useState<PayrollCashAccount[]>([]);

  const [
    paymentAccountsLoading,
    setPaymentAccountsLoading,
  ] = useState(false);

  const [
    paymentSubmitting,
    setPaymentSubmitting,
  ] = useState(false);

  // Dönem ödemesi için kasa/banka hesapları
  useEffect(() => {
    if (!companyId) {
      setPeriodCashAccounts([]);
      setPeriodCashAccountId("");
      return;
    }

    let cancelled = false;

    cashAccountService
      .getAll({ companyId })
      .then((result) => {
        if (cancelled) return;
        setPeriodCashAccounts(result);
        setPeriodCashAccountId((current) =>
          current && result.some((x) => x.id === current)
            ? current
            : result[0]?.id ?? ""
        );
      })
      .catch(() => {
        if (!cancelled) setPeriodCashAccounts([]);
      });

    return () => {
      cancelled = true;
    };
  }, [companyId]);

  const loadInitialData = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const companyRows =
        await companyService.getAll();

      setCompanies(companyRows);

      const selectedCompany =
        companyRows.find(
          (item) => item.isActive !== false
        ) ?? companyRows[0];

      if (!selectedCompany) {
        setError(
          "Aktif şirket bulunamadı."
        );
        return;
      }

      setCompanyId((current) =>
        current || selectedCompany.id
      );
    } catch (loadError) {
      setError(
        getErrorMessage(loadError)
      );
    } finally {
      setLoading(false);
    }
  }, []);

  const loadPayrollData = useCallback(async () => {
    if (!companyId) {
      setRecords([]);
      setSummary(null);
      setPersonnel([]);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const [
        payrollRows,
        summaryRow,
        personnelRows,
      ] = await Promise.all([
        hrPayrollService.getAll({
          companyId,
          year,
          month,
          status:
            statusFilter === ""
              ? undefined
              : Number(statusFilter),
        }),
        hrPayrollService.getSummary(
          companyId,
          year,
          month
        ),
        personnelService.getAll({
          companyId,
        }),
      ]);

      setRecords(payrollRows);
      setSummary(summaryRow);
      setPersonnel(personnelRows);
    } catch (loadError) {
      setError(
        getErrorMessage(loadError)
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyId,
    month,
    statusFilter,
    year,
  ]);

  useEffect(() => {
    void loadInitialData();
  }, [loadInitialData]);

  useEffect(() => {
    if (companyId) {
      void loadPayrollData();
    }
  }, [
    companyId,
    loadPayrollData,
  ]);

  const personnelMap = useMemo(() => {
    return new Map(
      personnel.map((item) => [
        item.id,
        item,
      ])
    );
  }, [personnel]);

  const filteredRecords = useMemo(() => {
    const keyword =
      foldTurkish(search);

    if (!keyword) {
      return records;
    }

    return records.filter((record) => {
      const employee =
        personnelMap.get(record.personnelId);

      const searchable = [
        employee?.fullName,
        employee?.employeeNumber,
        employee?.jobTitle,
        record.statusName,
        record.paymentReference,
      ]
        .filter(Boolean)
        .join(" ")
        ;

      return searchable.includes(keyword);
    });
  }, [
    personnelMap,
    records,
    search,
  ]);

  const selectedCompanyName =
    companies.find(
      (item) => item.id === companyId
    )?.name ?? "Şirket";

  const selectedMonthName =
    MONTHS.find(
      (item) => item.value === month
    )?.label ?? "";

  async function postPeriod() {
    if (!companyId) {
      setError("Önce şirket seçmelisiniz.");
      return;
    }

    setPending(null);
    setPeriodBusy(true);
    setError("");
    setMessage("");

    try {
      const result = await hrPayrollService.postPeriod({
        companyId,
        year,
        month,
      });

      setMessage(
        `${result.personnelCount} bordro muhasebeleştirildi. ` +
          `Fiş: ${result.accountingVoucherNumber} — ` +
          `işverene toplam maliyet ${formatMoney(result.totalEmployerCost)}.`
      );

      await loadPayrollData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setPeriodBusy(false);
    }
  }

  async function payPeriod() {
    if (!companyId || !periodCashAccountId) {
      setError("Ödeme için kasa/banka hesabı seçmelisiniz.");
      return;
    }

    setPending(null);
    setPeriodBusy(true);
    setError("");
    setMessage("");

    try {
      const result = await hrPayrollService.payPeriod({
        companyId,
        year,
        month,
        cashAccountId: periodCashAccountId,
        paymentDate: periodPaymentDate,
        paymentReference: null,
      });

      setMessage(
        `${result.personnelCount} bordro ödendi (${formatMoney(result.paidAmount)}). ` +
          `Fiş: ${result.accountingVoucherNumber}.`
      );

      await loadPayrollData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setPeriodBusy(false);
    }
  }

  async function calculateCompanyPayroll() {
    if (!companyId) {
      setError(
        "Önce şirket seçmelisiniz."
      );
      return;
    }

    setPending(null);
    setCalculating(true);
    setError("");
    setMessage("");
    setCalculationResult(null);

    try {
      const result =
        await hrPayrollService.calculateCompany({
          companyId,
          year,
          month,
          recalculateExisting: true,
        });

      setCalculationResult(result);

      setMessage(
        `${result.personnelCount} personel işlendi. ` +
        `${result.createdCount} bordro oluşturuldu, ` +
        `${result.updatedCount} bordro güncellendi.`
      );

      await loadPayrollData();
    } catch (calculateError) {
      setError(
        getErrorMessage(calculateError)
      );
    } finally {
      setCalculating(false);
    }
  }

  async function approvePayroll(
    record: PayrollRecord
  ) {
    const employee =
      personnelMap.get(record.personnelId);

    setPending(null);
    setActionRecordId(record.id);
    setError("");
    setMessage("");

    try {
      const updated =
        await hrPayrollService.approve(record.id);

      setMessage(
        `${employee?.fullName ?? "Personel"} bordrosu onaylandı.`
      );

      setSelectedRecord((current) =>
        current?.id === record.id
          ? updated
          : current
      );

      await loadPayrollData();
    } catch (approveError) {
      setError(
        getErrorMessage(approveError)
      );
    } finally {
      setActionRecordId(null);
    }
  }

  async function markPayrollPaid(
    record: PayrollRecord
  ) {
    setError("");
    setMessage("");
    setPaymentRecord(record);
    setPaymentMethod(0);
    setPaymentReference(
      record.paymentReference ?? ""
    );
    setPaymentDate(
      new Date().toISOString().slice(0, 10)
    );
    setPaymentBankAccountId("");
    setPaymentCashAccountId("");
    setPaymentAccountsLoading(true);

    try {
      const [
        bankRows,
        cashRows,
      ] = await Promise.all([
        payrollPaymentAccountService
          .getBankAccounts(record.companyId),
        payrollPaymentAccountService
          .getCashAccounts(record.companyId),
      ]);

      const currency =
        record.currencyCode || "TRY";

      /*
       * PARA BİRİMİ EKSİKSE HESAP ELENİR.
       *
       * `currencyCode` artık isteğe bağlı (modelde de `string?`).
       * Boşsa hesabı listeye ALMIYORUZ: hangi para biriminde olduğu
       * bilinmeyen bir hesaba ödeme işaretlemek, yanlış kurdan
       * ödeme kaydı üretebilir. Dar taraf.
       */
      const compatibleBanks =
        bankRows.filter(
          (item) =>
            (item.currencyCode ?? "").toUpperCase() ===
            currency.toUpperCase()
        );

      const compatibleCash =
        cashRows.filter(
          (item) =>
            item.currencyCode
              .toUpperCase() ===
            currency.toUpperCase()
        );

      setPaymentBankAccounts(
        compatibleBanks
      );

      setPaymentCashAccounts(
        compatibleCash
      );

      if (compatibleBanks.length > 0) {
        setPaymentMethod(0);
        setPaymentBankAccountId(
          compatibleBanks[0].id
        );
      } else if (
        compatibleCash.length > 0
      ) {
        setPaymentMethod(1);
        setPaymentCashAccountId(
          compatibleCash[0].id
        );
      }
    } catch (accountError) {
      setError(
        getErrorMessage(accountError)
      );
    } finally {
      setPaymentAccountsLoading(false);
    }
  }

  function closePaymentModal() {
    if (paymentSubmitting) {
      return;
    }

    setPaymentRecord(null);
    setPaymentBankAccountId("");
    setPaymentCashAccountId("");
    setPaymentReference("");
  }

  async function submitPayrollPayment() {
    if (!paymentRecord) {
      return;
    }

    if (
      paymentMethod === 0 &&
      !paymentBankAccountId
    ) {
      setError(
        "Ödeme yapılacak banka hesabını seçiniz."
      );
      return;
    }

    if (
      paymentMethod === 1 &&
      !paymentCashAccountId
    ) {
      setError(
        "Ödeme yapılacak kasa hesabını seçiniz."
      );
      return;
    }

    if (!paymentDate) {
      setError(
        "Ödeme tarihini seçiniz."
      );
      return;
    }

    const employee =
      personnelMap.get(
        paymentRecord.personnelId
      );

    const payload:
      MarkPayrollPaidRequest = {
        paymentReference:
          paymentReference.trim() || null,
        paymentMethod,
        bankAccountId:
          paymentMethod === 0
            ? paymentBankAccountId
            : null,
        cashAccountId:
          paymentMethod === 1
            ? paymentCashAccountId
            : null,
        paymentDate,
      };

    setPaymentSubmitting(true);
    setActionRecordId(
      paymentRecord.id
    );
    setError("");
    setMessage("");

    try {
      const updated =
        await hrPayrollService.markPaid(
          paymentRecord.id,
          payload
        );

      setSelectedRecord((current) =>
        current?.id ===
        paymentRecord.id
          ? updated
          : current
      );

      setMessage(
        `${employee?.fullName ?? "Personel"} bordro ödemesi tamamlandı ve muhasebeleştirildi.`
      );

      setPaymentRecord(null);

      await loadPayrollData();
    } catch (paidError) {
      setError(
        getErrorMessage(paidError)
      );
    } finally {
      setPaymentSubmitting(false);
      setActionRecordId(null);
    }
  }

  async function deletePayroll(
    record: PayrollRecord
  ) {
    const employee =
      personnelMap.get(record.personnelId);

    setPending(null);
    setActionRecordId(record.id);
    setError("");
    setMessage("");

    try {
      await hrPayrollService.delete(record.id);

      setMessage(
        `${employee?.fullName ?? "Personel"} bordrosu silindi.`
      );

      setSelectedRecord((current) =>
        current?.id === record.id
          ? null
          : current
      );

      await loadPayrollData();
    } catch (deleteError) {
      setError(
        getErrorMessage(deleteError)
      );
    } finally {
      setActionRecordId(null);
    }
  }

  function printPayroll(
    record: PayrollRecord
  ) {
    const employee =
      personnelMap.get(record.personnelId);

    const monthName =
      MONTHS.find(
        (item) => item.value === record.month
      )?.label ?? String(record.month);

    const officialNet =
      record.officialNetPayableAmount;

    const actualNet =
      record.actualPayableAmount ||
      record.netPayableAmount;

    const printWindow = window.open(
      "",
      "_blank",
      "width=980,height=760"
    );

    if (!printWindow) {
      setError(
        "Yazdırma penceresi açılamadı. Tarayıcı açılır pencere iznini kontrol edin."
      );
      return;
    }

    const money = (value: number) =>
      formatMoney(
        value,
        record.currencyCode
      );

    printWindow.document.write(`
      <!DOCTYPE html>
      <html lang="tr">
      <head>
        <meta charset="utf-8" />
        <title>
          ${employee?.fullName ?? "Personel"} Bordrosu
        </title>

        <style>
          * {
            box-sizing: border-box;
          }

          body {
            margin: 0;
            padding: 32px;
            color: var(--erp-text);
            font-family: Arial, Helvetica, sans-serif;
          }

          .header {
            display: flex;
            justify-content: space-between;
            gap: 24px;
            border-bottom: 3px solid var(--erp-text);
            padding-bottom: 18px;
            margin-bottom: 22px;
          }

          .header h1 {
            margin: 0 0 6px;
            font-size: 24px;
          }

          .header p {
            margin: 3px 0;
            color: var(--erp-muted);
          }

          .period {
            text-align: right;
          }

          .grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 18px;
          }

          .panel {
            border: 1px solid var(--erp-border);
            border-radius: 10px;
            overflow: hidden;
          }

          .panel h2 {
            margin: 0;
            padding: 11px 14px;
            font-size: 14px;
            background: var(--erp-bg);
          }

          .row {
            display: flex;
            justify-content: space-between;
            gap: 16px;
            padding: 9px 14px;
            border-top: 1px solid var(--erp-border);
          }

          .row strong {
            white-space: nowrap;
          }

          .summary {
            margin-top: 22px;
            border: 2px solid var(--erp-text);
            border-radius: 12px;
            padding: 18px;
          }

          .summary-row {
            display: flex;
            justify-content: space-between;
            font-size: 16px;
            margin: 8px 0;
          }

          .actual {
            margin-top: 12px;
            padding-top: 12px;
            border-top: 2px solid var(--erp-text);
            font-size: 20px;
            font-weight: 800;
          }

          .footer {
            margin-top: 36px;
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 36px;
            text-align: center;
          }

          .signature {
            padding-top: 55px;
            border-bottom: 1px solid var(--erp-muted);
          }

          @media print {
            body {
              padding: 12px;
            }
          }
        </style>
      </head>

      <body>
        <header class="header">
          <div>
            <h1>ENDERUN AI · PERSONEL BORDROSU</h1>
            <p>
              <strong>${employee?.fullName ?? "Personel"}</strong>
            </p>
            <p>
              Sicil:
              ${employee?.employeeNumber ?? "—"}
            </p>
            <p>
              Görev:
              ${employee?.jobTitle ?? "—"}
            </p>
          </div>

          <div class="period">
            <p>
              <strong>${monthName} ${record.year}</strong>
            </p>
            <p>
              Durum:
              ${statusLabel(record)}
            </p>
            <p>
              Para birimi:
              ${record.currencyCode}
            </p>
          </div>
        </header>

        <div class="grid">
          <section class="panel">
            <h2>KAZANÇLAR</h2>

            <div class="row">
              <span>Brüt ücret</span>
              <strong>${money(record.grossSalary)}</strong>
            </div>

            <div class="row">
              <span>Normal çalışma</span>
              <strong>${money(record.normalWorkAmount)}</strong>
            </div>

            <div class="row">
              <span>Fazla mesai</span>
              <strong>${money(record.overtimeAmount)}</strong>
            </div>

            <div class="row">
              <span>Pazar çalışması</span>
              <strong>${money(record.sundayWorkAmount)}</strong>
            </div>

            <div class="row">
              <span>Resmî tatil</span>
              <strong>${money(record.publicHolidayAmount)}</strong>
            </div>

            <div class="row">
              <span>Prim</span>
              <strong>${money(record.bonusAmount)}</strong>
            </div>

            <div class="row">
              <span>Yemek</span>
              <strong>${money(record.mealAmount)}</strong>
            </div>

            <div class="row">
              <span>Yol</span>
              <strong>${money(record.travelAmount)}</strong>
            </div>

            <div class="row">
              <span>Diğer kazanç</span>
              <strong>${money(record.otherEarningAmount)}</strong>
            </div>

            <div class="row">
              <span>Ek ücret bileşenleri</span>
              <strong>${money(record.compensationAmount)}</strong>
            </div>

            <div class="row">
              <span>Toplam kazanç</span>
              <strong>${money(record.totalEarnings)}</strong>
            </div>
          </section>

          <section class="panel">
            <h2>KESİNTİLER</h2>

            <div class="row">
              <span>SGK işçi kesintisi</span>
              <strong>${money(record.sgkEmployeeDeduction)}</strong>
            </div>

            <div class="row">
              <span>Gelir vergisi</span>
              <strong>${money(record.incomeTaxDeduction)}</strong>
            </div>

            <div class="row">
              <span>Damga vergisi</span>
              <strong>${money(record.stampTaxDeduction)}</strong>
            </div>

            <div class="row">
              <span>Avans mahsubu</span>
              <strong>${money(record.advanceDeduction)}</strong>
            </div>

            <div class="row">
              <span>Diğer kesinti</span>
              <strong>${money(record.otherDeductionAmount)}</strong>
            </div>

            <div class="row">
              <span>Toplam kesinti</span>
              <strong>${money(record.totalDeductions)}</strong>
            </div>
          </section>
        </div>

        <section class="summary">
          <div class="summary-row">
            <span>Resmî net ödeme</span>
            <strong>${money(officialNet)}</strong>
          </div>

          <div class="summary-row">
            <span>Ek ücret bileşenleri</span>
            <strong>+ ${money(record.compensationAmount)}</strong>
          </div>

          <div class="summary-row actual">
            <span>Gerçek ödenecek tutar</span>
            <strong>${money(actualNet)}</strong>
          </div>
        </section>

        ${
          record.paymentReference
            ? `
              <p>
                <strong>Ödeme referansı:</strong>
                ${record.paymentReference}
              </p>
            `
            : ""
        }

        <footer class="footer">
          <div>
            <div class="signature"></div>
            Personel
          </div>

          <div>
            <div class="signature"></div>
            İnsan Kaynakları
          </div>

          <div>
            <div class="signature"></div>
            Onaylayan
          </div>
        </footer>

        <script>
          window.onload = function () {
            window.print();
          };
        </script>
      </body>
      </html>
    `);

    printWindow.document.close();
  }

  const currencyCode =
    summary?.currencyCode || "TRY";

  const statCards = [
    {
      label: "Personel/Bordro",
      value: String(
        summary?.payrollCount ?? records.length
      ),
      detail:
        `${summary?.approvedCount ?? 0} onaylı · ` +
        `${summary?.paidCount ?? 0} ödendi`,
    },
    {
      label: "Toplam Brüt",
      value: formatMoney(
        summary?.totalGrossSalary ?? 0,
        currencyCode
      ),
      detail: "Dönem brüt ücret toplamı",
    },
    {
      label: "Ek Ücret",
      value: formatMoney(
        summary?.totalCompensationAmount ?? 0,
        currencyCode
      ),
      detail:
        "Ücret kartlarından gelen ek ödemeler",
    },
    {
      label: "Toplam Kesinti",
      value: formatMoney(
        summary?.totalDeductions ?? 0,
        currencyCode
      ),
      detail:
        "SGK, vergi, avans ve diğer kesintiler",
    },
    {
      label: "Resmî Net",
      value: formatMoney(
        summary?.totalOfficialNetPayableAmount ?? 0,
        currencyCode
      ),
      detail:
        "Ek ücretler hariç net bordro",
    },
    {
      label: "Gerçek Ödeme",
      value: formatMoney(
        summary?.totalNetPayableAmount ?? 0,
        currencyCode
      ),
      detail:
        "Personele toplam ödenecek tutar",
    },
  ];

  /**
   * Onay diyaloğunun metni ve eylemi.
   *
   * JSX içinde üçlü zincirle yazılamıyor: TypeScript `pending.kind`
   * ayrımını ifade zincirinde daraltamadığı için kayıt işlemlerinde
   * `record` alanına erişemiyordu. Deyim bazlı if/else doğru daraltıyor
   * ve her dalın metni tek yerde okunuyor.
   */
  const dialogProps = (() => {
    if (!pending) return null;

    const period = `${selectedMonthName} ${year}`;

    if (pending.kind === "calculate") {
      return {
        title: "Dönem Bordrosunu Hesapla",
        description: `${selectedCompanyName} için ${period} dönemi bordroları hesaplanacak. Mevcut hesaplamalar yeniden üretilir; onaylanmış bordrolar etkilenmez.`,
        confirmLabel: "Hesapla",
        busy: calculating,
        onConfirm: () => void calculateCompanyPayroll(),
      };
    }

    if (pending.kind === "post") {
      return {
        title: "Bordroyu Muhasebeleştir",
        description: `${period} dönemi bordrosu muhasebeleştirilecek. TEK BİR TAHAKKUK FİŞİ üretilir ve bu işlem GERİ ALINAMAZ.`,
        confirmLabel: "Muhasebeleştir",
        busy: periodBusy,
        onConfirm: () => void postPeriod(),
      };
    }

    if (pending.kind === "pay") {
      return {
        title: "Net Ücretleri Öde",
        description: `${period} dönemi net ücretleri seçilen kasa/banka hesabından ödenecek. Ödeme kaydı oluşur ve hesap bakiyesi düşer.`,
        confirmLabel: "Ödemeyi Yap",
        busy: periodBusy,
        onConfirm: () => void payPeriod(),
      };
    }

    const record = pending.record;
    const name =
      personnelMap.get(record.personnelId)?.fullName ?? "Personel";

    if (pending.kind === "approve") {
      return {
        title: "Bordroyu Onayla",
        description: `${name} bordrosu onaylanacak. Onaylanan bordro artık yeniden hesaplanmaz.`,
        confirmLabel: "Bordroyu Onayla",
        busy: actionRecordId === record.id,
        onConfirm: () => void approvePayroll(record),
      };
    }

    return {
      title: "Bordroyu Sil",
      description: `${name} bordrosu kalıcı olarak silinecek. Bu işlem GERİ ALINAMAZ.`,
      confirmLabel: "Bordroyu Sil",
      busy: actionRecordId === record.id,
      onConfirm: () => void deletePayroll(record),
    };
  })();

  return (
    <ErpShell
      design="redwood"
      title="Bordro Yönetim Merkezi"
      description={
        "Resmî bordro, ek ücret, kesinti ve gerçek ödeme yönetimi"
      }
    >
      <section
        style={{
          ...panelStyle,
          padding: "20px",
          marginBottom: "18px",
        }}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns:
              "minmax(220px, 2fr) repeat(3, minmax(130px, 1fr)) auto auto",
            gap: "12px",
            alignItems: "end",
          }}
        >
          <label>
            <span
              style={{
                display: "block",
                marginBottom: "6px",
                fontSize: "12px",
                fontWeight: 700,
                color: "var(--erp-muted)",
              }}
            >
              ŞİRKET
            </span>

            <select
              style={inputStyle}
              value={companyId}
              onChange={(event) =>
                setCompanyId(event.target.value)
              }
            >
              <option value="">
                Şirket seçiniz
              </option>

              {companies.map((company) => (
                <option
                  key={company.id}
                  value={company.id}
                >
                  {company.code
                    ? `${company.code} · ${company.name}`
                    : company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span
              style={{
                display: "block",
                marginBottom: "6px",
                fontSize: "12px",
                fontWeight: 700,
                color: "var(--erp-muted)",
              }}
            >
              YIL
            </span>

            <select
              style={inputStyle}
              value={year}
              onChange={(event) =>
                setYear(Number(event.target.value))
              }
            >
              {YEARS.map((item) => (
                <option
                  key={item}
                  value={item}
                >
                  {item}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span
              style={{
                display: "block",
                marginBottom: "6px",
                fontSize: "12px",
                fontWeight: 700,
                color: "var(--erp-muted)",
              }}
            >
              AY
            </span>

            <select
              style={inputStyle}
              value={month}
              onChange={(event) =>
                setMonth(Number(event.target.value))
              }
            >
              {MONTHS.map((item) => (
                <option
                  key={item.value}
                  value={item.value}
                >
                  {item.label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span
              style={{
                display: "block",
                marginBottom: "6px",
                fontSize: "12px",
                fontWeight: 700,
                color: "var(--erp-muted)",
              }}
            >
              DURUM
            </span>

            <select
              style={inputStyle}
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(event.target.value)
              }
            >
              <option value="">Tümü</option>
              <option value="0">Taslak</option>
              <option value="1">Hesaplandı</option>
              <option value="2">Onaylandı</option>
              <option value="3">Ödeme Yap</option>
            </select>
          </label>

          <button
            type="button"
            onClick={() =>
              void loadPayrollData()
            }
            disabled={loading}
            style={{
              minHeight: "42px",
              border: "1px solid var(--erp-border)",
              borderRadius: "10px",
              padding: "0 16px",
              background: "var(--erp-panel)",
              fontWeight: 700,
              cursor: "pointer",
            }}
          >
            {loading
              ? "Yükleniyor..."
              : "Yenile"}
          </button>

          {actions.can("create") && (
            <button
              type="button"
              onClick={() =>
                setPending({ kind: "calculate" })
              }
              disabled={
                calculating || !companyId
              }
              style={{
                minHeight: "42px",
                border: "none",
                borderRadius: "10px",
                padding: "0 18px",
                background: "var(--erp-text)",
                color: "var(--color-on-brand)",
                fontWeight: 800,
                cursor:
                  calculating
                    ? "wait"
                    : "pointer",
                opacity:
                  calculating || !companyId
                    ? 0.65
                    : 1,
              }}
            >
              {calculating
                ? "Hesaplanıyor..."
                : "Toplu Bordro Hesapla"}
            </button>
          )}

          {actions.can("approve") && (
            <button
              type="button"
              onClick={() => setPending({ kind: "post" })}
              disabled={periodBusy || !companyId}
              style={{
                minHeight: "42px",
                border: "1px solid var(--erp-text)",
                borderRadius: "10px",
                padding: "0 18px",
                background: "var(--erp-panel)",
                color: "var(--erp-text)",
                fontWeight: 800,
                cursor: periodBusy ? "wait" : "pointer",
                opacity: periodBusy || !companyId ? 0.65 : 1,
              }}
            >
              Dönemi Muhasebeleştir
            </button>
          )}

          <select
            value={periodCashAccountId}
            onChange={(event) =>
              setPeriodCashAccountId(event.target.value)
            }
            style={{
              minHeight: "42px",
              borderRadius: "10px",
              border: "1px solid var(--erp-border)",
              padding: "0 12px",
            }}
          >
            <option value="">Ödeme hesabı seçin</option>
            {periodCashAccounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.code} - {account.name}
              </option>
            ))}
          </select>

          <input
            type="date"
            value={periodPaymentDate}
            onChange={(event) => setPeriodPaymentDate(event.target.value)}
            style={{
              minHeight: "42px",
              borderRadius: "10px",
              border: "1px solid var(--erp-border)",
              padding: "0 12px",
            }}
          />

          {actions.can("edit") && (
            <button
              type="button"
              onClick={() => setPending({ kind: "pay" })}
              disabled={periodBusy || !companyId || !periodCashAccountId}
              style={{
                minHeight: "42px",
                border: "none",
                borderRadius: "10px",
                padding: "0 18px",
                background: "var(--color-semantic-success)",
                color: "var(--color-on-brand)",
                fontWeight: 800,
                cursor: periodBusy ? "wait" : "pointer",
                opacity:
                  periodBusy || !companyId || !periodCashAccountId ? 0.65 : 1,
              }}
            >
              Dönemi Öde
            </button>
          )}
        </div>

        {error && (
          <div
            style={{
              marginTop: "14px",
              padding: "12px 14px",
              borderRadius: "10px",
              background: "var(--color-semantic-danger-tint)",
              border: "1px solid var(--color-semantic-danger-border)",
              color: "var(--color-semantic-danger)",
            }}
          >
            {error}
          </div>
        )}

        {message && (
          <div
            style={{
              marginTop: "14px",
              padding: "12px 14px",
              borderRadius: "10px",
              background: "var(--color-semantic-success-tint)",
              border: "1px solid var(--color-semantic-success-border)",
              color: "var(--color-semantic-success)",
            }}
          >
            {message}
          </div>
        )}

        {calculationResult && (
          <div
            style={{
              marginTop: "12px",
              fontSize: "13px",
              color: "var(--erp-muted)",
            }}
          >
            Toplam gerçek ödeme:{" "}
            <strong>
              {formatMoney(
                calculationResult
                  .totalNetPayableAmount,
                currencyCode
              )}
            </strong>
            {" · "}
            Atlanan kayıt:{" "}
            <strong>
              {calculationResult.skippedCount}
            </strong>

            {(calculationResult.warnings?.length ?? 0) > 0 && (
              <ul
                style={{
                  marginTop: "10px",
                  padding: "10px 12px 10px 28px",
                  border: "1px solid var(--color-semantic-warning-border)",
                  background: "var(--color-semantic-warning-tint)",
                  borderRadius: "8px",
                  color: "var(--color-semantic-warning)",
                }}
              >
                {calculationResult.warnings?.map((warning) => (
                  <li key={warning}>{warning}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </section>

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(190px, 1fr))",
          gap: "14px",
          marginBottom: "18px",
        }}
      >
        {statCards.map((card) => (
          <article
            key={card.label}
            style={{
              ...panelStyle,
              padding: "18px",
            }}
          >
            <span
              style={{
                display: "block",
                fontSize: "11px",
                fontWeight: 800,
                letterSpacing: "0.08em",
                color: "var(--erp-muted)",
                marginBottom: "10px",
              }}
            >
              {card.label.toLocaleUpperCase(
                "tr-TR"
              )}
            </span>

            <strong
              style={{
                display: "block",
                fontSize: "22px",
                color: "var(--erp-text)",
                marginBottom: "6px",
              }}
            >
              {card.value}
            </strong>

            <small
              style={{
                color: "var(--erp-muted)",
                lineHeight: 1.4,
              }}
            >
              {card.detail}
            </small>
          </article>
        ))}
      </section>

      <section
        style={{
          ...panelStyle,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "18px 20px",
            borderBottom: "1px solid var(--erp-border)",
            display: "flex",
            gap: "16px",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <div>
            <span
              style={{
                fontSize: "11px",
                fontWeight: 800,
                letterSpacing: "0.08em",
                color: "var(--erp-muted)",
              }}
            >
              {selectedMonthName.toLocaleUpperCase(
                "tr-TR"
              )}{" "}
              {year}
            </span>

            <h2
              style={{
                margin: "5px 0 0",
                fontSize: "19px",
                color: "var(--erp-text)",
              }}
            >
              Personel bordroları
            </h2>
          </div>

          <input
            value={search}
            onChange={(event) =>
              setSearch(event.target.value)
            }
            placeholder={
              "Personel, sicil veya durum ara..."
            }
            style={{
              ...inputStyle,
              width: "300px",
            }}
          />
        </div>

        <div
          style={{
            overflowX: "auto",
          }}
        >
          <table
            style={{
              width: "100%",
              borderCollapse: "collapse",
              minWidth: "1380px",
            }}
          >
            <thead>
              <tr
                style={{
                  background: "var(--erp-bg)",
                  color: "var(--erp-muted)",
                  textAlign: "left",
                }}
              >
                {[
                  "Personel",
                  "Brüt",
                  "Normal",
                  "Fazla Mesai",
                  "Prim",
                  "Ek Ücret",
                  "Kesinti",
                  "Resmî Net",
                  "Gerçek Ödeme",
                  "Durum",
                  "İşlemler",
                ].map((column) => (
                  <th
                    key={column}
                    style={{
                      padding: "13px 14px",
                      borderBottom:
                        "1px solid var(--erp-border)",
                      fontSize: "12px",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {column}
                  </th>
                ))}
              </tr>
            </thead>

            <tbody>
              {!loading &&
                filteredRecords.length === 0 && (
                  <tr>
                    <td
                      colSpan={11}
                      style={{
                        padding: "44px",
                        textAlign: "center",
                        color: "var(--erp-muted)",
                      }}
                    >
                      Bu dönem için bordro kaydı
                      bulunamadı.
                    </td>
                  </tr>
                )}

              {filteredRecords.map((record) => {
                const employee =
                  personnelMap.get(
                    record.personnelId
                  );

                return (
                  <tr
                    key={record.id}
                    style={{
                      borderBottom:
                        "1px solid var(--erp-border)",
                    }}
                  >
                    <td
                      style={{
                        padding: "14px",
                      }}
                    >
                      <strong
                        style={{
                          display: "block",
                          color: "var(--erp-text)",
                        }}
                      >
                        {employee?.fullName ??
                          "Personel"}
                      </strong>

                      <small
                        style={{
                          color: "var(--erp-muted)",
                        }}
                      >
                        {employee?.employeeNumber ??
                          record.personnelId}
                        {employee?.jobTitle
                          ? ` · ${employee.jobTitle}`
                          : ""}
                      </small>
                    </td>

                    {[
                      record.grossSalary,
                      record.normalWorkAmount,
                      record.overtimeAmount,
                      record.bonusAmount,
                      record.compensationAmount,
                      record.totalDeductions,
                      record.officialNetPayableAmount,
                      record.actualPayableAmount ||
                        record.netPayableAmount,
                    ].map((amount, index) => (
                      <td
                        key={index}
                        style={{
                          padding: "14px",
                          whiteSpace: "nowrap",
                          fontWeight:
                            index === 7
                              ? 800
                              : 500,
                          color:
                            index === 7
                              ? "var(--erp-primary)"
                              : "var(--erp-muted)",
                        }}
                      >
                        {formatMoney(
                          amount,
                          record.currencyCode
                        )}
                      </td>
                    ))}

                    <td
                      style={{
                        padding: "14px",
                      }}
                    >
                      <span
                        className={`erp-status ${statusVariant(
                          record.status
                        )}`}
                      >
                        {statusLabel(record)}
                      </span>
                    </td>

                    <td
                      style={{
                        padding: "10px 14px",
                        whiteSpace: "nowrap",
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          gap: "6px",
                          alignItems: "center",
                        }}
                      >
                        <button
                          type="button"
                          onClick={() =>
                            setSelectedRecord(record)
                          }
                          style={{
                            border:
                              "1px solid var(--erp-border)",
                            borderRadius: "8px",
                            background: "var(--erp-panel)",
                            padding: "7px 10px",
                            cursor: "pointer",
                            fontWeight: 700,
                          }}
                        >
                          Detay
                        </button>

                        {record.status <
                          PayrollStatus.Approved && actions.can("approve") && (
                          <button
                            type="button"
                            disabled={
                              actionRecordId ===
                              record.id
                            }
                            onClick={() =>
                              setPending({ kind: "approve", record })
                            }
                            style={{
                              border:
                                "1px solid var(--color-semantic-info-border)",
                              borderRadius: "8px",
                              background: "var(--color-semantic-info-tint)",
                              color: "var(--color-semantic-info)",
                              padding: "7px 10px",
                              cursor: "pointer",
                              fontWeight: 700,
                            }}
                          >
                            Onayla
                          </button>
                        )}

                        {record.status ===
                          PayrollStatus.Approved && actions.can("edit") && odemeYapabilir && (
                          <button
                            type="button"
                            disabled={
                              actionRecordId ===
                              record.id
                            }
                            onClick={() =>
                              void markPayrollPaid(
                                record
                              )
                            }
                            style={{
                              border:
                                "1px solid var(--color-semantic-success-border)",
                              borderRadius: "8px",
                              background: "var(--color-semantic-success-tint)",
                              color: "var(--color-semantic-success)",
                              padding: "7px 10px",
                              cursor: "pointer",
                              fontWeight: 700,
                            }}
                          >
                            Ödendi
                          </button>
                        )}

                        <button
                          type="button"
                          onClick={() =>
                            printPayroll(record)
                          }
                          style={{
                            border:
                              "1px solid var(--erp-border)",
                            borderRadius: "8px",
                            background: "var(--erp-bg)",
                            padding: "7px 10px",
                            cursor: "pointer",
                            fontWeight: 700,
                          }}
                        >
                          Yazdır
                        </button>

                        {record.status <
                          PayrollStatus.Approved && actions.can("delete") && (
                          <button
                            type="button"
                            disabled={
                              actionRecordId ===
                              record.id
                            }
                            onClick={() =>
                              setPending({ kind: "delete", record })
                            }
                            style={{
                              border:
                                "1px solid var(--color-semantic-danger-border)",
                              borderRadius: "8px",
                              background: "var(--color-semantic-danger-tint)",
                              color: "var(--color-semantic-danger)",
                              padding: "7px 10px",
                              cursor: "pointer",
                              fontWeight: 700,
                            }}
                          >
                            Sil
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      {selectedRecord && (
        <div
          role="dialog"
          aria-modal="true"
          onClick={() =>
            setSelectedRecord(null)
          }
          style={{
            position: "fixed",
            inset: 0,
            zIndex: 1000,
            display: "flex",
            justifyContent: "flex-end",
            background:
              "rgba(15, 23, 42, 0.52)",
          }}
        >
          <aside
            onClick={(event) =>
              event.stopPropagation()
            }
            style={{
              width: "min(720px, 100%)",
              height: "100%",
              overflowY: "auto",
              background: "var(--erp-panel)",
              boxShadow:
                "-18px 0 40px rgba(15, 23, 42, 0.22)",
            }}
          >
            <header
              style={{
                position: "sticky",
                top: 0,
                zIndex: 2,
                display: "flex",
                justifyContent:
                  "space-between",
                gap: "20px",
                alignItems: "flex-start",
                padding: "22px",
                background: "var(--erp-panel)",
                borderBottom:
                  "1px solid var(--erp-border)",
              }}
            >
              <div>
                <span
                  style={{
                    display: "block",
                    fontSize: "11px",
                    fontWeight: 800,
                    letterSpacing: "0.08em",
                    color: "var(--erp-muted)",
                    marginBottom: "6px",
                  }}
                >
                  BORDRO DETAYI
                </span>

                <h2
                  style={{
                    margin: 0,
                    fontSize: "22px",
                    color: "var(--erp-text)",
                  }}
                >
                  {personnelMap.get(
                    selectedRecord.personnelId
                  )?.fullName ?? "Personel"}
                </h2>

                <p
                  style={{
                    margin: "6px 0 0",
                    color: "var(--erp-muted)",
                  }}
                >
                  {
                    MONTHS.find(
                      (item) =>
                        item.value ===
                        selectedRecord.month
                    )?.label
                  }{" "}
                  {selectedRecord.year}
                  {" · "}
                  {statusLabel(
                    selectedRecord
                  )}
                </p>
              </div>

              <button
                type="button"
                onClick={() =>
                  setSelectedRecord(null)
                }
                style={{
                  width: "38px",
                  height: "38px",
                  border:
                    "1px solid var(--erp-border)",
                  borderRadius: "10px",
                  background: "var(--erp-panel)",
                  cursor: "pointer",
                  fontSize: "20px",
                }}
              >
                ×
              </button>
            </header>

            <div
              style={{
                padding: "22px",
              }}
            >
              <section
                style={{
                  display: "grid",
                  gridTemplateColumns:
                    "repeat(2, minmax(0, 1fr))",
                  gap: "14px",
                  marginBottom: "18px",
                }}
              >
                <article
                  style={{
                    ...panelStyle,
                    padding: "17px",
                  }}
                >
                  <span
                    style={{
                      display: "block",
                      color: "var(--erp-muted)",
                      fontSize: "12px",
                      marginBottom: "8px",
                    }}
                  >
                    RESMÎ NET
                  </span>

                  <strong
                    style={{
                      fontSize: "22px",
                    }}
                  >
                    {formatMoney(
                      selectedRecord
                        .officialNetPayableAmount,
                      selectedRecord.currencyCode
                    )}
                  </strong>
                </article>

                {/* Elden ödeme yalnızca yetkiliye; yetki yoksa panel
                    hiç çizilmez ve "gizlendi" bile denmez değil —
                    aksine açıkça söylenir ki kullanıcı toplamın eksik
                    olduğunu bilsin. */}
                <article
                  style={{
                    ...panelStyle,
                    padding: "17px",
                  }}
                >
                  <span
                    style={{
                      display: "block",
                      color: "var(--erp-muted)",
                      fontSize: "12px",
                      marginBottom: "8px",
                    }}
                  >
                    ELDEN ÖDEME
                  </span>

                  <strong style={{ fontSize: "22px" }}>
                    {selectedRecord.extraPaymentHidden
                      ? "—"
                      : formatMoney(
                          selectedRecord
                            .extraPaymentAmount ?? 0,
                          selectedRecord.currencyCode
                        )}
                  </strong>

                  {selectedRecord.extraPaymentHidden && (
                    <small
                      style={{
                        display: "block",
                        color: "var(--erp-muted)",
                      }}
                    >
                      Görme yetkiniz yok
                    </small>
                  )}
                </article>

                <article
                  style={{
                    ...panelStyle,
                    padding: "17px",
                  }}
                >
                  <span
                    style={{
                      display: "block",
                      color: "var(--erp-muted)",
                      fontSize: "12px",
                      marginBottom: "8px",
                    }}
                  >
                    TOPLAM ELE GEÇEN
                  </span>

                  <strong
                    style={{
                      fontSize: "22px",
                      color: "var(--erp-primary)",
                    }}
                  >
                    {formatMoney(
                      selectedRecord.totalTakeHome ??
                        selectedRecord
                          .officialNetPayableAmount,
                      selectedRecord.currencyCode
                    )}
                  </strong>

                  {selectedRecord.extraPaymentHidden && (
                    <small
                      style={{
                        display: "block",
                        color: "var(--erp-muted)",
                      }}
                    >
                      Elden kısım dahil değil
                    </small>
                  )}
                </article>
              </section>

              <PayrollDetailSection
                title="Kazançlar"
                currencyCode={
                  selectedRecord.currencyCode
                }
                rows={[
                  [
                    "Brüt ücret",
                    selectedRecord.grossSalary,
                  ],
                  [
                    "Normal çalışma",
                    selectedRecord
                      .normalWorkAmount,
                  ],
                  [
                    "Fazla mesai",
                    selectedRecord
                      .overtimeAmount,
                  ],
                  [
                    "Pazar çalışması",
                    selectedRecord
                      .sundayWorkAmount,
                  ],
                  [
                    "Resmî tatil",
                    selectedRecord
                      .publicHolidayAmount,
                  ],
                  [
                    "Prim",
                    selectedRecord.bonusAmount,
                  ],
                  [
                    "Yemek",
                    selectedRecord.mealAmount,
                  ],
                  [
                    "Yol",
                    selectedRecord.travelAmount,
                  ],
                  [
                    "Diğer kazanç",
                    selectedRecord
                      .otherEarningAmount,
                  ],
                  [
                    "Ek ücret bileşenleri",
                    selectedRecord
                      .compensationAmount,
                  ],
                  [
                    "Toplam kazanç",
                    selectedRecord.totalEarnings,
                  ],
                ]}
              />

              <PayrollDetailSection
                title="Kesintiler"
                currencyCode={
                  selectedRecord.currencyCode
                }
                rows={[
                  [
                    "SGK işçi kesintisi",
                    selectedRecord
                      .sgkEmployeeDeduction,
                  ],
                  [
                    "Gelir vergisi",
                    selectedRecord
                      .incomeTaxDeduction,
                  ],
                  [
                    "Damga vergisi",
                    selectedRecord
                      .stampTaxDeduction,
                  ],
                  [
                    "Avans mahsubu",
                    selectedRecord
                      .advanceDeduction,
                  ],
                  [
                    "Diğer kesinti",
                    selectedRecord
                      .otherDeductionAmount,
                  ],
                  [
                    "Toplam kesinti",
                    selectedRecord
                      .totalDeductions,
                  ],
                ]}
              />

              <section
                style={{
                  ...panelStyle,
                  marginTop: "18px",
                  padding: "20px",
                  border: "2px solid var(--erp-text)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    justifyContent:
                      "space-between",
                    marginBottom: "12px",
                  }}
                >
                  <span>
                    Resmî net ödeme
                  </span>

                  <strong>
                    {formatMoney(
                      selectedRecord
                        .officialNetPayableAmount,
                      selectedRecord.currencyCode
                    )}
                  </strong>
                </div>

                <div
                  style={{
                    display: "flex",
                    justifyContent:
                      "space-between",
                    marginBottom: "14px",
                    color: "var(--erp-primary)",
                  }}
                >
                  <span>
                    Ek ücret bileşenleri
                  </span>

                  <strong>
                    +{" "}
                    {formatMoney(
                      selectedRecord
                        .compensationAmount,
                      selectedRecord.currencyCode
                    )}
                  </strong>
                </div>

                <div
                  style={{
                    display: "flex",
                    justifyContent:
                      "space-between",
                    paddingTop: "14px",
                    borderTop:
                      "2px solid var(--erp-text)",
                    fontSize: "20px",
                  }}
                >
                  <strong>
                    Gerçek ödenecek tutar
                  </strong>

                  <strong
                    style={{
                      color: "var(--erp-primary)",
                    }}
                  >
                    {formatMoney(
                      selectedRecord
                        .actualPayableAmount ||
                        selectedRecord
                          .netPayableAmount,
                      selectedRecord.currencyCode
                    )}
                  </strong>
                </div>
              </section>

              {selectedRecord.paymentReference && (
                <div
                  style={{
                    marginTop: "16px",
                    padding: "14px",
                    borderRadius: "10px",
                    background: "var(--erp-bg)",
                    color: "var(--erp-muted)",
                  }}
                >
                  <strong>
                    Ödeme referansı:
                  </strong>{" "}
                  {
                    selectedRecord
                      .paymentReference
                  }
                </div>
              )}

              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: "10px",
                  marginTop: "22px",
                }}
              >
                {selectedRecord.status <
                  PayrollStatus.Approved && (
                  <button
                    type="button"
                    onClick={() =>
                      void approvePayroll(
                        selectedRecord
                      )
                    }
                    disabled={
                      actionRecordId ===
                      selectedRecord.id
                    }
                    style={{
                      border: "none",
                      borderRadius: "10px",
                      background: "var(--color-semantic-info)",
                      color: "var(--color-on-brand)",
                      padding: "11px 16px",
                      fontWeight: 800,
                      cursor: "pointer",
                    }}
                  >
                    Bordroyu Onayla
                  </button>
                )}

                {selectedRecord.status ===
                  PayrollStatus.Approved && odemeYapabilir && (
                  <button
                    type="button"
                    onClick={() =>
                      void markPayrollPaid(
                        selectedRecord
                      )
                    }
                    disabled={
                      actionRecordId ===
                      selectedRecord.id
                    }
                    style={{
                      border: "none",
                      borderRadius: "10px",
                      background: "var(--color-semantic-success)",
                      color: "var(--color-on-brand)",
                      padding: "11px 16px",
                      fontWeight: 800,
                      cursor: "pointer",
                    }}
                  >
                    Bordro Ödemesi
                  </button>
                )}

                <button
                  type="button"
                  onClick={() =>
                    printPayroll(
                      selectedRecord
                    )
                  }
                  style={{
                    border:
                      "1px solid var(--erp-border)",
                    borderRadius: "10px",
                    background: "var(--erp-panel)",
                    padding: "11px 16px",
                    fontWeight: 800,
                    cursor: "pointer",
                  }}
                >
                  Yazdır / PDF Kaydet
                </button>

                {selectedRecord.status <
                  PayrollStatus.Approved && (
                  <button
                    type="button"
                    onClick={() =>
                      void deletePayroll(
                        selectedRecord
                      )
                    }
                    disabled={
                      actionRecordId ===
                      selectedRecord.id
                    }
                    style={{
                      border:
                        "1px solid var(--color-semantic-danger-border)",
                      borderRadius: "10px",
                      background: "var(--color-semantic-danger-tint)",
                      color: "var(--color-semantic-danger)",
                      padding: "11px 16px",
                      fontWeight: 800,
                      cursor: "pointer",
                    }}
                  >
                    Bordroyu Sil
                  </button>
                )}
              </div>
            </div>
          </aside>
        </div>
      )}

      {paymentRecord && (
        <div
          role="presentation"
          onMouseDown={(event) => {
            if (
              event.target ===
              event.currentTarget
            ) {
              closePaymentModal();
            }
          }}
          style={{
            position: "fixed",
            inset: 0,
            zIndex: 1000,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "20px",
            background:
              "rgba(15, 23, 42, 0.62)",
            backdropFilter: "blur(3px)",
          }}
        >
          <section
            role="dialog"
            aria-modal="true"
            aria-labelledby="payroll-payment-title"
            style={{
              width: "100%",
              maxWidth: "620px",
              maxHeight: "92vh",
              overflowY: "auto",
              borderRadius: "20px",
              background: "var(--erp-panel)",
              boxShadow:
                "0 24px 80px rgba(15, 23, 42, 0.3)",
            }}
          >
            <header
              style={{
                display: "flex",
                alignItems: "flex-start",
                justifyContent:
                  "space-between",
                gap: "16px",
                padding: "22px 24px",
                borderBottom:
                  "1px solid var(--erp-border)",
              }}
            >
              <div>
                <h2
                  id="payroll-payment-title"
                  style={{
                    margin: 0,
                    color: "var(--erp-text)",
                    fontSize: "21px",
                  }}
                >
                  Bordro Ödemesi
                </h2>

                <p
                  style={{
                    margin: "7px 0 0",
                    color: "var(--erp-muted)",
                    lineHeight: 1.5,
                  }}
                >
                  {
                    personnelMap.get(
                      paymentRecord
                        .personnelId
                    )?.fullName ??
                    "Personel"
                  }
                  {" · "}
                  {formatMoney(
                    paymentRecord
                      .actualPayableAmount ||
                      paymentRecord
                        .netPayableAmount,
                    paymentRecord.currencyCode
                  )}
                </p>
              </div>

              <button
                type="button"
                aria-label="Kapat"
                onClick={
                  closePaymentModal
                }
                disabled={
                  paymentSubmitting
                }
                style={{
                  width: "38px",
                  height: "38px",
                  border:
                    "1px solid var(--erp-border)",
                  borderRadius: "10px",
                  background: "var(--erp-panel)",
                  color: "var(--erp-muted)",
                  cursor: paymentSubmitting
                    ? "not-allowed"
                    : "pointer",
                  fontSize: "22px",
                  lineHeight: 1,
                }}
              >
                ×
              </button>
            </header>

            <div
              style={{
                display: "grid",
                gap: "20px",
                padding: "24px",
              }}
            >
              <div>
                <label
                  style={{
                    display: "block",
                    marginBottom: "9px",
                    color: "var(--erp-muted)",
                    fontWeight: 800,
                  }}
                >
                  Ödeme yöntemi
                </label>

                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns:
                      "repeat(2, minmax(0, 1fr))",
                    gap: "12px",
                  }}
                >
                  <button
                    type="button"
                    onClick={() =>
                      setPaymentMethod(0)
                    }
                    style={{
                      minHeight: "50px",
                      border:
                        paymentMethod === 0
                          ? "2px solid var(--color-semantic-info)"
                          : "1px solid var(--erp-border)",
                      borderRadius: "12px",
                      background:
                        paymentMethod === 0
                          ? "var(--color-semantic-info-tint)"
                          : "var(--erp-panel)",
                      color:
                        paymentMethod === 0
                          ? "var(--color-semantic-info)"
                          : "var(--erp-muted)",
                      fontWeight: 800,
                      cursor: "pointer",
                    }}
                  >
                    Banka
                  </button>

                  <button
                    type="button"
                    onClick={() =>
                      setPaymentMethod(1)
                    }
                    style={{
                      minHeight: "50px",
                      border:
                        paymentMethod === 1
                          ? "2px solid var(--color-semantic-success)"
                          : "1px solid var(--erp-border)",
                      borderRadius: "12px",
                      background:
                        paymentMethod === 1
                          ? "var(--color-semantic-success-tint)"
                          : "var(--erp-panel)",
                      color:
                        paymentMethod === 1
                          ? "var(--color-semantic-success)"
                          : "var(--erp-muted)",
                      fontWeight: 800,
                      cursor: "pointer",
                    }}
                  >
                    Kasa
                  </button>
                </div>
              </div>

              {paymentMethod === 0 ? (
                <div>
                  <label
                    htmlFor="payroll-bank-account"
                    style={{
                      display: "block",
                      marginBottom: "8px",
                      color: "var(--erp-muted)",
                      fontWeight: 800,
                    }}
                  >
                    Banka hesabı
                  </label>

                  <select
                    id="payroll-bank-account"
                    value={
                      paymentBankAccountId
                    }
                    onChange={(event) =>
                      setPaymentBankAccountId(
                        event.target.value
                      )
                    }
                    disabled={
                      paymentAccountsLoading
                    }
                    style={inputStyle}
                  >
                    <option value="">
                      Banka hesabı seçiniz
                    </option>

                    {paymentBankAccounts.map(
                      (account) => (
                        <option
                          key={account.id}
                          value={account.id}
                        >
                          {account.bankName}
                          {" · "}
                          {account.accountHolder ?? "—"}
                          {" · "}
                          {
                            account.currencyCode
                          }
                          {account.ibanMasked
                            ? ` · ${account.ibanMasked}`
                            : ""}
                        </option>
                      )
                    )}
                  </select>

                  {!paymentAccountsLoading &&
                    paymentBankAccounts
                      .length === 0 && (
                      <p
                        style={{
                          margin:
                            "8px 0 0",
                          color: "var(--color-semantic-danger)",
                          fontSize: "13px",
                          fontWeight: 700,
                        }}
                      >
                        Bu şirket ve para
                        birimi için uygun banka
                        hesabı bulunamadı.
                      </p>
                    )}
                </div>
              ) : (
                <div>
                  <label
                    htmlFor="payroll-cash-account"
                    style={{
                      display: "block",
                      marginBottom: "8px",
                      color: "var(--erp-muted)",
                      fontWeight: 800,
                    }}
                  >
                    Kasa hesabı
                  </label>

                  <select
                    id="payroll-cash-account"
                    value={
                      paymentCashAccountId
                    }
                    onChange={(event) =>
                      setPaymentCashAccountId(
                        event.target.value
                      )
                    }
                    disabled={
                      paymentAccountsLoading
                    }
                    style={inputStyle}
                  >
                    <option value="">
                      Kasa hesabı seçiniz
                    </option>

                    {paymentCashAccounts.map(
                      (account) => (
                        <option
                          key={account.id}
                          value={account.id}
                        >
                          {account.code}
                          {" · "}
                          {account.name}
                          {" · "}
                          {
                            account.currencyCode
                          }
                        </option>
                      )
                    )}
                  </select>

                  {!paymentAccountsLoading &&
                    paymentCashAccounts
                      .length === 0 && (
                      <p
                        style={{
                          margin:
                            "8px 0 0",
                          color: "var(--color-semantic-danger)",
                          fontSize: "13px",
                          fontWeight: 700,
                        }}
                      >
                        Bu şirket ve para
                        birimi için uygun kasa
                        hesabı bulunamadı.
                      </p>
                    )}
                </div>
              )}

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns:
                    "repeat(2, minmax(0, 1fr))",
                  gap: "14px",
                }}
              >
                <div>
                  <label
                    htmlFor="payroll-payment-date"
                    style={{
                      display: "block",
                      marginBottom: "8px",
                      color: "var(--erp-muted)",
                      fontWeight: 800,
                    }}
                  >
                    Ödeme tarihi
                  </label>

                  <input
                    id="payroll-payment-date"
                    type="date"
                    value={paymentDate}
                    onChange={(event) =>
                      setPaymentDate(
                        event.target.value
                      )
                    }
                    style={inputStyle}
                  />
                </div>

                <div>
                  <label
                    htmlFor="payroll-payment-reference"
                    style={{
                      display: "block",
                      marginBottom: "8px",
                      color: "var(--erp-muted)",
                      fontWeight: 800,
                    }}
                  >
                    Ödeme referansı
                  </label>

                  <input
                    id="payroll-payment-reference"
                    type="text"
                    value={
                      paymentReference
                    }
                    onChange={(event) =>
                      setPaymentReference(
                        event.target.value
                      )
                    }
                    placeholder={
                      paymentMethod === 0
                        ? "Dekont / işlem no"
                        : "Kasa fişi no"
                    }
                    style={inputStyle}
                  />
                </div>
              </div>

              <div
                style={{
                  padding: "14px 16px",
                  border:
                    "1px solid var(--color-semantic-info-border)",
                  borderRadius: "12px",
                  background: "var(--color-semantic-info-tint)",
                  color: "var(--color-semantic-info)",
                  fontSize: "14px",
                  lineHeight: 1.55,
                }}
              >
                Bu işlem finans hareketini
                oluşturur, 335 Personel
                Borçları hesabını kapatır ve
                ödeme yöntemine göre 102
                Bankalar veya 100 Kasa
                hesabına muhasebe kaydı
                oluşturur.
              </div>
            </div>

            <footer
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "12px",
                padding: "18px 24px",
                borderTop:
                  "1px solid var(--erp-border)",
                background: "var(--erp-bg)",
                borderRadius:
                  "0 0 20px 20px",
              }}
            >
              <button
                type="button"
                onClick={
                  closePaymentModal
                }
                disabled={
                  paymentSubmitting
                }
                style={{
                  minWidth: "110px",
                  minHeight: "44px",
                  border:
                    "1px solid var(--erp-border)",
                  borderRadius: "10px",
                  background: "var(--erp-panel)",
                  color: "var(--erp-muted)",
                  fontWeight: 800,
                  cursor: paymentSubmitting
                    ? "not-allowed"
                    : "pointer",
                }}
              >
                Vazgeç
              </button>

              <button
                type="button"
                onClick={() =>
                  void submitPayrollPayment()
                }
                disabled={
                  paymentSubmitting ||
                  paymentAccountsLoading ||
                  (paymentMethod === 0 &&
                    !paymentBankAccountId) ||
                  (paymentMethod === 1 &&
                    !paymentCashAccountId)
                }
                style={{
                  minWidth: "160px",
                  minHeight: "44px",
                  border: "none",
                  borderRadius: "10px",
                  background:
                    paymentSubmitting ||
                    paymentAccountsLoading ||
                    (paymentMethod === 0 &&
                      !paymentBankAccountId) ||
                    (paymentMethod === 1 &&
                      !paymentCashAccountId)
                      ? "var(--erp-muted)"
                      : "var(--color-semantic-success)",
                  color: "var(--color-on-brand)",
                  fontWeight: 900,
                  cursor:
                    paymentSubmitting ||
                    paymentAccountsLoading
                      ? "not-allowed"
                      : "pointer",
                }}
              >
                {paymentSubmitting
                  ? "Ödeme Yapılıyor..."
                  : paymentAccountsLoading
                    ? "Hesaplar Yükleniyor..."
                    : "Ödemeyi Tamamla"}
              </button>
            </footer>
          </section>
        </div>
      )}

      {pending && dialogProps && (
        <ConfirmDialog
          open
          title={dialogProps.title}
          description={dialogProps.description}
          confirmLabel={dialogProps.confirmLabel}
          busy={dialogProps.busy}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={dialogProps.onConfirm}
        />
      )}
    </ErpShell>
  );
}

function PayrollDetailSection({
  title,
  rows,
  currencyCode,
}: {
  title: string;
  rows: Array<[string, number]>;
  currencyCode: string;
}) {
  return (
    <section
      style={{
        ...panelStyle,
        marginTop: "18px",
        overflow: "hidden",
      }}
    >
      <h3
        style={{
          margin: 0,
          padding: "14px 16px",
          background: "var(--erp-bg)",
          borderBottom:
            "1px solid var(--erp-border)",
          fontSize: "15px",
        }}
      >
        {title}
      </h3>

      {rows.map(([label, amount]) => (
        <div
          key={label}
          style={{
            display: "flex",
            justifyContent:
              "space-between",
            gap: "20px",
            padding: "11px 16px",
            borderBottom:
              "1px solid var(--erp-border)",
          }}
        >
          <span
            style={{
              color: "var(--erp-muted)",
            }}
          >
            {label}
          </span>

          <strong
            style={{
              whiteSpace: "nowrap",
            }}
          >
            {formatMoney(
              amount,
              currencyCode
            )}
          </strong>
        </div>
      ))}
    </section>
  );
}
