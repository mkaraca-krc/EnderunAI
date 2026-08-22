"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { Button, ConfirmDialog } from "@/components/ui";
import DutyDetailPanel from "@/components/hr/duty-detail-panel";
import { usePermissions } from "@/lib/use-permissions";
import {
  personnelDutyService,
  PersonnelDutyItem,
  DutyType,
  dutyTypeOptions,
} from "@/services/personnel-duty.service";
import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";
import { companyService, CompanyListItem } from "@/services/company.service";
import { projectService, ProjectListItem } from "@/services/project.service";
import { foldTurkish, matchesSearch } from "@/lib/search/fold";

type DutyForm = {
  companyId: string;
  personnelId: string;
  dutyType: string;
  targetProjectId: string;
  sourceProjectId: string;
  startDate: string;
  endDate: string;
  isOutOfCity: boolean;
  dailyAllowance: string;
  purpose: string;
  notes: string;
};

const initialForm: DutyForm = {
  companyId: "",
  personnelId: "",
  dutyType: "0",
  targetProjectId: "",
  sourceProjectId: "",
  startDate: "",
  endDate: "",
  isOutOfCity: false,
  dailyAllowance: "0",
  purpose: "",
  notes: "",
};

const statusOptions = [
  { value: 0, label: "Onay bekliyor" },
  { value: 1, label: "Onaylandı" },
  { value: 2, label: "Reddedildi" },
  { value: 3, label: "Tamamlandı" },
  { value: 4, label: "İptal" },
];

function statusClass(value: number) {
  if (value === 1) return "border-emerald-200 bg-emerald-50 text-emerald-700";
  if (value === 0) return "border-amber-200 bg-amber-50 text-amber-700";
  if (value === 2 || value === 4) return "border-red-200 bg-red-50 text-red-700";

  return "border-slate-200 bg-slate-50 text-slate-600";
}

