import Link from "next/link";
import { AppShell } from "../components/app-shell";
import { PageHeader } from "../components/page-header";

const modules = [
  { title: "Finans", href: "/finans", description: "Gelir, gider, banka, nakit akışı ve cari hesap yönetimi", value: "₺ 12,65 Mn", label: "Toplam tahsilat" },
  { title: "Projeler", href: "/projeler", description: "Şantiyeler, hakedişler, fiyat farkları ve iş programları", value: "9", label: "Aktif proje" },
  { title: "Satın Alma", href: "/satin-alma", description: "Malzeme talebi, teklif, sipariş ve onay süreçleri", value: "14", label: "Bekleyen talep" },
  { title: "Personel", href: "/personel", description: "Puantaj, avans, ekip dağılımı ve özlük işlemleri", value: "48", label: "Saha personeli" },
  { title: "Depo ve Stok", href: "/depo-stok", description: "Depolar, malzeme giriş-çıkışları ve kritik stoklar", value: "7", label: "Kritik stok" },
  { title: "AI Asistan", href: "/ai-asistan", description: "Belge analizi, raporlama ve yönetici karar desteği", value: "Aktif", label: "Yapay zekâ servisi" },
];

const projects = [
  { name: "KKB Veri Merkezi", progress: 72, status: "Devam ediyor" },
  { name: "MKE Projesi", progress: 61, status: "Devam ediyor" },
  { name: "Deprem Okulları", progress: 84, status: "Devam ediyor" },
  { name: "Natura Orman", progress: 35, status: "Planlama" },
];

export default function Home() {
  return (
    <AppShell active="Dashboard">
      <PageHeader
        title="Hoş geldiniz, Mehmet Bey"
        description="Enderun Enerji operasyonlarının güncel yönetim özeti"
        eyebrow="16 Temmuz 2026, Perşembe"
      />

      <section className="mt-8 rounded-3xl border border-cyan-400/20 bg-gradient-to-br from-cyan-500/20 via-blue-600/10 to-transparent p-7 md:p-10">
        <p className="text-sm font-semibold text-cyan-300">ENDERUN ENERJİ YÖNETİM ÖZETİ</p>
        <h3 className="mt-3 max-w-4xl text-3xl font-bold leading-tight md:text-5xl">
          Tüm şirket operasyonlarını tek merkezden yönetin.
        </h3>
        <p className="mt-5 max-w-2xl leading-7 text-slate-300">
          Finans, hakediş, satın alma, personel, stok ve proje süreçlerinizi gerçek
          zamanlı takip edin. Enderun AI, karar süreçlerinizi hızlandırır.
        </p>
      </section>

      <section className="mt-8 grid gap-5 md:grid-cols-2 xl:grid-cols-3">
        {modules.map((module) => (
          <article
            key={module.title}
            className="rounded-2xl border border-white/10 bg-white/[0.04] p-6 transition hover:-translate-y-1 hover:border-cyan-400/30 hover:bg-white/[0.07]"
          >
            <div className="flex items-start justify-between">
              <div className="h-11 w-11 rounded-2xl bg-cyan-400/15 ring-1 ring-cyan-400/20" />
              <span className="rounded-full bg-emerald-400/10 px-3 py-1 text-xs text-emerald-300">Aktif</span>
            </div>
            <h4 className="mt-6 text-xl font-bold">{module.title}</h4>
            <p className="mt-2 min-h-12 text-sm leading-6 text-slate-400">{module.description}</p>
            <div className="mt-6 border-t border-white/10 pt-5">
              <p className="text-2xl font-bold text-cyan-300">{module.value}</p>
              <p className="mt-1 text-xs text-slate-500">{module.label}</p>
            </div>
            <Link href={module.href} className="mt-5 inline-block text-sm font-semibold text-cyan-400 hover:text-cyan-300">
              Modülü aç →
            </Link>
          </article>
        ))}
      </section>

      <section className="mt-8 grid gap-6 xl:grid-cols-[1.5fr_1fr]">
        <div className="rounded-2xl border border-white/10 bg-white/[0.04] p-6">
          <h4 className="text-xl font-bold">Aktif Projeler</h4>
          <p className="mt-1 text-sm text-slate-400">Güncel ilerleme durumları</p>
          <div className="mt-6 space-y-6">
            {projects.map((project) => (
              <div key={project.name}>
                <div className="mb-2 flex items-center justify-between text-sm">
                  <span className="font-medium">{project.name}</span>
                  <span className="text-slate-400">%{project.progress}</span>
                </div>
                <div className="h-2 overflow-hidden rounded-full bg-white/10">
                  <div className="h-full rounded-full bg-cyan-400" style={{ width: `${project.progress}%` }} />
                </div>
                <p className="mt-2 text-xs text-slate-500">{project.status}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="rounded-2xl border border-white/10 bg-white/[0.04] p-6">
          <h4 className="text-xl font-bold">AI Yönetici Özeti</h4>
          <p className="mt-1 text-sm text-slate-400">Günlük karar destek raporu</p>
          <div className="mt-6 space-y-4">
            {[
              "KKB projesinde hakediş güncellemesi bekleniyor.",
              "3 satın alma talebi onay süresini aşmak üzere.",
              "Deprem Okulları projesi hedef programın önünde.",
              "7 malzemede kritik stok seviyesi tespit edildi.",
            ].map((item) => (
              <div key={item} className="rounded-xl border border-white/10 bg-slate-950/50 p-4 text-sm leading-6 text-slate-300">
                {item}
              </div>
            ))}
          </div>
          <Link href="/ai-asistan" className="mt-6 block rounded-xl bg-cyan-500 py-3 text-center text-sm font-bold text-slate-950 hover:bg-cyan-400">
            AI Asistanı Aç
          </Link>
        </div>
      </section>
    </AppShell>
  );
}
