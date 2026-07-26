import Link from "next/link";

export default function WorkTaskDashboardWidget() {
  return (
    <section className="rounded-2xl border bg-white p-5 shadow-sm">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold">Görev Yönetimi</h2>
          <p className="mt-1 text-sm text-slate-500">
            Görevlerinizi görüntüleyin ve yönetin.
          </p>
        </div>

        <Link
          href="/gorevler"
          className="rounded-lg border px-4 py-2 text-sm font-medium"
        >
          Görevlere Git
        </Link>
      </div>
    </section>
  );
}
