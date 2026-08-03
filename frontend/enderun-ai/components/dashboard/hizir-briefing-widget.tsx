"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";

import { ApiError } from "@/lib/api/api-client";
import {
  hizirService,
  BriefingSeverity,
  type HizirBriefing,
} from "@/services/hizir.service";

const SEVERITY_STYLE: Record<number, string> = {
  [BriefingSeverity.Critical]: "border-red-200 bg-red-50 text-red-800",
  [BriefingSeverity.Warning]: "border-amber-200 bg-amber-50 text-amber-800",
  [BriefingSeverity.Info]: "border-slate-200 bg-white text-slate-700",
};

/**
 * "Hızır'ın Günlük Özeti" kartı. Maddeler sunucuda tamamen veriden
 * üretilir; kullanıcı yalnızca kendi yetkisindeki başlıkları görür.
 * Veri yoksa uydurma yapılmaz, "öne çıkan bir şey yok" denir.
 */
export default function HizirBriefingWidget() {
  const [briefing, setBriefing] = useState<HizirBriefing | null>(null);
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    hizirService
      .getBriefing()
      .then((result) => {
        if (!cancelled) setBriefing(result);
      })
      .catch(() => {
        // Yetkisi olmayan kullanıcıda kart hiç görünmesin.
        if (!cancelled) setBriefing(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const emailToSelf = useCallback(async () => {
    if (sending) return;

    setSending(true);
    setNotice("");
    setError("");

    try {
      const result = await hizirService.emailBriefing();
      setNotice(result.message);
    } catch (requestError) {
      setError(
        requestError instanceof ApiError || requestError instanceof Error
          ? requestError.message
          : "Özet gönderilemedi."
      );
    } finally {
      setSending(false);
    }
  }, [sending]);

  if (loading || !briefing) return null;

  return (
    <section className="mb-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs font-bold uppercase tracking-wide text-cyan-700">
            Hızır&apos;ın Günlük Özeti
          </p>
          <p className="mt-1 text-sm font-bold text-slate-900">
            {briefing.greeting}
          </p>
          <p className="mt-1 text-sm leading-6 text-slate-600">
            {briefing.headline}
          </p>
        </div>

        <button
          type="button"
          onClick={() => void emailToSelf()}
          disabled={sending}
          className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold text-slate-600 hover:border-cyan-300 hover:bg-cyan-50 disabled:opacity-60"
        >
          {sending ? "Gönderiliyor..." : "Bana e-postala"}
        </button>
      </div>

      {briefing.items.length > 0 && (
        <ul className="mt-4 space-y-2">
          {briefing.items.map((item, index) => {
            const style =
              SEVERITY_STYLE[item.severity] ??
              SEVERITY_STYLE[BriefingSeverity.Info];

            const content = (
              <>
                <p className="text-sm font-bold">{item.title}</p>
                {item.detail && (
                  <p className="mt-0.5 text-xs opacity-80">{item.detail}</p>
                )}
              </>
            );

            return (
              <li
                key={`${item.title}-${index}`}
                className={`rounded-xl border px-3 py-2.5 ${style}`}
              >
                {item.targetPath ? (
                  <Link href={item.targetPath} className="block hover:underline">
                    {content}
                  </Link>
                ) : (
                  content
                )}
              </li>
            );
          })}
        </ul>
      )}

      {notice && (
        <p className="mt-3 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
          {notice}
        </p>
      )}

      {error && (
        <p className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
          {error}
        </p>
      )}
    </section>
  );
}
