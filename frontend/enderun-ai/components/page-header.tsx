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
    <header className="flex flex-col gap-5 border-b border-slate-200 pb-6 md:flex-row md:items-center md:justify-between">
      <div>
        {eyebrow && <p className="text-sm text-slate-500">{eyebrow}</p>}
        <h2 className="mt-1 text-3xl font-bold text-slate-900">{title}</h2>
        <p className="mt-2 text-sm text-slate-500">{description}</p>
      </div>
      <div className="flex gap-3">
        <button className="rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 hover:bg-slate-50">
          Bildirimler
        </button>
        <button className="rounded-xl bg-cyan-700 px-4 py-3 text-sm font-semibold text-white hover:bg-cyan-800">
          Yeni İşlem
        </button>
      </div>
    </header>
  );
}
