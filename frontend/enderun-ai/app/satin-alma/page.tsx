"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company = { id: string; code: string; name: string };
type Counters = {
  pendingPurchaseRequests: number;
  openRfqs: number;
  offersUnderEvaluation: number;
  pendingApprovals: number;
  openPurchaseOrders: number;
  pendingGoodsReceipts: number;
  criticalBudgetAlerts: number;
  criticalSupplierRisks: number;
};
type Financial = {
  totalPurchaseVolume: number;
  currentMonthPurchaseVolume: number;
  averageOrderAmount: number;
  totalOrderCount: number;
  totalOfferCount: number;
  averageOffersPerRfq: number;
};
type Approval = {
  pendingCount: number;
  criticalPendingCount: number;
  averageCompletionHours: number;
  revisionRequestedCount: number;
};
type Trend = { year: number; month: number; amount: number; orderCount: number };
type ProjectKpi = { projectId: string; projectName: string; purchaseVolume: number; orderCount: number };
type SupplierKpi = {
  supplierId: string;
  supplierName: string;
  purchaseVolume: number;
  orderCount: number;
  performanceScore?: number | null;
  riskLevel?: string | null;
};
type BudgetKpi = {
  projectId: string;
  projectName: string;
  budget: number;
  committed: number;
  actual: number;
  remaining: number;
  usageRate: number;
  status: string;
};
type Dashboard = {
  generatedAtUtc: string;
  counters: Counters;
  financial: Financial;
  approvals: Approval;
  monthlyTrend: Trend[];
  topProjects: ProjectKpi[];
  topSuppliers: SupplierKpi[];
  budgets: BudgetKpi[];
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});
const decimal = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 1 });
const months = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

async function api<T>(path: string): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store" });
  if (response.status === 401) {
    window.location.href = "/login";
    throw new Error("Oturum süresi doldu.");
  }
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.message ?? `İstek başarısız (${response.status}).`);
  return body as T;
}

function StatCard({ label, value, tone = "default", detail }: { label: string; value: string | number; tone?: "default" | "warning" | "danger" | "success"; detail?: string }) {
  const toneClass = tone === "danger" ? "border-red-200 bg-red-50" : tone === "warning" ? "border-amber-200 bg-amber-50" : tone === "success" ? "border-emerald-200 bg-emerald-50" : "border-slate-200 bg-white";
  return <article className={`rounded-xl border p-5 shadow-sm ${toneClass}`}><span className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</span><strong className="mt-3 block text-3xl text-slate-900">{value}</strong>{detail && <small className="mt-2 block text-slate-500">{detail}</small>}</article>;
}

