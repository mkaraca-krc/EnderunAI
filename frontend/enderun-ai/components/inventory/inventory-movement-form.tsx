"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { inventoryMovementService, type SelectOption } from "@/services/inventory-movement.service";

export function InventoryMovementForm({ mode }: { mode: "receipt" | "issue" | "transfer" }) {
  const router = useRouter();
  const [warehouses,setWarehouses]=useState<SelectOption[]>([]);
  const [projects,setProjects]=useState<SelectOption[]>([]);
  const [items,setItems]=useState<SelectOption[]>([]);
  const [warehouseId,setWarehouseId]=useState("");
  const [targetWarehouseId,setTargetWarehouseId]=useState("");
  const [projectId,setProjectId]=useState("");
  const [inventoryItemId,setInventoryItemId]=useState("");
  const [quantity,setQuantity]=useState(0);
  const [referenceNumber,setReferenceNumber]=useState("");
  const [movementDate,setMovementDate]=useState(new Date().toISOString().slice(0,10));
  const [description,setDescription]=useState("");
  const [error,setError]=useState("");
  const [saving,setSaving]=useState(false);

  useEffect(()=>{ void Promise.all([
    inventoryMovementService.getWarehouses().then(setWarehouses),
    inventoryMovementService.getProjects().then(setProjects),
    inventoryMovementService.getItems().then(setItems),
  ]).catch(e=>setError(e instanceof Error?e.message:"Veriler yüklenemedi.")); },[]);

  const title=mode==="receipt"?"Depo girişi":mode==="issue"?"Depo çıkışı":"Depolar arası transfer";

  async function submit(e:React.FormEvent){
    e.preventDefault(); setError("");
    if(!warehouseId||!inventoryItemId||quantity<=0||!referenceNumber.trim()){setError("Depo, malzeme, miktar ve referans zorunludur.");return;}
    if(mode==="transfer"&&!targetWarehouseId){setError("Hedef depo seçilmelidir.");return;}
    const common={inventoryItemId,projectId:projectId||undefined,quantity,referenceNumber:referenceNumber.trim(),movementDate,description:description||undefined};
    try{
      setSaving(true);
      if(mode==="receipt") await inventoryMovementService.receipt({warehouseId,...common});
      else if(mode==="issue") await inventoryMovementService.issue({warehouseId,...common});
      else await inventoryMovementService.transfer({sourceWarehouseId:warehouseId,targetWarehouseId,...common});
      router.push("/depo-stok/hareketler"); router.refresh();
    }catch(e){setError(e instanceof Error?e.message:"İşlem kaydedilemedi.");}
    finally{setSaving(false);}
  }

  return <div className="mx-auto max-w-4xl space-y-6 p-6">
    <div><Link href="/depo-stok/hareketler" className="text-sm font-medium text-slate-600">← Stok hareketleri</Link><h1 className="mt-2 text-2xl font-semibold">{title}</h1></div>
    <form onSubmit={submit} className="space-y-5 rounded-xl border bg-white p-6 shadow-sm">
      {error&&<div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>}
      <div className="grid gap-4 md:grid-cols-2">
        <Select label={mode==="transfer"?"Kaynak depo":"Depo"} value={warehouseId} setValue={setWarehouseId} options={warehouses}/>
        {mode==="transfer"&&<Select label="Hedef depo" value={targetWarehouseId} setValue={setTargetWarehouseId} options={warehouses.filter(x=>x.id!==warehouseId)}/>}
        <Select label="Malzeme" value={inventoryItemId} setValue={setInventoryItemId} options={items}/>
        <Select label="Proje / Şantiye" value={projectId} setValue={setProjectId} options={projects} optional/>
        <Field label="Miktar"><input type="number" min="0.01" step="0.01" value={quantity||""} onChange={e=>setQuantity(Number(e.target.value))} className="w-full rounded-lg border px-3 py-2"/></Field>
        <Field label="Referans / İrsaliye No"><input value={referenceNumber} onChange={e=>setReferenceNumber(e.target.value)} className="w-full rounded-lg border px-3 py-2"/></Field>
        <Field label="Hareket tarihi"><input type="date" value={movementDate} onChange={e=>setMovementDate(e.target.value)} className="w-full rounded-lg border px-3 py-2"/></Field>
      </div>
      <Field label="Açıklama"><textarea value={description} onChange={e=>setDescription(e.target.value)} rows={4} className="w-full rounded-lg border px-3 py-2"/></Field>
      <div className="flex justify-end gap-3 border-t pt-4"><Link href="/depo-stok/hareketler" className="rounded-lg border px-4 py-2 text-sm">İptal</Link><button disabled={saving} className="rounded-lg bg-slate-950 px-5 py-2 text-sm text-white">{saving?"Kaydediliyor...":"Kaydet"}</button></div>
    </form>
  </div>;
}
function Field({label,children}:{label:string;children:React.ReactNode}){return <label className="block space-y-2"><span className="text-sm font-medium">{label}</span>{children}</label>}
function Select({label,value,setValue,options,optional=false}:{label:string;value:string;setValue:(v:string)=>void;options:SelectOption[];optional?:boolean}){return <Field label={label}><select value={value} onChange={e=>setValue(e.target.value)} className="w-full rounded-lg border px-3 py-2"><option value="">{optional?"Seçmeden devam et":"Seçin"}</option>{options.map(x=><option key={x.id} value={x.id}>{x.code?`${x.code} - `:""}{x.name}</option>)}</select></Field>}
