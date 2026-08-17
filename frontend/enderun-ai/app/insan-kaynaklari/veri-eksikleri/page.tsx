"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  Badge,
  Button,
  EmptyState,
  Input,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  DATA_SEVERITY,
  personnelService,
  type CompletePersonnelDataRequest,
  type PersonnelDataCompleteness,
  type PersonnelDataCompletenessSummary,
} from "@/services/personnel.service";

/**
 * Personel kartı veri eksikleri ve toplu tamamlama.
 *
 * Eksikler ENGELLEDİKLERİ SÜRECE göre ayrılıyor. Aynı listede aynı
 * ağırlıkta gösterilen dokuz alan gürültüye dönüşüyordu: telefonu
 * olmayan personel çalışmaya devam eder, SGK sicili olmayan
 * bildirilemez.
 *
 * Satır içi düzenleme yalnızca eksik alanları açar ve YALNIZCA
 * doldurulanları gönderir; dokunulmayan alan olduğu gibi kalır.
 */

/** Alan anahtarı → ekranda kullanılan etiket ve giriş türü. */
const FIELDS: Record<string, { label: string; type: "text" | "date" }> = {
  identityNumber: { label: "T.C. kimlik no", type: "text" },
  sgkRegistrationNumber: { label: "SGK sicil no", type: "text" },
  birthDate: { label: "Doğum tarihi", type: "date" },
  employmentStartDate: { label: "İşe giriş tarihi", type: "date" },
  phone: { label: "Telefon", type: "text" },
  jobTitle: { label: "Ünvan", type: "text" },
};

/** Bu ekrandan doldurulamayanlar — kendi akışları var. */
const ELSEWHERE: Record<string, { label: string; href: string; hint: string }> = {
  salaryCard: {
    label: "Ücret kartı",
    href: "/insan-kaynaklari/ucret-kartlari",
    hint: "Ücret kartı ayrı ekrandan açılır.",
  },
  workLocation: {
    label: "Görev yeri",
    href: "/insan-kaynaklari/personeller",
    hint: "Görev yeri personel kartından atanır.",
  },
  branchId: {
    label: "Şube",
    href: "/insan-kaynaklari/personeller",
    hint: "Şube personel kartından seçilir.",
  },
};

type Filter = "all" | "payroll" | "official" | string;

