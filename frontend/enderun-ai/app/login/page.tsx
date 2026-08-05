"use client";

import { FormEvent, useEffect, useState } from "react";
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
  outsideWorkHours?: boolean;
};

export default function LoginPage() {
  const router = useRouter();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);
  const [outsideWorkHours, setOutsideWorkHours] = useState(false);
  const [accessReason, setAccessReason] = useState("");
  const [accessRequestState, setAccessRequestState] = useState<
    "idle" | "submitting" | "submitted" | "error"
  >("idle");
  const [accessRequestMessage, setAccessRequestMessage] = useState("");

  useEffect(() => {
    document.title = "Enderun ERP - Giriş";
  }, []);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.get("reason") === "work-hours") {
      setMessage(
        "Mesai saatiniz sona erdiği için oturumunuz otomatik olarak kapatıldı."
      );
    }
  }, []);

  async function handleSubmitAccessRequest() {
    if (!accessReason.trim()) {
      setAccessRequestMessage("Gerekçe zorunludur.");
      setAccessRequestState("error");
      return;
    }

    setAccessRequestState("submitting");
    setAccessRequestMessage("");

    try {
      const response = await fetch("/api/auth/access-requests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          username,
          password,
          reason: accessReason.trim(),
        }),
      });

      const data = (await response.json()) as { message?: string };

      if (!response.ok) {
        setAccessRequestState("error");
        setAccessRequestMessage(
          data.message ?? "Erişim talebi gönderilemedi."
        );
        return;
      }

      setAccessRequestState("submitted");
      setAccessRequestMessage(
        data.message ?? "Erişim talebiniz gönderildi, onay bekleniyor."
      );
    } catch {
      setAccessRequestState("error");
      setAccessRequestMessage("Sunucuya ulaşılamadı. Lütfen tekrar deneyin.");
    }
  }

  async function handleSubmit(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();
    setMessage("");
    setOutsideWorkHours(false);
    setAccessRequestState("idle");
    setAccessRequestMessage("");
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
        if (data.outsideWorkHours) {
          setOutsideWorkHours(true);
        }
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
    <main className="min-h-screen bg-brand-950 text-white">
      <div className="grid min-h-screen lg:grid-cols-[1.1fr_0.9fr]">
        <section className="hidden border-r border-white/10 bg-brand-950 p-12 lg:flex lg:flex-col lg:justify-between">
          <div>
            <img
              src="/logo-full-white.png"
              alt="Enderun Enerji"
              className="h-24 w-auto"
            />

            <p className="mt-8 text-xs font-bold uppercase tracking-[0.35em] text-cyan-400">
              Yönetim Platformu
            </p>

            <h1 className="mt-4 max-w-xl text-5xl font-bold leading-tight text-white">
              Tüm operasyonu tek çatı altında yönetin.
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
            <img
              src="/logo-full-white.png"
              alt="Enderun Enerji"
              className="h-14 w-auto lg:hidden"
            />

            <p className="mt-6 text-xs font-bold uppercase tracking-[0.34em] text-cyan-400 lg:mt-0">
              Yönetim Platformu
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

              {outsideWorkHours && accessRequestState !== "submitted" && (
                <div className="space-y-3 rounded-xl border border-amber-400/20 bg-amber-400/5 p-4">
                  <p className="text-sm font-semibold text-amber-300">
                    Mesai dışı erişim talebi gönder
                  </p>
                  <p className="text-xs leading-5 text-slate-400">
                    Gerekçenizi yazın; Genel Müdür onayından sonra süreli
                    erişiminiz açılacaktır.
                  </p>
                  <textarea
                    className="w-full rounded-xl border border-white/10 bg-white/[0.04] px-3 py-2.5 text-sm text-white outline-none transition focus:border-amber-400/60 focus:ring-2 focus:ring-amber-400/10"
                    rows={3}
                    placeholder="Erişim talebinizin gerekçesi..."
                    value={accessReason}
                    onChange={(event) => setAccessReason(event.target.value)}
                  />
                  {accessRequestMessage && (
                    <p
                      className={`text-xs ${
                        accessRequestState === "error"
                          ? "text-rose-300"
                          : "text-amber-300"
                      }`}
                    >
                      {accessRequestMessage}
                    </p>
                  )}
                  <button
                    type="button"
                    disabled={accessRequestState === "submitting"}
                    onClick={handleSubmitAccessRequest}
                    className="w-full rounded-xl bg-amber-400 py-2.5 text-sm font-bold text-slate-950 transition hover:bg-amber-300 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {accessRequestState === "submitting"
                      ? "Gönderiliyor..."
                      : "Erişim Talebi Gönder"}
                  </button>
                </div>
              )}

              {accessRequestState === "submitted" && (
                <p className="rounded-xl border border-emerald-400/20 bg-emerald-400/10 p-3 text-sm text-emerald-300">
                  {accessRequestMessage}
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
