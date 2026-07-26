"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company = { id: string; code: string; name: string };
type Project = { id: string; companyId: string; code: string; name: string };
type Material = { id: string; companyId: string; code: string; name: string; unit: string; brand?: string };
type PurchaseRequest = {
  id: string; companyId: string; projectId: string; requestNumber: string; requestDateUtc: string;
  requiredDateUtc?: string; status: number; description?: string; itemCount: number; totalQuantity: number;
};
type RequestItemForm = { materialId: string; quantity: string; unit: string; description: string };

const statusInfo: Record<number, { label: string; className: string }> = {
  0: { label: "Taslak", className: "bg-slate-100 text-slate-700" },
  1: { label: "Onay Bekliyor", className: "bg-amber-100 text-amber-800" },
  2: { label: "Onaylandı", className: "bg-emerald-100 text-emerald-800" },
  3: { label: "Reddedildi", className: "bg-rose-100 text-rose-800" },
  4: { label: "İptal", className: "bg-slate-200 text-slate-600" },
};
const emptyItem = (): RequestItemForm => ({ materialId: "", quantity: "1", unit: "Adet", description: "" });

async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store", ...options });
  if (response.status === 401) { location.href = "/login"; throw new Error("Oturum süresi doldu."); }
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message ?? (typeof payload === "string" ? payload : `İşlem başarısız (${response.status})`));
  return payload as T;
}

