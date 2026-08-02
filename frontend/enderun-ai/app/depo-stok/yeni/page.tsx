"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  inventoryService,
  type CompanyOption,
  type CreateInventoryItemRequest,
  type InventoryItemType,
} from "@/services/inventory.service";

const initialForm: CreateInventoryItemRequest = {
  companyId: "",
  code: "",
  name: "",
  category: "",
  brand: "",
  model: "",
  unit: "Adet",
  barcode: "",
  minimumStock: 0,
  maximumStock: 0,
  type: 0,
};

export default function CreateInventoryItemPage() {
  const router = useRouter();
  const [form, setForm] =
    useState<CreateInventoryItemRequest>(initialForm);
  const [companies, setCompanies] = useState<CompanyOption[]>([]);
  const [loadingCompanies, setLoadingCompanies] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const data = await inventoryService.getCompanies();
        setCompanies(data);

        if (data.length === 1) {
          setForm((current) => ({
            ...current,
            companyId: data[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şirket listesi yüklenemedi.",
        );
      } finally {
        setLoadingCompanies(false);
      }
    })();
  }, []);

  function update<K extends keyof CreateInventoryItemRequest>(
    key: K,
    value: CreateInventoryItemRequest[K],
  ) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");

    if (!form.companyId || !form.code.trim() || !form.name.trim()) {
      setError("Şirket, malzeme kodu ve malzeme adı zorunludur.");
      return;
    }

    try {
      setSaving(true);
      await inventoryService.createItem({
        ...form,
        code: form.code.trim(),
        name: form.name.trim(),
        unit: form.unit.trim(),
      });
      router.push("/depo-stok");
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Malzeme kartı oluşturulamadı.",
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6">
      <div>
        <Link
          href="/depo-stok"
          className="text-sm font-medium text-slate-600 hover:text-slate-950"
        >
          ← Depo &amp; Stok
        </Link>
        <h1 className="mt-3 text-2xl font-semibold text-slate-950">
          Yeni malzeme kartı
        </h1>
        <p className="mt-1 text-sm text-slate-600">
          Şirket genelinde kullanılacak malzeme veya demirbaş kartını oluşturun.
        </p>
      </div>

      <form
        onSubmit={submit}
        className="space-y-6 rounded-xl border border-slate-200 bg-white p-6 shadow-sm"
      >
        {error ? (
          <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        <div className="grid gap-5 md:grid-cols-2">
          <Field label="Şirket" required>
            <select
              value={form.companyId}
              onChange={(event) =>
                update("companyId", event.target.value)
              }
              disabled={loadingCompanies}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2 disabled:bg-slate-100"
            >
              <option value="">
                {loadingCompanies ? "Yükleniyor..." : "Şirket seçin"}
              </option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Malzeme tipi" required>
            <select
              value={form.type}
              onChange={(event) =>
                update(
                  "type",
                  Number(event.target.value) as InventoryItemType,
                )
              }
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2"
            >
              <option value={0}>Stok malzemesi</option>
              <option value={1}>Sarf malzemesi</option>
              <option value={2}>Demirbaş</option>
            </select>
          </Field>

          <Field label="Malzeme kodu" required>
            <input
              value={form.code}
              onChange={(event) => update("code", event.target.value)}
              placeholder="Örn. ELK-KBL-0001"
              className="input"
            />
          </Field>

          <Field label="Malzeme adı" required>
            <input
              value={form.name}
              onChange={(event) => update("name", event.target.value)}
              placeholder="Örn. NYY 5x10 mm² kablo"
              className="input"
            />
          </Field>

          <Field label="Kategori">
            <input
              value={form.category}
              onChange={(event) =>
                update("category", event.target.value)
              }
              placeholder="Örn. Enerji kabloları"
              className="input"
            />
          </Field>

          <Field label="Birim" required>
            <select
              value={form.unit}
              onChange={(event) => update("unit", event.target.value)}
              className="input"
            >
              <option>Adet</option>
              <option>Metre</option>
              <option>Kg</option>
              <option>Takım</option>
              <option>Kutu</option>
              <option>Paket</option>
              <option>Rulo</option>
            </select>
          </Field>

          <Field label="Marka">
            <input
              value={form.brand}
              onChange={(event) => update("brand", event.target.value)}
              placeholder="Örn. Öznur"
              className="input"
            />
          </Field>

          <Field label="Model">
            <input
              value={form.model}
              onChange={(event) => update("model", event.target.value)}
              placeholder="Model veya üretici kodu"
              className="input"
            />
          </Field>

          <Field label="Barkod">
            <input
              value={form.barcode}
              onChange={(event) =>
                update("barcode", event.target.value)
              }
              placeholder="Barkod numarası"
              className="input"
            />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="Minimum stok">
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.minimumStock}
                onChange={(event) =>
                  update("minimumStock", Number(event.target.value))
                }
                className="input"
              />
            </Field>

            <Field label="Maksimum stok">
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.maximumStock}
                onChange={(event) =>
                  update("maximumStock", Number(event.target.value))
                }
                className="input"
              />
            </Field>
          </div>
        </div>

        <div className="flex justify-end gap-3 border-t border-slate-200 pt-5">
          <Link
            href="/depo-stok"
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            İptal
          </Link>
          <button
            type="submit"
            disabled={saving}
            className="rounded-lg bg-slate-950 px-5 py-2 text-sm font-medium text-white hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {saving ? "Kaydediliyor..." : "Malzeme kartını oluştur"}
          </button>
        </div>
      </form>

      <style jsx>{`
        :global(.input) {
          width: 100%;
          border: 1px solid rgb(203 213 225);
          border-radius: 0.5rem;
          padding: 0.5rem 0.75rem;
          font-size: 0.875rem;
          outline: none;
        }

        :global(.input:focus) {
          box-shadow: 0 0 0 2px rgb(203 213 225);
        }
      `}</style>
    </div>
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
        {required ? <span className="text-red-600"> *</span> : null}
      </span>
      {children}
    </label>
  );
}
