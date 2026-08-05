"use client";

import { useMemo, useRef, useState } from "react";

export type SearchSelectOption = {
  value: string;
  /** Ana satır: kod veya isim. */
  label: string;
  /** İkinci satır: açıklama, birim, bakiye gibi ayırt edici bilgi. */
  hint?: string;
  /** Arama bunun üzerinden de yapılır (kategori, marka, hesap adı). */
  keywords?: string;
};

type Props = {
  options: SearchSelectOption[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  emptyMessage?: string;
  disabled?: boolean;
  /** Listenin üstünde tek tıkla seçilen kısayollar (sık kullanılanlar). */
  quickPicks?: SearchSelectOption[];
  quickPickLabel?: string;
  /** Listede yoksa yeni kayıt açma kısayolu. */
  onCreate?: (query: string) => void;
  createLabel?: string;
};

/** Aynı anda gösterilen en fazla seçenek — uzun listede tarayıcı boğulmasın. */
const MAX_VISIBLE = 40;

/**
 * Arama ile seçim. Serbest metin DEĞİLDİR: kullanıcı yazarak arar ama
 * yalnızca listeden seçebilir; seçmediği sürece değer boş kalır.
 *
 * Stok kartı ve hesap planı binlerce satır olabildiği için düz
 * <select> kullanılamıyor: kullanıcı 3.000 satırlık açılır listede
 * "NYAF kablo"yu bulamaz.
 */
export default function ErpSearchSelect({
  options,
  value,
  onChange,
  placeholder = "Aramak için yazın",
  emptyMessage = "Eşleşen kayıt yok.",
  disabled = false,
  quickPicks,
  quickPickLabel = "Sık kullanılanlar",
  onCreate,
  createLabel = "Yeni kayıt oluştur",
}: Props) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const blurTimer = useRef<number | null>(null);

  const selected = useMemo(
    () => options.find((option) => option.value === value) ?? null,
    [options, value]
  );

  const filtered = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase("tr-TR");

    if (needle.length === 0) return options.slice(0, MAX_VISIBLE);

    return options
      .filter((option) =>
        `${option.label} ${option.hint ?? ""} ${option.keywords ?? ""}`
          .toLocaleLowerCase("tr-TR")
          .includes(needle)
      )
      .slice(0, MAX_VISIBLE);
  }, [options, query]);

  function choose(option: SearchSelectOption) {
    onChange(option.value);
    setQuery("");
    setOpen(false);
  }

  return (
    <div className="erp-search-select">
      <input
        type="text"
        disabled={disabled}
        value={open ? query : selected?.label ?? ""}
        placeholder={selected ? selected.label : placeholder}
        onChange={(event) => {
          setQuery(event.target.value);
          setOpen(true);
        }}
        onFocus={() => {
          setQuery("");
          setOpen(true);
        }}
        onBlur={() => {
          // Listeye tıklamak input'u blur ediyor; kapatma bir tık
          // geciktirilmezse seçim hiç gerçekleşmez.
          blurTimer.current = window.setTimeout(() => setOpen(false), 150);
        }}
      />

      {selected && !open && (
        <button
          type="button"
          className="erp-search-select-clear"
          onClick={() => onChange("")}
          aria-label="Seçimi temizle"
        >
          ×
        </button>
      )}

      {open && (
        <div
          className="erp-search-select-menu"
          onMouseDown={() => {
            if (blurTimer.current) window.clearTimeout(blurTimer.current);
          }}
        >
          {quickPicks && quickPicks.length > 0 && query.trim().length === 0 && (
            <div className="erp-search-select-quick">
              <span>{quickPickLabel}</span>
              <div>
                {quickPicks.map((option) => (
                  <button
                    key={`quick-${option.value}`}
                    type="button"
                    onClick={() => choose(option)}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>
          )}

          {filtered.length === 0 && (
            <p className="erp-search-select-empty">{emptyMessage}</p>
          )}

          {filtered.map((option) => (
            <button
              key={option.value}
              type="button"
              className={
                option.value === value
                  ? "erp-search-select-option selected"
                  : "erp-search-select-option"
              }
              onClick={() => choose(option)}
            >
              <strong>{option.label}</strong>
              {option.hint && <small>{option.hint}</small>}
            </button>
          ))}

          {onCreate && (
            <button
              type="button"
              className="erp-search-select-create"
              onClick={() => {
                const typed = query.trim();
                setOpen(false);
                onCreate(typed);
              }}
            >
              + {createLabel}
              {query.trim().length > 0 ? `: "${query.trim()}"` : ""}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
