import Link from "next/link";
import type { ReactNode } from "react";
import { LogoutButton } from "./logout-button";

const navigation = [
  { name: "Dashboard", href: "/" },
  { name: "Finans", href: "/finans" },
  { name: "Projeler", href: "/projeler" },
  { name: "Hakediş", href: "/hakedis" },
  { name: "Satın Alma", href: "/satin-alma" },
  { name: "Depo ve Stok", href: "/depo" },
  { name: "Personel", href: "/personel" },
  { name: "Dokümanlar", href: "/dokumanlar" },
  { name: "AI Asistan", href: "/ai-asistan" },
];

export function AppShell({
  active,
  children,
}: {
  active: string;
  children: ReactNode;
}) {
  return (
    <main className="min-h-screen text-white">
      <div className="flex min-h-screen">
        <aside className="hidden w-72 shrink-0 border-r border-white/10 bg-slate-950/65 p-6 backdrop-blur-xl lg:block">
          <Link href="/" className="block">
            <p className="text-xs font-bold tracking-[0.34em] text-cyan-400">
              ENDERUN AI
            </p>
            <h1 className="mt-2 text-2xl font-bold">Yönetim Merkezi</h1>
          </Link>

          <nav className="mt-10 space-y-2 text-sm">
            {navigation.map((item) => (
              <Link
                key={item.name}
                href={item.href}
                className={`block rounded-xl px-4 py-3 transition ${
                  active === item.name
                    ? "bg-cyan-500/15 text-cyan-300 ring-1 ring-cyan-400/15"
                    : "text-slate-300 hover:bg-white/5 hover:text-white"
                }`}
              >
                {item.name}
              </Link>
            ))}
          </nav>

          <div className="mt-10 rounded-2xl border border-cyan-400/20 bg-cyan-400/5 p-4">
            <p className="text-xs text-cyan-300">Sistem Durumu</p>
            <p className="mt-2 font-semibold text-emerald-400">
              Tüm servisler aktif
            </p>
            <p className="mt-1 text-xs leading-5 text-slate-400">
              Frontend, API ve veritabanı çalışıyor.
            </p>
          </div>

          <LogoutButton />
        </aside>

        <section className="min-w-0 flex-1 p-5 md:p-8">
          <div className="mx-auto max-w-[1550px]">{children}</div>
        </section>
      </div>
    </main>
  );
}
