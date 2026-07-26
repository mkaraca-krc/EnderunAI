"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company = { id: string; code: string; name: string };
type Project = { id: string; companyId: string; code: string; name: string };
type OrderRow = {
  id: string;
  companyId: string;
  projectId: string;
  orderNumber: string;
  orderDateUtc: string;
  deliveryDateUtc?: string | null;
  status: number;
  currencyCode: string;
  exchangeRate: number;
  vatRate: number;
  supplierId: string;
  supplierTitle: string;
  netAmount: number;
  receivedQuantity: number;
};
type OrderItem = {
  id: string;
  materialId: string;
  quantity: number;
  receivedQuantity: number;
  unit: string;
  unitPrice: number;
  discountRate: number;
  description?: string | null;
  material?: { code: string; name: string };
};
type OrderDetail = OrderRow & { description?: string | null; items: OrderItem[] };
type Receipt = {
  id: string;
  purchaseOrderId: string;
  receiptNumber: string;
  receiptDateUtc: string;
  status: number;
  items: Array<{ id: string; quantity: number; acceptedQuantity?: number; rejectedQuantity?: number }>;
};

const statusNames = ["Taslak", "Onay Bekliyor", "Onaylandı", "Kısmi Teslim", "Tamamlandı", "İptal"];
const receiptStatusNames = ["Taslak", "İşlendi", "İptal"];

async function api(path: string, options?: RequestInit) {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store", ...options });
  if (response.status === 401) {
    location.href = "/login";
    throw new Error("Oturum süresi doldu.");
  }
  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("application/json") ? await response.json().catch(() => null) : await response.text().catch(() => "");
  if (!response.ok) throw new Error(body?.message ?? body ?? `Hata ${response.status}`);
  return body;
}

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 2 }).format(value || 0);
}

function date(value?: string | null) {
  return value ? new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium" }).format(new Date(value)) : "-";
}

