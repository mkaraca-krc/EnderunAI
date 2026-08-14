"use client";
import { FormEvent,useCallback,useEffect,useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
type Company={id:string;code:string;name:string;tradeName?:string;taxOffice?:string;taxNumber?:string;phone?:string;email?:string;isActive:boolean};
const blank={code:"",name:"",tradeName:"",taxOffice:"",taxNumber:"",phone:"",email:"",website:"",address:""};
async function api(path:string,options?:RequestInit){const r=await fetch(`/api/backend/${path}`,{cache:"no-store",...options});if(r.status===401){location.href="/login";throw new Error("Oturum süresi doldu.");}const j=await r.json().catch(()=>null);if(!r.ok)throw new Error(j?.message??`Hata ${r.status}`);return j;}
export default function Page(){
  const [items,setItems]=useState<Company[]>([]),[form,setForm]=useState(blank),[show,setShow]=useState(false),[msg,setMsg]=useState(""),[err,setErr]=useState("");
  const load=useCallback(async()=>{try{setItems(await api("companies"));}catch(e){setErr(e instanceof Error?e.message:"Liste alınamadı.");}},[]);
  useEffect(()=>{load();},[load]);
  async function save(e:FormEvent){e.preventDefault();setMsg("");setErr("");try{await api("companies",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(form)});setForm(blank);setShow(false);setMsg("Şirket oluşturuldu.");await load();}catch(x){setErr(x instanceof Error?x.message:"Kayıt başarısız.");}}
  return <ErpShell design="redwood" title="Şirket Yönetimi"><div className="erp-toolbar"><strong>{items.length} şirket</strong><div className="erp-actions"><button type="button" onClick={()=>void load()}>Yenile</button><button onClick={()=>setShow(!show)}>+ Yeni Şirket</button></div></div>{msg&&<div className="erp-alert success">{msg}</div>}{err&&<div className="erp-alert error">{err}</div>}
  {show&&<form className="erp-form-card" onSubmit={save}><div className="erp-form-grid">
    <label><span>Kod *</span><input required value={form.code} onChange={e=>setForm({...form,code:e.target.value.toUpperCase()})}/></label>
    <label><span>Ad *</span><input required value={form.name} onChange={e=>setForm({...form,name:e.target.value})}/></label>
    <label className="span-2"><span>Ticari Ünvan</span><input value={form.tradeName} onChange={e=>setForm({...form,tradeName:e.target.value})}/></label>
    <label><span>Vergi Dairesi</span><input value={form.taxOffice} onChange={e=>setForm({...form,taxOffice:e.target.value})}/></label>
    <label><span>Vergi No</span><input value={form.taxNumber} onChange={e=>setForm({...form,taxNumber:e.target.value})}/></label>
    <label><span>Telefon</span><input value={form.phone} onChange={e=>setForm({...form,phone:e.target.value})}/></label>
    <label><span>E-posta</span><input type="email" value={form.email} onChange={e=>setForm({...form,email:e.target.value})}/></label>
  </div><div className="erp-actions"><button type="submit">Kaydet</button></div></form>}
  <div className="erp-table-card"><table className="erp-table"><thead><tr><th>Kod</th><th>Şirket</th><th>Vergi</th><th>İletişim</th><th>Durum</th></tr></thead><tbody>{items.map(x=><tr key={x.id}><td>{x.code}</td><td><strong>{x.name}</strong><small>{x.tradeName||"—"}</small></td><td>{x.taxOffice||"—"}<small>{x.taxNumber||"—"}</small></td><td>{x.phone||"—"}<small>{x.email||"—"}</small></td><td><span className="erp-status green">{x.isActive?"Aktif":"Pasif"}</span></td></tr>)}</tbody></table></div></ErpShell>;
}