export default function PurchaseRequestsPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [materials, setMaterials] = useState<Material[]>([]);
  const [requests, setRequests] = useState<PurchaseRequest[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [form, setForm] = useState({ projectId: "", requestNumber: "", requiredDateUtc: "", description: "" });
  const [items, setItems] = useState<RequestItemForm[]>([emptyItem()]);

  const loadCompanies = useCallback(async () => {
    const data = await api<Company[]>("companies");
    setCompanies(data);
    setCompanyId((current) => current || data[0]?.id || "");
  }, []);

  const loadCompanyData = useCallback(async (selectedCompanyId: string) => {
    if (!selectedCompanyId) return;
    const [projectData, materialData, requestData] = await Promise.all([
      api<Project[]>(`projects?companyId=${selectedCompanyId}`),
      api<Material[]>(`materials?companyId=${selectedCompanyId}`),
      api<PurchaseRequest[]>(`purchase-requests?companyId=${selectedCompanyId}`),
    ]);
    setProjects(projectData);
    setMaterials(materialData);
    setRequests(requestData);
    setForm((current) => ({ ...current, projectId: projectData.some((x) => x.id === current.projectId) ? current.projectId : projectData[0]?.id || "" }));
  }, []);

  useEffect(() => { loadCompanies().catch((e) => setError(e instanceof Error ? e.message : "Şirketler alınamadı.")); }, [loadCompanies]);
  useEffect(() => { loadCompanyData(companyId).catch((e) => setError(e instanceof Error ? e.message : "Veriler alınamadı.")); }, [companyId, loadCompanyData]);

  const filtered = useMemo(() => requests.filter((request) => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    const project = projects.find((x) => x.id === request.projectId);
    const matchesSearch = !term || request.requestNumber.toLocaleLowerCase("tr-TR").includes(term) || request.description?.toLocaleLowerCase("tr-TR").includes(term) || project?.name.toLocaleLowerCase("tr-TR").includes(term);
    return matchesSearch && (statusFilter === "all" || request.status === Number(statusFilter));
  }), [projects, requests, search, statusFilter]);

  const counters = useMemo(() => ({
    total: requests.length,
    draft: requests.filter((x) => x.status === 0).length,
    pending: requests.filter((x) => x.status === 1).length,
    approved: requests.filter((x) => x.status === 2).length,
  }), [requests]);

  function resetForm() {
    setForm({ projectId: projects[0]?.id || "", requestNumber: `ST-${new Date().getFullYear()}-${Date.now().toString().slice(-6)}`, requiredDateUtc: "", description: "" });
    setItems([emptyItem()]);
  }

  function openCreate() { resetForm(); setShowForm(true); setError(""); setMessage(""); }
  function updateItem(index: number, patch: Partial<RequestItemForm>) { setItems((current) => current.map((item, i) => i === index ? { ...item, ...patch } : item)); }
  function chooseMaterial(index: number, materialId: string) {
    const material = materials.find((x) => x.id === materialId);
    updateItem(index, { materialId, unit: material?.unit || "Adet" });
  }

  async function save(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError(""); setMessage("");
    try {
      if (!companyId || !form.projectId) throw new Error("Şirket ve proje seçimi zorunludur.");
      if (items.some((x) => !x.materialId || Number(x.quantity) <= 0)) throw new Error("Tüm kalemlerde malzeme ve geçerli miktar girilmelidir.");
      await api("purchase-requests", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          companyId, projectId: form.projectId, requestNumber: form.requestNumber,
          requiredDateUtc: form.requiredDateUtc ? new Date(`${form.requiredDateUtc}T12:00:00`).toISOString() : null,
          description: form.description || null, requestedByUserId: null,
          items: items.map((x) => ({ materialId: x.materialId, quantity: Number(x.quantity), unit: x.unit, description: x.description || null })),
        }),
      });
      setMessage("Satın alma talebi taslak olarak oluşturuldu."); setShowForm(false); await loadCompanyData(companyId);
    } catch (e) { setError(e instanceof Error ? e.message : "Talep kaydedilemedi."); }
    finally { setBusy(false); }
  }

  async function action(id: string, operation: "submit" | "approve" | "reject") {
    setBusy(true); setError(""); setMessage("");
    try {
      let options: RequestInit = { method: "POST" };
      if (operation === "reject") {
        const reason = window.prompt("Red nedenini yazınız:");
        if (!reason) return;
        options = { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ reason }) };
      }
      await api(`purchase-requests/${id}/${operation}`, options);
      setMessage(operation === "submit" ? "Talep onaya gönderildi." : operation === "approve" ? "Talep onaylandı." : "Talep reddedildi.");
      await loadCompanyData(companyId);
    } catch (e) { setError(e instanceof Error ? e.message : "İşlem tamamlanamadı."); }
    finally { setBusy(false); }
  }

  return <ErpShell title="Satın Alma Talepleri" description="Talep oluşturma, onaya gönderme ve takip ekranı">
    <div className="grid gap-4 md:grid-cols-4">
      {[['Toplam Talep', counters.total], ['Taslak', counters.draft], ['Onay Bekliyor', counters.pending], ['Onaylandı', counters.approved]].map(([label, value]) =>
        <div key={String(label)} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><div className="text-sm text-slate-500">{label}</div><strong className="mt-2 block text-3xl text-slate-900">{value}</strong></div>)}
    </div>

    <div className="mt-6 flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <select className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>{companies.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select>
      <input className="min-w-64 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm" placeholder="Talep no, proje veya açıklama ara" value={search} onChange={(e) => setSearch(e.target.value)} />
      <select className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}><option value="all">Tüm Durumlar</option><option value="0">Taslak</option><option value="1">Onay Bekliyor</option><option value="2">Onaylandı</option><option value="3">Reddedildi</option></select>
      <button className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-semibold text-white" onClick={openCreate}>+ Yeni Talep</button>
    </div>

    {message && <div className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800">{message}</div>}
    {error && <div className="mt-4 rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800">{error}</div>}

    {showForm && <form onSubmit={save} className="mt-6 rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
      <div className="flex items-center justify-between"><div><h2 className="m-0 text-lg">Yeni Satın Alma Talebi</h2><p className="mt-1 text-sm text-slate-500">Talep taslak olarak kaydedilir.</p></div><button type="button" className="text-sm text-slate-500" onClick={() => setShowForm(false)}>Kapat</button></div>
      <div className="mt-5 grid gap-4 md:grid-cols-2">
        <label className="text-sm"><span className="mb-1 block font-medium">Talep No *</span><input required className="w-full rounded-lg border border-slate-300 px-3 py-2" value={form.requestNumber} onChange={(e) => setForm({ ...form, requestNumber: e.target.value.toUpperCase() })} /></label>
        <label className="text-sm"><span className="mb-1 block font-medium">Proje *</span><select required className="w-full rounded-lg border border-slate-300 px-3 py-2" value={form.projectId} onChange={(e) => setForm({ ...form, projectId: e.target.value })}>{projects.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
        <label className="text-sm"><span className="mb-1 block font-medium">İstenen Tarih</span><input type="date" className="w-full rounded-lg border border-slate-300 px-3 py-2" value={form.requiredDateUtc} onChange={(e) => setForm({ ...form, requiredDateUtc: e.target.value })} /></label>
        <label className="text-sm"><span className="mb-1 block font-medium">Açıklama</span><input className="w-full rounded-lg border border-slate-300 px-3 py-2" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
      </div>
      <div className="mt-6 overflow-x-auto"><table className="w-full min-w-[820px] border-collapse text-sm"><thead><tr className="border-b border-slate-200 text-left text-slate-500"><th className="py-3">Malzeme</th><th>Miktar</th><th>Birim</th><th>Açıklama</th><th></th></tr></thead><tbody>{items.map((item, index) => <tr key={index} className="border-b border-slate-100"><td className="py-3 pr-3"><select required className="w-full rounded-lg border border-slate-300 px-3 py-2" value={item.materialId} onChange={(e) => chooseMaterial(index, e.target.value)}><option value="">Malzeme seçin</option>{materials.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></td><td className="pr-3"><input required type="number" min="0.0001" step="0.0001" className="w-28 rounded-lg border border-slate-300 px-3 py-2" value={item.quantity} onChange={(e) => updateItem(index, { quantity: e.target.value })} /></td><td className="pr-3"><input required className="w-24 rounded-lg border border-slate-300 px-3 py-2" value={item.unit} onChange={(e) => updateItem(index, { unit: e.target.value })} /></td><td className="pr-3"><input className="w-full rounded-lg border border-slate-300 px-3 py-2" value={item.description} onChange={(e) => updateItem(index, { description: e.target.value })} /></td><td><button type="button" className="text-rose-600 disabled:opacity-30" disabled={items.length === 1} onClick={() => setItems((current) => current.filter((_, i) => i !== index))}>Sil</button></td></tr>)}</tbody></table></div>
      <div className="mt-4 flex justify-between"><button type="button" className="rounded-lg border border-slate-300 px-4 py-2 text-sm" onClick={() => setItems((current) => [...current, emptyItem()])}>+ Kalem Ekle</button><button disabled={busy} className="rounded-lg bg-slate-900 px-5 py-2 text-sm font-semibold text-white disabled:opacity-50">{busy ? "Kaydediliyor..." : "Taslak Kaydet"}</button></div>
    </form>}

    <div className="mt-6 overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm"><table className="w-full min-w-[900px] border-collapse text-sm"><thead><tr className="border-b border-slate-200 bg-slate-50 text-left text-slate-500"><th className="px-4 py-3">Talep No</th><th>Proje</th><th>Talep Tarihi</th><th>İstenen Tarih</th><th>Kalem</th><th>Miktar</th><th>Durum</th><th>İşlem</th></tr></thead><tbody>{filtered.map((request) => { const status = statusInfo[request.status] ?? { label: `Durum ${request.status}`, className: "bg-slate-100 text-slate-700" }; const project = projects.find((x) => x.id === request.projectId); return <tr key={request.id} className="border-b border-slate-100 last:border-0"><td className="px-4 py-4 font-semibold text-slate-900">{request.requestNumber}<div className="mt-1 max-w-52 truncate text-xs font-normal text-slate-500">{request.description || "Açıklama yok"}</div></td><td>{project ? `${project.code} — ${project.name}` : "-"}</td><td>{new Date(request.requestDateUtc).toLocaleDateString("tr-TR")}</td><td>{request.requiredDateUtc ? new Date(request.requiredDateUtc).toLocaleDateString("tr-TR") : "-"}</td><td>{request.itemCount}</td><td>{request.totalQuantity.toLocaleString("tr-TR")}</td><td><span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${status.className}`}>{status.label}</span></td><td><div className="flex gap-2">{request.status === 0 && <button disabled={busy} className="rounded-md border border-slate-300 px-3 py-1.5" onClick={() => action(request.id, "submit")}>Onaya Gönder</button>}{request.status === 1 && <><button disabled={busy} className="rounded-md bg-emerald-600 px-3 py-1.5 text-white" onClick={() => action(request.id, "approve")}>Onayla</button><button disabled={busy} className="rounded-md border border-rose-300 px-3 py-1.5 text-rose-700" onClick={() => action(request.id, "reject")}>Reddet</button></>}</div></td></tr>; })}{filtered.length === 0 && <tr><td colSpan={8} className="p-10 text-center text-slate-500">Filtreye uygun satın alma talebi bulunamadı.</td></tr>}</tbody></table></div>
  </ErpShell>;
}
