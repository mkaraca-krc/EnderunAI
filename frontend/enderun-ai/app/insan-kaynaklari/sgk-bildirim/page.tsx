"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
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
  payrollReadinessService,
  type SgkEntryRow,
  type SgkExitRow,
  type SgkNotificationList,
} from "@/services/payroll-readiness.service";

/**
 * SGK işe giriş / çıkış bildirim dökümü.
 *
 * DOSYA BİÇİMİ ÜRETİLMİYOR — bilinçli. Bildirim SGK'nın kendi
 * ekranına elle giriliyor; bu ekran o girişte gereken alanları
 * eksiksiz ve KOPYALANABİLİR biçimde veriyor.
 *
 * Eksik alanı olan satır ayrıca işaretleniyor: bildirimi yapılamaz
 * olan kaydı listenin içinde saklamak, kullanıcının SGK ekranında
 * yarıda kalmasına yol açardı.
 *
 * "Bildirildi" bilgisi özlük dosyasına yüklenen bildirge
 * belgesinden okunuyor; ikinci bir bayrak tutulmuyor çünkü kimsenin
 * güncellemediği bir alan üretirdi.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/** ISO tarihi gg.aa.yyyy olarak — SGK ekranına bu biçimde giriliyor. */
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

function isoDate(date: Date) {
  return date.toISOString().slice(0, 10);
}

/** Varsayılan aralık: içinde bulunulan ay. */
function defaultRange() {
  const now = new Date();
  const first = new Date(now.getFullYear(), now.getMonth(), 1);
  const last = new Date(now.getFullYear(), now.getMonth() + 1, 0);

  return { from: isoDate(first), to: isoDate(last) };
}

type Tab = "entries" | "exits";

export default function SgkNotificationsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const initial = defaultRange();
  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);

  const [data, setData] = useState<SgkNotificationList | null>(null);
  const [tab, setTab] = useState<Tab>("entries");
  const [onlyNotifiable, setOnlyNotifiable] = useState(false);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [copied, setCopied] = useState("");

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

    if (to < from) {
      setError("Bitiş tarihi başlangıçtan önce olamaz.");
      return;
    }

    setLoading(true);
    setError("");

    try {
      setData(await payrollReadinessService.sgkNotifications(companyId, from, to));
    } catch (err) {
      setError(messageOf(err));
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [companyId, from, to]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const entries = useMemo(() => {
    const rows = data?.entries ?? [];
    return onlyNotifiable
      ? rows.filter((row) => row.missingFields.length === 0)
      : rows;
  }, [data, onlyNotifiable]);

  const exits = useMemo(() => {
    const rows = data?.exits ?? [];
    return onlyNotifiable
      ? rows.filter((row) => row.missingFields.length === 0)
      : rows;
  }, [data, onlyNotifiable]);

  /**
   * Satırı SGK ekranına yapıştırılacak biçimde panoya kopyalar.
   * Kopyalama başarısız olursa sessiz kalmıyor — kullanıcı neyin
   * olmadığını bilmeli.
   */
  async function copyRow(id: string, fields: (string | null)[]) {
    try {
      await navigator.clipboard.writeText(
        fields.map((field) => field ?? "").join("\t")
      );
      setCopied(id);
      window.setTimeout(() => setCopied(""), 2000);
    } catch {
      setError("Panoya kopyalanamadı. Tarayıcı izni gerekebilir.");
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="SGK Bildirim Dökümü"
      description="İşe giriş ve çıkış bildirimleri için SGK ekranına girilecek alanlar."
    >
      {/* Bildirim durumu SGK tarafında değişiyor. */}
      <div className="mb-4 flex justify-end">
        <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
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

            <div className="w-44">
              <Input
                label="Başlangıç"
                type="date"
                value={from}
                onChange={(event) => setFrom(event.target.value)}
              />
            </div>

            <div className="w-44">
              <Input
                label="Bitiş"
                type="date"
                value={to}
                onChange={(event) => setTo(event.target.value)}
              />
            </div>

            <Button onClick={() => void load()} disabled={loading || !companyId}>
              {loading ? "Yükleniyor..." : "Listele"}
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
            <div className="grid gap-4 sm:grid-cols-3">
              <StatCard title="İşe giriş" value={data.entryCount} />
              <StatCard title="İşten çıkış" value={data.exitCount} />
              <StatCard
                title="Bildirilemeyen"
                value={data.notNotifiableCount}
                description="Zorunlu alanı eksik olan satırlar"
              />
            </div>

            <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
              {data.note}
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex gap-2">
                <Button
                  variant={tab === "entries" ? "primary" : "secondary"}
                  onClick={() => setTab("entries")}
                >
                  İşe Giriş ({data.entryCount})
                </Button>
                <Button
                  variant={tab === "exits" ? "primary" : "secondary"}
                  onClick={() => setTab("exits")}
                >
                  İşten Çıkış ({data.exitCount})
                </Button>
              </div>

              <label className="flex items-center gap-2 text-sm text-slate-600">
                <input
                  type="checkbox"
                  checked={onlyNotifiable}
                  onChange={(event) => setOnlyNotifiable(event.target.checked)}
                  className="h-4 w-4 rounded border-slate-300"
                />
                Yalnızca bildirilebilecekler
              </label>
            </div>

            {tab === "entries" && (
              <EntryTable
                rows={entries}
                copiedId={copied}
                onCopy={copyRow}
              />
            )}

            {tab === "exits" && (
              <ExitTable rows={exits} copiedId={copied} onCopy={copyRow} />
            )}
          </>
        )}

        {!data && !loading && !error && (
          <EmptyState
            title="Aralık seçin"
            description="Şirket ve tarih aralığı seçip listeleyin."
          />
        )}
      </div>
    </ErpShell>
  );
}

