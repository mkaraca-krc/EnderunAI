"use client";

import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";

import { matchesSearch } from "@/lib/search/fold";

export type SearchableOption = {
  id: string;
  /** Kod — arama bunun içinde de geçiyor ("CARI-014"). */
  code?: string | null;
  title: string;
  /** Kısa ad, vergi no gibi ek arama alanları. */
  extra?: (string | null | undefined)[];
};

/**
 * ARANABİLİR SEÇİCİ — uzun listeler için.
 *
 * NEDEN: canlıda 150 cari var ve düz `<select>` ile seçiliyorlardı.
 * Tarayıcının kendi tuş davranışı yalnız İLK HARFE atlar; "Yılmaz
 * İnşaat" aramak için listeyi kaydırmak gerekiyordu. Kullanıcı üç harf
 * yazıp bulabilmeli.
 *
 * TÜRKÇE KATLAMA MEVCUT KAYNAKTAN (`lib/search/fold.ts`): "sube"
 * yazan "Şube"yi bulur. Burada ikinci bir arama mantığı yazılsaydı
 * liste ekranıyla seçici zamanla farklı sonuç verirdi.
 *
 * KLAVYE: yaz → süzülür, ↑/↓ → gezinir, Enter → seçer, Esc → listeyi
 * kapatır (diyaloğu DEĞİL, bkz. data-dialog-escape), Tab → seçili
 * kalanı koruyup çıkar.
 *
 * SEÇİM SESSİZCE KAYBOLMAZ: kullanıcı eşleşmeyen bir metin yazıp
 * alandan çıkarsa önceki seçim geri yazılır. Boşaltmak isteyen
 * "Temizle"ye basar — yazım hatası yüzünden cari kaybolmaz.
 */
