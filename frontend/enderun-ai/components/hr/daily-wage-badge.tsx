"use client";

import { useEffect, useState } from "react";
import { money } from "@/lib/format/turkish";

import {
  hrAttendanceService,
  type ActualDailyWage,
} from "@/services/hr-attendance.service";


/**
 * Seçili personelin o günkü yevmiyesi.
 *
 * Elden ödemeyi görme yetkisi olan kullanıcı GERÇEK günlük maliyeti
 * (resmî + elden/gün) görür; yetkisi olmayan yalnızca resmî yevmiyeyi
 * görür ve eksik olduğu açıkça yazılır — rakamın tam sanılması, elden
 * ödemenin görünmesinden daha tehlikeli.
 *
 * Yetki uca bağlı: ücret görmeyen rol (Şantiye Şefi, Formen, Teknik
 * Koordinatör) 403 alır ve bileşen hiçbir şey çizmez.
 *
 * SALT GÖSTERİM: burada görünen rakam bordroya, SGK matrahına ve
 * muhasebeye girmez.
 */
export default function DailyWageBadge({
  personnelId,
  workDate,
}: {
  personnelId: string;
  workDate?: string;
}) {
  const [wage, setWage] = useState<ActualDailyWage | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      if (!personnelId) {
        if (!cancelled) {
          setWage(null);
          setUnavailable(false);
        }
        return;
      }

      try {
        const result = await hrAttendanceService.getDailyWage(
          personnelId,
          workDate
        );

        if (!cancelled) {
          setWage(result);
          setUnavailable(false);
        }
      } catch {
        // Yetkisiz (403) ya da ücret kartı yok (404): rakam
        // gösterilmez. Sıfır yazmak yanlış olurdu.
        if (!cancelled) {
          setWage(null);
          setUnavailable(true);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [personnelId, workDate]);

  if (!personnelId || unavailable || !wage) return null;

  const hidden = wage.extraPaymentHidden;

  return (
    <div
      className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm"
      style={{ display: "flex", gap: 16, flexWrap: "wrap" }}
    >
      <span>
        Resmî yevmiye:{" "}
        <strong>{money(wage.officialDailyRate)}</strong>
        <small style={{ display: "block", color: "var(--erp-muted)" }}>
          saatlik {money(wage.officialHourlyRate)} ·{" "}
          {money(wage.dailyWorkHours)} saat/gün
        </small>
      </span>

      {hidden ? (
        <span style={{ color: "var(--erp-muted)" }}>
          Gerçek yevmiye gizli
          <small style={{ display: "block" }}>
            Elden ödeme görme yetkiniz yok; bu rakam eksik olabilir.
          </small>
        </span>
      ) : (
        <span>
          Gerçek yevmiye:{" "}
          <strong style={{ color: "var(--erp-primary)" }}>
            {money(wage.actualDailyRate ?? 0)}
          </strong>
          <small style={{ display: "block", color: "var(--erp-muted)" }}>
            elden payı {money(wage.extraDailyRate ?? 0)}/gün ·
            saatlik {money(wage.actualHourlyRate ?? 0)}
          </small>
        </span>
      )}
    </div>
  );
}
