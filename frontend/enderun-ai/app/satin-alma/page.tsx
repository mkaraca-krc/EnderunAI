"use client";

import type { PurchaseRequestPriority } from "@/services/purchase-request.service";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { Badge, Button, Card, CardContent, CardHeader, EmptyState, Input, Select, StatCard, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui";
import { companyService, CompanyListItem } from "@/services/company.service";
import { projectService, ProjectListItem } from "@/services/project.service";
import { PURCHASE_REQUEST_STATUS_LABELS, purchaseRequestService, PurchaseRequestListItem } from "@/services/purchase-request.service";
import PositionPicker from "@/components/purchasing/position-picker";
import SupplierQualityCard from "@/components/purchasing/supplier-quality-card";
import RequestedBrandField from "@/components/purchasing/requested-brand-field";
import { requestedBrandError } from "@/lib/purchasing/requested-brand";

// Durum adları servisle ortak; "Düzeltmeye İade" (8) dahil.
const statusLabels = PURCHASE_REQUEST_STATUS_LABELS;
const priorityLabels: Record<number,string> = {0:"Düşük",1:"Normal",2:"Yüksek",3:"Kritik"};
const today = new Date().toISOString().slice(0,10);

type SelectedPosition = { id:string; code:string; name:string; isCustom:boolean };

type Line = { materialDescription:string; quantity:string; unit:string; requestedDeliveryDate:string; notes:string; position:SelectedPosition|null; requestedBrand:string; brandIrrelevant:boolean };
// brandIrrelevant varsayilani TRUE: kullanici marka yazmadan da
// gecerli bir talep kurabilsin, marka bilincli bir SECIM olsun diye
// isareti kaldirdiginda marka zorunlu hale gelir.
const emptyLine = (): Line => ({ materialDescription:"", quantity:"", unit:"Adet", requestedDeliveryDate:"", notes:"", position:null, requestedBrand:"", brandIrrelevant:true });

export default function PurchasingPage(){
  const [items,setItems]=useState<PurchaseRequestListItem[]>([]);
  const [companies,setCompanies]=useState<CompanyListItem[]>([]);
  const [projects,setProjects]=useState<ProjectListItem[]>([]);
  const [loading,setLoading]=useState(true); const [saving,setSaving]=useState(false);
  const [error,setError]=useState(""); const [success,setSuccess]=useState(""); const [showForm,setShowForm]=useState(false);
  const [search,setSearch]=useState(""); const [companyFilter,setCompanyFilter]=useState(""); const [statusFilter,setStatusFilter]=useState("");
  const [form,setForm]=useState({companyId:"",projectId:"",requestNumber:"",requestDate:today,neededByDate:"",requestedByName:"",description:"",priority:"1",items:[emptyLine()]});

  async function load(){
    setLoading(true); setError("");
    try{setItems(await purchaseRequestService.getAll({companyId:companyFilter||undefined,status:statusFilter===""?undefined:Number(statusFilter),search:search.trim()||undefined}));}
    catch(e){setError(e instanceof Error?e.message:"Talepler yüklenemedi.");}finally{setLoading(false);}
  }

  useEffect(()=>{(async()=>{try{const [c,p,r]=await Promise.all([companyService.getAll(),projectService.getAll(),purchaseRequestService.getAll()]);setCompanies(c);setProjects(p);setItems(r);if(c.length===1)setForm(v=>({...v,companyId:c[0].id}));}catch(e){setError(e instanceof Error?e.message:"Ekran yüklenemedi.");}finally{setLoading(false);}})();},[]);

  const formProjects=useMemo(()=>projects.filter(p=>!form.companyId||p.companyId===form.companyId),[projects,form.companyId]);
  const pending=items.filter(x=>![5,6,7].includes(x.status)).length;
  const approval=items.filter(x=>x.status===1).length;
  const critical=items.filter(x=>x.priority===3&&![5,6,7].includes(x.status)).length;

  function updateLine(i:number,key:keyof Line,value:string){setForm(v=>({...v,items:v.items.map((x,n)=>n===i?{...x,[key]:value}:x)}));}
  function updateBrand(i:number,patch:Partial<Pick<Line,"requestedBrand"|"brandIrrelevant">>){setForm(v=>({...v,items:v.items.map((x,n)=>n===i?{...x,...patch}:x)}));}
  function addLine(){setForm(v=>({...v,items:[...v.items,emptyLine()]}));}
  function removeLine(i:number){setForm(v=>({...v,items:v.items.length===1?v.items:v.items.filter((_,n)=>n!==i)}));}

  async function create(e:FormEvent){e.preventDefault();
    // Marka kurali sunucuda da var; buradaki kontrol yalniz kullaniciya
    // erken geri bildirim icin — sunucu kurali kaldirilmadi.
    const brandIssue=form.items.findIndex(x=>requestedBrandError({requestedBrand:x.requestedBrand,brandIrrelevant:x.brandIrrelevant})!==null);
    if(brandIssue>=0){setError(`${brandIssue+1}. kalemde ${requestedBrandError({requestedBrand:form.items[brandIssue].requestedBrand,brandIrrelevant:form.items[brandIssue].brandIrrelevant})}.`);return;}
    setSaving(true);setError("");setSuccess("");
    try{await purchaseRequestService.create({companyId:form.companyId,projectId:form.projectId,requestType:0,requestDate:form.requestDate,neededByDate:form.neededByDate||null,requestedByName:form.requestedByName,description:form.description||null,priority:Number(form.priority) as PurchaseRequestPriority,items:form.items.map(x=>({materialDescription:x.materialDescription,quantity:Number(x.quantity),unit:x.unit,requestedDeliveryDate:x.requestedDeliveryDate||null,notes:x.notes||null,engineeringPositionId:x.position?.id??null,requestedBrand:x.requestedBrand.trim()||null,brandIrrelevant:x.brandIrrelevant}))});setSuccess("Satın alma talebi taslak olarak oluşturuldu.");setShowForm(false);setForm(v=>({...v,projectId:"",requestNumber:"",neededByDate:"",requestedByName:"",description:"",priority:"1",items:[emptyLine()]}));await load();}
    catch(e){setError(e instanceof Error?e.message:"Talep oluşturulamadı.");}finally{setSaving(false);}
  }

  return <ErpShell design="redwood" title="Satın Alma Talepleri" description="Proje bazlı talep ve onay süreçleri">
    {error&&<div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}
    {success&&<div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{success}</div>}
    <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4"><StatCard title="Toplam Talep" value={loading?"…":items.length} icon="⌑"/><StatCard title="Açık Talep" value={loading?"…":pending} icon="◷"/><StatCard title="Onay Bekleyen" value={loading?"…":approval} icon="✓"/><StatCard title="Kritik" value={loading?"…":critical} icon="!"/></div>
    <div className="mb-6 flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
      <form onSubmit={e=>{e.preventDefault();load();}} className="flex flex-1 flex-col gap-3 md:flex-row"><Input value={search} onChange={e=>setSearch(e.target.value)} placeholder="Talep no, proje veya talep eden"/><Select value={companyFilter} onChange={e=>setCompanyFilter(e.target.value)} placeholder="Tüm şirketler" options={companies.map(x=>({label:`${x.code} · ${x.name}`,value:x.id}))}/><Select value={statusFilter} onChange={e=>setStatusFilter(e.target.value)} placeholder="Tüm durumlar" options={Object.entries(statusLabels).map(([value,label])=>({value,label}))}/><Button type="submit" variant="secondary">Ara</Button></form>
      <Button onClick={()=>setShowForm(v=>!v)}>{showForm?"Formu Kapat":"+ Yeni Talep"}</Button>
    </div>
    {showForm&&<Card className="mb-6"><CardHeader><h2 className="text-lg font-semibold text-slate-900">Yeni Satın Alma Talebi</h2></CardHeader><CardContent><form onSubmit={create}>
      <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-3"><Select label="Şirket" required value={form.companyId} onChange={e=>setForm(v=>({...v,companyId:e.target.value,projectId:""}))} placeholder="Şirket seçin" options={companies.map(x=>({label:`${x.code} · ${x.name}`,value:x.id}))}/><Select label="Proje" required value={form.projectId} onChange={e=>setForm(v=>({...v,projectId:e.target.value}))} placeholder="Proje seçin" options={formProjects.map(x=>({label:`${x.code} · ${x.name}`,value:x.id}))}/><Input label="Talep No" required value={form.requestNumber} onChange={e=>setForm(v=>({...v,requestNumber:e.target.value}))}/><Input label="Talep Tarihi" type="date" required value={form.requestDate} onChange={e=>setForm(v=>({...v,requestDate:e.target.value}))}/><Input label="İhtiyaç Tarihi" type="date" value={form.neededByDate} onChange={e=>setForm(v=>({...v,neededByDate:e.target.value}))}/><Input label="Talep Eden" required value={form.requestedByName} onChange={e=>setForm(v=>({...v,requestedByName:e.target.value}))}/><Select label="Öncelik" value={form.priority} onChange={e=>setForm(v=>({...v,priority:e.target.value}))} options={Object.entries(priorityLabels).map(([value,label])=>({value,label}))}/><div className="md:col-span-2"><Input label="Açıklama" value={form.description} onChange={e=>setForm(v=>({...v,description:e.target.value}))}/></div></div>
      <div className="mt-7 flex items-center justify-between"><h3 className="font-semibold text-slate-900">Talep Kalemleri</h3><Button type="button" variant="secondary" onClick={addLine}>+ Kalem Ekle</Button></div>
      <div className="mt-4 space-y-4">{form.items.map((line,i)=><div key={i} className="rounded-xl border border-slate-200 bg-slate-50 p-4"><div className="mb-3 flex justify-between"><strong>Kalem {i+1}</strong><Button type="button" size="sm" variant="ghost" disabled={form.items.length===1} onClick={()=>removeLine(i)}>Kaldır</Button></div><div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5"><div className="md:col-span-2 xl:col-span-5"><PositionPicker companyId={form.companyId} selected={line.position} onSelect={(position,unit)=>setForm(v=>({...v,items:v.items.map((item,index)=>index===i?{...item,position,materialDescription:item.materialDescription.trim()||position.name,unit:unit||item.unit}:item)}))} onClear={()=>setForm(v=>({...v,items:v.items.map((item,index)=>index===i?{...item,position:null}:item)}))}/></div><div className="xl:col-span-2"><Input label="Malzeme / Hizmet" required value={line.materialDescription} onChange={e=>updateLine(i,"materialDescription",e.target.value)}/></div><Input label="Miktar" type="number" min="0.0001" step="0.0001" required value={line.quantity} onChange={e=>updateLine(i,"quantity",e.target.value)}/><Input label="Birim" required value={line.unit} onChange={e=>updateLine(i,"unit",e.target.value)}/><Input label="Teslim Tarihi" type="date" value={line.requestedDeliveryDate} onChange={e=>updateLine(i,"requestedDeliveryDate",e.target.value)}/><div className="md:col-span-2 xl:col-span-2"><RequestedBrandField brand={line.requestedBrand} irrelevant={line.brandIrrelevant} onBrandChange={value=>updateBrand(i,{requestedBrand:value})} onIrrelevantChange={value=>updateBrand(i,{brandIrrelevant:value})}/></div><div className="md:col-span-2 xl:col-span-3"><Input label="Not" value={line.notes} onChange={e=>updateLine(i,"notes",e.target.value)}/></div></div></div>)}</div>
      <div className="mt-6 flex justify-end gap-3"><Button type="button" variant="secondary" onClick={()=>setShowForm(false)}>Vazgeç</Button><Button type="submit" loading={saving}>Taslak Kaydet</Button></div>
    </form></CardContent></Card>}
    {/* Tedarikci kalite karnesi: ucu vardi, ekrani yoktu. Sirket
        filtresi talep listesiyle ayni state'ten besleniyor ki iki
        tablo ayni kapsami gostersin. */}
    <SupplierQualityCard companyId={companyFilter||undefined}/>
    <Card><CardHeader><div className="flex justify-between"><div><h2 className="text-lg font-semibold text-slate-900">Talep Listesi</h2><p className="mt-1 text-sm text-slate-500">Satın alma talepleri ve durumları</p></div><div className="flex gap-2">{items.filter(x=>x.status===8).length>0&&<Badge variant="warning">{items.filter(x=>x.status===8).length} düzeltme bekliyor</Badge>}<Badge variant="info">{items.length} kayıt</Badge></div></div></CardHeader><CardContent>{loading?<div className="py-12 text-center text-sm text-slate-500">Yükleniyor...</div>:items.length===0?<EmptyState title="Talep bulunamadı" action={<Button onClick={()=>setShowForm(true)}>Yeni Talep</Button>}/>:<Table><TableHeader><TableRow><TableHead>Talep</TableHead><TableHead>Proje</TableHead><TableHead>Talep Eden</TableHead><TableHead>Kalem</TableHead><TableHead>Öncelik</TableHead><TableHead>Durum</TableHead><TableHead className="text-right">İşlem</TableHead></TableRow></TableHeader><TableBody>{items.map(x=><TableRow key={x.id}><TableCell><strong>{x.requestNumber}</strong><span className="mt-1 block text-xs text-slate-500">{new Date(x.requestDate).toLocaleDateString("tr-TR")}</span></TableCell><TableCell><strong>{x.projectName}</strong><span className="mt-1 block text-xs text-slate-500">{x.projectCode}</span></TableCell><TableCell>{x.requestedByName}</TableCell><TableCell>{x.itemCount}</TableCell><TableCell><Badge variant={x.priority===3?"danger":x.priority===2?"warning":"info"}>{priorityLabels[x.priority]}</Badge></TableCell><TableCell><Badge variant={x.status===2||x.status===5?"success":x.status===6||x.status===7?"danger":x.status===0?"default":"warning"}>{statusLabels[x.status]}</Badge></TableCell><TableCell className="text-right"><Link href={`/satin-alma/${x.id}`} className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-3 text-sm font-medium text-slate-700">Talebi Aç</Link></TableCell></TableRow>)}</TableBody></Table>}</CardContent></Card>
  </ErpShell>;
}
