export function PageHeader({
  title,
  description,
  eyebrow,
}: {
  title: string;
  description: string;
  eyebrow?: string;
}) {
  return (
    <header className="flex flex-col gap-5 border-b border-white/10 pb-6 md:flex-row md:items-center md:justify-between">
      <div>
        {eyebrow && <p className="text-sm text-slate-400">{eyebrow}</p>}
        <h2 className="mt-1 text-3xl font-bold">{title}</h2>
        <p className="mt-2 text-sm text-slate-400">{description}</p>
      </div>
      <div className="flex gap-3">
        <button className="rounded-xl border border-white/10 bg-white/5 px-4 py-3 text-sm hover:bg-white/10">
          Bildirimler
        </button>
        <button className="rounded-xl bg-cyan-500 px-4 py-3 text-sm font-semibold text-slate-950 hover:bg-cyan-400">
          Yeni İşlem
        </button>
      </div>
    </header>
  );
}
