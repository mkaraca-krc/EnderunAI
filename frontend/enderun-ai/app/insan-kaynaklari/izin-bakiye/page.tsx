"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { decimal } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Select,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  leaveBalanceService,
  type LeaveBalance,
  type LeaveBalanceSummary,
} from "@/services/leave-balance.service";

/**
 * Yıllık izin bakiyesi.
 *
 * Ekran HİÇBİR HESAP YAPMAZ — bütün rakamlar backend'den geliyor.
 * Hak ediş kademesi (1 yıl 14, 5 yıl üstü 20, 15 yıl üstü 26) çıkış
 * tazminatıyla aynı kaynağı kullanıyor; burada tekrarlanırsa aynı
 * personel için ekranda ve çıkışta farklı iki rakam çıkardı.
 *
 * ÜÇ AYRI RAKAM ayrı sütunlarda ve karıştırılmıyor:
 *   hak ediş → kullanılan → KALAN (hak ediş − kullanılan)
 *   ve KULLANILABİLİR (kalan − onay bekleyen).
 * Yeni talep "kullanılabilir" ile karşılaştırılır; "kalan"la
 * karşılaştırmak aynı günü iki kez vaat etmeye yol açar.
 *
 * Dikkat isteyen iki durum ayrıca öne çıkarılıyor: hak edişini aşmış
 * olanlar (avans izin ya da hatalı veri) ve işe giriş tarihi
 * girilmediği için hiç hesaplanamayanlar.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/** Gün sayısı — tam sayıysa ondalık gösterme, yarım günler korunur. */
function days(value: number) {
  // `decimal` sondaki sıfırı zaten atıyor: 12 -> "12", 12,5 -> "12,5".
  // Ayrı bir tam sayı dalı gerekmiyordu.
  return decimal(value, 1);
}

