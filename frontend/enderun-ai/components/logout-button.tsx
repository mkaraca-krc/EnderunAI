"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

type LogoutButtonProps = {
  variant?: "default" | "erp";
};

export function LogoutButton({
  variant = "default",
}: LogoutButtonProps) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  async function logout() {
    setLoading(true);
    setFailed(false);

    try {
      const response = await fetch("/api/auth/logout", {
        method: "POST",
        cache: "no-store",
        credentials: "same-origin",
      });

      if (!response.ok) {
        throw new Error("Oturum kapatılamadı.");
      }

      localStorage.removeItem("token");
      localStorage.removeItem("enderun_token");
      sessionStorage.removeItem("enderun-ai-sidebar-scroll");

      router.replace("/login");
      router.refresh();
    } catch {
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }

  const label = loading
    ? "Çıkış yapılıyor..."
    : failed
      ? "Tekrar deneyin"
      : variant === "erp"
        ? "Çıkış Yap"
        : "Güvenli Çıkış";

  return (
    <button
      type="button"
      onClick={logout}
      disabled={loading}
      className={
        variant === "erp"
          ? `erp-logout-button${failed ? " error" : ""}`
          : "mt-4 w-full rounded-xl border border-white/10 bg-white/5 px-4 py-3 text-left text-sm text-slate-300 hover:bg-white/10 hover:text-white disabled:opacity-60"
      }
      aria-label="Çıkış Yap"
      title={failed ? "Çıkış başarısız. Tekrar deneyin." : "Çıkış Yap"}
    >
      {variant === "erp" && (
        <span className="erp-logout-icon" aria-hidden="true">
          ⇥
        </span>
      )}
      <span className={variant === "erp" ? "erp-logout-label" : undefined}>
        {label}
      </span>
    </button>
  );
}
