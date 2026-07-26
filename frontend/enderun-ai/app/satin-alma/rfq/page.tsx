"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company = { id: string; code: string; name: string };
type Project = { id: string; companyId: string; code: string; name: string };
type PurchaseRequest = {
  id: string;
  companyId: string;
  projectId: string;
  requestNumber: string;
  status: number;
  itemCount: number;
};
type PurchaseRequestDetail = {
  id: string;
  companyId: string;
  projectId: string;
  requestNumber: string;
  description?: string;
  requiredDateUtc?: string;
  items: Array<{
    materialId: string;
    quantity: number;
    unit: string;
    description?: string;
  }>;
};
type RfqRow = {
  id: string;
  rfqNumber: string;
  companyId: string;
  projectId: string;
  purchaseRequestId?: string;
  rfqDateUtc: string;
  offerDeadlineUtc?: string;
  status: number;
  currencyCode: string;
  itemCount: number;
  offerCount: number;
};
type RfqDetail = RfqRow & {
  description?: string;
  items: Array<{
    id: string;
    materialId: string;
    quantity: number;
    unit: string;
    description?: string;
    requiredDateUtc?: string;
  }>;
  offers: Array<{
    id: string;
    offerNumber: string;
    supplierCurrentAccountId: string;
    offerDateUtc: string;
    currencyCode: string;
    exchangeRate: number;
    discountRate: number;
    freightAmount: number;
    paymentTermDays: number;
    deliveryTermDays: number;
    supplierPerformanceScore: number;
    notes?: string;
  }>;
};
type Supplier = {
  id: string;
  title: string;
  code: string;
  email?: string;
  authorizedPerson?: string;
  roles: number;
  status: number;
};
type Invitation = {
  id: string;
  supplierCurrentAccountId: string;
  recipientEmail: string;
  recipientName: string;
  status: number;
  sentAtUtc?: string;
  openedAtUtc?: string;
  expiresAtUtc: string;
  reminderCount: number;
  lastError?: string;
};
type Evaluation = {
  offerId: string;
  offerNumber: string;
  totalCost: number;
  priceScore: number;
  paymentTermScore: number;
  stockScore: number;
  deliveryScore: number;
  freightScore: number;
  checkTermScore: number;
  supplierScore: number;
  totalScore: number;
  warnings: string[];
};

type RfqForm = {
  companyId: string;
  projectId: string;
  purchaseRequestId: string;
  rfqNumber: string;
  offerDeadlineLocal: string;
  currencyCode: string;
  description: string;
};

const statusLabels = ["Taslak", "Yayınlandı", "Teklif Toplanıyor", "Değerlendiriliyor", "Sonuçlandı", "İptal"];
const statusClasses = ["gray", "blue", "yellow", "purple", "green", "red"];
const invitationLabels = ["Bekliyor", "Gönderildi", "Açıldı", "Teklif Verildi", "Süresi Doldu", "Başarısız", "İptal"];

const initialForm: RfqForm = {
  companyId: "",
  projectId: "",
  purchaseRequestId: "",
  rfqNumber: "",
  offerDeadlineLocal: "",
  currencyCode: "TRY",
  description: "",
};

async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store", ...options });
  if (response.status === 401) {
    window.location.href = "/login";
    throw new Error("Oturum süresi doldu.");
  }
  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("application/json")
    ? await response.json().catch(() => null)
    : await response.text().catch(() => "");
  if (!response.ok) {
    const message = typeof body === "string" ? body : body?.message;
    throw new Error(message || `İşlem başarısız (${response.status}).`);
  }
  return body as T;
}

function formatDate(value?: string) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("tr-TR", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
}

function formatMoney(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 2 }).format(value || 0);
}

function defaultDeadline() {
  const date = new Date();
  date.setDate(date.getDate() + 7);
  date.setHours(17, 0, 0, 0);
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 16);
}

