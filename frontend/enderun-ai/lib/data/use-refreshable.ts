"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * VERİ TAZELEME — arayüzün TEK mekanizması.
 *
 * Bugün her ekran kendi <c>load()</c> fonksiyonunu yazıyor (126 çağrı);
 * kimi mutasyondan sonra tazeliyor, kimi tazelemiyor, kimi de tam
 * sayfa yeniden yüklüyor. Bu kanca üç şeyi tek yerde toplar:
 *
 *   1. MUTASYON SONRASI TAZELEME: kaydet/sil/onayla bitince ilgili
 *      liste yeniden çekilir (<c>mutate</c>).
 *   2. SON GÜNCELLEME ZAMANI: kullanıcı ekrandaki rakamın ne zamana
 *      ait olduğunu görebilmeli — "yenile" düğmesi olan her ekranda
 *      sorulacak ilk soru budur.
 *   3. FORM KORUMASI: kullanıcı form doldururken arka planda gelen bir
 *      tazeleme girdisini SIFIRLAMAMALI. Kanca "meşgul" işaretlenir ve
 *      sessiz tazelemeler o sırada atlanır.
 *
 * TAM SAYFA YENİDEN YÜKLEME YOK. <c>window.location.reload()</c>
 * uygulamanın bütün durumunu (açık panel, filtre, kaydırma) siler ve
 * kullanıcıyı yaptığı işin başına döndürür.
 */

export type RefreshableState<T> = {
  data: T | null;
  error: string | null;

  /** İlk yükleme — ekran henüz hiç veri görmedi. */
  loading: boolean;

  /** Arka plan tazelemesi — ekranda veri var, üstüne yenisi geliyor. */
  refreshing: boolean;

  /** Son BAŞARILI yüklemenin zamanı; hiç yüklenmediyse null. */
  lastUpdatedAt: Date | null;

  /** Kullanıcının "Yenile" düğmesi. */
  refresh: () => Promise<void>;

  /**
   * Mutasyon sarmalayıcı: iş bitince veriyi tazeler. Mutasyon hata
   * verirse tazeleme YAPILMAZ — başarısız bir işlemden sonra listeyi
   * yenilemek, kullanıcıya "oldu" izlenimi verirdi.
   */
  mutate: <TResult>(action: () => Promise<TResult>) => Promise<TResult>;

  /**
   * Form açıkken çağrılır: sessiz/periyodik tazelemeler durur.
   * Kullanıcının yazdığı değerler ekrandan silinmemeli.
   */
  setBusy: (busy: boolean) => void;
};

export type RefreshableOptions = {
  /**
   * Sessiz periyodik tazeleme aralığı (ms). Verilmezse periyodik
   * tazeleme YOK — varsayılan olarak açık olsaydı her ekran kullanıcı
   * farkında olmadan ağ trafiği üretirdi.
   */
  intervalMs?: number;

  /** false ise kanca hiç yüklemez (ör. gerekli parametre henüz yok). */
  enabled?: boolean;
};

export function useRefreshable<T>(
  fetcher: () => Promise<T>,
  options: RefreshableOptions = {},
): RefreshableState<T> {
  const { intervalMs, enabled = true } = options;

  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);

  // Fetcher her render'da yeniden kurulabilir; ref'te tutmak kancanın
  // bağımlılık listesini sabitler ve sonsuz döngüyü önler.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const busyRef = useRef(false);

  // Bileşen söküldükten sonra gelen yanıt state yazmamalı.
  const activeRef = useRef(true);

  useEffect(() => {
    activeRef.current = true;

    return () => {
      activeRef.current = false;
    };
  }, []);

  const run = useCallback(
    async (mode: "initial" | "manual" | "silent") => {
      if (!enabled) return;

      // FORM KORUMASI: kullanıcı yazarken sessiz tazeleme araya girmez.
      // Elle "Yenile"ye basmak niyet beyanıdır, o geçer.
      if (mode === "silent" && busyRef.current) return;

      if (mode === "initial") setLoading(true);
      else setRefreshing(true);

      try {
        const result = await fetcherRef.current();

        if (!activeRef.current) return;

        setData(result);
        setError(null);
        setLastUpdatedAt(new Date());
      } catch (err) {
        if (!activeRef.current) return;

        setError(err instanceof Error ? err.message : "Veri yüklenemedi.");

        // ESKİ VERİ EKRANDA KALIR: hata anında tabloyu boşaltmak,
        // kullanıcının baktığı rakamı sebepsiz yok etmek olurdu.
      } finally {
        if (activeRef.current) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [enabled],
  );

  useEffect(() => {
    if (!enabled) {
      setLoading(false);
      return;
    }

    void run("initial");
  }, [enabled, run]);

  useEffect(() => {
    if (!intervalMs || !enabled) return;

    const timer = setInterval(() => void run("silent"), intervalMs);

    return () => clearInterval(timer);
  }, [intervalMs, enabled, run]);

  const refresh = useCallback(async () => {
    await run("manual");
  }, [run]);

  const mutate = useCallback(
    async <TResult,>(action: () => Promise<TResult>): Promise<TResult> => {
      const result = await action();

      // Yalnız BAŞARILI mutasyondan sonra tazelenir: action hata
      // fırlatırsa buraya hiç gelinmez.
      await run("manual");

      return result;
    },
    [run],
  );

  const setBusy = useCallback((busy: boolean) => {
    busyRef.current = busy;
  }, []);

  return {
    data,
    error,
    loading,
    refreshing,
    lastUpdatedAt,
    refresh,
    mutate,
    setBusy,
  };
}
