"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { inventoryMovementService, type InventoryMovement } from "@/services/inventory-movement.service";

export default function Page(){
  const [items,setItems]=useState<InventoryMovement[]>([]);
  const [error,setError]=useState("");
  const [loading,setLoading]=useState(true);
  useEffect(()=>{void inventoryMovementService.getMovements().then(setItems).catch(e=>setError(e instanceof Error?e.message:"Hareketler yüklenemedi.")).finally(()=>setLoading(false));},[]);
  const label=(t:number)=>({0:"Giriş",1:"Çıkış",2:"Transfer çıkış",3:"Transfer giriş"}[t]??`Hareket ${t}`);
  return <div className="space-y-6 p-6">
    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
      <div><Link href="/depo-stok" className="text-sm font-medium text-slate-600">← Depo & Stok</Link><h1 className="mt-2 text-2xl font-semibold">Stok hareketleri</h1></div>
      <div className="flex flex-wrap gap-2"><Link href="/depo-stok/giris" className="rounded-lg bg-slate-950 px-4 py-2 text-sm text-white">Depo girişi</Link><Link href="/depo-stok/cikis" className="rounded-lg bg-slate-950 px-4 py-2 text-sm text-white">Depo çıkışı</Link><Link href="/depo-stok/transfer" className="rounded-lg bg-slate-950 px-4 py-2 text-sm text-white">Transfer</Link></div>
    </div>
    {error&&<div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>}
    <div className="overflow-x-auto rounded-xl border bg-white shadow-sm"><table className="min-w-full divide-y text-sm"><thead className="bg-slate-50"><tr><th className="px-4 py-3 text-left">Tarih</th><th className="px-4 py-3 text-left">Hareket</th><th className="px-4 py-3 text-left">Malzeme</th><th className="px-4 py-3 text-left">Depo</th><th className="px-4 py-3 text-left">Proje</th><th className="px-4 py-3 text-right">Miktar</th><th className="px-4 py-3 text-left">Referans</th></tr></thead><tbody className="divide-y">{loading?<tr><td colSpan={7} className="px-4 py-10 text-center">Yükleniyor...</td></tr>:items.length===0?<tr><td colSpan={7} className="px-4 py-10 text-center">Kayıt bulunamadı.</td></tr>:items.map(x=><tr key={x.id}><td className="px-4 py-3">{new Date(x.movementDate).toLocaleDateString("tr-TR")}</td><td className="px-4 py-3">{label(x.type)}</td><td className="px-4 py-3"><div className="font-medium">{x.itemName}</div><div className="text-xs text-slate-500">{x.itemCode}</div></td><td className="px-4 py-3">{x.warehouseName}</td><td className="px-4 py-3">{x.projectName||"—"}</td><td className="px-4 py-3 text-right">{x.quantity}</td><td className="px-4 py-3">{x.referenceNumber}</td></tr>)}</tbody></table></div>
  </div>
}
