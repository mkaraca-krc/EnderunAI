"use client";

import {
  useCallback,
  useEffect,
  useId,
  useRef,
  type ReactNode,
} from "react";

type ModalSize = "sm" | "md" | "lg";

interface ModalProps {
  open: boolean;
  title: string;
  /** Başlığın altındaki açıklama; ekran okuyucuya da bağlanır. */
  description?: string;
  onClose: () => void;
  children: ReactNode;
  /** Alt eylem çubuğu — genelde Vazgeç + birincil eylem. */
  footer?: ReactNode;
  size?: ModalSize;
  /**
   * İşlem sürüyorsa Esc ve arka plan tıklaması kapatmaz: yarım kalan
   * bir kayıt kullanıcıya "kapandı, demek ki olmadı" dedirtirdi.
   */
  busy?: boolean;
}

const WIDTHS: Record<ModalSize, string> = {
  sm: "max-w-md",
  md: "max-w-2xl",
  lg: "max-w-4xl",
};

/** Odak tuzağının döneceği elemanlar. */
const FOCUSABLE = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

/**
 * Uygulama içi diyalog — TARAYICI PENCERESİ DEĞİL.
 *
 * Uygulamada bugüne kadar her ekran kendi overlay'ini yazdı ve
 * düzeltme/iptal akışları window.prompt ile yürüdü. prompt tarayıcının
 * penceresi: biçimlendirilemez, arka plandaki bağlamı gizler, gerekçe
 * alanını zorunlu tutamaz ve erişilebilir değildir.
 *
 * Bu bileşen APP-WIDE STANDART olarak kuruldu: Esc ile kapanır, odak
 * içeride döner (tuzak), açılırken odağı alır ve kapanınca odağı
 * çağıran düğmeye geri verir, arkadaki sayfa kaydırılmaz. Arkadaki
 * liste DOM'da kalır — kapanınca kullanıcı bıraktığı yerde bulur.
 */
export function Modal({
  open,
  title,
  description,
  onClose,
  children,
  footer,
  size = "md",
  busy = false,
}: ModalProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const restoreRef = useRef<HTMLElement | null>(null);
  const titleId = useId();
  const descriptionId = useId();

  const requestClose = useCallback(() => {
    if (!busy) onClose();
  }, [busy, onClose]);

  useEffect(() => {
    if (!open) return;

    restoreRef.current = document.activeElement as HTMLElement | null;

    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.stopPropagation();
        requestClose();
        return;
      }

      if (event.key !== "Tab") return;

      const panel = panelRef.current;
      if (!panel) return;

      const focusable = [...panel.querySelectorAll<HTMLElement>(FOCUSABLE)]
        .filter((element) => element.offsetParent !== null);

      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      // Odak tuzağı: son elemandan sonra başa, ilkinden geriye sona.
      // Olmadan Tab kullanıcıyı arkadaki listeye düşürür ve diyalog
      // klavyeyle kullanılamaz hale gelir.
      if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      } else if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown, true);

    const timer = window.setTimeout(() => {
      const panel = panelRef.current;
      if (!panel) return;

      const focusable = panel.querySelector<HTMLElement>(FOCUSABLE);

      (focusable ?? panel).focus();
    }, 0);

    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      document.body.style.overflow = overflow;
      window.clearTimeout(timer);
      restoreRef.current?.focus?.();
    };
  }, [open, requestClose]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-950/55 p-4 backdrop-blur-sm sm:items-center"
      onMouseDown={(event) => {
        // Yalnız zemine basınca kapanır; panel içinde başlayan bir
        // sürükleme (metin seçimi) diyaloğu kapatmamalı.
        if (event.currentTarget === event.target) requestClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        className={`w-full ${WIDTHS[size]} rounded-xl border border-slate-200 bg-white shadow-xl outline-none`}
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
          <div>
            <h2 id={titleId} className="text-lg font-bold text-slate-800">
              {title}
            </h2>

            {description && (
              <p id={descriptionId} className="mt-1 text-sm text-slate-500">
                {description}
              </p>
            )}
          </div>

          <button
            type="button"
            onClick={requestClose}
            disabled={busy}
            aria-label="Kapat"
            className="rounded-lg border border-slate-300 px-2 py-1 text-sm text-slate-600 disabled:opacity-50"
          >
            ✕
          </button>
        </header>

        <div className="px-5 py-4">{children}</div>

        {footer && (
          <footer className="flex flex-wrap justify-end gap-2 border-t border-slate-200 px-5 py-4">
            {footer}
          </footer>
        )}
      </div>
    </div>
  );
}
