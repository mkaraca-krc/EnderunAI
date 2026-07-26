"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company = { id: string; code: string; name: string };
type Project = { id: string; companyId: string; code: string; name: string };
type BudgetItem = { id?: string; code: string; name: string; category?: string | null; plannedAmount: number; currencyCode: string; sequenceNo: number };
type Revision = { id: string; revisionNumber: number; previousAmount: number; revisedAmount: number; reason: string; createdAtUtc: string; createdByName?: string | null };
type Budget = {
  id: string; companyId: string; projectId: string; budgetNumber: string; name: string; currencyCode: string;
  baseAmount: number; warningThresholdPercent: number; criticalThresholdPercent: number; effectiveDateUtc: string;
  description?: string | null; status: number | string; items: BudgetItem[]; revisions?: Revision[];
};
type BudgetSummary = {
  budgetId?: string; projectId?: string; budgetAmount?: number; baseAmount?: number; committedAmount?: number; committed?: number;
  actualAmount?: number; actual?: number; remainingAmount?: number; remaining?: number; usageRate?: number; usagePercent?: number;
  alertLevel?: number | string; status?: string; isOverBudget?: boolean; warnings?: string[];
};
type Alert = { id: string; projectId: string; projectBudgetId?: string; level: number | string; message?: string; description?: string; isResolved: boolean; createdAtUtc: string };
type DraftItem = { code: string; name: string; category: string; plannedAmount: string; currencyCode: string };

const money = (value: number, currency = "TRY") => new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 2 }).format(value || 0);
const number = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 2 });
const statusNames: Record<string, string> = { "0": "Taslak", "1": "Aktif", "2": "Kapalı", "3": "İptal", Draft: "Taslak", Active: "Aktif", Closed: "Kapalı", Cancelled: "İptal" };
const statusName = (value: number | string) => statusNames[String(value)] ?? String(value);
const isDraft = (value: number | string) => String(value) === "0" || String(value) === "Draft";

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) } });
  if (response.status === 401) { window.location.href = "/login"; throw new Error("Oturum süresi doldu."); }
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(typeof body === "string" ? body : body?.message ?? `İstek başarısız (${response.status}).`);
  return body as T;
}

function Card({ label, value, detail, danger = false }: { label: string; value: string; detail?: string; danger?: boolean }) {
  return <article className={`rounded-xl border p-5 shadow-sm ${danger ? "border-red-200 bg-red-50" : "border-slate-200 bg-white"}`}><span className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</span><strong className="mt-3 block text-2xl text-slate-900">{value}</strong>{detail && <small className="mt-2 block text-slate-500">{detail}</small>}</article>;
}

