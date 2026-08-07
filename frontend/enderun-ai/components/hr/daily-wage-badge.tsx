"use client";

import { useEffect, useState } from "react";

import {
  hrAttendanceService,
  type ActualDailyWage,
} from "@/services/hr-attendance.service";

const money = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

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
        <strong>{money.format(wage.officialDailyRate)}</strong>
        <small style={{ display: "block", color: "#64748b" }}>
          saatlik {money.format(wage.officialHourlyRate)} ·{" "}
          {money.format(wage.dailyWorkHours)} saat/gün
        </small>
      </span>

      {hidden ? (
        <span style={{ color: "#64748b" }}>
          Gerçek yevmiye gizli
          <small style={{ display: "block" }}>
            Elden ödeme görme yetkiniz yok; bu rakam eksik olabilir.
          </small>
        </span>
      ) : (
        <span>
          Gerçek yevmiye:{" "}
          <strong style={{ color: "#0f766e" }}>
            {money.format(wage.actualDailyRate ?? 0)}
          </strong>
          <small style={{ display: "block", color: "#64748b" }}>
            elden payı {money.format(wage.extraDailyRate ?? 0)}/gün ·
            saatlik {money.format(wage.actualHourlyRate ?? 0)}
          </small>
        </span>
      )}
    </div>
  );
}
