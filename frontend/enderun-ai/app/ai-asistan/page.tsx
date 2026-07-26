"use client";

import { FormEvent, useMemo, useState } from "react";
import { AppShell } from "../../components/app-shell";

type Message = {
  id: number;
  role: "hizir" | "user";
  text: string;
};

const summaryCards = [
  {
    title: "Kritik Konular",
    value: "3",
    detail: "Bugün yönetici dikkati gerekiyor",
    icon: "⚠️",
  },
  {
    title: "Bekleyen Görevler",
    value: "7",
    detail: "Onay veya işlem bekliyor",
    icon: "⏳",
  },
  {
    title: "Yeni Evraklar",
    value: "4",
    detail: "Henüz yönlendirilmemiş kayıt",
    icon: "📥",
  },
  {
    title: "Aktif Projeler",
    value: "8",
    detail: "Proje ve şantiye takibi",
    icon: "🏗️",
  },
];

const quickCommands = [
  "Bugün neye odaklanmalıyım?",
  "MKE projesinin durumunu göster",
  "Bekleyen satın almaları listele",
  "Bugünkü ödemeleri ve tahsilatları özetle",
  "Son gelen evrakları analiz et",
  "Geciken görevleri sorumlularına göre göster",
];

const moduleCards = [
  { title: "Finans", detail: "Nakit, ödeme ve tahsilat özeti", icon: "💰" },
  { title: "Satın Alma", detail: "Talep, teklif ve sipariş takibi", icon: "📦" },
  { title: "Projeler", detail: "Şantiye, ilerleme ve risk görünümü", icon: "🏗️" },
  { title: "Personel", detail: "İzin, belge ve görevlendirmeler", icon: "👥" },
  { title: "Evrak", detail: "Gelen, giden ve onay bekleyenler", icon: "📑" },
  { title: "Hakediş", detail: "Dönem, kesinti ve tahsilat kontrolü", icon: "📊" },
];

