"use client";

import { useEffect, useRef, useState } from "react";

import {
  marketService,
  SUPPORTED_CURRENCIES,
  type ExchangeRateLookup,
} from "@/services/market.service";

type Props = {
  currency: string;
  exchangeRate: number;
  /** Belge tarihi — kur bu tarihe göre bulunur. */
  documentDate: string;
  onChange: (next: { currency: string; exchangeRate: number }) => void;
  disabled?: boolean;
};

function formatRate(value: number) {
  return value.toLocaleString("tr-TR", {
    minimumFractionDigits: 4,
    maximumFractionDigits: 4,
  });
}

/**
 * Fatura/belge başlığındaki para birimi ve kur alanları.
 *
 * TRY seçiliyken kur alanı hiç görünmez; TL akışında kullanıcıya
 * anlamsız bir "1,0000" göstermenin faydası yok.
 *
 * Döviz seçilince kur TCMB arşivinden belge tarihine göre gelir.
 * Arşivde kur yoksa bu açıkça yazılır ve kullanıcı elle girmek
 * zorunda kalır — sessizce 1 varsayılmaz, çünkü 1 ile kaydedilen
 * dövizli bir fatura defteri kırk kat yanlış gösterir.
 */
export default function CurrencyRateFields({
  currency,
  exchangeRate,
  documentDate,
  onChange,
  disabled,
}: Props) {
  const [lookup, setLookup] = useState<ExchangeRateLookup | null>(null);
  const [lookupError, setLookupError] = useState("");
  const [manual, setManual] = useState(false);

  // Efekt yalnızca para birimi/tarih değişince koşmalı; en güncel
  // onChange ve manual değerlerine ref üzerinden erişilir.
  const onChangeRef = useRef(onChange);
  const manualRef = useRef(manual);

  useEffect(() => {
    onChangeRef.current = onChange;
    manualRef.current = manual;
  });

  const isForeign = currency !== "TRY";

  // Kur sorgusu bir dış sisteme (TCMB arşivi) abonelik: efektin gövdesi
  // yalnızca isteği başlatır, bütün durum güncellemeleri await'ten sonra
  // yapılır. Efekt gövdesinde senkron setState çağırmak zincirleme
  // render'a yol açıyor.
  useEffect(() => {
    if (!isForeign || !documentDate) return;

    let cancelled = false;

    void (async () => {
      try {
        const result = await marketService.lookupRate(currency, documentDate);
        if (cancelled) return;

        setLookup(result);
        setLookupError("");

        if (!manualRef.current) {
          onChangeRef.current({ currency, exchangeRate: result.forexBuying });
        }
      } catch {
        if (cancelled) return;

        setLookup(null);
        setLookupError(
          `${currency} için ${documentDate} tarihine TCMB kuru bulunamadı. ` +
            "Kuru elle girmeniz gerekiyor."
        );
        setManual(true);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [currency, documentDate, isForeign]);

  return (
    <>
      <label>
        <span>Para Birimi</span>
        <select
          value={currency}
          disabled={disabled}
          onChange={(event) => {
            const next = event.target.value;
            setManual(false);
            onChange({
              currency: next,
              exchangeRate: next === "TRY" ? 1 : exchangeRate,
            });
          }}
        >
          {SUPPORTED_CURRENCIES.map((code) => (
            <option key={code} value={code}>
              {code}
            </option>
          ))}
        </select>
      </label>

      {isForeign && (
        <label>
          <span>Kur (TL karşılığı) *</span>
          <input
            type="number"
            step="0.0001"
            min="0"
            value={exchangeRate}
            disabled={disabled}
            onChange={(event) => {
              setManual(true);
              onChange({
                currency,
                exchangeRate: Number(event.target.value) || 0,
              });
            }}
          />

          {lookup && !manual && (
            <small>
              TCMB döviz alış · {new Date(lookup.effectiveDate).toLocaleDateString("tr-TR")}
              {lookup.daysBack > 0 &&
                ` (belge tarihinde bülten yok, ${lookup.daysBack} gün önceki kur)`}
              {" · "}
              {formatRate(lookup.forexBuying)}
            </small>
          )}

          {manual && (
            <small>
              Kur elle girildi — TCMB arşivindeki değerin yerine bu kullanılacak.
              {lookup && ` TCMB: ${formatRate(lookup.forexBuying)}`}
            </small>
          )}

          {lookupError && <small className="erp-text-danger">{lookupError}</small>}

          {(!exchangeRate || exchangeRate <= 0) && (
            <small className="erp-text-danger">
              Dövizli belge kur olmadan kaydedilemez.
            </small>
          )}
        </label>
      )}
    </>
  );
}
