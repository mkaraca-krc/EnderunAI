"use client";

import { useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api/api-client";

type WorkHoursStatus = {
  isAllowed: boolean;
  isExempt: boolean;
  windowEndsAtUtc: string | null;
  minutesRemaining: number | null;
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
        await fetch("/api/auth/logout", { method: "POST", cache: "no-store" });
      } catch {
        // Backend'e ulaşılamasa bile kullanıcı login'e yönlendirilir.
      }

      window.location.href = "/login?reason=work-hours";
    }

    async function poll() {
      if (loggingOutRef.current) return;

      try {
        const status = await apiClient<WorkHoursStatus>("auth/work-hours-status");
        if (!active) return;

        if (!status.isAllowed) {
          await forceLogout();
          return;
        }

        if (status.isExempt || status.minutesRemaining === null) {
          setMinutesRemaining(null);
          return;
        }

        if (status.minutesRemaining <= 0) {
          await forceLogout();
          return;
        }

        setMinutesRemaining(status.minutesRemaining);
      } catch {
        // apiClient 401'de zaten /login'e yönlendiriyor; ağ hatalarında sessiz geç.
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
