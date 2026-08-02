import { AppShell } from "../../components/app-shell";
import { PageHeader } from "../../components/page-header";

export default function AiPage() {
  return (
    <AppShell active="AI Asistan">
      <PageHeader title="AI Asistan" description="Yönetici karar desteği ve belge analizi" eyebrow="Enderun AI" />
      <section className="mt-8 grid gap-6 xl:grid-cols-[1fr_320px]">
        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <div className="min-h-[420px] rounded-2xl border border-slate-100 bg-slate-50 p-5">
            <p className="text-sm leading-7 text-slate-600">
              Merhaba Mehmet Bey. Hakediş, finans, proje ve satın alma verileri
              sisteme bağlandığında sorularınızı şirket verilerine göre yanıtlayacağım.
            </p>
          </div>
          <div className="mt-4 flex gap-3">
            <input
              className="min-w-0 flex-1 rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 outline-none focus:border-cyan-500"
              placeholder="Örn. KKB projesinin bu ayki tahmini kârı nedir?"
            />
            <button className="rounded-xl bg-cyan-700 px-5 py-3 text-sm font-bold text-white hover:bg-cyan-800">Gönder</button>
          </div>
        </div>
        <aside className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h3 className="font-bold text-slate-900">Hızlı Sorular</h3>
          <div className="mt-5 space-y-3 text-sm">
            {[
              "Bekleyen tahsilatları özetle",
              "Kritik stokları göster",
              "KKB hakedişini kontrol et",
              "Bu haftanın ödeme riskini analiz et",
            ].map((item) => (
              <button key={item} className="w-full rounded-xl border border-slate-200 bg-slate-50 p-3 text-left text-slate-600 hover:border-cyan-300 hover:bg-cyan-50">
                {item}
              </button>
            ))}
          </div>
        </aside>
      </section>
    </AppShell>
  );
}
