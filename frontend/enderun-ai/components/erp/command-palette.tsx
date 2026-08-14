"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";

import { useDialogBehavior } from "@/components/ui/use-dialog-behavior";
import {
  pathOnly,
  searchMenu,
  type MenuGroup,
  type MenuSearchResult,
} from "@/lib/navigation/menu";

type CommandPaletteProps = {
  open: boolean;
  onClose: () => void;

  /**
   * KULLANICININ GÖREBİLDİĞİ menü. Palet kendi listesini kurmuyor,
   * kabuğun süzdüğü listeyi alıyor: ayrı süzülseydi, menüde gizlenen
   * bir sayfa palette çıkabilirdi.
   */
  groups: MenuGroup[];

  favoritePaths: string[];
  onToggleFavorite: (path: string) => void;
};

/**
 * KOMUT PALETİ (Ctrl+K / ⌘K) — 170'ten fazla sayfası olan bir
 * uygulamada gezinmenin en kısa yolu.
 *
 * NEDEN: kullanıcı "hakediş" sayfasına gitmek için önce doğru bölümü
 * hatırlayıp menüyü açmak zorunda kalıyordu. Palet, sayfanın adını
 * bilen kullanıcıyı iki tuşla oraya götürür.
 *
 * GÜVENLİK: palet YALNIZCA kabuğun süzdüğü menüden besleniyor; yeni
 * bir görünürlük yolu açmıyor. Uçlar yine kendi yetkisini kontrol
 * ediyor.
 */
export default function CommandPalette({
  open,
  onClose,
  groups,
  favoritePaths,
  onToggleFavorite,
}: CommandPaletteProps) {
  const router = useRouter();
  const panelRef = useRef<HTMLDivElement>(null);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  useDialogBehavior({ open, panelRef, onRequestClose: onClose });

  // Her açılışta temiz başlar: önceki aramanın sonuçlarıyla açılmak,
  // kullanıcıya kaldığı yerden devam ettiği izlenimi verirdi.
  useEffect(() => {
    if (open) {
      setQuery("");
      setHighlighted(0);
    }
  }, [open]);

  const results = useMemo(
    () => searchMenu(query, groups),
    [query, groups],
  );

  useEffect(() => {
    setHighlighted(0);
  }, [query]);

  if (!open) return null;

  function go(result: MenuSearchResult) {
    onClose();
    router.push(result.item.href);
  }

  function onKeyDown(event: React.KeyboardEvent) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setHighlighted((index) => Math.min(index + 1, results.length - 1));
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlighted((index) => Math.max(index - 1, 0));
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();

      const result = results[highlighted];
      if (result) go(result);
    }
  }

  return (
    <div
      className="erp-palette-backdrop"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target) onClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label="Komut paleti"
        tabIndex={-1}
        className="erp-palette"
        data-testid="command-palette"
      >
        <div className="erp-palette-search">
          <span aria-hidden="true">⌕</span>
          <input
            autoFocus
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={onKeyDown}
            placeholder="Sayfa ara — örn. hakediş, kasa, personel"
            aria-label="Sayfa ara"
            aria-controls="erp-palette-results"
          />
          <kbd>esc</kbd>
        </div>

        <ul
          id="erp-palette-results"
          className="erp-palette-results"
          role="listbox"
          aria-label="Arama sonuçları"
        >
          {results.length === 0 && (
            <li className="erp-palette-empty">
              Eşleşen sayfa yok. Erişebildiğiniz sayfalar arasında arıyoruz.
            </li>
          )}

          {results.map((result, index) => {
            const href = pathOnly(result.item.href);
            const favorite = favoritePaths.includes(href);

            return (
              <li key={`${result.group.key}-${result.item.href}`}>
                <div
                  className={`erp-palette-row ${
                    index === highlighted ? "highlighted" : ""
                  }`}
                >
                  <button
                    type="button"
                    role="option"
                    aria-selected={index === highlighted}
                    className="erp-palette-go"
                    onMouseEnter={() => setHighlighted(index)}
                    onClick={() => go(result)}
                  >
                    <span className="erp-palette-icon" aria-hidden="true">
                      {result.item.icon ?? "○"}
                    </span>
                    <span className="erp-palette-label">
                      {result.item.label}
                    </span>
                    <span className="erp-palette-group">
                      {result.group.label}
                    </span>
                  </button>

                  {/*
                    Favoriye alma paletin İÇİNDE: kullanıcı zaten aradığı
                    sayfayı bulmuşken, ikinci kez bulmak zorunda kalmadan
                    kısayola çevirebilmeli.
                  */}
                  <button
                    type="button"
                    className={`erp-palette-star ${favorite ? "on" : ""}`}
                    onClick={() => onToggleFavorite(href)}
                    aria-label={
                      favorite
                        ? `${result.item.label} favorilerden çıkar`
                        : `${result.item.label} favorilere ekle`
                    }
                    aria-pressed={favorite}
                  >
                    {favorite ? "★" : "☆"}
                  </button>
                </div>
              </li>
            );
          })}
        </ul>

        <div className="erp-palette-footer">
          <span>
            <kbd>↑</kbd>
            <kbd>↓</kbd> gezin
          </span>
          <span>
            <kbd>enter</kbd> aç
          </span>
          <span>
            <kbd>ctrl</kbd>+<kbd>k</kbd> her yerde açar
          </span>
        </div>
      </div>
    </div>
  );
}
