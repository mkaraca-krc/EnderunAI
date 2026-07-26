"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

type Company={id:string;name:string;code:string};
type Supplier={id:string;companyId:string;code:string;title:string;email?:string;status:number;roles:number;isActive:boolean};
type Snapshot={id:string;supplierCurrentAccountId:string;periodStartUtc:string;periodEndUtc:string;deliveryScore:number;qualityScore:number;priceScore:number;technicalScore:number;financialScore:number;communicationScore:number;overallScore:number;riskLevel:number;totalOrderCount:number;completedOrderCount:number;lateOrderCount:number;totalOrderAmountTry:number;onTimeDeliveryRate:number;returnRate:number;notes?:string};
type Order={id:string;supplierId:string;orderNumber:string;orderDateUtc:string;deliveryDateUtc?:string;status:number;currencyCode:string;netAmount:number;receivedQuantity:number};

const riskText=["Düşük","Orta","Yüksek","Kritik"];
const riskClass=["bg-emerald-100 text-emerald-700","bg-amber-100 text-amber-700","bg-orange-100 text-orange-700","bg-rose-100 text-rose-700"];
const scoreClass=(v:number)=>v>=85?"text-emerald-600":v>=70?"text-amber-600":v>=50?"text-orange-600":"text-rose-600";
const fmt=(v:number)=>new Intl.NumberFormat("tr-TR",{style:"currency",currency:"TRY",maximumFractionDigits:0}).format(v||0);
const pct=(v:number)=>`%${new Intl.NumberFormat("tr-TR",{maximumFractionDigits:1}).format(v||0)}`;

async function api<T>(url:string,init?:RequestInit):Promise<T>{
  const r=await fetch(url,{...init,credentials:"include",headers:{"Content-Type":"application/json",...(init?.headers||{})}});
  if(r.status===401){window.location.href="/login";throw new Error("Oturum süresi doldu.");}
  if(!r.ok){let m="İşlem başarısız.";try{const x=await r.json();m=x.message||x.title||m;}catch{m=await r.text()||m;}throw new Error(m);}
  return r.status===204?({} as T):r.json();
}

function hizir(s:Snapshot|undefined,name:string){
  if(!s)return `${name} için henüz performans kaydı bulunmuyor. Hesaplama çalıştırılarak ilk dönem puanı oluşturulabilir.`;
  const parts=[`${name} son değerlendirme döneminde ${s.totalOrderCount} sipariş aldı.`, `Genel performans puanı ${s.overallScore.toFixed(1)}, zamanında teslim oranı ${pct(s.onTimeDeliveryRate)}.`];
  if(s.lateOrderCount>0)parts.push(`${s.lateOrderCount} siparişte gecikme bulunduğu için teslimat riski izlenmeli.`);
  if(s.returnRate>5)parts.push(`İade/uygunsuzluk oranı ${pct(s.returnRate)} ile dikkat gerektiriyor.`);
  if(s.overallScore>=85)parts.push("Hızır önerisi: stratejik tedarikçi olarak değerlendirilebilir.");
  else if(s.overallScore>=70)parts.push("Hızır önerisi: kontrollü şekilde çalışmaya devam edilebilir.");
  else parts.push("Hızır önerisi: yeni siparişlerde alternatif teklif ve ek kontrol uygulanmalı.");
  return parts.join(" ");
}