function formatDate(value: string | null) {
  if (!value) return "—";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleDateString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

type Filter = "all" | "overdraft" | "no-start-date" | "has-balance";

const FILTERS: { value: Filter; label: string }[] = [
  { value: "all", label: "Tümü" },
  { value: "has-balance", label: "Bakiyesi olanlar" },
  { value: "overdraft", label: "Hak edişini aşanlar" },
  { value: "no-start-date", label: "İşe giriş tarihi yok" },
];

export default function LeaveBalancePage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [data, setData] = useState<LeaveBalanceSummary | null>(null);
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");

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
      setData(await leaveBalanceService.get(companyId));
    } catch (err) {
      setError(messageOf(err));
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const rows = useMemo(() => {
    let items = data?.items ?? [];

    if (filter === "overdraft") {
      items = items.filter((x) => x.remainingDays < 0);
    } else if (filter === "no-start-date") {
      items = items.filter(
        (x) => x.serviceDays === 0 && x.nextAccrualDate === null
      );
    } else if (filter === "has-balance") {
      items = items.filter((x) => x.availableDays > 0);
    }

    const term = search.trim().toLocaleLowerCase("tr-TR");
    if (term) {
      items = items.filter(
        (x) =>
          x.fullName.toLocaleLowerCase("tr-TR").includes(term) ||
          (x.employeeNumber ?? "").toLocaleLowerCase("tr-TR").includes(term)
      );
    }

    return items;
  }, [data, filter, search]);

  return (
    <ErpShell
      design="redwood"
      title="Yıllık İzin Bakiyesi"
      description="Hak ediş, kullanılan ve kalan izin günleri; hak ediş kademesi çıkış tazminatıyla aynı kaynaktan gelir."
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

            <div className="min-w-56">
              <Select
                label="Filtre"
                value={filter}
                onChange={(event) => setFilter(event.target.value as Filter)}
                options={FILTERS.map((item) => ({
                  label: item.label,
                  value: item.value,
                }))}
              />
            </div>

            <div className="min-w-56 flex-1">
              <Input
                label="Ara"
                placeholder="Ad soyad ya da sicil no"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
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

        {data && (
          <>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <StatCard
                title="Personel"
                value={data.personnelCount}
                description={`${formatDate(data.asOf)} itibarıyla`}
              />
              <StatCard
                title="Toplam hak ediş"
                value={`${days(data.totalEntitlementDays)} gün`}
              />
              <StatCard
                title="Toplam kalan"
                value={`${days(data.totalRemainingDays)} gün`}
                description="Şirketin izin yükümlülüğü"
              />
              {/* İKİ SAYAÇ TOPLANMIYOR: kesişiyorlar. İşe giriş tarihi
                  olmayan personelin hak edişi 0 sayıldığı için
                  kullandığı her gün eksi bakiye üretiyor
                  (LeaveBalanceCalculator.Empty → RemainingDays =
                  −UsedDays); yani aynı kişi hem "hak edişini aşan"
                  hem "tarihi eksik" sayısına giriyor. Toplamak onu
                  iki kez sayıp olduğundan büyük bir uyarı gösteriyordu.
                  Tarihi eksik olanlar zaten tablonun üstündeki uyarı
                  şeridinde ayrıca duruyor. */}
              <StatCard
                title="Hak edişini aşan"
                value={data.overdraftCount}
                description="Avans izin ya da hatalı veri"
              />
            </div>

            {data.withoutStartDateCount > 0 && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                <span className="font-semibold">
                  {data.withoutStartDateCount} personelin izin hakkı
                  hesaplanamıyor
                </span>{" "}
                — işe giriş tarihi girilmemiş.{" "}
                <Link
                  href="/insan-kaynaklari/veri-eksikleri"
                  className="font-medium underline"
                >
                  Eksikleri tamamla
                </Link>
              </div>
            )}

            {rows.length === 0 ? (
              <EmptyState
                title="Kayıt yok"
                description="Bu filtreyle eşleşen personel bulunmuyor."
              />
            ) : (
              <Card>
                <CardHeader>
                  <div className="flex items-center gap-3">
                    <h2 className="text-sm font-semibold text-slate-900">
                      İzin bakiyeleri
                    </h2>
                    <Badge>{rows.length}</Badge>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">
                    Yeni talep <strong>kullanılabilir</strong> gün ile
                    karşılaştırılır — kalan gün onay bekleyenleri içerir.
                  </p>
                </CardHeader>

                <CardContent className="p-0 overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Sicil</TableHead>
                        <TableHead>Ad Soyad</TableHead>
                        <TableHead className="text-right">Kıdem</TableHead>
                        <TableHead className="text-right">Kademe</TableHead>
                        <TableHead className="text-right">Hak ediş</TableHead>
                        <TableHead className="text-right">Kullanılan</TableHead>
                        <TableHead className="text-right">Bekleyen</TableHead>
                        <TableHead className="text-right">Kalan</TableHead>
                        <TableHead className="text-right">
                          Kullanılabilir
                        </TableHead>
                        <TableHead>Sonraki hak ediş</TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {rows.map((row) => (
                        <BalanceRow key={row.personnelId} row={row} />
                      ))}
                    </TableBody>
                  </Table>
                </CardContent>
              </Card>
            )}
          </>
        )}

        {!data && !loading && !error && (
          <EmptyState
            title="Şirket seçin"
            description="İzin bakiyelerini görmek için şirket seçin."
          />
        )}
      </div>
    </ErpShell>
  );
}

function BalanceRow({ row }: { row: LeaveBalance }) {
  const overdraft = row.remainingDays < 0;
  const uncalculated = row.serviceDays === 0 && row.nextAccrualDate === null;

  return (
    <TableRow>
      <TableCell className="text-slate-500">
        {row.employeeNumber || "—"}
      </TableCell>

      <TableCell>
        <div className="font-medium">{row.fullName}</div>
        {row.note && (
          <div className="mt-0.5 text-xs text-amber-700">{row.note}</div>
        )}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {uncalculated ? "—" : `${row.serviceYears} yıl`}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {row.currentTierDays > 0 ? `${row.currentTierDays} gün` : "—"}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {days(row.entitlementDays)}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {days(row.usedDays)}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {row.pendingDays > 0 ? (
          <Badge variant="warning">{days(row.pendingDays)}</Badge>
        ) : (
          <span className="text-slate-400">0</span>
        )}
      </TableCell>

      <TableCell className="text-right tabular-nums font-medium">
        {overdraft ? (
          <Badge variant="danger">{days(row.remainingDays)}</Badge>
        ) : (
          days(row.remainingDays)
        )}
      </TableCell>

      <TableCell className="text-right tabular-nums font-semibold">
        {days(row.availableDays)}
      </TableCell>

      <TableCell className="text-sm text-slate-600">
        {row.nextAccrualDate ? (
          <>
            {formatDate(row.nextAccrualDate)}
            {row.nextAccrualDays > 0 && (
              <span className="ml-1 text-xs text-slate-400">
                (+{row.nextAccrualDays} gün)
              </span>
            )}
          </>
        ) : (
          "—"
        )}
      </TableCell>
    </TableRow>
  );
}
