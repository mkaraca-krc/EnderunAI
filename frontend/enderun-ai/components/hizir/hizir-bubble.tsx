"use client";

import { useState } from "react";
import { usePathname } from "next/navigation";

import HizirChat from "./hizir-chat";

/**
 * Her ERP sayfasının sağ alt köşesinde duran Hızır baloncuğu.
 * Kullanıcı hangi sayfada olursa olsun buradan yardım isteyebilir;
 * bulunduğu sayfa Hızır'a bağlam olarak gider.
 */
export default function HizirBubble() {
  const [open, setOpen] = useState(false);
  const pathname = usePathname() ?? "/";

  return (
    <>
      {open && (
        <div className="fixed bottom-24 right-5 z-50 flex w-[min(24rem,calc(100vw-2.5rem))] flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-2xl">
          <div className="mb-3 flex items-center justify-between">
            <div>
              <h3 className="font-bold text-slate-900">Hızır</h3>
              <p className="text-xs text-slate-500">Enderun AI asistanı</p>
            </div>
            <button
              type="button"
              onClick={() => setOpen(false)}
              aria-label="Hızır panelini kapat"
              className="rounded-lg px-2 py-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
            >
              ✕
            </button>
          </div>

          <div className="h-[26rem]">
            <HizirChat pagePath={pathname} variant="panel" />
          </div>
        </div>
      )}

      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-label={open ? "Hızır'ı kapat" : "Hızır'a sor"}
        className="fixed bottom-5 right-5 z-50 flex h-14 w-14 items-center justify-center rounded-full bg-cyan-700 text-lg font-black text-white shadow-xl transition hover:bg-cyan-800"
      >
        {open ? "✕" : "H"}
      </button>
    </>
  );
}
