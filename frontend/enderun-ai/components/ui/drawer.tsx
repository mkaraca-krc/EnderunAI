"use client";

import { useCallback, useId, useRef, useState, type ReactNode } from "react";

import { useDialogBehavior } from "./use-dialog-behavior";

type DrawerSize = "md" | "lg" | "xl";

interface DrawerProps {
  open: boolean;
  title: string;
  /** Başlığın altındaki açıklama; ekran okuyucuya da bağlanır. */
  description?: string;
  onClose: () => void;
  children: ReactNode;
  /** Alt eylem çubuğu — genelde Vazgeç + birincil eylem. */
  footer?: ReactNode;
  size?: DrawerSize;

  /**
   * İşlem sürüyorsa Esc ve zemin tıklaması kapatmaz: yarım kalan bir
   * kayıt kullanıcıya "kapandı, demek ki olmadı" dedirtirdi.
   */
  busy?: boolean;

  /**
   * İçeride KAYDEDİLMEMİŞ değişiklik var mı. True ise kapatma isteği
   * doğrudan kapatmaz, önce onay sorar — uzun bir formu yanlışlıkla
   * Esc'e basarak kaybetmek, kullanıcının yeniden yazması demektir.
   */
  dirty?: boolean;
}

const WIDTHS: Record<DrawerSize, string> = {
  md: "max-w-md",
  lg: "max-w-xl",
  xl: "max-w-3xl",
};

/**
 * SAĞDAN KAYAN PANEL — oluştur / düzenle / zengin detay için.
 *
 * NEDEN MODAL DEĞİL: modal ortada belirip arkadaki bağlamı örtüyor;
 * oysa kullanıcı bir listeden kayıt açtığında listede KALDIĞINI
 * hissetmeli. Panel yandan kayar, liste görünür kalır, kaydedince
 * panel kapanır ve liste tazelenir.
 *
 * MODAL İLE AYNI AİLEDEN: Esc, odak tuzağı, ilk alana odak, kapanışta
 * odağın geri dönmesi ve arka planın kilitlenmesi aynı kancadan
 * (useDialogBehavior) geliyor — ayrı bir sistem değil, aynı davranışın
 * farklı yerleşimi.
 *
 * KULLANIM KURALI (paket kararı): kısa onay/uyarı → Modal,
 * geri alınamaz işlem + gerekçe → ConfirmDialog, çok alanlı
 * oluştur/düzenle ve zengin detay → Drawer.
 */
export function Drawer({
  open,
  title,
  description,
  onClose,
  children,
  footer,
  size = "lg",
  busy = false,
  dirty = false,
}: DrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  const [confirmingDiscard, setConfirmingDiscard] = useState(false);

  const requestClose = useCallback(() => {
    if (busy) return;

    // KAYDEDİLMEMİŞ DEĞİŞİKLİK KORUMASI: kapatmadan önce sorulur.
    // Sormadan kapatmak, kullanıcının doldurduğu formu sessizce yok
    // etmek olurdu.
    if (dirty) {
      setConfirmingDiscard(true);
      return;
    }

    onClose();
  }, [busy, dirty, onClose]);

  useDialogBehavior({
    open,
    panelRef,
    onRequestClose: requestClose,

    // İlk odak İÇERİĞE: başlıktaki kapat düğmesi DOM'da formdan önce
    // geliyor; odak oraya düşseydi kullanıcı yazmaya başlamak için bir
    // Tab atmak zorunda kalırdı.
    initialFocusScopeRef: contentRef,
  });

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end bg-slate-950/45 backdrop-blur-[2px]"
      onMouseDown={(event) => {
        // Yalnız zemine basınca kapanır; panel içinde başlayan bir
        // sürükleme (metin seçimi) paneli kapatmamalı.
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
        data-testid="drawer-panel"
        className={`flex h-full w-full ${WIDTHS[size]} flex-col border-l border-slate-200 bg-white shadow-xl outline-none motion-safe:animate-[drawer-in_180ms_ease-out]`}
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-4">
          <div>
            <h2 id={titleId} className="text-lg font-semibold text-slate-900">
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
            aria-label="Paneli kapat"
            className="rounded-lg p-1 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"
          >
            ✕
          </button>
        </header>

        <div ref={contentRef} className="flex-1 overflow-y-auto px-6 py-5">
          {children}
        </div>

        {footer && (
          <footer className="border-t border-slate-200 px-6 py-4">
            {footer}
          </footer>
        )}

        {/*
          Kaydedilmemiş değişiklik onayı PANELİN İÇİNDE duruyor: ikinci
          bir katman açmak, kullanıcıyı iki üst üste pencereyle baş başa
          bırakırdı. Odak zaten panelin içinde olduğu için tuzak da
          bozulmuyor.
        */}
        {confirmingDiscard && (
          <div
            role="alertdialog"
            aria-label="Kaydedilmemiş değişiklikler"
            className="absolute inset-0 flex items-center justify-center bg-white/90 p-6"
          >
            <div className="w-full max-w-sm rounded-xl border border-slate-200 bg-white p-5 shadow-lg">
              <h3 className="text-base font-semibold text-slate-900">
                Kaydedilmemiş değişiklikler var
              </h3>

              <p className="mt-2 text-sm text-slate-600">
                Paneli kapatırsanız girdikleriniz kaybolur.
              </p>

              <div className="mt-5 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setConfirmingDiscard(false)}
                  className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                >
                  Düzenlemeye dön
                </button>

                <button
                  type="button"
                  onClick={() => {
                    setConfirmingDiscard(false);
                    onClose();
                  }}
                  className="inline-flex h-9 items-center rounded-lg bg-red-600 px-4 text-sm font-medium text-white transition hover:bg-red-500"
                >
                  Değişiklikleri at
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
