"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

type LoginResponse = {
  token?: string;
  expiresInSeconds?: number;
  user?: {
    id: string;
    username: string;
    fullName: string;
    email: string | null;
    roles: string[];
  };
  message?: string;
};

export default function LoginPage() {
  const router = useRouter();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);

  async function handleSubmit(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();
    setMessage("");
    setLoading(true);

    try {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          username,
          password,
        }),
      });

      const data =
        (await response.json()) as LoginResponse;

      if (!response.ok) {
        setMessage(
          data.message ??
            "Kullanıcı adı veya şifre hatalı."
        );
        return;
      }

      router.push("/dashboard");
      router.refresh();
    } catch {
      setMessage(
        "Sunucuya ulaşılamadı. Lütfen tekrar deneyin."
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="min-h-screen bg-slate-950 text-white">
      <div className="grid min-h-screen lg:grid-cols-[1.1fr_0.9fr]">
        <section className="hidden border-r border-white/10 bg-slate-950 p-12 lg:flex lg:flex-col lg:justify-between">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.35em] text-cyan-400">
              Enderun AI
            </p>

            <h1 className="mt-5 max-w-xl text-5xl font-bold leading-tight text-white">
              Enderun Enerji Yönetim Sistemi
            </h1>

            <p className="mt-5 max-w-xl text-base leading-7 text-slate-400">
              Hakediş, finans, satın alma, depo,
              personel ve proje yönetimini tek
              merkezden yönetin.
            </p>
          </div>

          <div className="rounded-2xl border border-cyan-400/20 bg-cyan-400/5 p-5">
            <p className="text-sm font-semibold text-cyan-300">
              Güvenli kurumsal erişim
            </p>

            <p className="mt-2 text-sm leading-6 text-slate-400">
              Oturumlar güvenli erişim anahtarıyla
              korunur ve 12 saat sonunda otomatik
              olarak sonlandırılır.
            </p>
          </div>
        </section>

        <section className="flex min-h-[650px] items-center p-7 md:p-12">
          <div className="mx-auto w-full max-w-md">
            <p className="text-xs font-bold uppercase tracking-[0.34em] text-cyan-400 lg:hidden">
              Enderun AI
            </p>

            <h2 className="mt-3 text-3xl font-bold">
              Yönetim Merkezine Giriş
            </h2>

            <p className="mt-3 text-sm leading-6 text-slate-400">
              Devam etmek için kurumsal hesabınızla
              giriş yapın.
            </p>

            <form
              className="mt-9 space-y-5"
              onSubmit={handleSubmit}
            >
              <label className="block">
                <span className="text-sm text-slate-300">
                  Kullanıcı adı
                </span>

                <input
                  autoComplete="username"
                  className="mt-2 w-full rounded-xl border border-white/10 bg-white/[0.04] px-4 py-3.5 text-white outline-none transition focus:border-cyan-400/60 focus:ring-2 focus:ring-cyan-400/10"
                  value={username}
                  onChange={(event) =>
                    setUsername(
                      event.target.value
                    )
                  }
                  required
                />
              </label>

              <label className="block">
                <span className="text-sm text-slate-300">
                  Şifre
                </span>

                <input
                  type="password"
                  autoComplete="current-password"
                  className="mt-2 w-full rounded-xl border border-white/10 bg-white/[0.04] px-4 py-3.5 text-white outline-none transition focus:border-cyan-400/60 focus:ring-2 focus:ring-cyan-400/10"
                  value={password}
                  onChange={(event) =>
                    setPassword(
                      event.target.value
                    )
                  }
                  required
                />
              </label>

              {message && (
                <p className="rounded-xl border border-rose-400/20 bg-rose-400/10 p-3 text-sm text-rose-300">
                  {message}
                </p>
              )}

              <button
                type="submit"
                disabled={loading}
                className="w-full rounded-xl bg-cyan-500 py-3.5 text-sm font-bold text-slate-950 transition hover:bg-cyan-400 disabled:cursor-not-allowed disabled:opacity-60"
              >
                {loading
                  ? "Giriş yapılıyor..."
                  : "Giriş Yap"}
              </button>
            </form>

            <p className="mt-8 text-center text-xs text-slate-500">
              Enderun Enerji · Yetkisiz erişim
              yasaktır.
            </p>
          </div>
        </section>
      </div>
    </main>
  );
}
