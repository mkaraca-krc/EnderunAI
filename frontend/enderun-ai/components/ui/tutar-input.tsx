"use client";

import {
  useLayoutEffect,
  useRef,
  useState,
  type InputHTMLAttributes,
} from "react";

import {
  caretAfterDigits,
  digitsBeforeCaret,
  formatAmountInput,
  normalizeAmountInput,
} from "@/lib/format/turkish";

type TutarInputProps = Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "value" | "onChange" | "type" | "inputMode"
> & {
  label?: string;
  error?: string;
  helperText?: string;
  /** HAM sayı — biçimli metin değil. Boş alan null (sıfır DEĞİL). */
  value: number | null;
  /** Her tuşta ham sayıyla çağrılır. */
  onChange: (value: number | null) => void;
};

/**
 * TUTAR GİRİŞİ — Türkçe biçim, imleç korumalı.
 *
 * NEDEN type="text": maskeli girişte type="number" çalışmıyor.
 * Tarayıcı "2.814.000,00" metnini geçersiz sayıp `value`yu boşaltıyor
 * ve `setSelectionRange` sayı alanlarında desteklenmiyor — yani imleç
 * konumu korunamıyor. inputMode="decimal" ile sayısal tuş takımı yine
 * açılıyor, biçimleme ve imleç kontrolü de mümkün oluyor.
 *
 * BİÇİMLEME BURADA KURULMUYOR: kural `lib/format/turkish.ts`
 * içinde, listelerdeki tutarla AYNI yerde. İkiye ayrılsaydı formdaki
 * tutar ile listedeki tutar zamanla farklı davranırdı.
 *
 * GERİ ÇAĞRI EFFECT BAĞIMLILIĞINDA DEĞİL: çağıran taraflar satır içi
 * ok fonksiyonu yazıyor (uygulamadaki 96 çağrı yerinin tamamı öyle).
 * Bağımlılığa konsaydı effect her tuşta yeniden kurulur ve tam olarak
 * yeni düzeltilen odak kaybı hatası burada yeniden doğardı.
 */
export function TutarInput({
  label,
  error,
  helperText,
  value,
  onChange,
  id,
  name,
  className = "",
  onBlur,
  ...props
}: TutarInputProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const [text, setText] = useState(() => normalizeAmountInput(value));

  /**
   * İMLEÇ HEDEFİ. Değer biçimlenip DOM'a yazıldıktan SONRA, tarayıcı
   * ekrana çizmeden ÖNCE geri konuyor — bu yüzden useLayoutEffect.
   * useEffect ile imleç bir kare sona atlar ve kullanıcı titremeyi
   * görür.
   */
  const caretRef = useRef<number | null>(null);

  useLayoutEffect(() => {
    const caret = caretRef.current;
    caretRef.current = null;

    if (caret === null) return;

    const input = inputRef.current;
    if (!input) return;

    input.setSelectionRange(caret, caret);
  });

  /*
   * DIŞARIDAN GELEN DEĞER. Alan odakta değilken üst bileşen değeri
   * değiştirdiyse (kayıt yüklendi, form sıfırlandı) ekran onu
   * gösterir. Odaktayken dokunulmuyor: kullanıcı "1.234," yazmışken
   * metni tam biçime çevirmek yazmanın ortasında imleci oynatırdı.
   */
  const lastExternal = useRef(value);

  if (lastExternal.current !== value) {
    lastExternal.current = value;

    if (document.activeElement !== inputRef.current) {
      const next = normalizeAmountInput(value);
      if (next !== text) setText(next);
    }
  }

  const inputId = id ?? name;

  return (
    <div className="w-full">
      {label && (
        <label
          htmlFor={inputId}
          className="mb-1.5 block text-sm font-medium text-slate-700"
        >
          {label}
        </label>
      )}

      <input
        {...props}
        ref={inputRef}
        id={inputId}
        name={name}
        type="text"
        inputMode="decimal"
        autoComplete="off"
        value={text}
        onChange={(event) => {
          const raw = event.target.value;
          const caret = event.target.selectionStart ?? raw.length;

          // İmleçten önceki RAKAM sayısı sabit kalan tek şey; ayıraçlar
          // biçimlemeyle gelip gittiği için karakter indeksi kayar.
          const digits = digitsBeforeCaret(raw, caret);

          // AYIRAÇ AZ ÖNCE YAZILDIYSA imleç onun sağına gitmeli:
          // rakam sayısı ayıracı saymaz, o yüzden imleç virgülün
          // soluna düşer ve bir sonraki tuş kuruş yerine tam kısma
          // girerdi ("1234," + "5" → 12.345).
          const justTypedSeparator = /[.,]/.test(raw[caret - 1] ?? "");

          const next = formatAmountInput(raw);

          caretRef.current = caretAfterDigits(
            next.text,
            digits,
            justTypedSeparator,
          );

          setText(next.text);
          lastExternal.current = next.value;
          onChangeRef.current(next.value);
        }}
        onBlur={(event) => {
          // Odaktan çıkınca tam biçime tamamlanır: "1.234,5" →
          // "1.234,50". Boş alan boş kalır — boş ile sıfır ayrı şey.
          setText(normalizeAmountInput(formatAmountInput(text).value));
          onBlur?.(event);
        }}
        // Tutar SAĞA HİZALI: basamaklar alt alta gelsin, gözle
        // karşılaştırılabilsin.
        className={[
          "h-10 w-full rounded-lg border bg-white px-3 text-right text-sm text-slate-900",
          "outline-none transition placeholder:text-slate-400",
          "tabular-nums focus:ring-2",
          error
            ? "border-red-400 focus:border-red-500 focus:ring-red-100"
            : "border-slate-300 focus:border-brand-500 focus:ring-brand-100",
          className,
        ].join(" ")}
      />

      {error ? (
        <p className="mt-1.5 text-sm text-red-600">{error}</p>
      ) : helperText ? (
        <p className="mt-1.5 text-sm text-slate-500">{helperText}</p>
      ) : null}
    </div>
  );
}
