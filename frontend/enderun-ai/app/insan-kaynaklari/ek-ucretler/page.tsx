"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import { currencyMoney } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";

import {
  CompensationComponent,
  CompensationSummary,
  CreateCompensationComponentRequest,
  hrCompensationService,
} from "@/services/hr-compensation.service";

import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";

import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";

import {
  projectService,
  ProjectListItem,
} from "@/services/project.service";

type CompensationForm = {
  companyId: string;
  personnelId: string;
  projectId: string;
  code: string;
  name: string;
  componentType: string;
  calculationType: string;
  paymentMethod: string;
  amount: string;
  currencyCode: string;
  effectiveStartDate: string;
  effectiveEndDate: string;
  isAttendanceBased: boolean;
  isInKindBenefit: boolean;
  includeInPayroll: boolean;
  includeInSgkBase: boolean;
  includeInIncomeTaxBase: boolean;
  includeInStampTaxBase: boolean;
  includeInProjectCost: boolean;
  includeInProgressPaymentCost: boolean;
  isActive: boolean;
  description: string;
};

const today = new Date().toISOString().slice(0, 10);

const componentTypeOptions = [
  { value: 0, label: "Düzenli Ek Ücret" },
  { value: 1, label: "Proje / Şantiye Ek Ücreti" },
  { value: 2, label: "Yemek Ödemesi" },
  { value: 3, label: "Yol Ödemesi" },
  { value: 4, label: "Konaklama Ödemesi" },
  { value: 5, label: "Puantaj Primi" },
  { value: 6, label: "Performans Primi" },
  { value: 7, label: "Kasa Üzerinden Ek Ödeme" },
  { value: 8, label: "Diğer" },
];

const calculationTypeOptions = [
  { value: 0, label: "Aylık Sabit" },
  { value: 1, label: "Günlük" },
  { value: 2, label: "Saatlik" },
  { value: 3, label: "Puantaj Günü Başına" },
  { value: 4, label: "Manuel" },
];

const paymentMethodOptions = [
  { value: 0, label: "Bordro" },
  { value: 1, label: "Banka" },
  { value: 2, label: "Kasa" },
  { value: 3, label: "Karma" },
];

const initialForm: CompensationForm = {
  companyId: "",
  personnelId: "",
  projectId: "",
  code: "",
  name: "",
  componentType: "0",
  calculationType: "0",
  paymentMethod: "0",
  amount: "0",
  currencyCode: "TRY",
  effectiveStartDate: today,
  effectiveEndDate: "",
  isAttendanceBased: false,
  isInKindBenefit: false,
  includeInPayroll: true,
  includeInSgkBase: false,
  includeInIncomeTaxBase: false,
  includeInStampTaxBase: false,
  includeInProjectCost: true,
  includeInProgressPaymentCost: false,
  isActive: true,
  description: "",
};

function money(
  value: number,
  currencyCode = "TRY"
) {
  return currencyMoney(
    value,
    currencyCode === "MIXED" ? "TRY" : currencyCode
  );
}

function dateValue(value?: string | null) {
  return value ? value.slice(0, 10) : "";
}

function componentTypeLabel(value: number) {
  return (
    componentTypeOptions.find(
      (item) => item.value === value
    )?.label ?? "Bilinmiyor"
  );
}

function calculationTypeLabel(value: number) {
  return (
    calculationTypeOptions.find(
      (item) => item.value === value
    )?.label ?? "Bilinmiyor"
  );
}

function paymentMethodLabel(value: number) {
  return (
    paymentMethodOptions.find(
      (item) => item.value === value
    )?.label ?? "Bilinmiyor"
  );
}