function dutyTypeClass(value: DutyType) {
  if (value === 0) return "border-blue-200 bg-blue-50 text-blue-700";
  if (value === 1) return "border-violet-200 bg-violet-50 text-violet-700";

  return "border-slate-200 bg-slate-50 text-slate-600";
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

/**
 * Görevlendirme akışının tek ekranı: talep açma, GM onayı, üç görev
 * türü, masraf/fiş/mahsup, keşif saha raporu ve kazan/kaybet sonucu.
 *
 * YETKİ: buradaki kontroller yalnızca kullanıcıya işe yaramayacak
 * düğme göstermemek için. Gerçek kapı uçlarda — tutarlar
 * extra_payment.view yoksa sunucudan hiç gelmiyor, onay yalnızca
 * Genel Müdür/Admin rolünde geçiyor.
 */
export default function PersonnelDutiesPage() {
  const { has, user } = usePermissions();

  const canEdit = has("personnel.edit");

  // Tutar yazmak görmekle aynı kapıda: görmediği rakamı yazan
  // kullanıcı yanlışını bir daha fark edemez. Talebi açmak bu kapıya
  // tabi değil — harcırahı yetkili sonradan girer.
  const canWriteAmounts = canEdit && has("extra_payment.view");
  const canWriteReport = has("projects.edit") || has("site-reports.edit");
  const canDecideOutcome = has("projects.edit");

  const canApprove = Boolean(
    user?.roles?.includes("Admin") || user?.roles?.includes("Genel Müdür")
  );

  const [items, setItems] = useState<PersonnelDutyItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [form, setForm] = useState<DutyForm>(initialForm);
  const [showForm, setShowForm] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const [companyFilter, setCompanyFilter] = useState("");
  const [personnelFilter, setPersonnelFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");

  /**
   * Ret ve iptal ConfirmDialog'da yürüyor.
   *
   * İkisi de GEREKÇE istiyor ve window.prompt gerekçeyi zorunlu
   * tutamıyordu: boş metni kabul ediyor, sunucunun reddini de
   * gösteremiyordu. Özellikle iptalde bu kritik — bordro avanstan
   * kesmişse uç "şu kadar kesilmiş" diyerek reddediyor ve o mesaj
   * kaybolmamalı.
   */
  const [confirmTarget, setConfirmTarget] =
    useState<{ item: PersonnelDutyItem; mode: "reject" | "cancel" } | null>(null);

  const [confirmError, setConfirmError] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Onay bekleyen görevlendirme. */
  const [pending, setPending] = useState<PersonnelDutyItem | null>(null);

  const [actionId, setActionId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const amountsHidden = items.length > 0 && items[0].amountsHidden;

  const formProjects = useMemo(() => {
    // Keşif görevi keşif statüsündeki projeye, diğerleri aktif
    // projeye açılır — uç da bunu doğruluyor; listeyi daraltmak
    // kullanıcıyı reddedilecek bir seçimden koruyor.
    const wantsSurvey = form.dutyType === "1";

    return projects.filter((project) => {
      if (form.companyId && project.companyId !== form.companyId) return false;

      return wantsSurvey ? project.status === 0 : project.status === 2;
    });
  }, [form.companyId, form.dutyType, projects]);

  const formPersonnel = useMemo(
    () =>
      form.companyId
        ? personnel.filter((x) => x.companyId === form.companyId)
        : personnel,
    [form.companyId, personnel]
  );

  const visibleItems = useMemo(() => {
    const term = foldTurkish(search);

    if (!term) return items;

    return items.filter((item) =>
      matchesSearch(
          search,
        item.personnelFullName,
        item.targetProjectCode,
        item.targetProjectName,
        item.purpose,
        item.statusName,
        item.dutyTypeName,
        )
    );
  }, [items, search]);

  const pendingApproval = items.filter((x) => x.status === 0).length;
  const approved = items.filter((x) => x.status === 1).length;
  const settlementPending = items.filter((x) => x.settlementPending).length;
  const missingReports = items.filter(
    (x) => x.dutyType === 1 && x.status === 1 && !x.hasSurveyReport
  ).length;

  async function loadItems() {
    setLoading(true);
    setError("");

    try {
      setItems(
        await personnelDutyService.getAll({
          companyId: companyFilter || undefined,
          personnelId: personnelFilter || undefined,
          status: statusFilter === "" ? undefined : Number(statusFilter),
        })
      );
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Görevlendirmeler yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadPage() {
      try {
        const [companyResult, personnelResult, projectResult, dutyResult] =
          await Promise.all([
            companyService.getAll(),
            personnelService.getAll(),
            projectService.getAll(),
            personnelDutyService.getAll(),
          ]);

        setCompanies(companyResult);
        setPersonnel(personnelResult);
        setProjects(projectResult);
        setItems(dutyResult);

        if (companyResult.length === 1) {
          setForm((current) => ({
            ...current,
            companyId: companyResult[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Görevlendirme ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadPage();
  }, []);

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (!form.companyId) throw new Error("Şirket seçilmelidir.");
      if (!form.personnelId) throw new Error("Personel seçilmelidir.");
      if (!form.targetProjectId) throw new Error("Hedef proje seçilmelidir.");
      if (!form.startDate || !form.endDate) {
        throw new Error("Başlangıç ve bitiş tarihi zorunludur.");
      }
      if (!form.purpose.trim()) throw new Error("Görev amacı zorunludur.");

      const dailyAllowance = Number(form.dailyAllowance);

      if (!Number.isFinite(dailyAllowance) || dailyAllowance < 0) {
        throw new Error("Günlük harcırah negatif olamaz.");
      }

      const response = await personnelDutyService.create({
        companyId: form.companyId,
        personnelId: form.personnelId,
        dutyType: Number(form.dutyType) as DutyType,
        targetProjectId: form.targetProjectId,
        sourceProjectId: form.sourceProjectId || null,
        startDate: form.startDate,
        endDate: form.endDate,
        isOutOfCity: form.isOutOfCity,
        // Yetkisiz kullanıcı zaten alanı görmüyor; uç da yazmıyor.
        dailyAllowance: canWriteAmounts ? dailyAllowance : 0,
        purpose: form.purpose.trim(),
        notes: form.notes.trim() || null,
      });

      setSuccess(response.message);
      setShowForm(false);
      setForm({
        ...initialForm,
        companyId: companies.length === 1 ? companies[0].id : "",
      });

      await loadItems();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt işlemi başarısız.");
    } finally {
      setSaving(false);
    }
  }

  async function approve(item: PersonnelDutyItem) {
    setPending(null);
    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      const response = await personnelDutyService.approve(item.id);

      setSuccess(response.message);
      await loadItems();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Onay işlemi başarısız.");
    } finally {
      setActionId(null);
    }
  }

  /**
   * Modaldan gelen onay: ret ya da iptal. Fark yalnız çağrılan uç ve
   * mesaj, o yüzden tek yoldan geçiyorlar.
   */
  async function runConfirmedAction(reason: string) {
    if (!confirmTarget) return;

    const { item, mode } = confirmTarget;

    setActionId(item.id);
    setConfirmError("");
    setError("");
    setSuccess("");

    try {
      const response = mode === "reject"
        ? await personnelDutyService.reject(item.id, reason)
        : await personnelDutyService.cancel(item.id, reason);

      setSuccess(response.message);
      setConfirmTarget(null);

      // Modal kapanınca liste tazeleniyor: durum rozeti ve mahsup
      // uyarısı arkada eski haliyle kalmasın.
      await loadItems();
    } catch (err) {
      // Hata MODALDA kalıyor. Bordro avanstan kesmişse sunucu
      // "şu kadar kesilmiş" diyor; o cümle kullanıcının kararını
      // değiştiren tek bilgi, kaybolmamalı.
      setConfirmError(err instanceof Error ? err.message : "İşlem başarısız.");
    } finally {
      setActionId(null);
    }
  }

  const selectedType = Number(form.dutyType) as DutyType;
  const selectedTypeHint = dutyTypeOptions.find(
    (x) => x.value === selectedType
  )?.hint;


  const columns: DataTableColumn<PersonnelDutyItem>[] = [
    {
      key: "personel",
      header: "Personel",
      value: (item) => `${item.personnelFullName} — ${item.purpose}`,
      render: (item) => (
        <>
          {item.personnelFullName}
          <span className="mt-1 block text-xs font-normal text-slate-500">
            {item.purpose}
          </span>
        </>
      ),
    },
    {
      key: "tur",
      header: "Tür",
      /* "Gün maliyeti hedefe kayar" bilgisi maliyet muhasebesini
         etkiliyor; çıktıda kaybolmaması gerekiyor. */
      value: (item) =>
        item.shiftsLaborCost
          ? `${item.dutyTypeName} (gün maliyeti hedefe kayar)`
          : item.dutyTypeName,
      render: (item) => (
        <>
          <span
            className={`rounded-full border px-3 py-1 text-xs font-semibold ${dutyTypeClass(
              item.dutyType
            )}`}
          >
            {item.dutyTypeName}
          </span>
          {item.shiftsLaborCost && (
            <span className="mt-1 block text-xs text-slate-500">
              Gün maliyeti hedefe kayar
            </span>
          )}
        </>
      ),
    },
    {
      key: "hedef",
      header: "Hedef Proje",
      value: (item) => `${item.targetProjectCode} ${item.targetProjectName}`,
      render: (item) => (
        <>
          {item.targetProjectCode}
          <span className="mt-1 block text-xs text-slate-500">
            {item.targetProjectName}
          </span>
        </>
      ),
    },
    {
      key: "tarih",
      header: "Tarih",
      value: (item) =>
        `${formatDate(item.startDate)} – ${formatDate(item.endDate)} (${item.dayCount} gün${item.isOutOfCity ? ", şehir dışı" : ""})`,
      render: (item) => (
        <>
          {formatDate(item.startDate)} – {formatDate(item.endDate)}
          <span className="mt-1 block text-xs text-slate-500">
            {item.dayCount} gün
            {item.isOutOfCity ? " · şehir dışı" : ""}
          </span>
        </>
      ),
    },
    {
      key: "harcirah",
      header: "Harcırah",
      numeric: true,
      value: (item) =>
        item.totalAllowance === null || item.totalAllowance === undefined
          ? "—"
          : item.settlementPending
            ? `${item.totalAllowance} (mahsup bekliyor)`
            : item.totalAllowance,
      render: (item) => (
        <>
          {item.totalAllowance === null || item.totalAllowance === undefined
            ? "—"
            : money(item.totalAllowance)}
          {item.settlementPending && (
            <span className="mt-1 block text-xs font-semibold text-amber-700">
              Mahsup bekliyor
            </span>
          )}
        </>
      ),
    },
    {
      key: "durum",
      header: "Durum",
      value: (item) =>
        item.dutyType === 1 && item.status === 1 && !item.hasSurveyReport
          ? `${item.statusName} (saha raporu bekliyor)`
          : item.statusName,
      render: (item) => (
        <>
          <span
            className={`rounded-full border px-3 py-1 text-xs font-semibold ${statusClass(
              item.status
            )}`}
          >
            {item.statusName}
          </span>
          {item.dutyType === 1 &&
            item.status === 1 &&
            !item.hasSurveyReport && (
              <span className="mt-1 block text-xs font-semibold text-violet-700">
                Saha raporu bekliyor
              </span>
            )}
        </>
      ),
    },
    {
      key: "islem",
      header: "İşlem",
      value: () => "",
      render: (item) => (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() =>
              setSelectedId((current) => (current === item.id ? null : item.id))
            }
            className="rounded-lg border border-slate-300 px-3 py-1 text-xs font-semibold"
          >
            {selectedId === item.id ? "Kapat" : "Detay"}
          </button>

          {canApprove && item.status === 0 && (
            <>
              <button
                type="button"
                disabled={actionId === item.id}
                onClick={() => setPending(item)}
                className="rounded-lg bg-emerald-700 px-3 py-1 text-xs font-semibold text-white disabled:opacity-60"
              >
                Onayla
              </button>

              <button
                type="button"
                disabled={actionId === item.id}
                onClick={() => {
                  setConfirmError("");
                  setConfirmTarget({ item, mode: "reject" });
                }}
                className="rounded-lg border border-red-300 px-3 py-1 text-xs font-semibold text-red-700 disabled:opacity-60"
              >
                Reddet
              </button>
            </>
          )}

          {/* İPTAL yalnız ONAYLI görevde ve yalnız onay makamına
              görünüyor. Kapı zaten uçta (GM/Admin + gerekçe); buradaki
              gizleme savunma derinliği. */}
          {canApprove && item.status === 1 && (
            <button
              type="button"
              disabled={actionId === item.id}
              onClick={() => {
                setConfirmError("");
                setConfirmTarget({ item, mode: "cancel" });
              }}
              className="rounded-lg border border-red-300 px-3 py-1 text-xs font-semibold text-red-700 disabled:opacity-60"
            >
              İptal Et
            </button>
          )}
        </div>
      ),
    },
  ];


  return (
    <ErpShell
      design="redwood"
      title="Görevlendirmeler"
      description="Talep, onay, harcırah ve keşif sonucu tek akışta"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void loadItems()}>Yenile</Button>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {[
          ["Toplam Görevlendirme", items.length],
          ["Onay Bekleyen", pendingApproval],
          ["Onaylı", approved],
          [
            amountsHidden ? "Rapor Bekleyen Keşif" : "Mahsup Bekleyen",
            amountsHidden ? missingReports : settlementPending,
          ],
        ].map(([title, value]) => (
          <article
            key={String(title)}
            className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
          >
            <span className="text-xs font-bold text-slate-500">{title}</span>
            <strong className="mt-3 block text-3xl text-slate-800">
              {loading ? "…" : value}
            </strong>
          </article>
        ))}
      </div>

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-xl font-bold text-slate-800">
            Görevlendirme Listesi
          </h2>

          <div className="flex flex-wrap gap-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Personel, proje, amaç ara"
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm"
            />

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm"
            >
              <option value="">Tüm durumlar</option>
              {statusOptions.map((x) => (
                <option key={x.value} value={x.value}>
                  {x.label}
                </option>
              ))}
            </select>

            {companies.length > 1 && (
              <select
                value={companyFilter}
                onChange={(e) => setCompanyFilter(e.target.value)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm"
              >
                <option value="">Tüm şirketler</option>
                {companies.map((x) => (
                  <option key={x.id} value={x.id}>
                    {x.name}
                  </option>
                ))}
              </select>
            )}

            <select
              value={personnelFilter}
              onChange={(e) => setPersonnelFilter(e.target.value)}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm"
            >
              <option value="">Tüm personel</option>
              {personnel.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.fullName}
                </option>
              ))}
            </select>

            <button
              type="button"
              onClick={loadItems}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
            >
              Uygula
            </button>

            {canEdit && (
              <button
                type="button"
                onClick={() => setShowForm((x) => !x)}
                className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
              >
                {showForm ? "Formu Kapat" : "+ Yeni Görevlendirme"}
              </button>
            )}
          </div>
        </div>

        {!canApprove && (
          <p className="mt-3 text-xs text-slate-500">
            Görevlendirmeyi Genel Müdür onaylar; onaylanmadan maliyet ve
            harcırah hedef projeye yansımaz.
          </p>
        )}
      </section>

      {showForm && canEdit && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <form onSubmit={save} className="grid gap-4">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <label className="text-sm text-slate-600">
                Şirket
                <select
                  value={form.companyId}
                  onChange={(e) =>
                    setForm((x) => ({
                      ...x,
                      companyId: e.target.value,
                      personnelId: "",
                      targetProjectId: "",
                    }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                >
                  <option value="">Şirket seçin</option>
                  {companies.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="text-sm text-slate-600">
                Personel
                <select
                  value={form.personnelId}
                  onChange={(e) =>
                    setForm((x) => ({ ...x, personnelId: e.target.value }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                >
                  <option value="">Personel seçin</option>
                  {formPersonnel.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.fullName}
                    </option>
                  ))}
                </select>
              </label>

              <label className="text-sm text-slate-600">
                Görev türü
                <select
                  value={form.dutyType}
                  onChange={(e) =>
                    setForm((x) => ({
                      ...x,
                      dutyType: e.target.value,
                      targetProjectId: "",
                    }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                >
                  {dutyTypeOptions.map((x) => (
                    <option key={x.value} value={x.value}>
                      {x.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="text-sm text-slate-600">
                Hedef proje
                <select
                  value={form.targetProjectId}
                  onChange={(e) =>
                    setForm((x) => ({ ...x, targetProjectId: e.target.value }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                >
                  <option value="">
                    {form.dutyType === "1"
                      ? "Keşif statüsündeki proje seçin"
                      : "Aktif proje seçin"}
                  </option>
                  {formProjects.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.code} — {x.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="text-sm text-slate-600">
                Başlangıç
                <input
                  type="date"
                  value={form.startDate}
                  onChange={(e) =>
                    setForm((x) => ({ ...x, startDate: e.target.value }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                />
              </label>

              <label className="text-sm text-slate-600">
                Bitiş
                <input
                  type="date"
                  value={form.endDate}
                  onChange={(e) =>
                    setForm((x) => ({ ...x, endDate: e.target.value }))
                  }
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                />
              </label>

              {canWriteAmounts ? (
                <label className="text-sm text-slate-600">
                  Günlük harcırah
                  <input
                    type="number"
                    step="0.01"
                    min="0"
                    value={form.dailyAllowance}
                    onChange={(e) =>
                      setForm((x) => ({ ...x, dailyAllowance: e.target.value }))
                    }
                    className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                  />
                </label>
              ) : (
                <p className="self-end rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
                  Harcırah tutarını ek ödeme yetkisi olan kullanıcı
                  girer; görev sıfır harcırahla açılır ve detayda
                  düzeltilir.
                </p>
              )}

              <label className="flex items-center gap-2 self-end text-sm text-slate-600">
                <input
                  type="checkbox"
                  checked={form.isOutOfCity}
                  onChange={(e) =>
                    setForm((x) => ({ ...x, isOutOfCity: e.target.checked }))
                  }
                />
                Şehir dışı görev
              </label>
            </div>

            <input
              value={form.purpose}
              onChange={(e) =>
                setForm((x) => ({ ...x, purpose: e.target.value }))
              }
              placeholder="Görev amacı (zorunlu)"
              className="rounded-lg border border-slate-300 p-3"
            />

            <textarea
              value={form.notes}
              onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))}
              rows={2}
              placeholder="Notlar"
              className="rounded-lg border border-slate-300 p-3"
            />

            {selectedTypeHint && (
              <p className="text-xs text-slate-500">{selectedTypeHint}</p>
            )}

            <div>
              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-3 text-sm font-semibold text-white disabled:opacity-60"
              >
                {saving ? "Kaydediliyor…" : "Talebi Aç"}
              </button>
            </div>
          </form>
        </section>
      )}

      {selectedId && (
        <DutyDetailPanel
          dutyId={selectedId}
          canEdit={canEdit}
          canWriteReport={canWriteReport}
          canDecideOutcome={canDecideOutcome}
          onChanged={loadItems}
          onClose={() => setSelectedId(null)}
        />
      )}

      <section className="overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
        <DataTable
          rows={visibleItems}
          columns={columns}
          rowKey={(item) => item.id}
          loading={loading}
          title="Görevlendirmeler"
          emptyText="Görevlendirme kaydı yok."
          resetKey={`${companyFilter}|${personnelFilter}|${statusFilter}`}
        />
      </section>

      <ConfirmDialog
        // key ile her hedefte yeniden kuruluyor: önceki işlemin
        // gerekçesi yenisine yapışmasın.
        key={`${confirmTarget?.mode ?? "kapali"}-${confirmTarget?.item.id ?? ""}`}
        open={confirmTarget !== null}
        title={
          confirmTarget?.mode === "cancel"
            ? "Görevlendirmeyi iptal et"
            : "Görevlendirmeyi reddet"
        }
        description={
          confirmTarget?.mode === "cancel"
            ? "Projeye yansıyan yol, konaklama ve harcırah aynı işlemde geri alınır. Mahsup avansı açılmışsa o da kapanır; bordro o avanstan zaten kesmişse iptal reddedilir."
            : "Talep kapanır ve talebi açana gerekçe iletilir. Onaylanmamış görev maliyet üretmediği için deftere dokunulmaz."
        }
        confirmLabel={confirmTarget?.mode === "cancel" ? "İptal Et" : "Reddet"}
        requireReason
        busy={actionId !== null}
        error={confirmError}
        onCancel={() => {
          setConfirmTarget(null);
          setConfirmError("");
        }}
        onConfirm={(reason) => void runConfirmedAction(reason)}
      />
      {pending && (
        <ConfirmDialog
          open
          title="Görevlendirmeyi Onayla"
          description="Görevlendirme onaylanacak. Onaylı görevlendirme masraf ve puantaj hesabına girer."
          confirmLabel="Onayla"
          busy={actionId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() => void approve(pending)}
        />
      )}
    </ErpShell>
  );
}
