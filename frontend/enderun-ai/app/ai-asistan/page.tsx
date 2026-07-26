"use client";

import { FormEvent, useMemo, useState } from "react";
import { AppShell } from "../../components/app-shell";

type Message = { id: number; role: "assistant" | "user"; text: string };
type ApiResponse = { reply: string; assistantName: string; createdAtUtc: string };

const summaries = [
  ["Kritik Konular", "3", "Bugün yönetici dikkati gerekiyor", "⚠️"],
  ["Bekleyen Görevler", "7", "Onay veya işlem bekliyor", "⏳"],
  ["Yeni Evraklar", "4", "Henüz yönlendirilmemiş kayıt", "📥"],
  ["Aktif Projeler", "8", "Proje ve şantiye takibi", "🏗️"],
];

const commands = [
  "Bugün neye odaklanmalıyım?",
  "MKE projesinin durumunu göster",
  "Bekleyen satın almaları listele",
  "Bugünkü ödemeleri ve tahsilatları özetle",
  "Son gelen evrakları analiz et",
];

export default function AiPage() {
  const [command, setCommand] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [messages, setMessages] = useState<Message[]>([
    {
      id: 1,
      role: "assistant",
      text: "Merhaba Mehmet Bey. Ben Hızır. Kararlarınıza güç katmak için hazırım.",
    },
  ]);

  const today = useMemo(
    () => new Intl.DateTimeFormat("tr-TR", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date()),
    [],
  );

  async function send(text: string) {
    const clean = text.trim();
    if (!clean || busy) return;

    const history = messages.slice(-12).map((item) => ({
      role: item.role,
      content: item.text,
    }));

    setMessages((current) => [...current, { id: Date.now(), role: "user", text: clean }]);
    setCommand("");
    setError("");
    setBusy(true);

    try {
      const response = await fetch("/api/backend/hizir/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: clean, history }),
      });

      const data = (await response.json().catch(() => null)) as ApiResponse | { message?: string } | null;
      if (response.status === 401) {
        window.location.href = "/login";
        return;
      }
      if (!response.ok) throw new Error((data as { message?: string } | null)?.message ?? `Hata ${response.status}`);

      setMessages((current) => [
        ...current,
        { id: Date.now() + 1, role: "assistant", text: (data as ApiResponse).reply },
      ]);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Hızır servisine ulaşılamadı.");
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void send(command);
  }

  return (
    <AppShell active="AI Asistan">
      <section className="overflow-hidden rounded-3xl border border-cyan-400/15 bg-gradient-to-br from-cyan-400/10 via-slate-950/70 to-indigo-500/10 p-6 shadow-2xl shadow-cyan-950/20 md:p-9">
        <div className="flex flex-col gap-7 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs font-semibold text-cyan-200">
              <span className={`h-2 w-2 rounded-full ${busy ? "bg-amber-400" : "bg-emerald-400"}`} />
              {busy ? "Hızır düşünüyor" : "Hızır aktif"}
            </div>
            <p className="mt-5 text-sm capitalize text-slate-400">{today}</p>
            <h1 className="mt-2 text-3xl font-black tracking-tight text-white md:text-5xl">Merhaba Mehmet Bey.</h1>
            <p className="mt-4 text-lg leading-8 text-slate-300">
              Ben <strong className="text-cyan-300">Hızır</strong>. Bugün şirketinizde dikkatinizi gerektiren konuları tek merkezde topluyorum.
            </p>
            <p className="mt-3 text-sm font-medium text-cyan-200/80">Kararlarınıza güç katan dijital asistan.</p>
          </div>
          <div className="rounded-2xl border border-white/10 bg-slate-950/50 px-5 py-4 text-sm text-slate-300 backdrop-blur">
            <p className="font-semibold text-white">Güvenli çalışma</p>
            <p className="mt-1 max-w-sm leading-6 text-slate-400">Hızır, şirket verisi olmadan kesin sonuç üretmez ve kritik işlemleri onaysız uygulamaz.</p>
          </div>
        </div>
      </section>

      <section className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {summaries.map(([title, value, detail, icon]) => (
          <article key={title} className="rounded-2xl border border-white/10 bg-white/[0.04] p-5 transition hover:border-cyan-400/25 hover:bg-white/[0.06]">
            <div className="flex items-start justify-between"><div><p className="text-sm text-slate-400">{title}</p><p className="mt-2 text-3xl font-black text-white">{value}</p></div><span className="text-2xl">{icon}</span></div>
            <p className="mt-3 text-xs leading-5 text-slate-500">{detail}</p>
          </article>
        ))}
      </section>

      <section className="mt-6 grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="rounded-3xl border border-white/10 bg-white/[0.04] p-4 md:p-6">
          <div className="border-b border-white/10 pb-4"><h2 className="text-xl font-bold text-white">Hızır ile Çalış</h2><p className="mt-1 text-sm text-slate-400">Bir soru sorun veya yapılmasını istediğiniz işi yazın.</p></div>
          <div className="mt-5 min-h-[390px] space-y-4 rounded-2xl border border-white/10 bg-slate-950/55 p-4 md:p-5">
            {messages.map((message) => (
              <div key={message.id} className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}>
                <div className={`max-w-[88%] whitespace-pre-wrap rounded-2xl px-4 py-3 text-sm leading-6 md:max-w-[78%] ${message.role === "user" ? "bg-cyan-500 text-slate-950" : "border border-white/10 bg-white/[0.06] text-slate-300"}`}>
                  {message.role === "assistant" && <p className="mb-1 text-xs font-bold uppercase tracking-wider text-cyan-300">Hızır</p>}
                  {message.text}
                </div>
              </div>
            ))}
            {busy && <div className="text-sm text-slate-500">Hızır yanıt hazırlıyor…</div>}
          </div>
          {error && <div className="mt-4 rounded-xl border border-red-400/20 bg-red-400/10 px-4 py-3 text-sm text-red-200">{error}</div>}
          <form onSubmit={submit} className="mt-4 flex flex-col gap-3 md:flex-row">
            <input value={command} onChange={(e) => setCommand(e.target.value)} className="min-w-0 flex-1 rounded-2xl border border-white/10 bg-slate-950/70 px-5 py-4 text-sm text-white outline-none focus:border-cyan-400/50" placeholder="Hızır'a bir görev yazın..." disabled={busy} />
            <button type="submit" className="rounded-2xl bg-cyan-400 px-6 py-4 text-sm font-black text-slate-950 hover:bg-cyan-300 disabled:opacity-50" disabled={!command.trim() || busy}>{busy ? "Bekleyin" : "Gönder"}</button>
          </form>
        </div>

        <aside className="rounded-3xl border border-white/10 bg-white/[0.04] p-5">
          <h2 className="font-bold text-white">Hızlı Komutlar</h2>
          <p className="mt-1 text-xs leading-5 text-slate-500">Bir komuta dokunarak Hızır'a gönderin.</p>
          <div className="mt-4 space-y-2">
            {commands.map((item) => <button key={item} type="button" onClick={() => void send(item)} disabled={busy} className="w-full rounded-xl border border-white/10 bg-slate-950/45 p-3 text-left text-sm leading-5 text-slate-300 hover:border-cyan-400/30 hover:bg-cyan-400/5 disabled:opacity-50">{item}</button>)}
          </div>
        </aside>
      </section>
    </AppShell>
  );
}
