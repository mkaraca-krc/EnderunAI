"use client";

import { useEffect, type RefObject } from "react";

/** Odak tuzağının döneceği elemanlar. */
export const FOCUSABLE = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

type DialogBehaviorOptions = {
  open: boolean;
  panelRef: RefObject<HTMLElement | null>;

  /**
   * Esc ya da zemin tıklaması. Kapatmayı ENGELLEMEK isteyen çağıran
   * (işlem sürüyor, kaydedilmemiş değişiklik var) bunu kendi
   * içinde karara bağlar.
   */
  onRequestClose: () => void;

  /**
   * Açılışta odağın gideceği bölge. Verilmezse panelin tamamındaki ilk
   * odaklanabilir eleman seçilir.
   *
   * NEDEN GEREKLİ: drawer'da başlıktaki KAPAT düğmesi DOM sırasında
   * formdan önce geliyor. Odak oraya giderse kullanıcı paneli açar
   * açmaz yazmaya başlayamaz — bir Tab atmak zorunda kalır. İçerik
   * bölgesi verildiğinde odak doğrudan ilk form alanına düşer.
   */
  initialFocusScopeRef?: RefObject<HTMLElement | null>;
};

/**
 * AÇILIR KATMANLARIN ORTAK DAVRANIŞI — modal ve drawer aynı aileden.
 *
 * Esc ile kapanma, odak tuzağı, açılışta ilk alana odak, kapanışta
 * odağın çağıran düğmeye dönmesi, arkadaki sayfanın kaydırılmaması.
 *
 * NEDEN TEK KANCA: davranış iki bileşende ayrı ayrı yazılsaydı biri
 * düzeltilip diğeri unutulurdu — bugün modalda çalışan odak tuzağı
 * yarın drawer'da çalışmazdı ve klavye kullanıcısı panelin dışına
 * düşerdi. Görsel fark (ortada beliren kutu / sağdan kayan panel)
 * bileşenlerin kendi işi; davranış ortak.
 */
export function useDialogBehavior({
  open,
  panelRef,
  onRequestClose,
  initialFocusScopeRef,
}: DialogBehaviorOptions) {
  useEffect(() => {
    if (!open) return;

    const restore = document.activeElement as HTMLElement | null;
    const { overflow } = document.body.style;

    document.body.style.overflow = "hidden";

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.stopPropagation();
        onRequestClose();
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
      // Olmadan Tab kullanıcıyı arkadaki listeye düşürür ve katman
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

    // İlk odaklanabilir alana odak: kullanıcı panel açılır açılmaz
    // yazmaya başlayabilmeli. setTimeout, panelin DOM'a yerleşmesini
    // bekliyor.
    const timer = window.setTimeout(() => {
      const panel = panelRef.current;
      if (!panel) return;

      const scope = initialFocusScopeRef?.current ?? panel;

      const focusable =
        scope.querySelector<HTMLElement>(FOCUSABLE) ??
        panel.querySelector<HTMLElement>(FOCUSABLE);

      (focusable ?? panel).focus();
    }, 0);

    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      document.body.style.overflow = overflow;
      window.clearTimeout(timer);
      restore?.focus?.();
    };
  }, [open, onRequestClose, panelRef, initialFocusScopeRef]);
}
