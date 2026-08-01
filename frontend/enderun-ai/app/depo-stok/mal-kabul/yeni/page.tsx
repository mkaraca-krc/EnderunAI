"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import {
  FormEvent,
  Suspense,
  useCallback,
  useEffect,
  useState,
} from "react";
import { apiClient } from "@/lib/api/api-client";
import {
  goodsReceiptService,
  type CreateGoodsReceiptRequest,
} from "@/services/goods-receipt.service";
import {
  purchaseOrderService,
  type PurchaseOrderDetail,
} from "@/services/purchase-order.service";

type ProjectWarehouse = {
  id: string;
  code: string;
  name: string;
  type: number;
  isActive: boolean;
};

type ProjectDetail = {
  id: string;
  warehouses: ProjectWarehouse[];
};

function dateToUtc(value: string) {
  return value ? `${value}T00:00:00.000Z` : null;
}

export default function NewGoodsReceiptPage() {
  return (
    <Suspense
      fallback={
        <div className="p-6">
          <div className="rounded-xl border border-slate-200 bg-white p-12 text-center text-sm text-slate-500 shadow-sm">
            Mal kabul ekranı hazırlanıyor...
          </div>
        </div>
      }
    >
      <NewGoodsReceiptContent />
    </Suspense>
  );
}

function NewGoodsReceiptContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const purchaseOrderId = searchParams.get("siparis") ?? "";

  const [order, setOrder] = useState<PurchaseOrderDetail | null>(null);
  const [warehouses, setWarehouses] = useState<ProjectWarehouse[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const [form, setForm] = useState({
    warehouseId: "",
    receiptDate: new Date().toISOString().slice(0, 10),
    receivedByName: "",
    dispatchNoteNumber: "",
    dispatchNoteDate: "",
    invoiceNumber: "",
    invoiceDate: "",
    vehiclePlate: "",
    driverName: "",
    description: "",
    notes: "",
  });

  const load = useCallback(async () => {
    if (!purchaseOrderId) {
      setError("Mal kabul oluşturmak için satın alma siparişi seçilmelidir.");
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setError("");
      const purchaseOrder = await purchaseOrderService.getById(
        purchaseOrderId,
      );

      if (![2, 3].includes(purchaseOrder.status)) {
        throw new Error(
          "Mal kabul yalnız onaylı veya kısmi teslim durumundaki sipariş için oluşturulabilir.",
        );
      }

      const project = await apiClient<ProjectDetail>(
        `projects/${purchaseOrder.projectId}`,
      );
      const activeWarehouses = (project.warehouses ?? []).filter(
        (warehouse) => warehouse.isActive,
      );

      setOrder(purchaseOrder);
      setWarehouses(activeWarehouses);
      setForm((current) => ({
        ...current,
        warehouseId:
          current.warehouseId || activeWarehouses[0]?.id || "",
      }));
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Sipariş ve depo bilgileri yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }, [purchaseOrderId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!order || !form.warehouseId || !form.receivedByName.trim()) {
      setError("Depo ve teslim alan kişi zorunludur.");
      return;
    }

    const payload: CreateGoodsReceiptRequest = {
      warehouseId: form.warehouseId,
      receiptDate: dateToUtc(form.receiptDate)!,
      receivedByName: form.receivedByName.trim(),
      dispatchNoteNumber: form.dispatchNoteNumber.trim() || null,
      dispatchNoteDate: dateToUtc(form.dispatchNoteDate),
      invoiceNumber: form.invoiceNumber.trim() || null,
      invoiceDate: dateToUtc(form.invoiceDate),
      vehiclePlate: form.vehiclePlate.trim() || null,
      driverName: form.driverName.trim() || null,
      description: form.description.trim() || null,
      notes: form.notes.trim() || null,
    };

    try {
      setSubmitting(true);
      setError("");
      const receipt = await goodsReceiptService.createFromPurchaseOrder(
        order.id,
        payload,
      );
      router.push(`/depo-stok/mal-kabul/${receipt.id}`);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal kabul taslağı oluşturulamadı.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="space-y-6 p-6">
      <div>
        <Link
          href={
            purchaseOrderId
              ? `/satin-alma/siparis/${purchaseOrderId}`
              : "/satin-alma/siparis"
          }
          className="text-sm font-medium text-slate-600 hover:text-slate-950"
        >
          ← Satın alma siparişine dön
        </Link>
        <h1 className="mt-2 text-2xl font-semibold text-slate-950">
          Mal Kabul Oluştur
        </h1>
        <p className="mt-1 text-sm text-slate-600">
          Teslimat belgesini taslak olarak açın; miktar ve stok kartlarını
          sonraki ekranda doğrulayın.
        </p>
      </div>

      {error ? (
        <div className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      ) : null}

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-12 text-center text-sm text-slate-500 shadow-sm">
          Sipariş ve depo bilgileri yükleniyor...
        </div>
      ) : order ? (
        <form onSubmit={submit} className="space-y-6">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="text-lg font-semibold text-slate-950">
              Sipariş Bilgileri
            </h2>
            <dl className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <Info label="Sipariş" value={order.orderNumber} />
              <Info label="Tedarikçi" value={order.supplierTitle} />
              <Info
                label="Proje"
                value={`${order.projectCode} · ${order.projectName}`}
              />
              <Info
                label="Kalan Kalem"
                value={String(
                  order.items.filter(
                    (item) => item.receivedQuantity < item.quantity,
                  ).length,
                )}
              />
            </dl>
          </section>

          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="text-lg font-semibold text-slate-950">
              Teslimat Bilgileri
            </h2>
            <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <Field label="Depo" required>
                <select
                  required
                  value={form.warehouseId}
                  onChange={(event) =>
                    setForm({ ...form, warehouseId: event.target.value })
                  }
                  className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none ring-slate-300 focus:ring-2"
                >
                  <option value="">Depo seçin</option>
                  {warehouses.map((warehouse) => (
                    <option key={warehouse.id} value={warehouse.id}>
                      {warehouse.code} · {warehouse.name}
                    </option>
                  ))}
                </select>
              </Field>
              <TextInput
                label="Mal Kabul Tarihi"
                type="date"
                required
                value={form.receiptDate}
                onChange={(value) => setForm({ ...form, receiptDate: value })}
              />
              <TextInput
                label="Teslim Alan"
                required
                value={form.receivedByName}
                onChange={(value) =>
                  setForm({ ...form, receivedByName: value })
                }
              />
              <TextInput
                label="İrsaliye No"
                value={form.dispatchNoteNumber}
                onChange={(value) =>
                  setForm({ ...form, dispatchNoteNumber: value })
                }
              />
              <TextInput
                label="İrsaliye Tarihi"
                type="date"
                value={form.dispatchNoteDate}
                onChange={(value) =>
                  setForm({ ...form, dispatchNoteDate: value })
                }
              />
              <TextInput
                label="Araç Plakası"
                value={form.vehiclePlate}
                onChange={(value) => setForm({ ...form, vehiclePlate: value })}
              />
              <TextInput
                label="Fatura No"
                value={form.invoiceNumber}
                onChange={(value) => setForm({ ...form, invoiceNumber: value })}
              />
              <TextInput
                label="Fatura Tarihi"
                type="date"
                value={form.invoiceDate}
                onChange={(value) => setForm({ ...form, invoiceDate: value })}
              />
              <TextInput
                label="Sürücü"
                value={form.driverName}
                onChange={(value) => setForm({ ...form, driverName: value })}
              />
            </div>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <TextArea
                label="Açıklama"
                value={form.description}
                onChange={(value) => setForm({ ...form, description: value })}
              />
              <TextArea
                label="Notlar"
                value={form.notes}
                onChange={(value) => setForm({ ...form, notes: value })}
              />
            </div>
          </section>

          {warehouses.length === 0 ? (
            <div className="rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
              Bu projede aktif depo bulunamadı. Önce proje depo kaydını açın.
            </div>
          ) : null}

          <div className="flex flex-wrap justify-end gap-3">
            <Link
              href={`/satin-alma/siparis/${order.id}`}
              className="rounded-lg border border-slate-300 bg-white px-5 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              Vazgeç
            </Link>
            <button
              type="submit"
              disabled={submitting || warehouses.length === 0}
              className="rounded-lg bg-slate-950 px-5 py-2.5 text-sm font-medium text-white hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {submitting ? "Oluşturuluyor..." : "Taslak Oluştur"}
            </button>
          </div>
        </form>
      ) : null}
    </div>
  );
}

function Field({
  label,
  required,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="block text-sm font-medium text-slate-700">
      {label}
      {required ? " *" : ""}
      <div className="mt-1">{children}</div>
    </label>
  );
}

function TextInput({
  label,
  value,
  onChange,
  type = "text",
  required,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  type?: string;
  required?: boolean;
}) {
  return (
    <Field label={label} required={required}>
      <input
        type={type}
        required={required}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none ring-slate-300 focus:ring-2"
      />
    </Field>
  );
}

function TextArea({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <Field label={label}>
      <textarea
        rows={3}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-full resize-y rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none ring-slate-300 focus:ring-2"
      />
    </Field>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
        {label}
      </dt>
      <dd className="mt-1 font-medium text-slate-900">{value}</dd>
    </div>
  );
}
