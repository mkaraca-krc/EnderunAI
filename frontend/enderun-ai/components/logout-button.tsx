"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function LogoutButton() {
  const router = useRouter();
  const [loading, setLoading] = useState(false);

  async function logout() {
    setLoading(true);
    try {
      await fetch("/api/auth/logout", { method: "POST" });
      router.replace("/login");
      router.refresh();
    } finally {
      setLoading(false);
    }
  }

  return (
    <button
      onClick={logout}
      disabled={loading}
      className="mt-4 w-full rounded-xl border border-white/10 bg-white/5 px-4 py-3 text-left text-sm text-slate-300 hover:bg-white/10 hover:text-white disabled:opacity-60"
    >
      {loading ? "Çıkış yapılıyor..." : "Güvenli Çıkış"}
    </button>
  );
}
