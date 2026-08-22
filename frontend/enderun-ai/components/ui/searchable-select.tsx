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
  /**
   * İkinci satır: birim, bakiye, hesap adı gibi AYIRT EDİCİ bilgi.
   * Aynı isimde iki kart olduğunda seçimi bu satır belirliyor.
   */
  hint?: string | null;
  /** Kısa ad, vergi no, marka gibi ek arama alanları. */
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
  quickPicks,
  quickPickLabel = "Sık kullanılanlar",
  onCreate,
  createLabel = "Yeni kayıt oluştur",
  maxVisible = 50,
  loadOptions,
  minQueryLength = 2,
  debounceMs = 300,
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
  /** Listenin üstünde tek tıkla seçilen kısayollar (son kullanılanlar). */
  quickPicks?: SearchableOption[];
  quickPickLabel?: string;
  /** Listede yoksa yeni kayıt açma kısayolu. */
  onCreate?: (query: string) => void;
  createLabel?: string;
  /**
   * Aynı anda çizilen en fazla satır — binlerce satırlık stok kartı
   * listesinde tarayıcı boğulmasın. KESİLEN SATIR SAYISI YAZILIYOR:
   * sessizce kırpmak, kullanıcıya "kaydım yok" dedirtir.
   */
  maxVisible?: number;
  /**
   * SUNUCU KİPİ. Verilirse liste `options`tan değil, yazdıkça bu
   * işlevden geliyor.
   *
   * NE ZAMAN: kayıt sayısı birkaç yüzü geçtiğinde. Hesap planı canlıda
   * 1.114 satır (~168 KB) ve her ekran açılışında tamamı iniyordu.
   * Cari 150, stok 9, personel 81 — onlar istemcide kalıyor; eşik 500
   * (bkz. DURUM.md).
   *
   * Aynı bileşen iki kipi de taşıyor ki eşik aşıldığında geçiş tek
   * satır olsun; ikinci bir bileşen yazmak, iki arama davranışı
   * demekti.
   */
  loadOptions?: (
    query: string,
    signal: AbortSignal
  ) => Promise<{ options: SearchableOption[]; total: number }>;
  /** Sunucuya sormadan önce en az kaç karakter (varsayılan 2). */
  minQueryLength?: number;
  /** Yazma durduktan sonra kaç ms beklenip sorulacağı (varsayılan 300). */
  debounceMs?: number;
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

  /*
   * LİSTE SABİT KONUMLU — MUTLAK DEĞİL.
   *
   * ÖLÇÜLDÜ: bu seçiciler tablo hücrelerinde de kullanılıyor ve
   * `.rw .erp-table-wrap` kuralı `overflow: auto` taşıyor. Mutlak
   * konumlu bir liste o kutu tarafından KIRPILIR — kullanıcı yazar,
   * hiçbir şey görmez. (Eski `ErpSearchSelect` fatura kalem tablosunda
   * tam olarak bunu yaşıyordu.)
   *
   * Sabit konum kırpılmadan çıkıyor; bedeli, kutunun konumunu açılışta
   * ve kaydırma/boyutlanma sırasında yeniden ölçmek.
   */
  const [rect, setRect] = useState<{
    left: number;
    top: number;
    width: number;
  } | null>(null);

  const listId = useId();
  const inputId = id ?? listId;

  const selected = options.find((option) => option.id === value);

  const labelOf = (option: SearchableOption) =>
    option.code ? `${option.code} — ${option.title}` : option.title;

  /*
   * SUNUCU KİPİ — YARIŞ KORUMASI ŞART.
   *
   * Hızlı yazan kullanıcıda istekler sırayla dönmez: "150" için açılan
   * istek, "1500" için açılandan SONRA dönebilir ve eski sonuç yenisini
   * ezer. Kullanıcı ekranda gördüğü listeden seçer — yani YANLIŞ
   * hesabı seçer ve bunu fark etmez. Sessiz ve pahalı.
   *
   * Koruma AbortController ile: yeni istek açılırken önceki İPTAL
   * ediliyor, iptal edilen isteğin yanıtı hiç işlenmiyor. Sıra numarası
   * taşımaktan daha kesin — geç dönen yanıt ağa bile çıkmıyor.
   */
  const [remote, setRemote] = useState<{
    options: SearchableOption[];
    total: number;
  } | null>(null);

  const [loading, setLoading] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!loadOptions || !open) return;

    const trimmed = query.trim();

    if (trimmed.length < minQueryLength) {
      abortRef.current?.abort();
      setRemote(null);
      setLoading(false);
      return;
    }

    const timer = window.setTimeout(() => {
      abortRef.current?.abort();

      const controller = new AbortController();
      abortRef.current = controller;

      setLoading(true);

      loadOptions(trimmed, controller.signal)
        .then((result) => {
          if (controller.signal.aborted) return;

          setRemote(result);
          setHighlight(0);
        })
        .catch(() => {
          // İptal edilen istek hata gibi görünmemeli; başka bir hata
          // olduğunda da liste boş kalır ve "eşleşen kayıt yok" yazar.
          if (!controller.signal.aborted) setRemote(null);
        })
        .finally(() => {
          if (!controller.signal.aborted) setLoading(false);
        });
    }, debounceMs);

    return () => window.clearTimeout(timer);
  }, [loadOptions, open, query, minQueryLength, debounceMs]);

  // Kapanınca uçuşan istek bırakılmıyor.
  useEffect(() => {
    if (!open) {
      abortRef.current?.abort();
      setRemote(null);
      setLoading(false);
    }
  }, [open]);

  const filtered = useMemo(() => {
    if (loadOptions) return remote?.options ?? [];
    if (!open || query.trim() === "") return options;

    return options.filter((option) =>
      matchesSearch(
        query,
        option.code,
        option.title,
        option.hint,
        ...(option.extra ?? [])
      )
    );
  }, [open, query, options, loadOptions, remote]);

  // Çizilen satır sayısı sınırlı (stok kartı listesi binlerce satır
  // olabiliyor) ama KAÇ TANESİNİN gizlendiği kullanıcıya söyleniyor.
  const visible = useMemo(
    () => filtered.slice(0, maxVisible),
    [filtered, maxVisible]
  );

  /*
   * "KAÇ KAYIT DAHA VAR" — sunucu kipinde SUNUCUNUN saydığı toplam.
   * Çizilen satır sayısından türetilseydi, sınırın tam üstündeki bir
   * aramada yanlış sayı yazar ve kullanıcı olmayan kayıtları aramaya
   * devam ederdi.
   */
  const hiddenCount = loadOptions
    ? Math.max((remote?.total ?? 0) - visible.length, 0)
    : filtered.length - visible.length;

  // Kapalıyken kutuda SEÇİLİ kaydın adı yazıyor; açılınca kullanıcının
  // yazdığı süzgeç. İki durum tek alanda çakışmasın diye ayrı tutuluyor.
  const text = open ? query : selected ? labelOf(selected) : "";

  useEffect(() => {
    if (!open) {
      setRect(null);
      return;
    }

    const measure = () => {
      const box = inputRef.current?.getBoundingClientRect();
      if (!box) return;

      setRect({ left: box.left, top: box.bottom + 4, width: box.width });
    };

    measure();

    // Kaydırma YAKALAMA evresinde dinleniyor: liste iç içe kaydırılan
    // bir kutunun (tablo) içindeyse dış kaydırma olayı balonlanmaz.
    window.addEventListener("scroll", measure, true);
    window.addEventListener("resize", measure);

    return () => {
      window.removeEventListener("scroll", measure, true);
      window.removeEventListener("resize", measure);
    };
  }, [open]);

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
          open && visible[highlight] ? `${listId}-${highlight}` : undefined
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
              if (visible.length === 0) return 0;

              // Uçlarda başa/sona sarıyor: uzun listede kullanıcı
              // sondaki kaydı aramak için baştan aşağı inmesin.
              return (next + visible.length) % visible.length;
            });

            return;
          }

          if (event.key === "Enter") {
            if (!open) return;

            event.preventDefault();
            commit(visible[highlight]);
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
          style={
            rect
              ? { position: "fixed", left: rect.left, top: rect.top, width: rect.width }
              : undefined
          }
          className="z-50 max-h-64 overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg"
        >
          {/* SIK KULLANILANLAR: yalnız arama boşken. Kullanıcı yazmaya
              başladığında kısayollar sonucu boğmamalı. */}
          {quickPicks && quickPicks.length > 0 && query.trim() === "" && (
            <li className="border-b border-slate-100 px-3 py-2">
              <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-400">
                {quickPickLabel}
              </span>

              <span className="flex flex-wrap gap-1">
                {quickPicks.map((option) => (
                  <button
                    key={`quick-${option.id}`}
                    type="button"
                    onMouseDown={(event) => {
                      event.preventDefault();
                      commit(option);
                    }}
                    className="rounded border border-slate-200 px-2 py-1 text-xs text-slate-700 hover:bg-slate-50"
                  >
                    {labelOf(option)}
                  </button>
                ))}
              </span>
            </li>
          )}

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

          {loadOptions && query.trim().length < minQueryLength ? (
            <li className="px-3 py-2 text-sm text-slate-500">
              Aramak için en az {minQueryLength} karakter yazın.
            </li>
          ) : loading ? (
            <li className="px-3 py-2 text-sm text-slate-500">Aranıyor…</li>
          ) : visible.length === 0 ? (
            <li className="px-3 py-2 text-sm text-slate-500">
              Eşleşen kayıt yok.
            </li>
          ) : (
            visible.map((option, index) => (
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
                <span className="block">{labelOf(option)}</span>

                {option.hint && (
                  <small className="block text-xs text-slate-500">
                    {option.hint}
                  </small>
                )}
              </li>
            ))
          )}

          {/* KESİLEN SATIR SAYISI YAZILIYOR. Sessizce kırpmak
              kullanıcıya "kaydım yok" dedirtir — bu programda F0'da
              yaşanan hatanın aynısı. */}
          {hiddenCount > 0 && (
            <li className="border-t border-slate-100 px-3 py-2 text-xs text-slate-500">
              {hiddenCount} kayıt daha var — aramayı daraltın.
            </li>
          )}

          {onCreate && (
            <li
              onMouseDown={(event) => {
                event.preventDefault();
                const typed = query.trim();
                close();
                onCreate(typed);
              }}
              className="cursor-pointer border-t border-slate-100 px-3 py-2 text-sm font-medium text-brand-600 hover:bg-slate-50"
            >
              + {createLabel}
              {query.trim() ? `: "${query.trim()}"` : ""}
            </li>
          )}
        </ul>
      )}

      {error && <p className="mt-1.5 text-sm text-red-600">{error}</p>}
    </div>
  );
}