export default function Page(){
 const [companies,setCompanies]=useState<Company[]>([]),[companyId,setCompanyId]=useState("");
 const [suppliers,setSuppliers]=useState<Supplier[]>([]),[ranking,setRanking]=useState<Snapshot[]>([]),[orders,setOrders]=useState<Order[]>([]);
 const [selectedId,setSelectedId]=useState(""),[history,setHistory]=useState<Snapshot[]>([]),[loading,setLoading]=useState(false),[error,setError]=useState(""),[search,setSearch]=useState("");
 const [periodStart,setPeriodStart]=useState(()=>new Date(new Date().setFullYear(new Date().getFullYear()-1)).toISOString().slice(0,10));
 const [periodEnd,setPeriodEnd]=useState(()=>new Date().toISOString().slice(0,10));
 const loadCompanies=useCallback(async()=>{try{const x=await api<Company[]>("/api/companies");setCompanies(x);if(x[0])setCompanyId(v=>v||x[0].id);}catch(e){setError((e as Error).message)}},[]);
 const load=useCallback(async()=>{if(!companyId)return;setLoading(true);setError("");try{const [s,r,o]=await Promise.all([api<Supplier[]>(`/api/current-accounts?companyId=${companyId}&status=2`),api<Snapshot[]>(`/api/supplier-performance/ranking?companyId=${companyId}&take=100`),api<Order[]>(`/api/purchase-orders?companyId=${companyId}`)]);setSuppliers(s.filter(x=>x.isActive&&(x.roles&2)===2));setRanking(r);setOrders(o);}catch(e){setError((e as Error).message)}finally{setLoading(false)}},[companyId]);
 useEffect(()=>{loadCompanies()},[loadCompanies]);useEffect(()=>{load()},[load]);
 useEffect(()=>{if(!selectedId||!companyId){setHistory([]);return;}api<Snapshot[]>(`/api/supplier-performance/suppliers/${selectedId}/history?companyId=${companyId}`).then(setHistory).catch(e=>setError(e.message));},[selectedId,companyId,ranking]);
 const rows=useMemo(()=>suppliers.map(s=>({supplier:s,snapshot:ranking.find(x=>x.supplierCurrentAccountId===s.id)})).filter(x=>(x.supplier.title+" "+x.supplier.code).toLowerCase().includes(search.toLowerCase())),[suppliers,ranking,search]);
 const selected=rows.find(x=>x.supplier.id===selectedId)||rows[0];
 useEffect(()=>{if(!selectedId&&rows[0])setSelectedId(rows[0].supplier.id)},[rows,selectedId]);
 const selectedOrders=orders.filter(x=>x.supplierId===selected?.supplier.id);
 const avg=ranking.length?ranking.reduce((a,b)=>a+b.overallScore,0)/ranking.length:0;
 const critical=ranking.filter(x=>x.riskLevel===3).length,late=ranking.reduce((a,b)=>a+b.lateOrderCount,0),volume=ranking.reduce((a,b)=>a+b.totalOrderAmountTry,0);
 async function calculate(){if(!selected?.supplier.id)return;setLoading(true);setError("");try{await api(`/api/supplier-performance/calculate/${selected.supplier.id}?companyId=${companyId}&periodStartUtc=${periodStart}T00:00:00Z&periodEndUtc=${periodEnd}T23:59:59Z`,{method:"POST"});await load();}catch(e){setError((e as Error).message)}finally{setLoading(false)}}
 return <ErpShell title="Tedarikçi Performansı" description="Teslimat, kalite, fiyat ve risk puanları">
  <div className="space-y-5">
   {error&&<div className="rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{error}</div>}
   <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
    {[["Toplam Tedarikçi",suppliers.length],["Ortalama Puan",avg.toFixed(1)],["Kritik Risk",critical],["Geciken Sipariş",late],["Değerlendirilen",ranking.length],["TRY Sipariş Hacmi",fmt(volume)]].map(([a,b])=><div key={a} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm"><div className="text-xs text-slate-500">{a}</div><div className="mt-2 text-2xl font-bold text-slate-900">{b}</div></div>)}
   </div>
   <div className="grid gap-5 xl:grid-cols-[1.1fr_.9fr]">
    <section className="rounded-xl border border-slate-200 bg-white shadow-sm">
     <div className="flex flex-col gap-3 border-b p-4 md:flex-row md:items-center"><select className="rounded-lg border p-2 text-sm" value={companyId} onChange={e=>setCompanyId(e.target.value)}>{companies.map(x=><option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}</select><input className="flex-1 rounded-lg border p-2 text-sm" placeholder="Tedarikçi ara" value={search} onChange={e=>setSearch(e.target.value)}/><button onClick={load} className="rounded-lg border px-3 py-2 text-sm">Yenile</button></div>
     <div className="overflow-x-auto"><table className="min-w-full text-sm"><thead className="bg-slate-50 text-left text-xs text-slate-500"><tr><th className="p-3">Tedarikçi</th><th className="p-3">Puan</th><th className="p-3">Risk</th><th className="p-3">Sipariş</th><th className="p-3">Teslim</th><th className="p-3">Hacim</th></tr></thead><tbody>{rows.map(({supplier,snapshot})=><tr key={supplier.id} onClick={()=>setSelectedId(supplier.id)} className={`cursor-pointer border-t hover:bg-slate-50 ${selected?.supplier.id===supplier.id?"bg-sky-50":""}`}><td className="p-3"><div className="font-medium">{supplier.title}</div><div className="text-xs text-slate-400">{supplier.code}</div></td><td className={`p-3 text-lg font-bold ${scoreClass(snapshot?.overallScore||0)}`}>{snapshot? snapshot.overallScore.toFixed(1):"—"}</td><td className="p-3">{snapshot?<span className={`rounded-full px-2 py-1 text-xs font-medium ${riskClass[snapshot.riskLevel]}`}>{riskText[snapshot.riskLevel]}</span>:"—"}</td><td className="p-3">{snapshot?.totalOrderCount??0}</td><td className="p-3">{snapshot?pct(snapshot.onTimeDeliveryRate):"—"}</td><td className="p-3">{fmt(snapshot?.totalOrderAmountTry||0)}</td></tr>)}</tbody></table>{!loading&&rows.length===0&&<div className="p-8 text-center text-sm text-slate-500">Tedarikçi bulunamadı.</div>}</div>
    </section>
    <section className="space-y-4">
     <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><div className="flex items-start justify-between gap-3"><div><h2 className="m-0 text-lg font-semibold">{selected?.supplier.title||"Tedarikçi seçin"}</h2><p className="mt-1 text-xs text-slate-500">Performans puan kartı</p></div>{selected?.snapshot&&<span className={`rounded-full px-3 py-1 text-xs font-semibold ${riskClass[selected.snapshot.riskLevel]}`}>{riskText[selected.snapshot.riskLevel]} risk</span>}</div>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">{[["Teslimat",selected?.snapshot?.deliveryScore],["Kalite",selected?.snapshot?.qualityScore],["Fiyat",selected?.snapshot?.priceScore],["Teknik",selected?.snapshot?.technicalScore],["Finansal",selected?.snapshot?.financialScore],["İletişim",selected?.snapshot?.communicationScore]].map(([n,v])=><div key={n as string} className="rounded-lg bg-slate-50 p-3"><div className="text-xs text-slate-500">{n}</div><div className={`mt-1 text-xl font-bold ${scoreClass(Number(v||0))}`}>{v===undefined?"—":Number(v).toFixed(1)}</div></div>)}</div>
      <div className="mt-4 rounded-lg border border-sky-100 bg-sky-50 p-4 text-sm leading-6 text-sky-900"><strong>Hızır:</strong> {hizir(selected?.snapshot,selected?.supplier.title||"Tedarikçi")}</div>
      <div className="mt-4 flex flex-wrap gap-2"><input type="date" className="rounded-lg border p-2 text-sm" value={periodStart} onChange={e=>setPeriodStart(e.target.value)}/><input type="date" className="rounded-lg border p-2 text-sm" value={periodEnd} onChange={e=>setPeriodEnd(e.target.value)}/><button disabled={loading||!selected} onClick={calculate} className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">Performansı Hesapla</button></div>
     </div>
     <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"><h3 className="m-0 text-sm font-semibold">Dönemsel performans</h3><div className="mt-4 space-y-3">{history.slice(0,12).map(x=><div key={x.id} className="flex items-center gap-3"><div className="w-24 text-xs text-slate-500">{new Date(x.periodEndUtc).toLocaleDateString("tr-TR",{month:"short",year:"numeric"})}</div><div className="h-2 flex-1 overflow-hidden rounded bg-slate-100"><div className="h-full bg-slate-800" style={{width:`${Math.max(0,Math.min(100,x.overallScore))}%`}}/></div><div className={`w-12 text-right text-sm font-semibold ${scoreClass(x.overallScore)}`}>{x.overallScore.toFixed(1)}</div></div>)}{history.length===0&&<p className="text-sm text-slate-500">Henüz dönemsel kayıt yok.</p>}</div></div>
    </section>
   </div>
   <section className="rounded-xl border border-slate-200 bg-white shadow-sm"><div className="border-b p-4"><h3 className="m-0 text-sm font-semibold">Sipariş geçmişi — {selected?.supplier.title||""}</h3></div><div className="overflow-x-auto"><table className="min-w-full text-sm"><thead className="bg-slate-50 text-left text-xs text-slate-500"><tr><th className="p-3">Sipariş</th><th className="p-3">Tarih</th><th className="p-3">Teslim tarihi</th><th className="p-3">Durum</th><th className="p-3">Tutar</th></tr></thead><tbody>{selectedOrders.map(o=><tr key={o.id} className="border-t"><td className="p-3 font-medium">{o.orderNumber}</td><td className="p-3">{new Date(o.orderDateUtc).toLocaleDateString("tr-TR")}</td><td className="p-3">{o.deliveryDateUtc?new Date(o.deliveryDateUtc).toLocaleDateString("tr-TR"):"—"}</td><td className="p-3">{["Taslak","Onay Bekliyor","Onaylandı","Kısmi Teslim","Tamamlandı","İptal","Reddedildi"][o.status]||o.status}</td><td className="p-3">{new Intl.NumberFormat("tr-TR",{style:"currency",currency:o.currencyCode||"TRY"}).format(o.netAmount||0)}</td></tr>)}</tbody></table>{selectedOrders.length===0&&<div className="p-6 text-center text-sm text-slate-500">Sipariş geçmişi bulunmuyor.</div>}</div></section>
  </div>
 </ErpShell>
}