/** Eksik alan rozetleri — satır neden bildirilemez, tek bakışta. */
function MissingCell({ fields }: { fields: string[] }) {
  if (fields.length === 0) {
    return <Badge variant="success">Hazır</Badge>;
  }

  return (
    <div className="flex flex-wrap gap-1">
      {fields.map((field) => (
        <Badge key={field} variant="danger">
          {field}
        </Badge>
      ))}
    </div>
  );
}

function EntryTable({
  rows,
  copiedId,
  onCopy,
}: {
  rows: SgkEntryRow[];
  copiedId: string;
  onCopy: (id: string, fields: (string | null)[]) => void;
}) {
  if (rows.length === 0) {
    return (
      <EmptyState
        title="İşe giriş yok"
        description="Seçilen aralıkta bildirilecek işe giriş bulunmuyor."
      />
    );
  }

  return (
    <Card>
      <CardHeader>
        <h2 className="text-sm font-semibold text-slate-900">
          İşe giriş bildirimleri
        </h2>
      </CardHeader>

      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Ad Soyad</TableHead>
              <TableHead>T.C. Kimlik</TableHead>
              <TableHead>Doğum</TableHead>
              <TableHead>SGK Sicil</TableHead>
              <TableHead>Giriş Tarihi</TableHead>
              <TableHead>Ünvan</TableHead>
              <TableHead>Durum</TableHead>
              <TableHead className="text-right">İşlem</TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell className="font-medium">
                  {row.fullName}
                  {row.employeeNumber && (
                    <span className="ml-2 text-xs text-slate-400">
                      {row.employeeNumber}
                    </span>
                  )}
                </TableCell>
                <TableCell className="font-mono text-sm">
                  {row.identityNumber || "—"}
                </TableCell>
                <TableCell>{formatDate(row.birthDate)}</TableCell>
                <TableCell className="font-mono text-sm">
                  {row.sgkRegistrationNumber || "—"}
                </TableCell>
                <TableCell>{formatDate(row.date)}</TableCell>
                <TableCell>{row.jobTitle || "—"}</TableCell>
                <TableCell>
                  <div className="space-y-1">
                    <MissingCell fields={row.missingFields} />
                    {row.noticeUploaded && (
                      <Badge variant="info">Bildirge yüklü</Badge>
                    )}
                  </div>
                </TableCell>
                <TableCell className="text-right">
                  {row.missingFields.length > 0 ? (
                    <Link
                      href="/insan-kaynaklari/veri-eksikleri"
                      className="text-sm font-medium text-brand-700 underline"
                    >
                      Eksikleri tamamla
                    </Link>
                  ) : (
                    <Button
                      variant="secondary"
                      onClick={() =>
                        onCopy(row.id, [
                          row.fullName,
                          row.identityNumber,
                          formatDate(row.birthDate),
                          row.sgkRegistrationNumber,
                          formatDate(row.date),
                          row.jobTitle,
                        ])
                      }
                    >
                      {copiedId === row.id ? "Kopyalandı" : "Kopyala"}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function ExitTable({
  rows,
  copiedId,
  onCopy,
}: {
  rows: SgkExitRow[];
  copiedId: string;
  onCopy: (id: string, fields: (string | null)[]) => void;
}) {
  if (rows.length === 0) {
    return (
      <EmptyState
        title="İşten çıkış yok"
        description="Seçilen aralıkta bildirilecek işten çıkış bulunmuyor."
      />
    );
  }

  return (
    <Card>
      <CardHeader>
        <h2 className="text-sm font-semibold text-slate-900">
          İşten çıkış bildirimleri
        </h2>
      </CardHeader>

      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Ad Soyad</TableHead>
              <TableHead>T.C. Kimlik</TableHead>
              <TableHead>Doğum</TableHead>
              <TableHead>SGK Sicil</TableHead>
              <TableHead>Çıkış Tarihi</TableHead>
              <TableHead>Çıkış Kodu</TableHead>
              <TableHead>Durum</TableHead>
              <TableHead className="text-right">İşlem</TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.personnelId}>
                <TableCell className="font-medium">
                  {row.fullName}
                  {row.employeeNumber && (
                    <span className="ml-2 text-xs text-slate-400">
                      {row.employeeNumber}
                    </span>
                  )}
                </TableCell>
                <TableCell className="font-mono text-sm">
                  {row.identityNumber || "—"}
                </TableCell>
                <TableCell>{formatDate(row.birthDate)}</TableCell>
                <TableCell className="font-mono text-sm">
                  {row.sgkRegistrationNumber || "—"}
                </TableCell>
                <TableCell>{formatDate(row.date)}</TableCell>
                <TableCell>
                  <span className="font-mono text-sm">{row.reason}</span>
                  <span className="ml-2 text-xs text-slate-500">
                    {row.reasonName}
                  </span>
                </TableCell>
                <TableCell>
                  <div className="space-y-1">
                    <MissingCell fields={row.missingFields} />
                    {!row.isFinalized && (
                      <Badge variant="warning">Kesinleşmedi</Badge>
                    )}
                    {row.noticeUploaded && (
                      <Badge variant="info">Bildirge yüklü</Badge>
                    )}
                  </div>
                </TableCell>
                <TableCell className="text-right">
                  {row.missingFields.length > 0 ? (
                    <Link
                      href="/insan-kaynaklari/veri-eksikleri"
                      className="text-sm font-medium text-brand-700 underline"
                    >
                      Eksikleri tamamla
                    </Link>
                  ) : (
                    <Button
                      variant="secondary"
                      onClick={() =>
                        onCopy(row.personnelId, [
                          row.fullName,
                          row.identityNumber,
                          formatDate(row.birthDate),
                          row.sgkRegistrationNumber,
                          formatDate(row.date),
                          String(row.reason),
                        ])
                      }
                    >
                      {copiedId === row.personnelId ? "Kopyalandı" : "Kopyala"}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
