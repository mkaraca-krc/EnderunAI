"use client";
import { FormEvent,useCallback,useEffect,useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
type Company={id:string;code:string;name:string};type Account={id:string;companyName:string;code:string;title:string;roles:number;status:number;taxOffice?:string;taxNumber?:string;receivableAccountingAccountId?:string|null;payableAccountingAccountId?:string|null};
const roles=[[1,"Müşteri"],[2,"Tedarikçi"],[4,"Alt Yüklenici"],[8,"Resmî Kurum"],[16,"Banka"],[32,"Servis"],[64,"Kiralama"],[128,"Diğer"]] as const;const blank={companyId:"",code:"",title:"",shortName:"",roles:1,taxOffice:"",taxNumber:"",authorizedPerson:"",phone:"",email:"",address:"",paymentTerm:"",creditLimit:""};
async function api(path:string,options?:RequestInit){const r=await fetch(`/api/backend/${path}`,{cache:"no-store",...options});if(r.status===401){location.href="/login";throw new Error("Oturum süresi doldu.");}const j=await r.json().catch(()=>null);if(!r.ok)throw new Error(j?.message??`Hata ${r.status}`);return j;}
export default function Page(){const[companies,setCompanies]=useState<Company[]>([]),[items,setItems]=useState<Account[]>([]),[form,setForm]=useState(blank),[show,setShow]=useState(false),[syncing,setSyncing]=useState(false),[msg,setMsg]=useState(""),[err,setErr]=useState("");
const load=useCallback(async()=>{try{const[c,a]=await Promise.all([api("companies"),api("current-accounts")]);setCompanies(c);setItems(a);setForm(f=>({...f,companyId:f.companyId||c[0]?.id||""}));}catch(e){setErr(e instanceof Error?e.message:"Liste alınamadı.");}},[]);
useEffect(()=>{load();},[load]);function toggle(v:number){setForm(f=>({...f,roles:((f.roles&v)===v?f.roles&~v:f.roles|v)||1}));}
async function save(e:FormEvent){e.preventDefault();try{await api("current-accounts",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({...form,creditLimit:form.creditLimit?Number(form.creditLimit):null})});setMsg("Cari kart oluşturuldu.");setShow(false);setForm({...blank,companyId:form.companyId});await load();}catch(x){setErr(x instanceof Error?x.message:"Kayıt başarısız.");}}
async function act(id:string,a:"submit"|"approve"){try{setMsg("");setErr("");const j=await api(`current-accounts/${id}/${a}`,{method:"POST"});setMsg(j.message);await load();}catch(x){setErr(x instanceof Error?x.message:"İşlem başarısız.");}}

async function synchronizeAccounting(){
  const companyId=form.companyId||companies[0]?.id;

  if(!companyId){
    setErr("Muhasebe eşleştirmesi için şirket bulunamadı.");
    return;
  }

  const company=companies.find(x=>x.id===companyId);

  if(!window.confirm(`${company?.name??"Seçili şirket"} için cari muhasebe hesapları eşleştirilsin mi?`)){
    return;
  }

  try{
    setSyncing(true);
    setMsg("");
    setErr("");

    const j=await api(
      `current-accounts/synchronize-accounting?companyId=${encodeURIComponent(companyId)}`,
      {method:"POST"}
    );

    setMsg(j.message??"Muhasebe hesapları eşleştirildi.");
    await load();
  }catch(x){
    setErr(x instanceof Error?x.message:"Muhasebe eşleştirmesi başarısız.");
  }finally{
    setSyncing(false);
  }
}

return <ErpShell title="Cari Kartlar"><div className="erp-toolbar"><strong>{items.length} cari kart</strong><div className="erp-actions"><button type="button" disabled={syncing||!form.companyId} onClick={synchronizeAccounting}>{syncing?"Eşleştiriliyor...":"Muhasebe Hesaplarını Eşleştir"}</button><button type="button" onClick={()=>setShow(!show)}>+ Yeni Cari Kart</button></div></div>{msg&&<div className="erp-alert success">{msg}</div>}{err&&<div className="erp-alert error">{err}</div>}
{show&&<form className="erp-form-card" onSubmit={save}><div className="erp-form-grid"><label className="span-2"><span>Şirket *</span><select required value={form.companyId} onChange={e=>setForm({...form,companyId:e.target.value})}>{companies.map(c=><option key={c.id} value={c.id}>{c.code} — {c.name}</option>)}</select></label><label><span>Kod *</span><input required value={form.code} onChange={e=>setForm({...form,code:e.target.value.toUpperCase()})}/></label><label><span>Kısa Ad</span><input value={form.shortName} onChange={e=>setForm({...form,shortName:e.target.value})}/></label><label className="span-2"><span>Ünvan *</span><input required value={form.title} onChange={e=>setForm({...form,title:e.target.value})}/></label><div className="span-2 erp-role-grid">{roles.map(([v,l])=><label key={v}><input type="checkbox" checked={(form.roles&v)===v} onChange={()=>toggle(v)}/>{l}</label>)}</div><label><span>Vergi Dairesi</span><input value={form.taxOffice} onChange={e=>setForm({...form,taxOffice:e.target.value})}/></label><label><span>Vergi No</span><input value={form.taxNumber} onChange={e=>setForm({...form,taxNumber:e.target.value})}/></label></div><div className="erp-actions"><button type="submit">Taslak Kaydet</button></div></form>}
<div className="erp-table-card"><table className="erp-table"><thead><tr><th>Kod</th><th>Ünvan</th><th>Şirket</th><th>120 Alıcı</th><th>320 Satıcı</th><th>Durum</th><th>İşlem</th></tr></thead><tbody>{items.map(x=><tr key={x.id}><td>{x.code}</td><td>{x.title}</td><td>{x.companyName}</td><td><span className={x.receivableAccountingAccountId?"erp-status green":"erp-status gray"}>{x.receivableAccountingAccountId?"Bağlı":"Bağlı Değil"}</span></td><td><span className={x.payableAccountingAccountId?"erp-status green":"erp-status gray"}>{x.payableAccountingAccountId?"Bağlı":"Bağlı Değil"}</span></td><td><span className={x.status===2?"erp-status green":x.status===1?"erp-status yellow":"erp-status gray"}>{["Taslak","Onay Bekliyor","Onaylandı","Askıda","Pasif"][x.status]||"Bilinmiyor"}</span></td><td>{x.status===0?<button onClick={()=>act(x.id,"submit")}>Onaya Gönder</button>:x.status===1?<button onClick={()=>act(x.id,"approve")}>Onayla</button>:"✓ Kullanılabilir"}</td></tr>)}</tbody></table></div></ErpShell>}