function severityVariant(severity: number) {
  if (severity === DATA_SEVERITY.PayrollBlocking) return "danger" as const;
  if (severity === DATA_SEVERITY.OfficialBlocking) return "warning" as const;
  return "default" as const;
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

export default function PersonnelDataGapsPage() {
  /**
   * Düğme -> uç -> izin:
   *   PUT hr/personnel/{id}/veri-tamamla -> personnel.edit
   */
  const actions = useModuleActions("personnel");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [summary, setSummary] = useState<PersonnelDataCompletenessSummary | null>(
    null
  );

  const [filter, setFilter] = useState<Filter>("all");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<CompletePersonnelDataRequest>({});

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setSummary(await personnelService.dataCompleteness(companyId || undefined));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId]);

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

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const rows = useMemo(() => {
    const items = summary?.items ?? [];

    if (filter === "all") return items.filter((x) => x.issues.length > 0);

    if (filter === "payroll") return items.filter((x) => !x.payrollReady);

    if (filter === "official") {
      return items.filter((x) => x.payrollReady && !x.officialReady);
    }

    return items.filter((x) => x.issues.some((issue) => issue.field === filter));
  }, [summary, filter]);

  function startEditing(person: PersonnelDataCompleteness) {
    setEditingId(person.personnelId);
    setDraft({});
    setNotice("");
    setError("");
  }

  async function save(person: PersonnelDataCompleteness) {
    const payload = Object.fromEntries(
      Object.entries(draft).filter(([, value]) => value)
    ) as CompletePersonnelDataRequest;

    if (Object.keys(payload).length === 0) {
      setEditingId(null);
      return;
    }

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await personnelService.completeData(
        person.personnelId,
        payload
      );

      setNotice(`${person.fullName}: ${result.message}`);
      setEditingId(null);
      setDraft({});
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Personel Veri Eksikleri"
      description="Eksik alanlar, engelledikleri sürece göre ve toplu tamamlama"
    >
      {error && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}
      {notice && (
        <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {notice}
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="max-w-xs">
          <Select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            options={companies.map((company) => ({
              value: company.id,
              label: `${company.code} · ${company.name}`,
            }))}
          />
        </div>

        <Link href="/insan-kaynaklari/personeller">
          <Button variant="secondary">Personel Kartları</Button>
        </Link>
      </div>

      {summary && (
        <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Metric
            label="Aktif personel"
            value={summary.total}
            hint="değerlendirilen kayıt"
          />
          <Metric
            label="Bordroya hazır"
            value={summary.payrollReadyCount}
            hint={`${summary.total - summary.payrollReadyCount} kişi giremez`}
            tone={
              summary.payrollReadyCount < summary.total ? "danger" : "ok"
            }
          />
          <Metric
            label="Resmî bildirime hazır"
            value={summary.officialReadyCount}
            hint={`${summary.total - summary.officialReadyCount} kişi eksik`}
            tone={
              summary.officialReadyCount < summary.total ? "warning" : "ok"
            }
          />
          <Metric
            label="Tam kayıt"
            value={summary.completeCount}
            hint="hiçbir eksiği yok"
            tone="ok"
          />
        </div>
      )}

      {summary && Object.keys(summary.byField).length > 0 && (
        <div className="mb-4 flex flex-wrap gap-2">
          <FilterChip active={filter === "all"} onClick={() => setFilter("all")}>
            Eksiği olan herkes
          </FilterChip>
          <FilterChip
            active={filter === "payroll"}
            onClick={() => setFilter("payroll")}
          >
            Bordroya giremez ({summary.total - summary.payrollReadyCount})
          </FilterChip>
          <FilterChip
            active={filter === "official"}
            onClick={() => setFilter("official")}
          >
            Bildirim engeli (
            {summary.payrollReadyCount - summary.officialReadyCount})
          </FilterChip>

          {Object.entries(summary.byField)
            .sort(([, a], [, b]) => b - a)
            .map(([field, count]) => (
              <FilterChip
                key={field}
                active={filter === field}
                onClick={() => setFilter(field)}
              >
                {FIELDS[field]?.label ?? ELSEWHERE[field]?.label ?? field} (
                {count})
              </FilterChip>
            ))}
        </div>
      )}

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-6 text-sm text-slate-500">
          Yükleniyor...
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          title="Bu süzgeçte eksik kayıt yok"
          description="Filtreyi değiştirin ya da tebrikler — bu alanda eksik kalmamış."
        />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Personel</TableHead>
              <TableHead>Doluluk</TableHead>
              <TableHead>Eksikler</TableHead>
              <TableHead className="w-64">Tamamla</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((person) => {
              const editable = person.issues.filter((x) => FIELDS[x.field]);
              const editing = editingId === person.personnelId;

              return (
                <TableRow key={person.personnelId}>
                  <TableCell>
                    <span className="block font-medium text-slate-800">
                      {person.fullName}
                    </span>
                    <span className="mt-1 block text-xs text-slate-500">
                      {person.employeeNumber}
                    </span>
                  </TableCell>

                  <TableCell className="whitespace-nowrap">
                    %{person.completionRate}
                  </TableCell>

                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {person.issues.map((issue) => (
                        <Badge
                          key={issue.field}
                          variant={severityVariant(issue.severity)}
                          title={issue.reason}
                        >
                          {issue.label}
                        </Badge>
                      ))}
                    </div>

                    {person.issues
                      .filter((x) => ELSEWHERE[x.field])
                      .map((issue) => (
                        <Link
                          key={issue.field}
                          href={ELSEWHERE[issue.field].href}
                          className="mt-1 block text-xs text-brand-700 underline"
                        >
                          {ELSEWHERE[issue.field].hint}
                        </Link>
                      ))}
                  </TableCell>

                  <TableCell>
                    {editable.length === 0 ? (
                      <span className="text-xs text-slate-500">
                        Bu ekrandan doldurulacak alan yok.
                      </span>
                    ) : editing ? (
                      <div className="flex flex-col gap-2">
                        {editable.map((issue) => (
                          <label key={issue.field} className="block text-xs">
                            <span className="mb-1 block text-slate-500">
                              {FIELDS[issue.field].label}
                            </span>
                            <Input
                              type={FIELDS[issue.field].type}
                              value={
                                (draft[
                                  issue.field as keyof CompletePersonnelDataRequest
                                ] as string) ?? ""
                              }
                              onChange={(event) =>
                                setDraft((current) => ({
                                  ...current,
                                  [issue.field]: event.target.value,
                                }))
                              }
                            />
                          </label>
                        ))}

                        <div className="flex gap-2">
                          {actions.can("edit") && (
                            <Button
                              disabled={busy}
                              onClick={() => void save(person)}
                            >
                              Kaydet
                            </Button>
                          )}
                          <Button
                            variant="secondary"
                            onClick={() => {
                              setEditingId(null);
                              setDraft({});
                            }}
                          >
                            Vazgeç
                          </Button>
                        </div>
                      </div>
                    ) : (
                      actions.can("edit") && (
                      <Button
                        variant="secondary"
                        onClick={() => startEditing(person)}
                      >
                        {editable.length} alanı doldur
                      </Button>
                      )
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
    </ErpShell>
  );
}

function Metric({
  label,
  value,
  hint,
  tone = "neutral",
}: {
  label: string;
  value: number;
  hint: string;
  tone?: "neutral" | "ok" | "warning" | "danger";
}) {
  const color =
    tone === "danger"
      ? "text-red-700"
      : tone === "warning"
        ? "text-amber-700"
        : tone === "ok"
          ? "text-emerald-700"
          : "text-slate-800";

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <span className="block text-xs uppercase tracking-wide text-slate-500">
        {label}
      </span>
      <strong className={`mt-1 block text-2xl ${color}`}>{value}</strong>
      <span className="mt-1 block text-xs text-slate-500">{hint}</span>
    </div>
  );
}

function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full border px-3 py-1 text-xs transition ${
        active
          ? "border-brand-700 bg-brand-700 text-white"
          : "border-slate-300 bg-white text-slate-700 hover:border-brand-600"
      }`}
    >
      {children}
    </button>
  );
}
