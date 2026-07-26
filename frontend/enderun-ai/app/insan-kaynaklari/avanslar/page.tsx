"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  hrAdvanceService,
  HrAdvanceItem,
} from "@/services/hr-advance.service";
import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";
import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";

type AdvanceForm = {
  companyId: string;
  personnelId: string;
  requestDate: string;
  requestedAmount: string;
  approvedAmount: string;
  currencyCode: string;
  deductionInstallmentCount: string;
  firstDeductionDate: string;
  reason: string;
  status: string;
  paymentReference: string;
};

const initialForm: AdvanceForm = {
  companyId: "",
  personnelId: "",
  requestDate: "",
  requestedAmount: "",
  approvedAmount: "0",
  currencyCode: "TRY",
  deductionInstallmentCount: "1",
  firstDeductionDate: "",
  reason: "",
  status: "1",
  paymentReference: "",
};

const statuses = [
  { value: 0, label: "Taslak" },
  { value: 1, label: "Onay Bekliyor" },
  { value: 2, label: "Onaylandı" },
  { value: 3, label: "Reddedildi" },
  { value: 4, label: "İptal Edildi" },
];

function statusLabel(value: number) {
  return statuses.find((x) => x.value === value)?.label ?? "Bilinmiyor";
}

function money(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: currency || "TRY",
  }).format(value);
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function AdvancePage() {
  const [items, setItems] = useState<HrAdvanceItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [form, setForm] = useState<AdvanceForm>(initialForm);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] = useState("");
  const [personnelFilter, setPersonnelFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [actionId, setActionId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const personnelById = useMemo(
    () => new Map(personnel.map((x) => [x.id, x])),
    [personnel]
  );

  const formPersonnel = useMemo(
    () =>
      form.companyId
        ? personnel.filter((x) => x.companyId === form.companyId)
        : personnel,
    [form.companyId, personnel]
  );

  const visibleItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    if (!term) return items;

    return items.filter((item) => {
      const person = personnelById.get(item.personnelId);

      return [
        person?.fullName,
        person?.employeeNumber,
        item.reason,
        item.paymentReference,
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR")
        .includes(term);
    });
  }, [items, personnelById, search]);

  const pendingCount = items.filter((x) => x.status === 1).length;
  const approvedCount = items.filter((x) => x.status === 2).length;
  const paidCount = items.filter((x) => Boolean(x.paidAtUtc)).length;
  const totalRequested = items.reduce(
    (sum, x) => sum + Number(x.requestedAmount),
    0
  );

  async function loadItems() {
    setLoading(true);
    setError("");

    try {
      setItems(
        await hrAdvanceService.getAll({
          companyId: companyFilter || undefined,
          personnelId: personnelFilter || undefined,
          status:
            statusFilter === "" ? undefined : Number(statusFilter),
        })
      );
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Avanslar yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadPage() {
      try {
        const [companyResult, personnelResult, advanceResult] =
          await Promise.all([
            companyService.getAll(),
            personnelService.getAll(),
            hrAdvanceService.getAll(),
          ]);

        setCompanies(companyResult);
        setPersonnel(personnelResult);
        setItems(advanceResult);

        if (companyResult.length === 1) {
          setForm((x) => ({
            ...x,
            companyId: companyResult[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Avans ekranı yüklenemedi."
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
      companyId: companies.length === 1 ? companies[0].id : "",
    });
    setShowForm(true);
  }

  function openEdit(item: HrAdvanceItem) {
    setEditingId(item.id);
    setForm({
      companyId: item.companyId,
      personnelId: item.personnelId,
      requestDate: item.requestDate.slice(0, 10),
      requestedAmount: String(item.requestedAmount),
      approvedAmount: String(item.approvedAmount),
      currencyCode: item.currencyCode,
      deductionInstallmentCount: String(
        item.deductionInstallmentCount
      ),
      firstDeductionDate: item.firstDeductionDate?.slice(0, 10) ?? "",
      reason: item.reason,
      status: String(item.status),
      paymentReference: item.paymentReference ?? "",
    });
    setShowForm(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const requestedAmount = Number(form.requestedAmount);
      const approvedAmount = Number(form.approvedAmount);
      const installments = Number(form.deductionInstallmentCount);

      if (!form.companyId) throw new Error("Şirket seçilmelidir.");
      if (!form.personnelId) throw new Error("Personel seçilmelidir.");
      if (!form.requestDate) throw new Error("Talep tarihi zorunludur.");

      if (!Number.isFinite(requestedAmount) || requestedAmount <= 0) {
        throw new Error("Avans tutarı sıfırdan büyük olmalıdır.");
      }

      if (!Number.isInteger(installments) || installments < 1) {
        throw new Error("Taksit sayısı en az 1 olmalıdır.");
      }

      if (!form.reason.trim()) {
        throw new Error("Avans nedeni zorunludur.");
      }

      if (editingId) {
        await hrAdvanceService.update(editingId, {
          projectId: null,
          requestDate: form.requestDate,
          requestedAmount,
          approvedAmount,
          currencyCode: form.currencyCode,
          deductionInstallmentCount: installments,
          firstDeductionDate: form.firstDeductionDate || null,
          reason: form.reason.trim(),
          status: Number(form.status),
          paymentReference: form.paymentReference.trim() || null,
        });

        setSuccess("Avans kaydı güncellendi.");
      } else {
        await hrAdvanceService.create({
          companyId: form.companyId,
          personnelId: form.personnelId,
          projectId: null,
          requestDate: form.requestDate,
          requestedAmount,
          currencyCode: form.currencyCode,
          deductionInstallmentCount: installments,
          firstDeductionDate: form.firstDeductionDate || null,
          reason: form.reason.trim(),
        });

        setSuccess("Avans talebi oluşturuldu.");
      }

      setShowForm(false);
      setEditingId(null);
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Avans kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function approve(item: HrAdvanceItem) {
    if (!window.confirm("Avans talebi onaylansın mı?")) return;

    setActionId(item.id);

    try {
      await hrAdvanceService.approve(item.id);
      setSuccess("Avans talebi onaylandı.");
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Avans onaylanamadı."
      );
    } finally {
      setActionId(null);
    }
  }

  async function markPaid(item: HrAdvanceItem) {
    const reference = window.prompt(
      "Ödeme referansı / dekont numarası:",
      item.paymentReference ?? ""
    );

    if (reference === null) return;

    setActionId(item.id);

    try {
      await hrAdvanceService.markPaid(item.id, reference.trim() || null);
      setSuccess("Avans ödenmiş olarak işaretlendi.");
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Ödeme kaydedilemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  async function remove(item: HrAdvanceItem) {
    if (!window.confirm("Avans kaydı silinsin mi?")) return;

    setActionId(item.id);

    try {
      await hrAdvanceService.delete(item.id);
      setSuccess("Avans kaydı silindi.");
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Avans silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  return (
    <ErpShell
      title="Personel Avansları"
      description="Avans talep, onay, ödeme ve bordro mahsup süreçleri"
    >
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
          ["Toplam Talep", items.length],
          ["Onay Bekleyen", pendingCount],
          ["Onaylanan", approvedCount],
          ["Ödenen", paidCount],
        ].map(([title, value]) => (
          <article
            key={String(title)}
            className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
          >
            <span className="text-xs font-bold text-slate-500">
              {title}
            </span>
            <strong className="mt-3 block text-3xl text-slate-800">
              {loading ? "…" : value}
            </strong>
          </article>
        ))}
      </div>

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-bold text-slate-800">
              Avans Kayıtları
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Toplam talep: {money(totalRequested, "TRY")}
            </p>
          </div>

          <div className="flex gap-2">
            <button
              type="button"
              onClick={loadItems}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
            >
              Yenile
            </button>

            <button
              type="button"
              onClick={openCreate}
              className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
            >
              + Yeni Avans Talebi
            </button>
          </div>
        </div>
      </section>

      {showForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <form onSubmit={save}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <select
                value={form.companyId}
                disabled={Boolean(editingId)}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    companyId: e.target.value,
                    personnelId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Şirket seçin</option>
                {companies.map((x) => (
                  <option value={x.id} key={x.id}>
                    {x.name}
                  </option>
                ))}
              </select>

              <select
                value={form.personnelId}
                disabled={Boolean(editingId)}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    personnelId: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Personel seçin</option>
                {formPersonnel.map((x) => (
                  <option value={x.id} key={x.id}>
                    {x.employeeNumber} - {x.fullName}
                  </option>
                ))}
              </select>

              <input
                type="date"
                value={form.requestDate}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    requestDate: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="number"
                min="1"
                step="0.01"
                placeholder="Talep edilen tutar"
                value={form.requestedAmount}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    requestedAmount: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              {editingId && (
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  placeholder="Onaylanan tutar"
                  value={form.approvedAmount}
                  onChange={(e) =>
                    setForm((x) => ({
                      ...x,
                      approvedAmount: e.target.value,
                    }))
                  }
                  className="rounded-lg border border-slate-300 p-3"
                />
              )}

              <select
                value={form.currencyCode}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    currencyCode: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="TRY">TRY - Türk Lirası</option>
                <option value="USD">USD - Amerikan Doları</option>
                <option value="EUR">EUR - Euro</option>
                <option value="GBP">GBP - İngiliz Sterlini</option>
              </select>

              <input
                type="number"
                min="1"
                step="1"
                placeholder="Taksit sayısı"
                value={form.deductionInstallmentCount}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    deductionInstallmentCount: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="date"
                value={form.firstDeductionDate}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    firstDeductionDate: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              {editingId && (
                <>
                  <select
                    value={form.status}
                    onChange={(e) =>
                      setForm((x) => ({
                        ...x,
                        status: e.target.value,
                      }))
                    }
                    className="rounded-lg border border-slate-300 p-3"
                  >
                    {statuses.map((x) => (
                      <option value={x.value} key={x.value}>
                        {x.label}
                      </option>
                    ))}
                  </select>

                  <input
                    placeholder="Ödeme referansı"
                    value={form.paymentReference}
                    onChange={(e) =>
                      setForm((x) => ({
                        ...x,
                        paymentReference: e.target.value,
                      }))
                    }
                    className="rounded-lg border border-slate-300 p-3"
                  />
                </>
              )}

              <textarea
                rows={3}
                placeholder="Avans nedeni"
                value={form.reason}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    reason: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-3"
              />
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowForm(false)}
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2 text-white"
              >
                {saving ? "Kaydediliyor…" : "Kaydet"}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Personel, neden veya referans ara"
            className="rounded-lg border border-slate-300 p-3"
          />

          <select
            value={companyFilter}
            onChange={(e) => setCompanyFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm şirketler</option>
            {companies.map((x) => (
              <option value={x.id} key={x.id}>
                {x.name}
              </option>
            ))}
          </select>

          <select
            value={personnelFilter}
            onChange={(e) => setPersonnelFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm personeller</option>
            {personnel.map((x) => (
              <option value={x.id} key={x.id}>
                {x.fullName}
              </option>
            ))}
          </select>

          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm durumlar</option>
            {statuses.map((x) => (
              <option value={x.value} key={x.value}>
                {x.label}
              </option>
            ))}
          </select>

          <button
            type="button"
            onClick={loadItems}
            className="rounded-lg bg-slate-800 p-3 font-semibold text-white"
          >
            Filtrele
          </button>
        </div>
      </section>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[1150px]">
            <thead className="bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="p-4">Personel</th>
                <th className="p-4">Talep Tarihi</th>
                <th className="p-4">Talep</th>
                <th className="p-4">Onaylanan</th>
                <th className="p-4">Taksit</th>
                <th className="p-4">İlk Mahsup</th>
                <th className="p-4">Durum</th>
                <th className="p-4">Ödeme</th>
                <th className="p-4 text-right">İşlemler</th>
              </tr>
            </thead>

            <tbody>
              {visibleItems.map((item) => {
                const person = personnelById.get(item.personnelId);
                const busy = actionId === item.id;

                return (
                  <tr key={item.id} className="border-t text-sm">
                    <td className="p-4">
                      <strong>{person?.fullName ?? "—"}</strong>
                      <small className="block text-slate-500">
                        {person?.employeeNumber ?? "—"}
                      </small>
                    </td>
                    <td className="p-4">{formatDate(item.requestDate)}</td>
                    <td className="p-4 font-semibold">
                      {money(item.requestedAmount, item.currencyCode)}
                    </td>
                    <td className="p-4">
                      {money(item.approvedAmount, item.currencyCode)}
                    </td>
                    <td className="p-4">
                      {item.deductionInstallmentCount}
                    </td>
                    <td className="p-4">
                      {formatDate(item.firstDeductionDate)}
                    </td>
                    <td className="p-4">
                      {statusLabel(item.status)}
                    </td>
                    <td className="p-4">
                      {item.paidAtUtc
                        ? `Ödendi · ${formatDate(item.paidAtUtc)}`
                        : "Ödenmedi"}
                    </td>
                    <td className="p-4">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => openEdit(item)}
                          className="rounded border px-3 py-1.5 text-xs"
                        >
                          Düzenle
                        </button>

                        {item.status === 1 && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => approve(item)}
                            className="rounded bg-emerald-600 px-3 py-1.5 text-xs text-white"
                          >
                            Onayla
                          </button>
                        )}

                        {item.status === 2 && !item.paidAtUtc && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => markPaid(item)}
                            className="rounded bg-blue-700 px-3 py-1.5 text-xs text-white"
                          >
                            Ödendi
                          </button>
                        )}

                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => remove(item)}
                          className="rounded bg-red-50 px-3 py-1.5 text-xs text-red-700"
                        >
                          Sil
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}

              {!loading && visibleItems.length === 0 && (
                <tr>
                  <td colSpan={9} className="p-12 text-center text-slate-500">
                    Kayıt bulunamadı.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </ErpShell>
  );
}