export default function AdditionalCompensationPage() {
  /*
   * Aksiyon izinleri UÇLARDAN türetildi:
   *   yeni kayıt -> attendance-payroll.create
   *   güncelleme -> attendance-payroll.edit
   *   onay       -> attendance-payroll.approve
   *   silme      -> attendance-payroll.delete
   */
  const actions = useModuleActions("attendance-payroll");

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [personnel, setPersonnel] =
    useState<PersonnelListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [items, setItems] =
    useState<CompensationComponent[]>([]);

  const [summary, setSummary] =
    useState<CompensationSummary | null>(null);

  const [form, setForm] =
    useState<CompensationForm>(initialForm);

  const [editingId, setEditingId] =
    useState<string | null>(null);

  const [showForm, setShowForm] =
    useState(false);

  const [companyFilter, setCompanyFilter] =
    useState("");

  const [personnelFilter, setPersonnelFilter] =
    useState("");

  const [projectFilter, setProjectFilter] =
    useState("");

  const [activeFilter, setActiveFilter] =
    useState("true");

  const [effectiveDate, setEffectiveDate] =
    useState(today);

  const [loading, setLoading] =
    useState(true);

  const [saving, setSaving] =
    useState(false);

  /** Silinmek üzere onay bekleyen ücret bileşeni. */
  const [pending, setPending] = useState<CompensationComponent | null>(
    null
  );

  const [actionId, setActionId] =
    useState<string | null>(null);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const personnelById = useMemo(
    () =>
      new Map(
        personnel.map((item) => [item.id, item])
      ),
    [personnel]
  );

  const projectById = useMemo(
    () =>
      new Map(
        projects.map((item) => [item.id, item])
      ),
    [projects]
  );

  const filteredPersonnel = useMemo(
    () =>
      personnel.filter(
        (item) =>
          !companyFilter ||
          item.companyId === companyFilter
      ),
    [companyFilter, personnel]
  );

  const formPersonnel = useMemo(
    () =>
      personnel.filter(
        (item) =>
          !form.companyId ||
          item.companyId === form.companyId
      ),
    [form.companyId, personnel]
  );

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (item) =>
          !companyFilter ||
          item.companyId === companyFilter
      ),
    [companyFilter, projects]
  );

  const formProjects = useMemo(
    () =>
      projects.filter(
        (item) =>
          !form.companyId ||
          item.companyId === form.companyId
      ),
    [form.companyId, projects]
  );

  const totals = useMemo(() => {
    const activeItems = items.filter(
      (item) => item.isActive
    );

    return {
      activeCount: activeItems.length,
      payrollCount: activeItems.filter(
        (item) => item.includeInPayroll
      ).length,
      projectCostCount: activeItems.filter(
        (item) => item.includeInProjectCost
      ).length,
      attendanceBasedCount: activeItems.filter(
        (item) => item.isAttendanceBased
      ).length,
      amount: activeItems.reduce(
        (sum, item) => sum + Number(item.amount),
        0
      ),
    };
  }, [items]);

  async function loadData() {
    setLoading(true);
    setError("");
    setSuccess("");

    try {
      const result =
        await hrCompensationService.getAll({
          companyId: companyFilter || undefined,
          personnelId:
            personnelFilter || undefined,
          projectId: projectFilter || undefined,
          isActive:
            activeFilter === ""
              ? undefined
              : activeFilter === "true",
          effectiveDate:
            effectiveDate || undefined,
        });

      setItems(result);

      if (personnelFilter && effectiveDate) {
        const summaryResult =
          await hrCompensationService.getSummary(
            personnelFilter,
            effectiveDate
          );

        setSummary(summaryResult);
      } else {
        setSummary(null);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ücret bileşenleri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadPage() {
      try {
        const [
          companyResult,
          personnelResult,
          projectResult,
          componentResult,
        ] = await Promise.all([
          companyService.getAll(),
          personnelService.getAll(),
          projectService.getAll(),
          hrCompensationService.getAll({
            isActive: true,
            effectiveDate: today,
          }),
        ]);

        setCompanies(companyResult);
        setPersonnel(personnelResult);
        setProjects(projectResult);
        setItems(componentResult);

        if (companyResult.length === 1) {
          const companyId =
            companyResult[0].id;

          setCompanyFilter(companyId);

          setForm((current) => ({
            ...current,
            companyId,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Ücret kartı ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadPage();
  }, []);

  function openCreate() {
    setEditingId(null);

    setForm({
      ...initialForm,
      companyId:
        companyFilter ||
        (companies.length === 1
          ? companies[0].id
          : ""),
      personnelId: personnelFilter,
      projectId: projectFilter,
      effectiveStartDate: effectiveDate || today,
    });

    setShowForm(true);
    setError("");
    setSuccess("");
  }

  function openEdit(
    item: CompensationComponent
  ) {
    setEditingId(item.id);

    setForm({
      companyId: item.companyId,
      personnelId: item.personnelId,
      projectId: item.projectId ?? "",
      code: item.code,
      name: item.name,
      componentType:
        String(item.componentType),
      calculationType:
        String(item.calculationType),
      paymentMethod:
        String(item.paymentMethod),
      amount: String(item.amount),
      currencyCode: item.currencyCode,
      effectiveStartDate:
        dateValue(item.effectiveStartDate),
      effectiveEndDate:
        dateValue(item.effectiveEndDate),
      isAttendanceBased:
        item.isAttendanceBased,
      isInKindBenefit:
        item.isInKindBenefit ?? false,
      includeInPayroll:
        item.includeInPayroll,
      includeInSgkBase:
        item.includeInSgkBase,
      includeInIncomeTaxBase:
        item.includeInIncomeTaxBase,
      includeInStampTaxBase:
        item.includeInStampTaxBase,
      includeInProjectCost:
        item.includeInProjectCost,
      includeInProgressPaymentCost:
        item.includeInProgressPaymentCost,
      isActive: item.isActive,
      description: item.description ?? "",
    });

    setShowForm(true);
    setError("");
    setSuccess("");

    window.scrollTo({
      top: 0,
      behavior: "smooth",
    });
  }

  function buildPayload():
    CreateCompensationComponentRequest {
    const amount = Number(
      form.amount.replace(",", ".")
    );

    if (!form.companyId) {
      throw new Error("Şirket seçilmelidir.");
    }

    if (!form.personnelId) {
      throw new Error("Personel seçilmelidir.");
    }

    if (!form.code.trim()) {
      throw new Error(
        "Ücret bileşeni kodu zorunludur."
      );
    }

    if (!form.name.trim()) {
      throw new Error(
        "Ücret bileşeni adı zorunludur."
      );
    }

    if (!Number.isFinite(amount) || amount < 0) {
      throw new Error(
        "Geçerli bir tutar girilmelidir."
      );
    }

    if (
      form.effectiveEndDate &&
      form.effectiveEndDate <
        form.effectiveStartDate
    ) {
      throw new Error(
        "Bitiş tarihi başlangıç tarihinden önce olamaz."
      );
    }

    return {
      companyId: form.companyId,
      personnelId: form.personnelId,
      projectId: form.projectId || null,
      code: form.code.trim().toUpperCase(),
      name: form.name.trim(),
      componentType:
        Number(form.componentType),
      calculationType:
        Number(form.calculationType),
      paymentMethod:
        Number(form.paymentMethod),
      amount,
      currencyCode:
        form.currencyCode.trim().toUpperCase(),
      effectiveStartDate:
        form.effectiveStartDate,
      effectiveEndDate:
        form.effectiveEndDate || null,
      isAttendanceBased:
        form.isAttendanceBased,
      isInKindBenefit:
        form.isInKindBenefit,
      includeInPayroll:
        form.includeInPayroll,
      includeInSgkBase:
        form.includeInSgkBase,
      includeInIncomeTaxBase:
        form.includeInIncomeTaxBase,
      includeInStampTaxBase:
        form.includeInStampTaxBase,
      includeInProjectCost:
        form.includeInProjectCost,
      includeInProgressPaymentCost:
        form.includeInProgressPaymentCost,
      isActive: form.isActive,
      description:
        form.description.trim() || null,
    };
  }

  async function save(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const payload = buildPayload();

      if (editingId) {
        const {
          companyId: _companyId,
          personnelId: _personnelId,
          ...updatePayload
        } = payload;

        await hrCompensationService.update(
          editingId,
          updatePayload
        );

        setSuccess(
          "Ücret bileşeni güncellendi."
        );
      } else {
        await hrCompensationService.create(
          payload
        );

        setSuccess(
          "Ücret bileşeni oluşturuldu."
        );
      }

      setShowForm(false);
      setEditingId(null);

      await loadData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ücret bileşeni kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function remove(
    item: CompensationComponent
  ) {
    setPending(null);
    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrCompensationService.delete(
        item.id
      );

      setSuccess(
        "Ücret bileşeni silindi."
      );

      await loadData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ücret bileşeni silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Personel Ücret Kartları"
      description="Resmî maaş, düzenli ek ödemeler ve puantaja bağlı ücret bileşenleri"
    >
      {/* Ücret bileşenleri bordro döneminde sık değişiyor. */}
      <div className="mb-4 flex justify-end">
        <Button variant="secondary" onClick={() => void loadData()}>Yenile</Button>
      </div>
      {error && (
        <div className="mb-5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        {[
          ["Aktif Bileşen", totals.activeCount],
          ["Bordroya Dâhil", totals.payrollCount],
          ["Proje Maliyeti", totals.projectCostCount],
          [
            "Puantaja Bağlı",
            totals.attendanceBasedCount,
          ],
          [
            "Listelenen Tutar",
            loading
              ? "…"
              : money(
                  totals.amount,
                  items[0]?.currencyCode ?? "TRY"
                ),
          ],
        ].map(([title, value]) => (
          <article
            key={String(title)}
            className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
          >
            <span className="text-xs font-bold text-slate-500">
              {title}
            </span>

            <strong className="mt-3 block text-xl text-slate-800">
              {value}
            </strong>
          </article>
        ))}
      </div>

      {summary && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-blue-50 p-5">
          <h2 className="font-bold text-blue-900">
            Seçili Personel Ücret Özeti
          </h2>

          <div className="mt-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
            {[
              [
                "Bileşen",
                summary.componentCount,
              ],
              [
                "Aylık Sabit",
                money(
                  summary.monthlyFixedAmount,
                  summary.currencyCode
                ),
              ],
              [
                "Günlük",
                money(
                  summary.dailyAmount,
                  summary.currencyCode
                ),
              ],
              [
                "Saatlik",
                money(
                  summary.hourlyAmount,
                  summary.currencyCode
                ),
              ],
              [
                "Bordro",
                money(
                  summary.payrollIncludedAmount,
                  summary.currencyCode
                ),
              ],
              [
                "Proje Maliyeti",
                money(
                  summary.projectCostIncludedAmount,
                  summary.currencyCode
                ),
              ],
            ].map(([title, value]) => (
              <div
                key={String(title)}
                className="rounded-lg bg-white p-3"
              >
                <small className="text-slate-500">
                  {title}
                </small>

                <strong className="mt-1 block">
                  {value}
                </strong>
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-bold text-slate-800">
              Ücret Kartı İşlemleri
            </h2>

            <p className="mt-1 text-sm text-slate-500">
              Her ödeme bileşeni tarihçeli ve ayrı kurallarla izlenir.
            </p>
          </div>

          {actions.can("create") && (
            <button
              type="button"
              onClick={openCreate}
              className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
            >
              + Yeni Ücret Bileşeni
            </button>
          )}
        </div>
      </section>

      {showForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <h3 className="mb-4 text-lg font-bold text-slate-800">
            {editingId
              ? "Ücret Bileşenini Düzenle"
              : "Yeni Ücret Bileşeni"}
          </h3>

          <form onSubmit={save}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <select
                value={form.companyId}
                disabled={Boolean(editingId)}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    companyId:
                      event.target.value,
                    personnelId: "",
                    projectId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.name}
                  </option>
                ))}
              </select>

              <select
                value={form.personnelId}
                disabled={Boolean(editingId)}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    personnelId:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">
                  Personel seçin
                </option>

                {formPersonnel.map((person) => (
                  <option
                    key={person.id}
                    value={person.id}
                  >
                    {person.employeeNumber} -{" "}
                    {person.fullName}
                  </option>
                ))}
              </select>

              <select
                value={form.projectId}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    projectId:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">
                  Tüm projeler / genel
                </option>

                {formProjects.map((project) => (
                  <option
                    key={project.id}
                    value={project.id}
                  >
                    {project.code} -{" "}
                    {project.name}
                  </option>
                ))}
              </select>

              <select
                value={form.componentType}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    componentType:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                {componentTypeOptions.map(
                  (option) => (
                    <option
                      key={option.value}
                      value={option.value}
                    >
                      {option.label}
                    </option>
                  )
                )}
              </select>

              <input
                value={form.code}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    code: event.target.value,
                  }))
                }
                placeholder="Kod: SANTIYE-EK"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={form.name}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    name: event.target.value,
                  }))
                }
                placeholder="Ödeme adı"
                className="rounded-lg border border-slate-300 p-3"
              />

              <select
                value={form.calculationType}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    calculationType:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                {calculationTypeOptions.map(
                  (option) => (
                    <option
                      key={option.value}
                      value={option.value}
                    >
                      {option.label}
                    </option>
                  )
                )}
              </select>

              <select
                value={form.paymentMethod}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    paymentMethod:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                {paymentMethodOptions.map(
                  (option) => (
                    <option
                      key={option.value}
                      value={option.value}
                    >
                      {option.label}
                    </option>
                  )
                )}
              </select>

              <input
                type="number"
                min="0"
                step="0.01"
                value={form.amount}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    amount: event.target.value,
                  }))
                }
                placeholder="Tutar"
                className="rounded-lg border border-slate-300 p-3"
              />

              <select
                value={form.currencyCode}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    currencyCode:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="TRY">TRY</option>
                <option value="USD">USD</option>
                <option value="EUR">EUR</option>
                <option value="GBP">GBP</option>
              </select>

              <input
                type="date"
                value={form.effectiveStartDate}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    effectiveStartDate:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="date"
                value={form.effectiveEndDate}
                min={
                  form.effectiveStartDate ||
                  undefined
                }
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    effectiveEndDate:
                      event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

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
                placeholder="Açıklama"
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-4"
              />
            </div>

            {(form.includeInProjectCost || form.includeInProgressPaymentCost) && (
              <p className="mt-4 rounded-lg border border-slate-300 bg-slate-50 p-3 text-sm text-slate-700">
                <strong>Proje maliyetine dâhil</strong>, kalemi çalışılan günlere
                dağıtıp projenin işçilik maliyetine yazar.{" "}
                <strong>Hakediş maliyetine dâhil</strong> ise ayrı bir kapı:
                işaretlenmezse kalem şirketin üstünde kalır — proje kârını
                düşürür ama hakediş kârını düşürmez.
              </p>
            )}

            {form.paymentMethod === "1" && (
              <p className="mt-4 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
                Ödeme yöntemi <strong>Nakit</strong> seçili. Bu kalem resmî
                bordroya, SGK matrahına ve muhasebeye yansımaz — &quot;Bordroya
                dâhil&quot; işaretlense bile hesap dışında kalır.
              </p>
            )}

            {form.isInKindBenefit && (
              <p className="mt-4 rounded-lg border border-slate-300 bg-slate-50 p-3 text-sm text-slate-700">
                Ayni yardımda günlük istisna tavanı uygulanmaz: matrah
                işareti kapalı olduğu sürece kalemin tamamı istisnadır.
              </p>
            )}

            <div className="mt-5 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {[
                [
                  "isAttendanceBased",
                  "Puantaja bağlı",
                ],
                [
                  "isInKindBenefit",
                  "Ayni yardım (nakdî değil)",
                ],
                [
                  "includeInPayroll",
                  "Bordroya dâhil",
                ],
                [
                  "includeInSgkBase",
                  "SGK matrahına dâhil",
                ],
                [
                  "includeInIncomeTaxBase",
                  "Gelir vergisine dâhil",
                ],
                [
                  "includeInStampTaxBase",
                  "Damga vergisine dâhil",
                ],
                [
                  "includeInProjectCost",
                  "Proje maliyetine dâhil",
                ],
                [
                  "includeInProgressPaymentCost",
                  "Hakediş maliyetine dâhil",
                ],
                [
                  "isActive",
                  "Aktif",
                ],
              ].map(([key, label]) => (
                <label
                  key={key}
                  className="flex items-center gap-3 rounded-lg border border-slate-200 p-3"
                >
                  <input
                    type="checkbox"
                    checked={
                      form[
                        key as keyof CompensationForm
                      ] as boolean
                    }
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        [key]:
                          event.target.checked,
                      }))
                    }
                  />

                  <span className="text-sm font-semibold">
                    {label}
                  </span>
                </label>
              ))}
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() =>
                  setShowForm(false)
                }
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2 text-white disabled:opacity-50"
              >
                {saving
                  ? "Kaydediliyor…"
                  : "Kaydet"}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          <select
            value={companyFilter}
            onChange={(event) => {
              setCompanyFilter(
                event.target.value
              );
              setPersonnelFilter("");
              setProjectFilter("");
            }}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">
              Tüm şirketler
            </option>

            {companies.map((company) => (
              <option
                key={company.id}
                value={company.id}
              >
                {company.name}
              </option>
            ))}
          </select>

          <select
            value={personnelFilter}
            onChange={(event) =>
              setPersonnelFilter(
                event.target.value
              )
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">
              Tüm personeller
            </option>

            {filteredPersonnel.map(
              (person) => (
                <option
                  key={person.id}
                  value={person.id}
                >
                  {person.employeeNumber} -{" "}
                  {person.fullName}
                </option>
              )
            )}
          </select>

          <select
            value={projectFilter}
            onChange={(event) =>
              setProjectFilter(
                event.target.value
              )
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">
              Tüm projeler
            </option>

            {filteredProjects.map(
              (project) => (
                <option
                  key={project.id}
                  value={project.id}
                >
                  {project.code} -{" "}
                  {project.name}
                </option>
              )
            )}
          </select>

          <select
            value={activeFilter}
            onChange={(event) =>
              setActiveFilter(
                event.target.value
              )
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">
              Tüm durumlar
            </option>
            <option value="true">
              Aktif
            </option>
            <option value="false">
              Pasif
            </option>
          </select>

          <input
            type="date"
            value={effectiveDate}
            onChange={(event) =>
              setEffectiveDate(
                event.target.value
              )
            }
            className="rounded-lg border border-slate-300 p-3"
          />

          <button
            type="button"
            onClick={loadData}
            disabled={loading}
            className="rounded-lg bg-brand-700 p-3 font-semibold text-white disabled:opacity-50"
          >
            {loading
              ? "Yükleniyor…"
              : "Filtrele"}
          </button>
        </div>
      </section>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[1700px]">
            <thead className="bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="p-4">
                  Personel
                </th>
                <th className="p-4">
                  Proje
                </th>
                <th className="p-4">
                  Kod / Ad
                </th>
                <th className="p-4">
                  Tür
                </th>
                <th className="p-4">
                  Hesaplama
                </th>
                <th className="p-4">
                  Ödeme
                </th>
                <th className="p-4">
                  Tutar
                </th>
                <th className="p-4">
                  Geçerlilik
                </th>
                <th className="p-4">
                  Kapsam
                </th>
                <th className="p-4">
                  Durum
                </th>
                <th className="p-4">
                  İşlemler
                </th>
              </tr>
            </thead>

            <tbody>
              {items.map((item) => {
                const person =
                  personnelById.get(
                    item.personnelId
                  );

                const project =
                  item.projectId
                    ? projectById.get(
                        item.projectId
                      )
                    : null;

                return (
                  <tr
                    key={item.id}
                    className="border-t border-slate-100 text-sm"
                  >
                    <td className="p-4">
                      <strong className="block">
                        {person?.fullName ??
                          item.personnelId}
                      </strong>

                      <small className="text-slate-500">
                        {person?.employeeNumber}
                      </small>
                    </td>

                    <td className="p-4">
                      {project
                        ? `${project.code} - ${project.name}`
                        : "Genel"}
                    </td>

                    <td className="p-4">
                      <strong className="block">
                        {item.code}
                      </strong>

                      <span>
                        {item.name}
                      </span>
                    </td>

                    <td className="p-4">
                      {componentTypeLabel(
                        item.componentType
                      )}
                    </td>

                    <td className="p-4">
                      {calculationTypeLabel(
                        item.calculationType
                      )}
                    </td>

                    <td className="p-4">
                      {paymentMethodLabel(
                        item.paymentMethod
                      )}
                    </td>

                    <td className="p-4 font-bold">
                      {money(
                        Number(item.amount),
                        item.currencyCode
                      )}
                    </td>

                    <td className="p-4">
                      <span className="block">
                        {new Date(
                          item.effectiveStartDate
                        ).toLocaleDateString(
                          "tr-TR"
                        )}
                      </span>

                      <small className="text-slate-500">
                        {item.effectiveEndDate
                          ? new Date(
                              item.effectiveEndDate
                            ).toLocaleDateString(
                              "tr-TR"
                            )
                          : "Süresiz"}
                      </small>
                    </td>

                    <td className="p-4">
                      <div className="flex max-w-72 flex-wrap gap-1">
                        {item.isAttendanceBased && (
                          <span className="rounded bg-blue-50 px-2 py-1 text-xs text-blue-700">
                            Puantaj
                          </span>
                        )}

                        {item.includeInPayroll && (
                          <span className="rounded bg-emerald-50 px-2 py-1 text-xs text-emerald-700">
                            Bordro
                          </span>
                        )}

                        {item.includeInProjectCost && (
                          <span className="rounded bg-indigo-50 px-2 py-1 text-xs text-indigo-700">
                            Proje
                          </span>
                        )}

                        {item.includeInSgkBase && (
                          <span className="rounded bg-amber-50 px-2 py-1 text-xs text-amber-700">
                            SGK
                          </span>
                        )}

                        {item.includeInIncomeTaxBase && (
                          <span className="rounded bg-slate-100 px-2 py-1 text-xs">
                            Gelir V.
                          </span>
                        )}
                      </div>
                    </td>

                    <td className="p-4">
                      <span
                        className={`rounded-full px-3 py-1 text-xs font-bold ${
                          item.isActive
                            ? "bg-emerald-50 text-emerald-700"
                            : "bg-slate-100 text-slate-600"
                        }`}
                      >
                        {item.isActive
                          ? "Aktif"
                          : "Pasif"}
                      </span>
                    </td>

                    <td className="p-4">
                      <div className="flex gap-2">
                        {actions.can("edit") && (
                          <button
                            type="button"
                            onClick={() =>
                              openEdit(item)
                            }
                            className="rounded border border-slate-300 px-3 py-2 text-xs font-semibold"
                          >
                            Düzenle
                          </button>
                        )}

                        {actions.can("delete") && (
                          <button
                            type="button"
                            disabled={
                              actionId === item.id
                            }
                            onClick={() => setPending(item)}
                            className="rounded border border-red-200 px-3 py-2 text-xs font-semibold text-red-700 disabled:opacity-50"
                          >
                            Sil
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}

              {!loading &&
                items.length === 0 && (
                  <tr>
                    <td
                      colSpan={11}
                      className="p-10 text-center text-slate-500"
                    >
                      Ücret bileşeni bulunamadı.
                    </td>
                  </tr>
                )}
            </tbody>
          </table>
        </div>
      </section>
      {pending && (
        <ConfirmDialog
          open
          title="Ücret Bileşenini Sil"
          description={`${
            personnelById.get(pending.personnelId)?.fullName ?? "Personel"
          } için "${pending.name}" bileşeni kalıcı olarak silinecek. Bileşen bundan sonraki bordrolara girmez.`}
          confirmLabel="Bileşeni Sil"
          busy={actionId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() => void remove(pending)}
        />
      )}
    </ErpShell>
  );
}
