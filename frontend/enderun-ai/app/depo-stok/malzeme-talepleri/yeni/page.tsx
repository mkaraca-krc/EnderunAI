"use client";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";

import {
  purchaseRequestService,
  type PurchaseRequestPriority,
} from "@/services/purchase-request.service";

const today = new Date().toISOString().slice(0, 10);

type MaterialRequestLine = {
  inventoryItemId: string;
  materialDescription: string;
  quantity: string;
  unit: string;
  requestedDeliveryDate: string;
  notes: string;
};

type MaterialRequestForm = {
  companyId: string;
  projectId: string;
  requestDate: string;
  neededByDate: string;
  requestedByName: string;
  description: string;
  priority: string;
  items: MaterialRequestLine[];
};

function emptyLine(): MaterialRequestLine {
  return {
    inventoryItemId: "",
    materialDescription: "",
    quantity: "",
    unit: "Adet",
    requestedDeliveryDate: "",
    notes: "",
  };
}

const initialForm: MaterialRequestForm = {
  companyId: "",
  projectId: "",
  requestDate: today,
  neededByDate: "",
  requestedByName: "",
  description: "",
  priority: "1",
  items: [emptyLine()],
};

export default function NewMaterialRequestPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [inventoryItems, setInventoryItems] =
    useState<InventoryItemListItem[]>([]);

  const [form, setForm] =
    useState<MaterialRequestForm>(initialForm);

  const [loading, setLoading] = useState(true);
  const [loadingItems, setLoadingItems] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void loadInitialData();
  }, []);

  async function loadInitialData() {
    try {
      setLoading(true);
      setError("");

      const [companyData, projectData] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
      ]);

      setCompanies(companyData);
      setProjects(projectData);

      if (companyData.length === 1) {
        const companyId = companyData[0].id;

        setForm((current) => ({
          ...current,
          companyId,
        }));

        await loadInventoryItems(companyId);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Form verileri yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }

  async function loadInventoryItems(companyId: string) {
    if (!companyId) {
      setInventoryItems([]);
      return;
    }

    try {
      setLoadingItems(true);

      const data = await inventoryService.getItems({
        companyId,
      });

      setInventoryItems(
        data.filter((item) => item.isActive),
      );
    } catch (err) {
      setInventoryItems([]);

      setError(
        err instanceof Error
          ? err.message
          : "Malzeme kartları yüklenemedi.",
      );
    } finally {
      setLoadingItems(false);
    }
  }

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !form.companyId ||
          project.companyId === form.companyId,
      ),
    [projects, form.companyId],
  );

  function changeCompany(companyId: string) {
    setForm((current) => ({
      ...current,
      companyId,
      projectId: "",
      items: [emptyLine()],
    }));

    void loadInventoryItems(companyId);
  }

  function updateForm<K extends keyof MaterialRequestForm>(
    key: K,
    value: MaterialRequestForm[K],
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }));
  }

  function updateLine<K extends keyof MaterialRequestLine>(
    index: number,
    key: K,
    value: MaterialRequestLine[K],
  ) {
    setForm((current) => ({
      ...current,
      items: current.items.map((line, lineIndex) =>
        lineIndex === index
          ? {
              ...line,
              [key]: value,
            }
          : line,
      ),
    }));
  }

  function selectInventoryItem(
    index: number,
    inventoryItemId: string,
  ) {
    const selectedItem = inventoryItems.find(
      (item) => item.id === inventoryItemId,
    );

    setForm((current) => ({
      ...current,
      items: current.items.map((line, lineIndex) =>
        lineIndex === index
          ? {
              ...line,
              inventoryItemId,
              materialDescription: selectedItem
                ? `${selectedItem.code} - ${selectedItem.name}`
                : "",
              unit: selectedItem?.unit || "Adet",
            }
          : line,
      ),
    }));
  }

  function addLine() {
    setForm((current) => ({
      ...current,
      items: [...current.items, emptyLine()],
    }));
  }

  function removeLine(index: number) {
    setForm((current) => ({
      ...current,
      items:
        current.items.length === 1
          ? current.items
          : current.items.filter(
              (_, lineIndex) => lineIndex !== index,
            ),
    }));
  }

  function validate(): string | null {
    if (!form.companyId) {
      return "Şirket seçilmelidir.";
    }

    if (!form.projectId) {
      return "Proje seçilmelidir.";
    }

    if (!form.requestDate) {
      return "Talep tarihi girilmelidir.";
    }

    if (!form.requestedByName.trim()) {
      return "Talep eden kişi girilmelidir.";
    }

    if (form.items.length === 0) {
      return "En az bir malzeme kalemi eklenmelidir.";
    }

    for (let index = 0; index < form.items.length; index += 1) {
      const line = form.items[index];

      if (!line.inventoryItemId) {
        return `${index + 1}. kalemde stok kartı seçilmelidir.`;
      }

      if (
        !line.quantity ||
        Number(line.quantity) <= 0
      ) {
        return `${index + 1}. kalemde miktar sıfırdan büyük olmalıdır.`;
      }
    }

    return null;
  }

  async function submit(
    event: FormEvent<HTMLFormElement>,
  ) {
    event.preventDefault();
    setError("");

    const validationError = validate();

    if (validationError) {
      setError(validationError);
      return;
    }

    try {
      setSaving(true);

      const result = await purchaseRequestService.create({
        companyId: form.companyId,
        projectId: form.projectId,

        // 0: Satın Alma
        // 1: Şantiye / Depo Malzeme Talebi
        requestType: 1,

        requestDate: form.requestDate,
        neededByDate: form.neededByDate || null,
        requestedByName: form.requestedByName.trim(),
        description: form.description.trim() || null,
        priority: Number(
          form.priority,
        ) as PurchaseRequestPriority,

        items: form.items.map((line) => ({
          inventoryItemId: line.inventoryItemId,
          materialDescription:
            line.materialDescription.trim(),
          quantity: Number(line.quantity),
          unit: line.unit,
          requestedDeliveryDate:
            line.requestedDeliveryDate || null,
          notes: line.notes.trim() || null,
        })),
      });

      router.push(
        `/depo-stok/malzeme-talepleri/${result.id}`,
      );
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Malzeme talebi oluşturulamadı.",
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Yeni Malzeme Talebi"
      description="Şantiye veya proje için depodan karşılanacak malzemeleri talep edin."
    >
      <div className="mb-5">
        <Link
          href="/depo-stok/malzeme-talepleri"
          className="text-sm font-medium text-slate-600 hover:text-slate-950"
        >
          ← Malzeme Taleplerine Dön
        </Link>
      </div>

      {error ? (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      ) : null}

      <form
        onSubmit={submit}
        className="space-y-6"
      >
        <section className="rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="border-b border-slate-200 px-5 py-4">
            <h2 className="text-lg font-semibold text-slate-950">
              Talep Bilgileri
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Talebin hangi şirket ve proje için
              oluşturulacağını belirleyin.
            </p>
          </div>

          <div className="grid gap-5 p-5 md:grid-cols-2 xl:grid-cols-3">
            <Field label="Şirket" required>
              <select
                value={form.companyId}
                onChange={(event) =>
                  changeCompany(event.target.value)
                }
                disabled={loading}
                className="input"
              >
                <option value="">
                  {loading
                    ? "Yükleniyor..."
                    : "Şirket seçin"}
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.code} · {company.name}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Proje" required>
              <select
                value={form.projectId}
                onChange={(event) =>
                  updateForm(
                    "projectId",
                    event.target.value,
                  )
                }
                disabled={!form.companyId}
                className="input"
              >
                <option value="">
                  {form.companyId
                    ? "Proje seçin"
                    : "Önce şirket seçin"}
                </option>

                {filteredProjects.map((project) => (
                  <option
                    key={project.id}
                    value={project.id}
                  >
                    {project.code} · {project.name}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Talep Eden" required>
              <input
                value={form.requestedByName}
                onChange={(event) =>
                  updateForm(
                    "requestedByName",
                    event.target.value,
                  )
                }
                placeholder="Ad soyad"
                className="input"
              />
            </Field>

            <Field label="Talep Tarihi" required>
              <input
                type="date"
                value={form.requestDate}
                onChange={(event) =>
                  updateForm(
                    "requestDate",
                    event.target.value,
                  )
                }
                className="input"
              />
            </Field>

            <Field label="İhtiyaç Tarihi">
              <input
                type="date"
                value={form.neededByDate}
                onChange={(event) =>
                  updateForm(
                    "neededByDate",
                    event.target.value,
                  )
                }
                className="input"
              />
            </Field>

            <Field label="Öncelik">
              <select
                value={form.priority}
                onChange={(event) =>
                  updateForm(
                    "priority",
                    event.target.value,
                  )
                }
                className="input"
              >
                <option value="0">Düşük</option>
                <option value="1">Normal</option>
                <option value="2">Yüksek</option>
                <option value="3">Kritik</option>
              </select>
            </Field>

            <div className="md:col-span-2 xl:col-span-3">
              <Field label="Açıklama">
                <textarea
                  value={form.description}
                  onChange={(event) =>
                    updateForm(
                      "description",
                      event.target.value,
                    )
                  }
                  rows={3}
                  placeholder="Talep hakkında genel açıklama"
                  className="input resize-y"
                />
              </Field>
            </div>
          </div>
        </section>

        <section className="rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="flex flex-col gap-3 border-b border-slate-200 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-950">
                Talep Kalemleri
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Depodan talep edilen stok kartlarını ve
                miktarlarını girin.
              </p>
            </div>

            <button
              type="button"
              onClick={addLine}
              className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              + Malzeme Ekle
            </button>
          </div>

          <div className="space-y-4 p-5">
            {form.items.map((line, index) => {
              const selectedItem =
                inventoryItems.find(
                  (item) =>
                    item.id === line.inventoryItemId,
                );

              return (
                <div
                  key={index}
                  className="rounded-xl border border-slate-200 bg-slate-50 p-4"
                >
                  <div className="mb-4 flex items-center justify-between">
                    <div>
                      <h3 className="font-semibold text-slate-900">
                        Malzeme {index + 1}
                      </h3>

                      {selectedItem ? (
                        <p className="mt-1 text-xs text-slate-500">
                          Kullanılabilir stok:{" "}
                          {selectedItem.availableStock}{" "}
                          {selectedItem.unit}
                        </p>
                      ) : null}
                    </div>

                    <button
                      type="button"
                      disabled={form.items.length === 1}
                      onClick={() => removeLine(index)}
                      className="rounded-lg px-3 py-2 text-sm font-medium text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      Kaldır
                    </button>
                  </div>

                  <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-6">
                    <div className="md:col-span-2 xl:col-span-3">
                      <Field label="Stok Kartı" required>
                        <select
                          value={line.inventoryItemId}
                          onChange={(event) =>
                            selectInventoryItem(
                              index,
                              event.target.value,
                            )
                          }
                          disabled={
                            !form.companyId ||
                            loadingItems
                          }
                          className="input"
                        >
                          <option value="">
                            {!form.companyId
                              ? "Önce şirket seçin"
                              : loadingItems
                                ? "Malzemeler yükleniyor..."
                                : "Malzeme seçin"}
                          </option>

                          {inventoryItems.map((item) => (
                            <option
                              key={item.id}
                              value={item.id}
                            >
                              {item.code} · {item.name} ·{" "}
                              {item.availableStock}{" "}
                              {item.unit}
                            </option>
                          ))}
                        </select>
                      </Field>
                    </div>

                    <Field label="Miktar" required>
                      <input
                        type="number"
                        min="0.0001"
                        step="0.0001"
                        value={line.quantity}
                        onChange={(event) =>
                          updateLine(
                            index,
                            "quantity",
                            event.target.value,
                          )
                        }
                        className="input"
                      />
                    </Field>

                    <Field label="Birim">
                      <input
                        value={line.unit}
                        readOnly
                        className="input bg-slate-100"
                      />
                    </Field>

                    <Field label="İstenen Tarih">
                      <input
                        type="date"
                        value={
                          line.requestedDeliveryDate
                        }
                        onChange={(event) =>
                          updateLine(
                            index,
                            "requestedDeliveryDate",
                            event.target.value,
                          )
                        }
                        className="input"
                      />
                    </Field>

                    <div className="md:col-span-2 xl:col-span-6">
                      <Field label="Kalem Notu">
                        <input
                          value={line.notes}
                          onChange={(event) =>
                            updateLine(
                              index,
                              "notes",
                              event.target.value,
                            )
                          }
                          placeholder="Kullanım yeri, kat, mahal veya teknik not"
                          className="input"
                        />
                      </Field>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </section>

        <div className="flex flex-col-reverse justify-end gap-3 sm:flex-row">
          <Link
            href="/depo-stok/malzeme-talepleri"
            className="inline-flex items-center justify-center rounded-lg border border-slate-300 bg-white px-5 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Vazgeç
          </Link>

          <button
            type="submit"
            disabled={saving || loading}
            className="rounded-lg bg-brand-700 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-600 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {saving
              ? "Talep Kaydediliyor..."
              : "Malzeme Talebini Oluştur"}
          </button>
        </div>
      </form>

      <style jsx>{`
        :global(.input) {
          width: 100%;
          border: 1px solid rgb(203 213 225);
          border-radius: 0.5rem;
          padding: 0.625rem 0.75rem;
          font-size: 0.875rem;
          outline: none;
          background-color: white;
        }

        :global(.input:focus) {
          border-color: rgb(100 116 139);
          box-shadow: 0 0 0 2px rgb(226 232 240);
        }

        :global(.input:disabled) {
          cursor: not-allowed;
          background-color: rgb(241 245 249);
          color: rgb(100 116 139);
        }
      `}</style>
    </ErpShell>
  );
}

function Field({
  label,
  required = false,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="block space-y-2">
      <span className="text-sm font-medium text-slate-700">
        {label}
        {required ? (
          <span className="text-red-600"> *</span>
        ) : null}
      </span>

      {children}
    </label>
  );
}