export default function Page() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [requests, setRequests] = useState<PurchaseRequest[]>([]);
  const [rfqs, setRfqs] = useState<RfqRow[]>([]);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [selectedRfq, setSelectedRfq] = useState<RfqDetail | null>(null);
  const [invitations, setInvitations] = useState<Invitation[]>([]);
  const [evaluations, setEvaluations] = useState<Evaluation[]>([]);
  const [selectedSupplierIds, setSelectedSupplierIds] = useState<string[]>([]);
  const [form, setForm] = useState<RfqForm>({ ...initialForm, offerDeadlineLocal: defaultDeadline() });
  const [showForm, setShowForm] = useState(false);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const loadBase = useCallback(async () => {
    try {
      const [companyRows, rfqRows] = await Promise.all([
        api<Company[]>("companies"),
        api<RfqRow[]>("rfqs"),
      ]);
      setCompanies(companyRows);
      setRfqs(rfqRows);
      setForm((current) => ({ ...current, companyId: current.companyId || companyRows[0]?.id || "" }));
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Veriler alınamadı.");
    }
  }, []);

  const loadCompanyData = useCallback(async (companyId: string) => {
    if (!companyId) return;
    try {
      const [projectRows, requestRows, accountRows] = await Promise.all([
        api<Project[]>(`projects?companyId=${companyId}`),
        api<PurchaseRequest[]>(`purchase-requests?companyId=${companyId}&status=2`),
        api<Supplier[]>(`current-accounts?companyId=${companyId}&status=2`),
      ]);
      setProjects(projectRows);
      setRequests(requestRows);
      setSuppliers(accountRows.filter((supplier) => (supplier.roles & 2) === 2));
      setForm((current) => ({
        ...current,
        projectId: projectRows.some((project) => project.id === current.projectId) ? current.projectId : projectRows[0]?.id || "",
        purchaseRequestId: requestRows.some((request) => request.id === current.purchaseRequestId) ? current.purchaseRequestId : "",
      }));
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Şirket verileri alınamadı.");
    }
  }, []);

  useEffect(() => { loadBase(); }, [loadBase]);
  useEffect(() => { if (form.companyId) loadCompanyData(form.companyId); }, [form.companyId, loadCompanyData]);

  const projectNames = useMemo(() => new Map(projects.map((project) => [project.id, `${project.code} — ${project.name}`])), [projects]);
  const supplierNames = useMemo(() => new Map(suppliers.map((supplier) => [supplier.id, supplier.title])), [suppliers]);

  const filteredRfqs = useMemo(() => rfqs.filter((rfq) => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const matchesText = !query || rfq.rfqNumber.toLocaleLowerCase("tr-TR").includes(query) || (projectNames.get(rfq.projectId) ?? "").toLocaleLowerCase("tr-TR").includes(query);
    const matchesStatus = statusFilter === "" || String(rfq.status) === statusFilter;
    return matchesText && matchesStatus;
  }), [rfqs, search, statusFilter, projectNames]);

  const counters = useMemo(() => ({
    total: rfqs.length,
    collecting: rfqs.filter((rfq) => rfq.status === 2).length,
    withOffers: rfqs.filter((rfq) => rfq.offerCount > 0).length,
    awarded: rfqs.filter((rfq) => rfq.status === 4).length,
    expiring: rfqs.filter((rfq) => rfq.offerDeadlineUtc && rfq.status < 4 && new Date(rfq.offerDeadlineUtc).getTime() <= Date.now() + 48 * 60 * 60 * 1000).length,
  }), [rfqs]);

  async function selectRequest(requestId: string) {
    setForm((current) => ({ ...current, purchaseRequestId: requestId }));
    if (!requestId) return;
    try {
      const detail = await api<PurchaseRequestDetail>(`purchase-requests/${requestId}`);
      setForm((current) => ({
        ...current,
        companyId: detail.companyId,
        projectId: detail.projectId,
        purchaseRequestId: detail.id,
        description: detail.description || `${detail.requestNumber} numaralı satın alma talebinden oluşturuldu.`,
        rfqNumber: `RFQ-${new Date().toISOString().slice(0, 10).replaceAll("-", "")}-${detail.requestNumber.replace(/[^a-zA-Z0-9]/g, "").slice(-8)}`,
      }));
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Talep detayı alınamadı.");
    }
  }

  async function createRfq(event: FormEvent) {
    event.preventDefault();
    if (!form.purchaseRequestId) {
      setError("Onaylanmış bir satın alma talebi seçmelisiniz.");
      return;
    }
    setBusy(true); setError(""); setMessage("");
    try {
      const request = await api<PurchaseRequestDetail>(`purchase-requests/${form.purchaseRequestId}`);
      const created = await api<RfqDetail>("rfqs", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          companyId: form.companyId,
          projectId: form.projectId,
          purchaseRequestId: form.purchaseRequestId,
          rfqNumber: form.rfqNumber || null,
          offerDeadlineUtc: form.offerDeadlineLocal ? new Date(form.offerDeadlineLocal).toISOString() : null,
          currencyCode: form.currencyCode,
          description: form.description || null,
          items: request.items.map((item) => ({
            materialId: item.materialId,
            quantity: item.quantity,
            unit: item.unit,
            requiredDateUtc: request.requiredDateUtc || null,
            description: item.description || null,
          })),
        }),
      });
      setMessage(`${created.rfqNumber} oluşturuldu.`);
      setShowForm(false);
      setForm((current) => ({ ...initialForm, companyId: current.companyId, projectId: current.projectId, offerDeadlineLocal: defaultDeadline() }));
      await loadBase();
      await openRfq(created.id);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "RFQ oluşturulamadı.");
    } finally { setBusy(false); }
  }

  async function openRfq(id: string) {
    setBusy(true); setError("");
    try {
      const [detail, history, scores] = await Promise.all([
        api<RfqDetail>(`rfqs/${id}`),
        api<Invitation[]>(`rfq-invitations/rfqs/${id}`),
        api<Evaluation[]>(`rfqs/${id}/evaluation`),
      ]);
      setSelectedRfq(detail);
      setInvitations(history);
      setEvaluations(scores);
      setSelectedSupplierIds([]);
      if (detail.companyId !== form.companyId) {
        setForm((current) => ({ ...current, companyId: detail.companyId }));
        await loadCompanyData(detail.companyId);
      }
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "RFQ detayı alınamadı.");
    } finally { setBusy(false); }
  }

  async function publishRfq() {
    if (!selectedRfq) return;
    setBusy(true); setError("");
    try {
      await api(`rfqs/${selectedRfq.id}/publish`, { method: "POST" });
      setMessage("RFQ yayınlandı ve teklif toplamaya açıldı.");
      await loadBase();
      await openRfq(selectedRfq.id);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "RFQ yayınlanamadı.");
    } finally { setBusy(false); }
  }

  async function sendInvitations() {
    if (!selectedRfq || selectedSupplierIds.length === 0) {
      setError("En az bir e-posta adresi bulunan tedarikçi seçmelisiniz.");
      return;
    }
    const recipients = suppliers
      .filter((supplier) => selectedSupplierIds.includes(supplier.id) && supplier.email)
      .map((supplier) => ({ supplierId: supplier.id, email: supplier.email!, name: supplier.authorizedPerson || supplier.title }));
    if (recipients.length === 0) {
      setError("Seçilen tedarikçilerin e-posta adresi bulunmuyor.");
      return;
    }
    setBusy(true); setError("");
    try {
      const results = await api<Array<{ error?: string }>>(`rfq-invitations/rfqs/${selectedRfq.id}/send`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ recipients, portalBaseUrl: window.location.origin, singleUse: false }),
      });
      const failed = results.filter((result) => result.error).length;
      setMessage(`${results.length - failed} davet gönderildi${failed ? `, ${failed} gönderim başarısız` : ""}.`);
      await openRfq(selectedRfq.id);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Davetler gönderilemedi.");
    } finally { setBusy(false); }
  }

  async function resendInvitation(invitationId: string) {
    if (!selectedRfq) return;
    setBusy(true); setError("");
    try {
      await api(`rfq-invitations/${invitationId}/resend?portalBaseUrl=${encodeURIComponent(window.location.origin)}`, { method: "POST" });
      setMessage("Davet yeniden gönderildi.");
      await openRfq(selectedRfq.id);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Davet yeniden gönderilemedi.");
    } finally { setBusy(false); }
  }

  async function awardOffer(offerId: string) {
    if (!selectedRfq || !window.confirm("Bu teklifi kazanan olarak seçip satın alma siparişi oluşturmak istiyor musunuz?")) return;
    setBusy(true); setError("");
    try {
      const result = await api<{ orderNumber: string }>(`rfqs/${selectedRfq.id}/award`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ offerId, orderNumber: null, vatRate: 20, deliveryDateUtc: null, description: `${selectedRfq.rfqNumber} sonucunda oluşturuldu.` }),
      });
      setMessage(`${result.orderNumber} numaralı satın alma siparişi oluşturuldu.`);
      await loadBase();
      await openRfq(selectedRfq.id);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Teklif sonuçlandırılamadı.");
    } finally { setBusy(false); }
  }

  function toggleSupplier(id: string) {
    setSelectedSupplierIds((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]);
  }

  return (
    <ErpShell title="RFQ ve Teklif Yönetimi" description="Onaylı talepten RFQ oluşturma, tedarikçi daveti ve teklif karşılaştırma">
      <div className="space-y-5">
        {message && <div className="erp-alert success">{message}</div>}
        {error && <div className="erp-alert error">{error}</div>}

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
          {[
            ["Toplam RFQ", counters.total], ["Teklif Toplanıyor", counters.collecting], ["Teklif Gelen", counters.withOffers], ["Sonuçlanan", counters.awarded], ["48 Saat İçinde", counters.expiring],
          ].map(([label, value]) => <div key={String(label)} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><span className="text-sm text-slate-500">{label}</span><strong className="mt-2 block text-3xl text-slate-900">{value}</strong></div>)}
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-1 flex-wrap gap-3">
              <input className="min-w-[240px] flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm" placeholder="RFQ no veya proje ara" value={search} onChange={(event) => setSearch(event.target.value)} />
              <select className="rounded-lg border border-slate-300 px-3 py-2 text-sm" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="">Tüm durumlar</option>{statusLabels.map((label, index) => <option key={label} value={index}>{label}</option>)}</select>
            </div>
            <button className="erp-primary-button" onClick={() => setShowForm((value) => !value)}>+ Yeni RFQ</button>
          </div>
        </div>

        {showForm && <form onSubmit={createRfq} className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="m-0 text-lg">Onaylı Talepten RFQ Oluştur</h2>
          <div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <label className="grid gap-1 text-sm"><span>Şirket *</span><select required className="rounded-lg border border-slate-300 px-3 py-2" value={form.companyId} onChange={(event) => setForm({ ...form, companyId: event.target.value, purchaseRequestId: "" })}>{companies.map((company) => <option key={company.id} value={company.id}>{company.code} — {company.name}</option>)}</select></label>
            <label className="grid gap-1 text-sm"><span>Onaylı Satın Alma Talebi *</span><select required className="rounded-lg border border-slate-300 px-3 py-2" value={form.purchaseRequestId} onChange={(event) => selectRequest(event.target.value)}><option value="">Talep seçin</option>{requests.map((request) => <option key={request.id} value={request.id}>{request.requestNumber} · {request.itemCount} kalem</option>)}</select></label>
            <label className="grid gap-1 text-sm"><span>Proje *</span><select required className="rounded-lg border border-slate-300 px-3 py-2" value={form.projectId} onChange={(event) => setForm({ ...form, projectId: event.target.value })}>{projects.map((project) => <option key={project.id} value={project.id}>{project.code} — {project.name}</option>)}</select></label>
            <label className="grid gap-1 text-sm"><span>RFQ No</span><input className="rounded-lg border border-slate-300 px-3 py-2" value={form.rfqNumber} onChange={(event) => setForm({ ...form, rfqNumber: event.target.value.toUpperCase() })} /></label>
            <label className="grid gap-1 text-sm"><span>Son Teklif Tarihi *</span><input required type="datetime-local" className="rounded-lg border border-slate-300 px-3 py-2" value={form.offerDeadlineLocal} onChange={(event) => setForm({ ...form, offerDeadlineLocal: event.target.value })} /></label>
            <label className="grid gap-1 text-sm"><span>Para Birimi</span><select className="rounded-lg border border-slate-300 px-3 py-2" value={form.currencyCode} onChange={(event) => setForm({ ...form, currencyCode: event.target.value })}><option>TRY</option><option>USD</option><option>EUR</option><option>GBP</option></select></label>
            <label className="grid gap-1 text-sm md:col-span-2 xl:col-span-3"><span>Açıklama</span><textarea rows={3} className="rounded-lg border border-slate-300 px-3 py-2" value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} /></label>
          </div>
          <div className="mt-5 flex justify-end gap-3"><button type="button" className="erp-secondary-button" onClick={() => setShowForm(false)}>Vazgeç</button><button disabled={busy} className="erp-primary-button">{busy ? "Kaydediliyor..." : "Taslak RFQ Oluştur"}</button></div>
        </form>}

        <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
          <table className="erp-table min-w-[900px]"><thead><tr><th>RFQ No</th><th>Proje</th><th>Tarih</th><th>Son Teklif</th><th>Kalem</th><th>Teklif</th><th>Durum</th><th>İşlem</th></tr></thead><tbody>{filteredRfqs.map((rfq) => <tr key={rfq.id}><td className="font-semibold">{rfq.rfqNumber}</td><td>{projectNames.get(rfq.projectId) ?? rfq.projectId.slice(0, 8)}</td><td>{formatDate(rfq.rfqDateUtc)}</td><td>{formatDate(rfq.offerDeadlineUtc)}</td><td>{rfq.itemCount}</td><td>{rfq.offerCount}</td><td><span className={`erp-status ${statusClasses[rfq.status] ?? "gray"}`}>{statusLabels[rfq.status] ?? "Bilinmiyor"}</span></td><td><button className="erp-secondary-button" onClick={() => openRfq(rfq.id)}>Aç</button></td></tr>)}{filteredRfqs.length === 0 && <tr><td colSpan={8} className="py-10 text-center text-slate-500">RFQ bulunamadı.</td></tr>}</tbody></table>
        </div>

        {selectedRfq && <section className="space-y-5 rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <div className="flex flex-wrap items-start justify-between gap-4"><div><h2 className="m-0 text-xl">{selectedRfq.rfqNumber}</h2><p className="mt-1 text-sm text-slate-500">{projectNames.get(selectedRfq.projectId)} · {selectedRfq.currencyCode} · Son teklif {formatDate(selectedRfq.offerDeadlineUtc)}</p></div><div className="flex gap-2">{selectedRfq.status === 0 && <button disabled={busy} className="erp-primary-button" onClick={publishRfq}>Yayınla</button>}<button className="erp-secondary-button" onClick={() => setSelectedRfq(null)}>Kapat</button></div></div>

          <div className="grid gap-5 xl:grid-cols-2">
            <div className="rounded-xl border border-slate-200 p-4"><h3 className="mt-0">RFQ Kalemleri</h3><div className="overflow-x-auto"><table className="erp-table min-w-[600px]"><thead><tr><th>Açıklama</th><th>Miktar</th><th>Birim</th></tr></thead><tbody>{selectedRfq.items.map((item) => <tr key={item.id}><td>{item.description || item.materialId.slice(0, 8)}</td><td>{new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 4 }).format(item.quantity)}</td><td>{item.unit}</td></tr>)}</tbody></table></div></div>
            <div className="rounded-xl border border-slate-200 p-4"><div className="flex items-center justify-between"><h3 className="mt-0">Tedarikçi Daveti</h3><span className="text-xs text-slate-500">{selectedSupplierIds.length} seçili</span></div><div className="max-h-56 space-y-2 overflow-y-auto">{suppliers.map((supplier) => <label key={supplier.id} className={`flex items-center gap-3 rounded-lg border p-3 text-sm ${supplier.email ? "border-slate-200" : "border-red-100 bg-red-50"}`}><input type="checkbox" disabled={!supplier.email || selectedRfq.status === 4} checked={selectedSupplierIds.includes(supplier.id)} onChange={() => toggleSupplier(supplier.id)} /><span className="flex-1"><strong className="block">{supplier.title}</strong><span className="text-slate-500">{supplier.email || "E-posta tanımlı değil"}</span></span></label>)}{suppliers.length === 0 && <p className="text-sm text-slate-500">Onaylı tedarikçi bulunamadı.</p>}</div><button disabled={busy || selectedSupplierIds.length === 0 || selectedRfq.status === 4} className="erp-primary-button mt-4 w-full" onClick={sendInvitations}>Seçilenlere Davet Gönder</button></div>
          </div>

          <div className="rounded-xl border border-slate-200 p-4"><h3 className="mt-0">Davet Geçmişi</h3><div className="overflow-x-auto"><table className="erp-table min-w-[800px]"><thead><tr><th>Tedarikçi</th><th>E-posta</th><th>Durum</th><th>Gönderim</th><th>Açılma</th><th>Hatırlatma</th><th>İşlem</th></tr></thead><tbody>{invitations.map((invitation) => <tr key={invitation.id}><td>{supplierNames.get(invitation.supplierCurrentAccountId) || invitation.recipientName}</td><td>{invitation.recipientEmail}</td><td>{invitationLabels[invitation.status] || invitation.status}</td><td>{formatDate(invitation.sentAtUtc)}</td><td>{formatDate(invitation.openedAtUtc)}</td><td>{invitation.reminderCount}</td><td><button disabled={busy || selectedRfq.status === 4} className="erp-secondary-button" onClick={() => resendInvitation(invitation.id)}>Yeniden Gönder</button></td></tr>)}{invitations.length === 0 && <tr><td colSpan={7} className="py-8 text-center text-slate-500">Henüz davet gönderilmedi.</td></tr>}</tbody></table></div></div>

          <div className="rounded-xl border border-slate-200 p-4"><h3 className="mt-0">Teklif Karşılaştırma ve Puanlama</h3><div className="overflow-x-auto"><table className="erp-table min-w-[1100px]"><thead><tr><th>Sıra</th><th>Teklif</th><th>Toplam</th><th>Fiyat</th><th>Vade</th><th>Stok</th><th>Teslim</th><th>Tedarikçi</th><th>Genel Puan</th><th>Uyarılar</th><th>Karar</th></tr></thead><tbody>{evaluations.map((evaluation, index) => <tr key={evaluation.offerId} className={index === 0 ? "bg-emerald-50" : ""}><td>{index + 1}</td><td className="font-semibold">{evaluation.offerNumber}</td><td>{formatMoney(evaluation.totalCost, selectedRfq.currencyCode)}</td><td>{evaluation.priceScore.toFixed(1)}</td><td>{evaluation.paymentTermScore.toFixed(1)}</td><td>{evaluation.stockScore.toFixed(1)}</td><td>{evaluation.deliveryScore.toFixed(1)}</td><td>{evaluation.supplierScore.toFixed(1)}</td><td><strong>{evaluation.totalScore.toFixed(2)}</strong></td><td className="max-w-xs text-xs text-amber-700">{evaluation.warnings.join(" · ") || "—"}</td><td><button disabled={busy || selectedRfq.status === 4} className="erp-primary-button" onClick={() => awardOffer(evaluation.offerId)}>Kazanan Seç</button></td></tr>)}{evaluations.length === 0 && <tr><td colSpan={11} className="py-8 text-center text-slate-500">Henüz karşılaştırılacak teklif bulunmuyor.</td></tr>}</tbody></table></div></div>
        </section>}
      </div>
    </ErpShell>
  );
}
