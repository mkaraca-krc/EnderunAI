"use client";

import { useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api/api-client";
import { cikisIstegi } from "@/lib/auth/cikis";

/*
 * ═══ KALICI ÇIKIŞI YALNIZ GERÇEK BİR "MESAİ DIŞI" KARARI ÜRETİR (MESAİ/1) ═══
 *
 * Eskiden `!status.isAllowed` çıkış yapıyordu: çıkış bir kararın
 * VARLIĞINA değil, bir iznin YOKLUĞUNA bağlıydı. `isAllowed` taşımayan
 * HER 200 cevabı — bir HTML sayfası, eksik bir gövde — çerezi kalıcı
 * olarak siliyordu (ölçüldü, `mesai-izleyicisi-karar.test.tsx`).
 *
 * Artık sunucu kararı AÇIKÇA söylüyor (`karar`) ve çıkış yalnız
 * "mesai-disi"de. Başka her cevap "karar yok" demek: oturum düşürülmez,
 * bir sonraki yoklamada yeniden sorulur.
 */
type WorkHoursStatus = {
  karar?: string;
  isAllowed?: boolean;
  isExempt?: boolean;
  windowEndsAtUtc?: string | null;
  minutesRemaining?: number | null;
};

const POLL_INTERVAL_MS = 60_000;
const WARNING_THRESHOLD_MINUTES = 5;

export default function WorkHourSessionWatcher() {
  const [minutesRemaining, setMinutesRemaining] = useState<number | null>(null);
  const [dismissed, setDismissed] = useState(false);
  const loggingOutRef = useRef(false);

  useEffect(() => {
    let active = true;

    async function forceLogout() {
      if (loggingOutRef.current) return;
      loggingOutRef.current = true;

      try {
        // GÜNLÜK/1: çıkışı KİM tetikledi, günlükte görünsün.
        // Çağrı `lib/auth/cikis` üzerinden: bu dosya `apiClient` de
        // kullanıyor ve iki kalıbı karıştırmak gövde sözleşmesi
        // kapısını düşürüyordu.
        await cikisIstegi("mesai-izleyicisi");
      } catch {
        // Backend'e ulaşılamasa bile kullanıcı login'e yönlendirilir.
      }

      window.location.href = "/login?reason=work-hours";
    }

    async function poll() {
      if (loggingOutRef.current) return;

      try {
        const status = await apiClient<WorkHoursStatus | null>("auth/work-hours-status");
        if (!active) return;

        if (status?.karar === "mesai-disi") {
          await forceLogout();
          return;
        }

        // Okunamayan ya da beklenmeyen cevap: karar yok, oturum kalır.
        if (status?.karar !== "izinli") return;

        if (status.isExempt || status.minutesRemaining == null) {
          setMinutesRemaining(null);
          return;
        }

        // `minutesRemaining <= 0` artık çıkış YAPMIYOR: sunucunun "izinli"
        // dediği bir cevapta istemcinin kendi çıkarımıydı. Pencere
        // kapandıysa bir sonraki yoklama "mesai-disi" der; arka uç ara
        // katmanı o arada her isteği zaten kesiyor.
        setMinutesRemaining(Math.max(0, status.minutesRemaining));
      } catch {
        // apiClient 401'de zaten /login'e yönlendiriyor; geçici hatalarda
        // (5xx, ağ) sessiz geç — bir sonraki yoklama yeniden sorar.
      }
    }

    void poll();
    const interval = window.setInterval(poll, POLL_INTERVAL_MS);

    return () => {
      active = false;
      window.clearInterval(interval);
    };
  }, []);

  useEffect(() => {
    if (minutesRemaining === null || minutesRemaining > WARNING_THRESHOLD_MINUTES) {
      setDismissed(false);
    }
  }, [minutesRemaining]);

  if (
    minutesRemaining === null ||
    minutesRemaining > WARNING_THRESHOLD_MINUTES ||
    dismissed
  ) {
    return null;
  }

  return (
    <div className="work-hour-warning-banner" role="alert">
      <span>
        Mesai pencereniz {minutesRemaining} dakika içinde kapanacak, oturumunuz
        otomatik olarak sonlandırılacak.
      </span>
      <button
        type="button"
        onClick={() => setDismissed(true)}
        aria-label="Uyarıyı kapat"
      >
        ✕
      </button>
    </div>
  );
}