export default function ProcurementDashboardPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [data, setData] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadCompanies = useCallback(async () => {
    const list = await api<Company[]>("companies");
    setCompanies(list);
    setCompanyId((current) => current || list[0]?.id || "");
  }, []);

  const loadDashboard = useCallback(async (selectedCompanyId: string) => {
    if (!selectedCompanyId) return;
    setLoading(true);
    setError("");
    try {
      setData(await api<Dashboard>(`procurement-dashboard?companyId=${selectedCompanyId}&months=12`));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Dashboard alınamadı.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadCompanies().catch((err) => { setError(err instanceof Error ? err.message : "Şirketler alınamadı."); setLoading(false); }); }, [loadCompanies]);
  useEffect(() => { if (companyId) loadDashboard(companyId); }, [companyId, loadDashboard]);

  const maxTrend = useMemo(() => Math.max(...(data?.monthlyTrend.map((x) => x.amount) ?? [1]), 1), [data]);

  return (
    <ErpShell title="Satın Alma" description="Talep, RFQ, sipariş, onay, bütçe ve tedarikçi performansı">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap gap-2">
          <Link className="erp-primary-button" href="/satin-alma/talepler">Satın Alma Talepleri</Link>
          <Link className="erp-secondary-button" href="/satin-alma/rfq">RFQ Yönetimi</Link>
          <Link className="erp-secondary-button" href="/satin-alma/siparisler">Siparişler</Link>
          <Link className="erp-secondary-button" href="/onay-merkezi">Onay Merkezi</Link>
        </div>
        <select className="min-h-10 rounded-lg border border-slate-300 bg-white px-3 text-sm" value={companyId} onChange={(event) => setCompanyId(event.target.value)}>
          {companies.map((company) => <option key={company.id} value={company.id}>{company.code} — {company.name}</option>)}
        </select>
      </div>

      {error && <div className="mb-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>}
      {loading && <div className="rounded-xl border border-slate-200 bg-white p-10 text-center text-slate-500">Satın alma verileri yükleniyor…</div>}

      {!loading && data && <>
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Bekleyen Onay" value={data.counters.pendingApprovals} tone={data.counters.pendingApprovals > 0 ? "warning" : "success"} detail={`${data.approvals.criticalPendingCount} kritik gecikme`} />
          <StatCard label="Açık RFQ" value={data.counters.openRfqs} detail={`${data.counters.offersUnderEvaluation} değerlendirmede`} />
          <StatCard label="Bu Ay Satın Alma" value={money.format(data.financial.currentMonthPurchaseVolume)} detail={`${data.financial.totalOrderCount} toplam sipariş`} />
          <StatCard label="Kritik Bütçe" value={data.counters.criticalBudgetAlerts} tone={data.counters.criticalBudgetAlerts > 0 ? "danger" : "success"} detail={`${data.counters.criticalSupplierRisks} kritik tedarikçi`} />
        </section>

        <section className="mt-6 grid gap-6 xl:grid-cols-[1.6fr_1fr]">
          <article className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex items-center justify-between"><div><h2 className="m-0 text-lg">Aylık Satın Alma Trendi</h2><p className="mt-1 text-sm text-slate-500">Son 12 ay sipariş hacmi</p></div><strong>{money.format(data.financial.totalPurchaseVolume)}</strong></div>
            <div className="mt-6 flex h-64 items-end gap-2 border-b border-slate-200 px-1">
              {data.monthlyTrend.map((item) => <div key={`${item.year}-${item.month}`} className="flex min-w-0 flex-1 flex-col items-center justify-end gap-2" title={`${months[item.month - 1]} ${item.year}: ${money.format(item.amount)}`}><span className="text-[10px] text-slate-500">{item.orderCount}</span><div className="w-full rounded-t bg-slate-700 transition-all hover:bg-slate-900" style={{ height: `${Math.max((item.amount / maxTrend) * 190, item.amount > 0 ? 8 : 1)}px` }} /><span className="text-[10px] text-slate-500">{months[item.month - 1]}</span></div>)}
            </div>
          </article>

          <article className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="m-0 text-lg">Operasyon Özeti</h2>
            <div className="mt-5 grid grid-cols-2 gap-3 text-sm">
              <div className="rounded-lg bg-slate-50 p-4"><span className="text-slate-500">Bekleyen Talep</span><strong className="mt-2 block text-2xl">{data.counters.pendingPurchaseRequests}</strong></div>
              <div className="rounded-lg bg-slate-50 p-4"><span className="text-slate-500">Açık Sipariş</span><strong className="mt-2 block text-2xl">{data.counters.openPurchaseOrders}</strong></div>
              <div className="rounded-lg bg-slate-50 p-4"><span className="text-slate-500">Mal Kabul</span><strong className="mt-2 block text-2xl">{data.counters.pendingGoodsReceipts}</strong></div>
              <div className="rounded-lg bg-slate-50 p-4"><span className="text-slate-500">Ort. Teklif/RFQ</span><strong className="mt-2 block text-2xl">{decimal.format(data.financial.averageOffersPerRfq)}</strong></div>
            </div>
            <div className="mt-4 rounded-lg border border-slate-200 p-4 text-sm"><div className="flex justify-between"><span>Ortalama sipariş</span><strong>{money.format(data.financial.averageOrderAmount)}</strong></div><div className="mt-3 flex justify-between"><span>Ortalama onay süresi</span><strong>{decimal.format(data.approvals.averageCompletionHours)} saat</strong></div><div className="mt-3 flex justify-between"><span>Revizyona dönen</span><strong>{data.approvals.revisionRequestedCount}</strong></div></div>
          </article>
        </section>

        <section className="mt-6 grid gap-6 xl:grid-cols-2">
          <article className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 p-5"><h2 className="m-0 text-lg">En Yüksek Hacimli Projeler</h2></div><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr><th className="p-4">Proje</th><th className="p-4 text-right">Sipariş</th><th className="p-4 text-right">Hacim</th></tr></thead><tbody>{data.topProjects.map((item) => <tr key={item.projectId} className="border-t border-slate-100"><td className="p-4 font-medium">{item.projectName}</td><td className="p-4 text-right">{item.orderCount}</td><td className="p-4 text-right font-semibold">{money.format(item.purchaseVolume)}</td></tr>)}</tbody></table></div></article>
          <article className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 p-5"><h2 className="m-0 text-lg">Tedarikçi Performansı</h2></div><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr><th className="p-4">Tedarikçi</th><th className="p-4 text-right">Puan</th><th className="p-4 text-right">Hacim</th></tr></thead><tbody>{data.topSuppliers.map((item) => <tr key={item.supplierId} className="border-t border-slate-100"><td className="p-4"><strong>{item.supplierName}</strong><small className="mt-1 block text-slate-500">{item.riskLevel ?? "Puanlanmadı"}</small></td><td className="p-4 text-right">{item.performanceScore == null ? "—" : decimal.format(item.performanceScore)}</td><td className="p-4 text-right font-semibold">{money.format(item.purchaseVolume)}</td></tr>)}</tbody></table></div></article>
        </section>

        <article className="mt-6 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 p-5"><h2 className="m-0 text-lg">Proje Bütçe Kontrolü</h2></div><div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr><th className="p-4">Proje</th><th className="p-4 text-right">Bütçe</th><th className="p-4 text-right">Taahhüt</th><th className="p-4 text-right">Gerçekleşen</th><th className="p-4 text-right">Kalan</th><th className="p-4">Kullanım</th></tr></thead><tbody>{data.budgets.map((item) => <tr key={item.projectId} className="border-t border-slate-100"><td className="p-4 font-medium">{item.projectName}</td><td className="p-4 text-right">{money.format(item.budget)}</td><td className="p-4 text-right">{money.format(item.committed)}</td><td className="p-4 text-right">{money.format(item.actual)}</td><td className={`p-4 text-right font-semibold ${item.remaining < 0 ? "text-red-600" : ""}`}>{money.format(item.remaining)}</td><td className="min-w-40 p-4"><div className="mb-1 flex justify-between"><span>{decimal.format(item.usageRate)}%</span><span className={item.status === "Critical" ? "text-red-600" : item.status === "Warning" ? "text-amber-600" : "text-emerald-600"}>{item.status}</span></div><div className="h-2 overflow-hidden rounded bg-slate-100"><div className={item.status === "Critical" ? "h-full bg-red-500" : item.status === "Warning" ? "h-full bg-amber-500" : "h-full bg-emerald-500"} style={{ width: `${Math.min(Math.max(item.usageRate, 0), 100)}%` }} /></div></td></tr>)}</tbody></table></div></article>
      </>}
    </ErpShell>
  );
}
