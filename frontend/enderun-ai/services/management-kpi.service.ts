import { apiClient } from "@/lib/api/api-client";

const root = "yonetim/kpi";

/**
 * Yönetim KPI'ları — yalnızca okur.
 *
 * ANA KURAL: her gösterge kendi alanının yetkili servisinden geliyor;
 * ne backend toplayıcısında ne bu ekranda yeniden hesaplanıyor.
 * Ekranda tek bir `reduce` bile yazılmamalı — yazılırsa aynı sayı iki
 * yerde hesaplanmış olur ve zamanla ayrışır.
 *
 * YETKİSİ OLMAYAN KPI YANITTA HİÇ YOK: ne `kpis` içinde ne
 * `unavailable` içinde. Ekran "eksik kart" diye bir şey aramamalı.
 */

export const KpiValueKind = {
  Money: 0,
  Count: 1,
  Percent: 2,
} as const;

export interface ManagementKpi {
  key: string;
  title: string;
  value: number;
  kind: number;
  /** İkincil satır: "en kötü: X projesi", "3 çek 7 gün içinde". */
  detail?: string | null;
  /** Maskeleme/eksik veri uyarısı; kaynağın kendi notu. */
  note?: string | null;
  /** Yalnızca kaynağı dönem alan KPI'larda dolu. */
  previousValue?: number | null;
  link: string;
}

/** Yetkisi olan ama kaynağı üretilemeyen KPI. */
export interface ManagementKpiUnavailable {
  key: string;
  title: string;
  reason: string;
}

export interface ManagementKpiResponse {
  companyId: string;
  year: number;
  month: number;
  generatedAtUtc: string;
  kpis: ManagementKpi[];
  unavailable: ManagementKpiUnavailable[];
}

export const managementKpiService = {
  get(companyId: string, year?: number, month?: number) {
    const query = new URLSearchParams({ companyId });
    if (year) query.set("year", String(year));
    if (month) query.set("month", String(month));

    return apiClient<ManagementKpiResponse>(`${root}?${query.toString()}`);
  },
};
