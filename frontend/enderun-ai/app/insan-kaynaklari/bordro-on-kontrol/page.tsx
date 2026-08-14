"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
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
  payrollReadinessService,
  type PayrollReadiness,
} from "@/services/payroll-readiness.service";

/**
 * Bordro ön kontrolü — bordro ÜRETİLMEDEN önceki son bakış.
 *
 * Neden ayrı bir ekran: bordro bugün eksik verili personeli sessizce
 * içine alıp üretiyor. Sorun ancak bordro çıktıktan ve resmî bildirim
 * reddedildikten sonra görülüyor; o noktada bordronun iptal edilip
 * yeniden üretilmesi gerekiyor.
 *
 * ENGEL ve UYARI ayrı tutuluyor: engel bordroyu durdurur, uyarı
 * durdurmaz ama düzeltilmelidir. İkisini aynı listede aynı ağırlıkta
 * göstermek, gerçek engeli gürültüye gömerdi.
 *
 * Ekran hiçbir şey YAZMAZ; yalnızca okur ve düzeltmenin yapılacağı
 * ekrana bağlar.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

/** Bordro dönemi varsayılanı: içinde bulunulan ay. */
function currentPeriod() {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

export default function PayrollReadinessPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const initial = currentPeriod();
  const [year, setYear] = useState(initial.year);
  const [month, setMonth] = useState(initial.month);

  const [data, setData] = useState<PayrollReadiness | null>(null);
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
      setData(await payrollReadinessService.readiness(companyId, year, month));
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

  const years = Array.from({ length: 5 }, (_, i) => initial.year - 2 + i);

  return (
    <ErpShell
      design="redwood"
      title="Bordro Ön Kontrolü"
      description="Bordro üretilmeden önce eksikleri gösterir; hiçbir kayıt oluşturmaz."
    >
      {/* Ön kontrol uyarıları puantaj ve ücret kartı değiştikçe güncelleniyor. */}
      <div className="mb-4 flex justify-end">
        <button
          type="button"
          className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          onClick={() => void load()}
        >
          Yenile
        </button>
      </div>
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
              {loading ? "Kontrol ediliyor..." : "Yeniden kontrol et"}
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
            {/* Sonucun tek cümlelik özeti; ayrıntıya inmeden önce
                kullanıcı "bugün bordro çıkar mı" sorusunu yanıtlamalı. */}
            <div
              className={
                data.canCalculate
                  ? "rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800"
                  : "rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
              }
            >
              {data.canCalculate ? (
                <span className="font-semibold">
                  {MONTHS[data.month - 1]} {data.year} bordrosu hesaplanabilir.
                  {data.warnings.length > 0 &&
                    ` Ancak ${data.warnings.length} uyarı var.`}
                </span>
              ) : (
                <span className="font-semibold">
                  {MONTHS[data.month - 1]} {data.year} bordrosu hesaplanamaz —{" "}
                  {data.blockers.length} engel var.
                </span>
              )}
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <StatCard
                title="Aktif personel"
                value={data.personnelCount}
              />
              <StatCard
                title="Bordroya hazır"
                value={`${data.payrollReadyCount} / ${data.personnelCount}`}
                description="Kartı bordro için yeterli olanlar"
              />
              <StatCard
                title="Resmî bildirime hazır"
                value={`${data.officialReadyCount} / ${data.personnelCount}`}
                description="SGK alanları da tam olanlar"
              />
              <StatCard
                title="Onaylı puantaj"
                value={`${data.approvedAttendanceCount} / ${data.attendanceRecordCount}`}
                description="Bu ayın puantaj kayıtları"
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-3">
              <ParameterCheck
                label="Bordro parametreleri"
                ok={data.settingsVerified}
                href="/sistem-yonetimi/sirket-ayarlari"
                missingHint="Yıl parametreleri doğrulanmamış."
              />
              <ParameterCheck
                label="Resmî tatil takvimi"
                ok={data.holidayCalendarVerified}
                href="/insan-kaynaklari/tatil-takvimi"
                missingHint="Takvim doğrulanmamış."
              />
              <ParameterCheck
                label="Yemek/yol istisna tavanları"
                ok={data.mealTravelExemptionCapsDefined}
                href="/sistem-yonetimi/sirket-ayarlari"
                missingHint="Günlük tavanlar tanımlanmamış."
              />
            </div>

            {data.blockers.length > 0 && (
              <Card>
                <CardHeader>
                  <h2 className="text-sm font-semibold text-slate-900">
                    Engeller — bunlar giderilmeden bordro üretilemez
                  </h2>
                </CardHeader>
                <CardContent>
                  <ul className="space-y-2">
                    {data.blockers.map((item) => (
                      <li
                        key={item}
                        className="flex gap-2 text-sm text-slate-700"
                      >
                        <span className="mt-0.5 text-red-600">■</span>
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            )}

            {data.warnings.length > 0 && (
              <Card>
                <CardHeader>
                  <h2 className="text-sm font-semibold text-slate-900">
                    Uyarılar — bordroyu durdurmaz, düzeltilmesi gerekir
                  </h2>
                </CardHeader>
                <CardContent>
                  <ul className="space-y-2">
                    {data.warnings.map((item) => (
                      <li
                        key={item}
                        className="flex gap-2 text-sm text-slate-700"
                      >
                        <span className="mt-0.5 text-amber-600">▲</span>
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            )}

            <PersonList
              title="Bordrosu çıkarılamayan personel"
              description="Kartındaki eksik bordro hesabını engelliyor."
              people={data.blocked}
              variant="danger"
            />

            <PersonList
              title="Bordrosu çıkar ama resmî bildirimi eksik"
              description="Hesap yapılır; SGK bildirimi için alan eksiği var."
              people={data.incomplete}
              variant="warning"
              showMissing
            />

            {data.canCalculate &&
              data.blocked.length === 0 &&
              data.incomplete.length === 0 &&
              data.warnings.length === 0 && (
                <EmptyState
                  title="Eksik yok"
                  description="Bu dönem için engel ya da uyarı bulunmuyor."
                  action={
                    <Link href="/insan-kaynaklari/bordro">
                      <Button>Bordro ekranına git</Button>
                    </Link>
                  }
                />
              )}
          </>
        )}

        {!data && !loading && !error && (
          <EmptyState
            title="Dönem seçin"
            description="Kontrol için şirket, yıl ve ay seçin."
          />
        )}
      </div>
    </ErpShell>
  );
}

/** Tek satırlık parametre kontrolü; eksikse düzeltme ekranına bağlar. */
function ParameterCheck({
  label,
  ok,
  href,
  missingHint,
}: {
  label: string;
  ok: boolean;
  href: string;
  missingHint: string;
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-medium text-slate-700">{label}</span>
        <Badge variant={ok ? "success" : "danger"}>
          {ok ? "Tamam" : "Eksik"}
        </Badge>
      </div>

      {!ok && (
        <p className="mt-2 text-xs text-slate-500">
          {missingHint}{" "}
          <Link href={href} className="font-medium text-brand-700 underline">
            Düzelt
          </Link>
        </p>
      )}
    </div>
  );
}

/** Adı geçen kişiler — kullanıcı sayıyı görüp kimi arayacağını bilmeli. */
function PersonList({
  title,
  description,
  people,
  variant,
  showMissing = false,
}: {
  title: string;
  description: string;
  people: {
    personnelId: string;
    employeeNumber: string | null;
    fullName: string;
    missingFields?: string[];
  }[];
  variant: "danger" | "warning";
  showMissing?: boolean;
}) {
  if (people.length === 0) return null;

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-3">
          <h2 className="text-sm font-semibold text-slate-900">{title}</h2>
          <Badge variant={variant}>{people.length}</Badge>
        </div>
        <p className="mt-1 text-xs text-slate-500">{description}</p>
      </CardHeader>

      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Sicil</TableHead>
              <TableHead>Ad Soyad</TableHead>
              {showMissing && <TableHead>Eksik alanlar</TableHead>}
              <TableHead className="text-right">İşlem</TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            {people.map((person) => (
              <TableRow key={person.personnelId}>
                <TableCell className="text-slate-500">
                  {person.employeeNumber || "—"}
                </TableCell>
                <TableCell className="font-medium">{person.fullName}</TableCell>

                {showMissing && (
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {(person.missingFields ?? []).map((field) => (
                        <Badge key={field} variant="warning">
                          {field}
                        </Badge>
                      ))}
                    </div>
                  </TableCell>
                )}

                <TableCell className="text-right">
                  <Link
                    href="/insan-kaynaklari/veri-eksikleri"
                    className="text-sm font-medium text-brand-700 underline"
                  >
                    Eksikleri tamamla
                  </Link>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