export default function AiPage() {
  const [command, setCommand] = useState("");
  const [messages, setMessages] = useState<Message[]>([
    {
      id: 1,
      role: "hizir",
      text: "Merhaba Mehmet Bey. Ben Hızır. Kararlarınıza güç katmak ve Enderun AI içindeki işlerinizi tek merkezden takip etmenize yardımcı olmak için hazırım.",
    },
  ]);

  const todayLabel = useMemo(
    () =>
      new Intl.DateTimeFormat("tr-TR", {
        weekday: "long",
        day: "numeric",
        month: "long",
        year: "numeric",
      }).format(new Date()),
    [],
  );

  function sendCommand(text: string) {
    const clean = text.trim();
    if (!clean) return;

    const time = Date.now();
    setMessages((current) => [
      ...current,
      { id: time, role: "user", text: clean },
      {
        id: time + 1,
        role: "hizir",
        text: "Komutunuzu aldım. Bu ilk arayüz sürümünde ekran akışını hazırlıyorum. Bir sonraki aşamada şirket verilerine ve uzman modüllere bağlanarak doğrulanmış sonuçları burada sunacağım.",
      },
    ]);
    setCommand("");
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    sendCommand(command);
  }

  return (
    <AppShell active="AI Asistan">
      <section className="overflow-hidden rounded-3xl border border-cyan-400/15 bg-gradient-to-br from-cyan-400/10 via-slate-950/70 to-indigo-500/10 p-6 shadow-2xl shadow-cyan-950/20 md:p-9">
        <div className="flex flex-col gap-7 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs font-semibold text-cyan-200">
              <span className="h-2 w-2 rounded-full bg-emerald-400" />
              Hızır aktif
            </div>
            <p className="mt-5 text-sm capitalize text-slate-400">{todayLabel}</p>
            <h1 className="mt-2 text-3xl font-black tracking-tight text-white md:text-5xl">
              Merhaba Mehmet Bey.
            </h1>
            <p className="mt-4 text-lg leading-8 text-slate-300">
              Ben <strong className="text-cyan-300">Hızır</strong>. Bugün şirketinizde
              dikkatinizi gerektiren konuları tek merkezde topladım.
            </p>
            <p className="mt-3 text-sm font-medium text-cyan-200/80">
              Kararlarınıza güç katan dijital asistan.
            </p>
          </div>

          <div className="rounded-2xl border border-white/10 bg-slate-950/50 px-5 py-4 text-sm text-slate-300 backdrop-blur">
            <p className="font-semibold text-white">Bugünkü öncelik</p>
            <p className="mt-1 max-w-sm leading-6 text-slate-400">
              Kritik evraklar, yaklaşan ödemeler ve geciken proje görevleri birlikte
              değerlendirilmelidir.
            </p>
          </div>
        </div>
      </section>

      <section className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {summaryCards.map((card) => (
          <article
            key={card.title}
            className="rounded-2xl border border-white/10 bg-white/[0.04] p-5 transition hover:-translate-y-0.5 hover:border-cyan-400/25 hover:bg-white/[0.06]"
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-sm text-slate-400">{card.title}</p>
                <p className="mt-2 text-3xl font-black text-white">{card.value}</p>
              </div>
              <span className="text-2xl" aria-hidden="true">
                {card.icon}
              </span>
            </div>
            <p className="mt-3 text-xs leading-5 text-slate-500">{card.detail}</p>
          </article>
        ))}
      </section>

      <section className="mt-6 grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="rounded-3xl border border-white/10 bg-white/[0.04] p-4 md:p-6">
          <div className="flex items-center justify-between gap-4 border-b border-white/10 pb-4">
            <div>
              <h2 className="text-xl font-bold text-white">Hızır ile Çalış</h2>
              <p className="mt-1 text-sm text-slate-400">
                Bir soru sorun veya yapılmasını istediğiniz işi yazın.
              </p>
            </div>
            <span className="rounded-full bg-emerald-400/10 px-3 py-1 text-xs font-semibold text-emerald-300">
              Hazır
            </span>
          </div>

          <div className="mt-5 min-h-[360px] space-y-4 rounded-2xl border border-white/10 bg-slate-950/55 p-4 md:p-5">
            {messages.map((message) => (
              <div
                key={message.id}
                className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}
              >
                <div
                  className={`max-w-[88%] rounded-2xl px-4 py-3 text-sm leading-6 md:max-w-[75%] ${
                    message.role === "user"
                      ? "bg-cyan-500 text-slate-950"
                      : "border border-white/10 bg-white/[0.06] text-slate-300"
                  }`}
                >
                  {message.role === "hizir" && (
                    <p className="mb-1 text-xs font-bold uppercase tracking-wider text-cyan-300">
                      Hızır
                    </p>
                  )}
                  {message.text}
                </div>
              </div>
            ))}
          </div>

          <form onSubmit={submit} className="mt-4 flex flex-col gap-3 md:flex-row">
            <input
              value={command}
              onChange={(event) => setCommand(event.target.value)}
              className="min-w-0 flex-1 rounded-2xl border border-white/10 bg-slate-950/70 px-5 py-4 text-sm text-white outline-none transition placeholder:text-slate-500 focus:border-cyan-400/50 focus:ring-4 focus:ring-cyan-400/5"
              placeholder="Hızır'a bir görev yazın..."
              aria-label="Hızır'a komut yazın"
            />
            <button
              type="submit"
              className="rounded-2xl bg-cyan-400 px-6 py-4 text-sm font-black text-slate-950 transition hover:bg-cyan-300 disabled:cursor-not-allowed disabled:opacity-50"
              disabled={!command.trim()}
            >
              Gönder
            </button>
          </form>
        </div>

        <aside className="space-y-6">
          <div className="rounded-3xl border border-white/10 bg-white/[0.04] p-5">
            <h2 className="font-bold text-white">Hızlı Komutlar</h2>
            <p className="mt-1 text-xs leading-5 text-slate-500">
              Bir komuta dokunarak Hızır'a gönderin.
            </p>
            <div className="mt-4 space-y-2">
              {quickCommands.map((item) => (
                <button
                  key={item}
                  type="button"
                  onClick={() => sendCommand(item)}
                  className="w-full rounded-xl border border-white/10 bg-slate-950/45 p-3 text-left text-sm leading-5 text-slate-300 transition hover:border-cyan-400/30 hover:bg-cyan-400/5 hover:text-white"
                >
                  {item}
                </button>
              ))}
            </div>
          </div>

          <div className="rounded-3xl border border-amber-400/15 bg-amber-400/[0.05] p-5">
            <p className="text-xs font-bold uppercase tracking-wider text-amber-300">
              Güvenli çalışma ilkesi
            </p>
            <p className="mt-3 text-sm leading-6 text-slate-400">
              Hızır, şirket verisine bağlanmadan tahmini bilgiyi kesin sonuç olarak
              sunmayacak. Uygulanacak işlemler kullanıcı onayından sonra başlayacak.
            </p>
          </div>
        </aside>
      </section>

      <section className="mt-6">
        <div className="mb-4 flex items-end justify-between gap-4">
          <div>
            <h2 className="text-xl font-bold text-white">Uzmanlık Alanları</h2>
            <p className="mt-1 text-sm text-slate-400">
              Hızır gerektiğinde ilgili uzman modülü devreye alacak.
            </p>
          </div>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {moduleCards.map((module) => (
            <article
              key={module.title}
              className="flex items-center gap-4 rounded-2xl border border-white/10 bg-white/[0.035] p-5"
            >
              <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-white/[0.06] text-xl">
                {module.icon}
              </span>
              <div>
                <h3 className="font-bold text-white">{module.title}</h3>
                <p className="mt-1 text-xs leading-5 text-slate-500">{module.detail}</p>
              </div>
            </article>
          ))}
        </div>
      </section>
    </AppShell>
  );
}
