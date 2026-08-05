import Link from "next/link";

export default function UnauthorizedPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-100 p-6">
      <section className="w-full max-w-lg rounded-2xl border border-slate-200 bg-white p-8 text-center shadow-sm">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-amber-100 text-2xl text-amber-700">
          !
        </div>
        <h1 className="mt-5 text-2xl font-semibold text-slate-950">
          Bu sayfa için yetkiniz yok
        </h1>
        <p className="mt-3 text-sm leading-6 text-slate-600">
          Görev rolünüz bu modüle erişim vermiyor. Yetki değişikliği yapıldıysa
          çıkış yapıp yeniden giriş yapın.
        </p>
        <Link
          href="/dashboard"
          className="mt-6 inline-flex h-10 items-center justify-center rounded-lg bg-brand-700 px-5 text-sm font-medium text-white transition hover:bg-brand-600"
        >
          Dashboard&apos;a dön
        </Link>
      </section>
    </main>
  );
}