export default function ProjectBudgetsPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [budgets, setBudgets] = useState<Budget[]>([]);
  const [alerts, setAlerts] = useState<Alert[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [selected, setSelected] = useState<Budget | null>(null);
  const [summary, setSummary] = useState<BudgetSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [revisionAmount, setRevisionAmount] = useState("");
  const [revisionReason, setRevisionReason] = useState("");
  const [form, setForm] = useState({ budgetNumber: `BUT-${new Date().getFullYear()}-${String(Date.now()).slice(-5)}`, name: "", currencyCode: "TRY", baseAmount: "", warningThresholdPercent: "70", criticalThresholdPercent: "90", effectiveDate: new Date().toISOString().slice(0, 10), description: "" });
  const [items, setItems] = useState<DraftItem[]>([{ code: "GENEL", name: "Genel Satın Alma Bütçesi", category: "Genel", plannedAmount: "", currencyCode: "TRY" }]);

  const loadCompanies = useCallback(async () => {
    const rows = await api<Company[]>("companies"); setCompanies(rows); setCompanyId((x) => x || rows[0]?.id || "");
  }, []);
  const loadCompanyData = useCallback(async (cid: string) => {
    if (!cid) return; setLoading(true); setError("");
    try {
      const [projectRows, budgetRows, alertRows] = await Promise.all([
        api<Project[]>(`projects?companyId=${cid}`), api<Budget[]>(`project-budgets?companyId=${cid}`), api<Alert[]>("project-budgets/alerts?unresolvedOnly=true")
      ]);
      setProjects(projectRows); setBudgets(budgetRows); setAlerts(alertRows.filter((x) => projectRows.some((p) => p.id === x.projectId)));
      setProjectId((current) => projectRows.some((p) => p.id === current) ? current : projectRows[0]?.id || "");
      setSelectedId((current) => budgetRows.some((b) => b.id === current) ? current : budgetRows[0]?.id || "");
    } catch (err) { setError(err instanceof Error ? err.message : "Bütçe verileri alınamadı."); } finally { setLoading(false); }
  }, []);
  const loadDetail = useCallback(async (id: string) => {
    if (!id) { setSelected(null); return; }
    try { const detail = await api<Budget>(`project-budgets/${id}`); setSelected(detail); setRevisionAmount(String(detail.baseAmount)); } catch (err) { setError(err instanceof Error ? err.message : "Bütçe detayı alınamadı."); }
  }, []);
  const loadSummary = useCallback(async (pid: string) => {
    if (!pid) { setSummary(null); return; }
    try { setSummary(await api<BudgetSummary>(`project-budgets/projects/${pid}/summary`)); } catch { setSummary(null); }
  }, []);

  useEffect(() => { loadCompanies().catch((e) => { setError(e instanceof Error ? e.message : "Şirketler alınamadı."); setLoading(false); }); }, [loadCompanies]);
  useEffect(() => { if (companyId) loadCompanyData(companyId); }, [companyId, loadCompanyData]);
  useEffect(() => { loadDetail(selectedId); }, [selectedId, loadDetail]);
  useEffect(() => { loadSummary(projectId); }, [projectId, loadSummary]);

  const projectMap = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects]);
  const activeBudgets = budgets.filter((x) => statusName(x.status) === "Aktif");
  const totalBudget = activeBudgets.reduce((sum, x) => sum + x.baseAmount, 0);
  const summaryBudget = summary?.budgetAmount ?? summary?.baseAmount ?? selected?.baseAmount ?? 0;
  const committed = summary?.committedAmount ?? summary?.committed ?? 0;
  const actual = summary?.actualAmount ?? summary?.actual ?? 0;
  const remaining = summary?.remainingAmount ?? summary?.remaining ?? summaryBudget - committed - actual;
  const usage = summary?.usageRate ?? summary?.usagePercent ?? (summaryBudget > 0 ? (committed + actual) / summaryBudget * 100 : 0);

  const refresh = async () => { await loadCompanyData(companyId); if (selectedId) await loadDetail(selectedId); if (projectId) await loadSummary(projectId); };
  const addItem = () => setItems((rows) => [...rows, { code: "", name: "", category: "", plannedAmount: "", currencyCode: form.currencyCode }]);
  const updateItem = (index: number, key: keyof DraftItem, value: string) => setItems((rows) => rows.map((row, i) => i === index ? { ...row, [key]: value } : row));
  const removeItem = (index: number) => setItems((rows) => rows.filter((_, i) => i !== index));

  const createBudget = async () => {
    if (!companyId || !projectId || !form.name.trim() || Number(form.baseAmount) <= 0) { setError("Şirket, proje, bütçe adı ve geçerli bütçe tutarı zorunludur."); return; }
    setBusy(true); setError(""); setNotice("");
    try {
      const created = await api<Budget>("project-budgets", { method: "POST", body: JSON.stringify({ companyId, projectId, budgetNumber: form.budgetNumber, name: form.name, currencyCode: form.currencyCode, baseAmount: Number(form.baseAmount), warningThresholdPercent: Number(form.warningThresholdPercent), criticalThresholdPercent: Number(form.criticalThresholdPercent), effectiveDateUtc: form.effectiveDate ? new Date(`${form.effectiveDate}T00:00:00Z`).toISOString() : null, description: form.description || null, items: items.filter((x) => x.code.trim() && x.name.trim()).map((x, index) => ({ code: x.code, name: x.name, materialId: null, category: x.category || null, plannedAmount: Number(x.plannedAmount || 0), currencyCode: x.currencyCode, sequenceNo: index + 1 })) }) });
      setNotice("Proje bütçesi taslak olarak oluşturuldu."); setShowCreate(false); setSelectedId(created.id); await refresh();
    } catch (err) { setError(err instanceof Error ? err.message : "Bütçe oluşturulamadı."); } finally { setBusy(false); }
  };
  const activate = async () => { if (!selected) return; setBusy(true); setError(""); try { await api(`project-budgets/${selected.id}/activate`, { method: "POST" }); setNotice("Bütçe aktifleştirildi. Projedeki önceki aktif bütçe kapatıldı."); await refresh(); } catch (err) { setError(err instanceof Error ? err.message : "Bütçe aktifleştirilemedi."); } finally { setBusy(false); } };
  const revise = async () => { if (!selected || Number(revisionAmount) <= 0 || !revisionReason.trim()) { setError("Revize tutarı ve gerekçe zorunludur."); return; } setBusy(true); setError(""); try { await api(`project-budgets/${selected.id}/revisions`, { method: "POST", body: JSON.stringify({ revisedAmount: Number(revisionAmount), reason: revisionReason }) }); setRevisionReason(""); setNotice("Bütçe revizyonu kaydedildi."); await refresh(); } catch (err) { setError(err instanceof Error ? err.message : "Bütçe revize edilemedi."); } finally { setBusy(false); } };

  const hizir = usage >= 100 ? "Proje bütçesi aşılmış durumda. Yeni siparişleri durdurup bütçe revizyonu ve satın alma taahhütlerini kontrol edin." : usage >= 90 ? "Bütçe kritik eşikte. Açık siparişler ve taahhütler incelenmeden yeni satın alma yapılmamalı." : usage >= 70 ? "Bütçe uyarı seviyesinde. Kalan ihtiyaçları önceliklendirip teklif karşılaştırmalarında tasarruf hedefi uygulayın." : "Bütçe kullanımı sağlıklı seviyede. Taahhüt ve gerçekleşen harcamaları düzenli izlemeye devam edin.";

  return <ErpShell title="Proje Bütçeleri" description="Bütçe, taahhüt, gerçekleşen maliyet, revizyon ve sapma takibi">
    <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
      <div className="flex flex-wrap gap-2"><select className="min-h-10 rounded-lg border border-slate-300 bg-white px-3 text-sm" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>{companies.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select><select className="min-h-10 min-w-64 rounded-lg border border-slate-300 bg-white px-3 text-sm" value={projectId} onChange={(e) => setProjectId(e.target.value)}>{projects.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></div>
      <button className="erp-primary-button" onClick={() => setShowCreate(true)}>Yeni Bütçe</button>
    </div>
    {error && <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>}
    {notice && <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">{notice}</div>}
    {loading ? <div className="rounded-xl border border-slate-200 bg-white p-10 text-center text-slate-500">Bütçe verileri yükleniyor…</div> : <>
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4"><Card label="Aktif Bütçe Toplamı" value={money(totalBudget)} detail={`${activeBudgets.length} aktif proje bütçesi`} /><Card label="Seçili Proje Bütçesi" value={money(summaryBudget)} detail={projectMap.get(projectId)?.name ?? "Proje seçilmedi"} /><Card label="Kalan Bütçe" value={money(remaining)} detail={`Kullanım %${number.format(usage)}`} danger={remaining < 0} /><Card label="Kritik Uyarı" value={String(alerts.length)} detail="Çözülmemiş bütçe alarmı" danger={alerts.length > 0} /></section>
      <section className="mt-6 grid gap-6 xl:grid-cols-[1.35fr_1fr]">
        <article className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="flex items-center justify-between border-b border-slate-200 p-5"><div><h2 className="m-0 text-lg">Bütçe Listesi</h2><p className="mt-1 text-sm text-slate-500">Şirket kapsamındaki proje bütçeleri</p></div><span className="text-sm text-slate-500">{budgets.length} kayıt</span></div><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr><th className="p-4">Bütçe</th><th className="p-4">Proje</th><th className="p-4">Durum</th><th className="p-4 text-right">Tutar</th></tr></thead><tbody>{budgets.map((x) => <tr key={x.id} onClick={() => { setSelectedId(x.id); setProjectId(x.projectId); }} className={`cursor-pointer border-t border-slate-100 hover:bg-slate-50 ${selectedId === x.id ? "bg-slate-50" : ""}`}><td className="p-4"><strong>{x.budgetNumber}</strong><small className="mt-1 block text-slate-500">{x.name}</small></td><td className="p-4">{projectMap.get(x.projectId)?.name ?? "—"}</td><td className="p-4"><span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-semibold">{statusName(x.status)}</span></td><td className="p-4 text-right font-semibold">{money(x.baseAmount, x.currencyCode)}</td></tr>)}</tbody></table>{budgets.length === 0 && <div className="p-8 text-center text-sm text-slate-500">Henüz bütçe tanımı bulunmuyor.</div>}</div></article>
        <article className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><h2 className="m-0 text-lg">Proje Bütçe Kullanımı</h2><div className="mt-5 h-3 overflow-hidden rounded-full bg-slate-100"><div className={`h-full ${usage >= 90 ? "bg-red-500" : usage >= 70 ? "bg-amber-500" : "bg-emerald-500"}`} style={{ width: `${Math.min(Math.max(usage, 0), 100)}%` }} /></div><div className="mt-2 flex justify-between text-sm"><strong>%{number.format(usage)}</strong><span className={usage >= 90 ? "text-red-600" : usage >= 70 ? "text-amber-600" : "text-emerald-600"}>{usage >= 90 ? "Kritik" : usage >= 70 ? "Uyarı" : "Sağlıklı"}</span></div><div className="mt-5 space-y-3 text-sm"><div className="flex justify-between"><span className="text-slate-500">Taahhüt</span><strong>{money(committed)}</strong></div><div className="flex justify-between"><span className="text-slate-500">Gerçekleşen</span><strong>{money(actual)}</strong></div><div className="flex justify-between border-t border-slate-200 pt-3"><span className="text-slate-500">Kalan</span><strong className={remaining < 0 ? "text-red-600" : ""}>{money(remaining)}</strong></div></div><div className="mt-5 rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-900"><strong>Hızır Yönetici Özeti</strong><p className="mb-0 mt-2 leading-6">{hizir}</p></div></article>
      </section>
      {selected && <section className="mt-6 grid gap-6 xl:grid-cols-[1.4fr_1fr]"><article className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 p-5"><div><h2 className="m-0 text-lg">{selected.name}</h2><p className="mt-1 text-sm text-slate-500">{selected.budgetNumber} · {statusName(selected.status)}</p></div>{isDraft(selected.status) && <button disabled={busy} className="erp-primary-button" onClick={activate}>Bütçeyi Aktifleştir</button>}</div><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr><th className="p-4">Kod</th><th className="p-4">Kalem</th><th className="p-4">Kategori</th><th className="p-4 text-right">Planlanan</th></tr></thead><tbody>{(selected.items ?? []).map((x, index) => <tr key={x.id ?? `${x.code}-${index}`} className="border-t border-slate-100"><td className="p-4 font-mono text-xs">{x.code}</td><td className="p-4 font-medium">{x.name}</td><td className="p-4">{x.category ?? "—"}</td><td className="p-4 text-right font-semibold">{money(x.plannedAmount, x.currencyCode)}</td></tr>)}</tbody></table></div></article><article className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><h2 className="m-0 text-lg">Bütçe Revizyonu</h2><label className="mt-4 block text-sm font-medium">Yeni bütçe tutarı<input className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" type="number" min="0" value={revisionAmount} onChange={(e) => setRevisionAmount(e.target.value)} /></label><label className="mt-3 block text-sm font-medium">Revizyon gerekçesi<textarea className="mt-1 min-h-24 w-full rounded-lg border border-slate-300 px-3 py-2" value={revisionReason} onChange={(e) => setRevisionReason(e.target.value)} /></label><button disabled={busy} onClick={revise} className="erp-secondary-button mt-3 w-full">Revizyonu Kaydet</button><div className="mt-5 border-t border-slate-200 pt-4"><strong className="text-sm">Revizyon Geçmişi</strong><div className="mt-3 max-h-48 space-y-2 overflow-auto">{(selected.revisions ?? []).map((x) => <div key={x.id} className="rounded-lg bg-slate-50 p-3 text-xs"><div className="flex justify-between"><strong>Revizyon {x.revisionNumber}</strong><span>{money(x.revisedAmount, selected.currencyCode)}</span></div><p className="mb-0 mt-1 text-slate-500">{x.reason}</p></div>)}{(selected.revisions ?? []).length === 0 && <p className="text-sm text-slate-500">Revizyon kaydı yok.</p>}</div></div></article></section>}
      <article className="mt-6 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 p-5"><h2 className="m-0 text-lg">Kritik Bütçe Uyarıları</h2></div>{alerts.length ? <div className="divide-y divide-slate-100">{alerts.map((x) => <div key={x.id} className="flex items-start justify-between gap-4 p-4"><div><strong className="text-sm">{projectMap.get(x.projectId)?.name ?? "Proje"}</strong><p className="mb-0 mt-1 text-sm text-slate-600">{x.message ?? x.description ?? "Bütçe kullanım eşiği aşıldı."}</p></div><time className="whitespace-nowrap text-xs text-slate-500">{new Date(x.createdAtUtc).toLocaleDateString("tr-TR")}</time></div>)}</div> : <div className="p-8 text-center text-sm text-slate-500">Çözülmemiş bütçe alarmı bulunmuyor.</div>}</article>
    </>}
    {showCreate && <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4"><div className="max-h-[92vh] w-full max-w-5xl overflow-auto rounded-2xl bg-white shadow-2xl"><div className="flex items-center justify-between border-b border-slate-200 p-5"><div><h2 className="m-0 text-xl">Yeni Proje Bütçesi</h2><p className="mt-1 text-sm text-slate-500">Taslak bütçe ve bütçe kalemleri</p></div><button onClick={() => setShowCreate(false)} className="rounded-lg px-3 py-2 text-slate-500 hover:bg-slate-100">Kapat</button></div><div className="grid gap-4 p-5 md:grid-cols-2 xl:grid-cols-4"><label className="text-sm font-medium">Bütçe No<input className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.budgetNumber} onChange={(e) => setForm({ ...form, budgetNumber: e.target.value })} /></label><label className="text-sm font-medium md:col-span-2">Bütçe Adı<input className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label><label className="text-sm font-medium">Para Birimi<select className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.currencyCode} onChange={(e) => setForm({ ...form, currencyCode: e.target.value })}><option>TRY</option><option>USD</option><option>EUR</option><option>GBP</option></select></label><label className="text-sm font-medium">Bütçe Tutarı<input type="number" min="0" className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.baseAmount} onChange={(e) => setForm({ ...form, baseAmount: e.target.value })} /></label><label className="text-sm font-medium">Uyarı Eşiği %<input type="number" className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.warningThresholdPercent} onChange={(e) => setForm({ ...form, warningThresholdPercent: e.target.value })} /></label><label className="text-sm font-medium">Kritik Eşik %<input type="number" className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.criticalThresholdPercent} onChange={(e) => setForm({ ...form, criticalThresholdPercent: e.target.value })} /></label><label className="text-sm font-medium">Geçerlilik Tarihi<input type="date" className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.effectiveDate} onChange={(e) => setForm({ ...form, effectiveDate: e.target.value })} /></label><label className="text-sm font-medium md:col-span-2 xl:col-span-4">Açıklama<textarea className="mt-1 min-h-20 w-full rounded-lg border border-slate-300 px-3 py-2" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label></div><div className="border-t border-slate-200 p-5"><div className="flex items-center justify-between"><h3 className="m-0 text-lg">Bütçe Kalemleri</h3><button className="erp-secondary-button" onClick={addItem}>Kalem Ekle</button></div><div className="mt-4 space-y-3">{items.map((x, index) => <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-[1fr_2fr_1.5fr_1fr_1fr_auto]"><input placeholder="Kod" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={x.code} onChange={(e) => updateItem(index, "code", e.target.value)} /><input placeholder="Kalem adı" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={x.name} onChange={(e) => updateItem(index, "name", e.target.value)} /><input placeholder="Kategori" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={x.category} onChange={(e) => updateItem(index, "category", e.target.value)} /><input placeholder="Tutar" type="number" min="0" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={x.plannedAmount} onChange={(e) => updateItem(index, "plannedAmount", e.target.value)} /><select className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={x.currencyCode} onChange={(e) => updateItem(index, "currencyCode", e.target.value)}><option>TRY</option><option>USD</option><option>EUR</option><option>GBP</option></select><button className="rounded-lg px-3 py-2 text-sm text-red-600 hover:bg-red-50" onClick={() => removeItem(index)}>Sil</button></div>)}</div></div><div className="flex justify-end gap-2 border-t border-slate-200 p-5"><button className="erp-secondary-button" onClick={() => setShowCreate(false)}>Vazgeç</button><button disabled={busy} className="erp-primary-button" onClick={createBudget}>{busy ? "Kaydediliyor…" : "Taslak Bütçeyi Kaydet"}</button></div></div></div>}
  </ErpShell>;
}
