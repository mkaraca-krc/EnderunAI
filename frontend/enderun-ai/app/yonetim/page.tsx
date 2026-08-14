"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  dateTime,
  money,
  // Yerel `percent` DEĞİŞKENİ var (trend yüzdesi); takma ad
  // olmasaydı ikisi çakışırdı.
  percent as sharedPercent,
  whole,
} from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Select,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  KpiValueKind,
  managementKpiService,
  type ManagementKpi,
  type ManagementKpiResponse,
} from "@/services/management-kpi.service";

/**
 * Yönetim KPI panosu.
 *
 * EKRANDA HİÇBİR HESAP YOK — tek bir toplama ya da filtre bile.
 * Bütün sayılar `api/yonetim/kpi` üzerinden, her biri kendi alanının
 * yetkili servisinden geliyor. Burada bir `reduce` yazılırsa aynı sayı
 * iki yerde hesaplanmış olur ve zamanla ayrışır; bunu önlemek paketin
 * ana kuralı.
 *
 * YETKİSİZ KPI HİÇ ÇİZİLMEZ: yanıtta zaten yok. Kilitli bir kart
 * göstermek, o göstergenin var olduğunu ve mertebesini ele verirdi.
 * Ekran "hangi kart eksik" diye de sormaz — yalnız geleni çizer.
 *
 * Yeni renk ya da bileşen icat edilmedi: mevcut Card/Badge/Select ve
 * marka paleti kullanılıyor. Grafik yok — kütüphane kurmak yeni bir
 * görsel dil getirirdi; sayılar ve yön oku bu tur için yeterli.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Göstergeler yüklenemedi.";
}

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

/** Değeri türüne göre biçimler; biçim dışında hiçbir işlem yapmaz. */
/**
 * KPI değeri — TÜRÜNE göre biçimlenir.
 *
 * Üç tür üç ayrı sayı tipi: adet tam sayıdır, oran yüzdedir,
 * geri kalanı tutardır. Tek bir biçim hepsine uygulansaydı ya
 * "42,00 proje" ya da "1.250 ₺" (kuruşsuz tutar) çıkardı.
 */
function formatValue(kpi: ManagementKpi) {
  if (kpi.kind === KpiValueKind.Count) {
    return whole(kpi.value);
  }

  return kpi.kind === KpiValueKind.Percent
    ? sharedPercent(kpi.value, 2)
    : money(kpi.value);
}

/**
 * Önceki döneme göre yön.
 *
 * YÖN "İYİ/KÖTÜ" DEMEZ, yalnızca arttı/azaldı der. Giderin artması
 * kötü, nakdin artması iyidir; hangisinin hangisi olduğunu KPI'ın
 * kendisi bilmiyor ve burada tahmin etmek yanlış renk gösterirdi.
 */
function trendOf(kpi: ManagementKpi) {
  if (kpi.previousValue === null || kpi.previousValue === undefined) return null;

  const diff = kpi.value - kpi.previousValue;

  // Kuruş altı fark yuvarlamadan gelir; yön saymaz.
  if (Math.abs(diff) < 0.01) {
    return { symbol: "→", label: "önceki dönemle aynı" };
  }

  const percent =
    kpi.previousValue === 0
      ? null
      : Math.abs((diff / kpi.previousValue) * 100);

  const size =
    percent === null
      ? ""
      : ` (${sharedPercent(percent, 1)})`;

  return diff > 0
    ? { symbol: "▲", label: `önceki döneme göre arttı${size}` }
    : { symbol: "▼", label: `önceki döneme göre azaldı${size}` };
}