export default function Page() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [orders, setOrders] = useState<OrderRow[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<OrderDetail | null>(null);
  const [receipts, setReceipts] = useState<Receipt[]>([]);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const loadOrders = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const query = new URLSearchParams();
      if (companyId) query.set("companyId", companyId);
      if (projectId) query.set("projectId", projectId);
      if (status !== "") query.set("status", status);
      const rows = await api(`purchase-orders?${query.toString()}`);
      setOrders(rows);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Siparişler alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, projectId, status]);

  useEffect(() => {
    (async () => {
      try {
        const [companyRows, projectRows] = await Promise.all([api("companies"), api("projects")]);
        setCompanies(companyRows);
        setProjects(projectRows);
        setCompanyId(companyRows[0]?.id ?? "");
      } catch (e) {
        setError(e instanceof Error ? e.message : "Başlangıç verileri alınamadı.");
      }
    })();
  }, []);

  useEffect(() => { loadOrders(); }, [loadOrders]);

  const filteredProjects = useMemo(
    () => projects.filter((x) => !companyId || x.companyId === companyId),
    [projects, companyId]
  );

  const visibleOrders = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    if (!term) return orders;
    return orders.filter((x) =>
      x.orderNumber.toLocaleLowerCase("tr-TR").includes(term) ||
      x.supplierTitle.toLocaleLowerCase("tr-TR").includes(term) ||
      projects.find((p) => p.id === x.projectId)?.name.toLocaleLowerCase("tr-TR").includes(term)
    );
  }, [orders, projects, search]);

  const stats = useMemo(() => ({
    total: orders.length,
    pending: orders.filter((x) => x.status === 1).length,
    approved: orders.filter((x) => x.status === 2).length,
    partial: orders.filter((x) => x.status === 3).length,
    volume: orders.filter((x) => x.currencyCode === "TRY").reduce((sum, x) => sum + x.netAmount * x.exchangeRate, 0),
  }), [orders]);

  async function openDetail(id: string) {
    setError("");
    try {
      const [detail, receiptRows] = await Promise.all([
        api(`purchase-orders/${id}`),
        api(`goods-receipts?purchaseOrderId=${id}`),
      ]);
      setSelected(detail);
      setReceipts(receiptRows);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Sipariş detayı alınamadı.");
    }
  }

  async function action(id: string, name: "submit" | "approve" | "cancel") {
    setError("");
    setMessage("");
    try {
      await api(`purchase-orders/${id}/${name}`, { method: "POST" });
      setMessage(name === "submit" ? "Sipariş onaya gönderildi." : name === "approve" ? "Sipariş onaylandı." : "Sipariş iptal edildi.");
      await loadOrders();
      if (selected?.id === id) await openDetail(id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "İşlem başarısız.");
    }
  }

  async function postReceipt(id: string) {
    setError("");
    try {
      await api(`goods-receipts/${id}/post`, { method: "POST" });
      setMessage("Mal kabul fişi stoklara işlendi.");
      if (selected) await openDetail(selected.id);
      await loadOrders();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Mal kabul fişi işlenemedi.");
    }
  }

  function openPdf(id: string, preview: boolean) {
    window.open(`/api/backend/procurement-documents/purchase-orders/${id}/${preview ? "preview" : "pdf"}`, "_blank", "noopener,noreferrer");
  }

  return (
    <ErpShell title="Satın Alma Siparişleri" description="Sipariş onayı, PDF dokümanı ve teslimat takibi">
      {message && <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800">{message}</div>}
      {error && <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        {[["Toplam Sipariş", stats.total], ["Onay Bekleyen", stats.pending], ["Onaylanan", stats.approved], ["Kısmi Teslim", stats.partial], ["TRY Sipariş Hacmi", money(stats.volume)]].map(([label, value]) => (
          <div key={String(label)} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><span className="text-sm text-slate-500">{label}</span><strong className="mt-2 block text-2xl text-slate-900">{value}</strong></div>
        ))}
      </div>

      <div className="mt-6 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
          <select value={companyId} onChange={(e) => { setCompanyId(e.target.value); setProjectId(""); }} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
            <option value="">Tüm şirketler</option>{companies.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
          </select>
          <select value={projectId} onChange={(e) => setProjectId(e.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
            <option value="">Tüm projeler</option>{filteredProjects.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
          </select>
          <select value={status} onChange={(e) => setStatus(e.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
            <option value="">Tüm durumlar</option>{statusNames.map((x, i) => <option key={x} value={i}>{x}</option>)}
          </select>
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Sipariş, tedarikçi veya proje ara" className="rounded-lg border border-slate-300 px-3 py-2 text-sm xl:col-span-2" />
        </div>
      </div>

      <div className="mt-6 overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
        <table className="min-w-full text-left text-sm">
          <thead className="bg-slate-50 text-slate-600"><tr><th className="p-3">Sipariş No</th><th className="p-3">Tedarikçi</th><th className="p-3">Proje</th><th className="p-3">Tarih</th><th className="p-3">Teslim</th><th className="p-3 text-right">Net Tutar</th><th className="p-3">Durum</th><th className="p-3">İşlem</th></tr></thead>
          <tbody>{visibleOrders.map((x) => <tr key={x.id} className="border-t border-slate-100"><td className="p-3 font-semibold">{x.orderNumber}</td><td className="p-3">{x.supplierTitle}</td><td className="p-3">{projects.find((p) => p.id === x.projectId)?.name ?? "-"}</td><td className="p-3">{date(x.orderDateUtc)}</td><td className="p-3">{date(x.deliveryDateUtc)}</td><td className="p-3 text-right">{money(x.netAmount, x.currencyCode)}</td><td className="p-3"><span className="rounded-full bg-slate-100 px-2 py-1 text-xs font-semibold">{statusNames[x.status] ?? "Bilinmiyor"}</span></td><td className="p-3"><button onClick={() => openDetail(x.id)} className="rounded-md border border-slate-300 px-3 py-1.5 font-semibold">Detay</button></td></tr>)}</tbody>
        </table>
        {!loading && visibleOrders.length === 0 && <div className="p-8 text-center text-sm text-slate-500">Sipariş bulunamadı.</div>}
        {loading && <div className="p-8 text-center text-sm text-slate-500">Siparişler yükleniyor…</div>}
      </div>

      {selected && <div className="fixed inset-0 z-50 flex items-start justify-end bg-slate-950/40" onClick={() => setSelected(null)}>
        <div className="h-full w-full max-w-3xl overflow-y-auto bg-white p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
          <div className="flex items-start justify-between gap-4"><div><p className="m-0 text-xs font-bold uppercase tracking-wider text-slate-500">Satın Alma Siparişi</p><h2 className="mt-1 text-2xl">{selected.orderNumber}</h2><p className="text-sm text-slate-500">{selected.supplierTitle} · {statusNames[selected.status]}</p></div><button onClick={() => setSelected(null)} className="text-2xl">×</button></div>
          <div className="mt-5 flex flex-wrap gap-2">
            {selected.status === 0 && <button onClick={() => action(selected.id, "submit")} className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-bold text-white">Onaya Gönder</button>}
            {selected.status === 1 && <button onClick={() => action(selected.id, "approve")} className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-bold text-white">Onayla</button>}
            {![4, 5].includes(selected.status) && <button onClick={() => action(selected.id, "cancel")} className="rounded-lg border border-red-300 px-4 py-2 text-sm font-bold text-red-700">İptal Et</button>}
            <button onClick={() => openPdf(selected.id, true)} className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-bold">PDF Önizle</button>
            <button onClick={() => openPdf(selected.id, false)} className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-bold">PDF İndir</button>
          </div>

          <div className="mt-6 grid gap-3 sm:grid-cols-4"><div><span className="text-xs text-slate-500">Sipariş Tarihi</span><strong className="block">{date(selected.orderDateUtc)}</strong></div><div><span className="text-xs text-slate-500">Teslim Tarihi</span><strong className="block">{date(selected.deliveryDateUtc)}</strong></div><div><span className="text-xs text-slate-500">Para Birimi</span><strong className="block">{selected.currencyCode}</strong></div><div><span className="text-xs text-slate-500">KDV</span><strong className="block">%{selected.vatRate}</strong></div></div>

          <h3 className="mt-8">Sipariş Kalemleri</h3>
          <div className="overflow-x-auto rounded-lg border border-slate-200"><table className="min-w-full text-sm"><thead className="bg-slate-50"><tr><th className="p-3 text-left">Malzeme</th><th className="p-3 text-right">Miktar</th><th className="p-3 text-right">Teslim Alınan</th><th className="p-3 text-right">Birim Fiyat</th><th className="p-3 text-right">Net</th></tr></thead><tbody>{selected.items.map((x) => <tr key={x.id} className="border-t"><td className="p-3">{x.material ? `${x.material.code} — ${x.material.name}` : x.description ?? "-"}</td><td className="p-3 text-right">{x.quantity.toLocaleString("tr-TR")} {x.unit}</td><td className="p-3 text-right">{x.receivedQuantity.toLocaleString("tr-TR")}</td><td className="p-3 text-right">{money(x.unitPrice, selected.currencyCode)}</td><td className="p-3 text-right">{money(x.quantity * x.unitPrice * (1 - x.discountRate / 100), selected.currencyCode)}</td></tr>)}</tbody></table></div>

          <h3 className="mt-8">Mal Kabul Fişleri</h3>
          {receipts.length === 0 ? <div className="rounded-lg border border-dashed border-slate-300 p-5 text-sm text-slate-500">Bu sipariş için henüz mal kabul fişi bulunmuyor.</div> : <div className="space-y-3">{receipts.map((x) => <div key={x.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-slate-200 p-4"><div><strong>{x.receiptNumber}</strong><p className="m-0 mt-1 text-sm text-slate-500">{date(x.receiptDateUtc)} · {receiptStatusNames[x.status] ?? x.status} · {x.items.length} kalem</p></div>{x.status === 0 && <button onClick={() => postReceipt(x.id)} className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-bold text-white">Stoklara İşle</button>}</div>)}</div>}
        </div>
      </div>}
    </ErpShell>
  );
}