export function SearchableSelect({
  label,
  value,
  onChange,
  options,
  placeholder = "Yazarak arayın…",
  emptyLabel = "Seçin",
  required = false,
  disabled = false,
  id,
  error,
}: {
  label?: string;
  /** Seçili kaydın kimliği; boşsa seçim yok. */
  value: string;
  onChange: (id: string) => void;
  options: SearchableOption[];
  placeholder?: string;
  emptyLabel?: string;
  required?: boolean;
  disabled?: boolean;
  id?: string;
  error?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  // Geri çağrı REF'TE: çağıran taraflar satır içi ok fonksiyonu
  // yazıyor. Effect bağımlılığına konsaydı her tuşta effect yeniden
  // kurulur ve odak kaçardı (bkz. use-dialog-behavior'daki aynı hata).
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);

  const listId = useId();
  const inputId = id ?? listId;

  const selected = options.find((option) => option.id === value);

  const labelOf = (option: SearchableOption) =>
    option.code ? `${option.code} — ${option.title}` : option.title;

  const filtered = useMemo(() => {
    if (!open || query.trim() === "") return options;

    return options.filter((option) =>
      matchesSearch(query, option.code, option.title, ...(option.extra ?? []))
    );
  }, [open, query, options]);

  // Kapalıyken kutuda SEÇİLİ kaydın adı yazıyor; açılınca kullanıcının
  // yazdığı süzgeç. İki durum tek alanda çakışmasın diye ayrı tutuluyor.
  const text = open ? query : selected ? labelOf(selected) : "";

  useEffect(() => {
    if (!open) return;

    // Vurgulanan satır görünürde kalsın: klavyeyle gezinen kullanıcı
    // listenin dışına çıkan bir satırı seçmeye çalışırdı.
    //
    // `scrollIntoView` HER ORTAMDA YOK (jsdom uygulamıyor). Varlığı
    // kontrol ediliyor: kaydırma bir KOLAYLIK, olmadığında seçicinin
    // tamamen çökmesi kabul edilemez.
    const row = listRef.current?.querySelector<HTMLElement>(
      '[data-highlighted="true"]'
    );

    row?.scrollIntoView?.({ block: "nearest" });
  }, [open, highlight]);

  function commit(option: SearchableOption | undefined) {
    if (!option) return;

    onChangeRef.current(option.id);
    setQuery("");
    setOpen(false);
  }

  function close() {
    setQuery("");
    setOpen(false);
  }

  return (
    <div
      className="relative w-full"
      // Liste açıkken Esc'i BU kontrol sahipleniyor: diyalog içindeyken
      // Esc önce listeyi kapatmalı, formu değil.
      data-dialog-escape={open ? "hold" : undefined}
    >
      {label && (
        <label
          htmlFor={inputId}
          className="mb-1.5 block text-sm font-medium text-slate-700"
        >
          {label}
        </label>
      )}

      <input
        ref={inputRef}
        id={inputId}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={
          open && filtered[highlight] ? `${listId}-${highlight}` : undefined
        }
        autoComplete="off"
        disabled={disabled}
        required={required && !value}
        placeholder={selected ? undefined : placeholder}
        value={text}
        onChange={(event) => {
          setQuery(event.target.value);
          setHighlight(0);
          setOpen(true);
        }}
        onFocus={() => {
          setOpen(true);
          setQuery("");
          setHighlight(0);
        }}
        onBlur={() => {
          // Eşleşmeyen metin yazıp çıkan kullanıcının SEÇİMİ KORUNUR.
          // Sessizce boşaltmak, formu kaydeden kişiye carisiz bir kayıt
          // yazdırırdı.
          close();
        }}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();

            if (!open) {
              setOpen(true);
              return;
            }

            setHighlight((current) => {
              const next = event.key === "ArrowDown" ? current + 1 : current - 1;
              if (filtered.length === 0) return 0;

              // Uçlarda başa/sona sarıyor: uzun listede kullanıcı
              // sondaki kaydı aramak için baştan aşağı inmesin.
              return (next + filtered.length) % filtered.length;
            });

            return;
          }

          if (event.key === "Enter") {
            if (!open) return;

            event.preventDefault();
            commit(filtered[highlight]);
            return;
          }

          if (event.key === "Escape" && open) {
            // Diyalog içindeyken Esc'in formu kapatmasını engelliyoruz;
            // kural `data-dialog-escape` ile sözleşmeye bağlı.
            event.stopPropagation();
            close();
          }
        }}
        className={[
          "h-10 w-full rounded-lg border bg-white px-3 text-sm text-slate-900",
          "outline-none transition placeholder:text-slate-400 focus:ring-2",
          error
            ? "border-red-400 focus:border-red-500 focus:ring-red-100"
            : "border-slate-300 focus:border-brand-500 focus:ring-brand-100",
        ].join(" ")}
      />

      {/* Seçimi temizlemenin AÇIK yolu: yazım hatasıyla kaybolmaması
          için blur temizlemiyor, kullanıcı açıkça basıyor. */}
      {value && !required && !disabled && (
        <button
          type="button"
          aria-label="Seçimi temizle"
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => {
            onChangeRef.current("");
            close();
          }}
          className="absolute right-2 top-[34px] rounded px-1 text-slate-400 hover:text-slate-600"
        >
          ✕
        </button>
      )}

      {open && (
        <ul
          ref={listRef}
          id={listId}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg"
        >
          {!required && (
            <li
              role="option"
              aria-selected={value === ""}
              onMouseDown={(event) => {
                event.preventDefault();
                onChangeRef.current("");
                close();
              }}
              className="cursor-pointer px-3 py-2 text-sm text-slate-500 hover:bg-slate-50"
            >
              {emptyLabel}
            </li>
          )}

          {filtered.length === 0 ? (
            <li className="px-3 py-2 text-sm text-slate-500">
              Eşleşen kayıt yok.
            </li>
          ) : (
            filtered.map((option, index) => (
              <li
                key={option.id}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={option.id === value}
                data-highlighted={index === highlight}
                onMouseEnter={() => setHighlight(index)}
                // mousedown'da preventDefault: blur tetiklenip liste
                // kapanmadan seçim yapılabilsin.
                onMouseDown={(event) => {
                  event.preventDefault();
                  commit(option);
                }}
                className={[
                  "cursor-pointer px-3 py-2 text-sm",
                  index === highlight ? "bg-brand-50" : "",
                  option.id === value ? "font-semibold text-slate-900" : "text-slate-700",
                ].join(" ")}
              >
                {labelOf(option)}
              </li>
            ))
          )}
        </ul>
      )}

      {error && <p className="mt-1.5 text-sm text-red-600">{error}</p>}
    </div>
  );
}