export default function ManagementDashboardPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);

  const [data, setData] = useState<ManagementKpiResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const rows = await companyService.getAll();
        setCompanies(rows);

        const first = rows.find((x) => x.isActive !== false) ?? rows[0];
        if (first) setCompanyId((current) => current || first.id);
      } catch (err) {
        setError(messageOf(err));
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setData(await managementKpiService.get(companyId, year, month));
    } catch (err) {
      setError(messageOf(err));
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [companyId, year, month]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const years = Array.from({ length: 5 }, (_, i) => now.getFullYear() - 2 + i);

  return (
    <ErpShell
      design="redwood"
      title="Yönetim Göstergeleri"
      description="Her gösterge kendi alanının kaynağından okunur; bu ekranda hesap yapılmaz."
    >
      <div className="space-y-6">
        <Card>
          <CardContent className="flex flex-wrap items-end gap-4">
            <div className="min-w-56">
              <Select
                label="Şirket"
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
                options={companies.map((company) => ({
                  label: company.name,
                  value: company.id,
                }))}
              />
            </div>

            <div className="w-32">
              <Select
                label="Yıl"
                value={String(year)}
                onChange={(event) => setYear(Number(event.target.value))}
                options={years.map((value) => ({
                  label: String(value),
                  value: String(value),
                }))}
              />
            </div>

            <div className="w-40">
              <Select
                label="Ay"
                value={String(month)}
                onChange={(event) => setMonth(Number(event.target.value))}
                options={MONTHS.map((name, index) => ({
                  label: name,
                  value: String(index + 1),
                }))}
              />
            </div>

            <Button onClick={() => void load()} disabled={loading || !companyId}>
              {loading ? "Yükleniyor..." : "Yenile"}
            </Button>
          </CardContent>
        </Card>

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}

        {data && data.kpis.length === 0 && !loading && (
          <EmptyState
            title="Görüntüleyebileceğiniz gösterge yok"
            description="Göstergeler yetkiye göre gelir; bu hesapta görünen bir gösterge bulunmuyor."
          />
        )}

        {data && data.kpis.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {data.kpis.map((kpi) => (
              <KpiCard key={kpi.key} kpi={kpi} />
            ))}
          </div>
        )}

        {data && data.unavailable.length > 0 && (
          <Card>
            <CardHeader>
              <h2 className="text-sm font-semibold text-slate-900">
                Şu anda okunamayan göstergeler
              </h2>
              <p className="mt-1 text-xs text-slate-500">
                Yetkiniz var ama kaynak yanıt vermedi. Yenilemeyi deneyin.
              </p>
            </CardHeader>

            <CardContent>
              <ul className="space-y-1 text-sm text-slate-600">
                {data.unavailable.map((item) => (
                  <li key={item.key}>
                    <strong>{item.title}</strong> — {item.reason}
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        )}

        {data && (
          <p className="text-xs text-slate-400">
            {MONTHS[data.month - 1]} {data.year} ·{" "}
            {dateTime(data.generatedAtUtc)} itibarıyla
          </p>
        )}
      </div>
    </ErpShell>
  );
}

function KpiCard({ kpi }: { kpi: ManagementKpi }) {
  const trend = trendOf(kpi);

  return (
    <Link href={kpi.link} className="block">
      <div className="h-full rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-brand-300 hover:shadow">
        <div className="flex items-start justify-between gap-3">
          <span className="text-sm font-medium text-slate-600">{kpi.title}</span>

          {trend && (
            <span
              className="shrink-0 text-xs text-slate-500"
              title={trend.label}
            >
              {trend.symbol}
            </span>
          )}
        </div>

        <div className="mt-2 text-2xl font-semibold tabular-nums text-slate-900">
          {formatValue(kpi)}
        </div>

        {kpi.detail && (
          <p className="mt-2 text-xs leading-5 text-slate-500">{kpi.detail}</p>
        )}

        {/* Kaynağın kendi uyarısı — maskelenmiş toplam gibi. Yutulursa
            "rakamlar tutmuyor" tartışması çıkar. */}
        {kpi.note && (
          <div className="mt-3">
            <Badge variant="warning">{kpi.note}</Badge>
          </div>
        )}

        {trend && (
          <p className="mt-3 text-xs text-slate-400">{trend.label}</p>
        )}
      </div>
    </Link>
  );
}
