"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { amount } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  ConfirmDialog,
  EmptyState,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import {
  hakedisFileService,
  hakedisFileUrl,
  type HakedisAnalysis,
  type HakedisFile,
} from "@/services/hakedis-file.service";

/**
 * Hakediş dosyaları ve otomatik okuma.
 *
 * Uçların tamamı (yükleme, liste, analiz, indirme, silme) aylardır
 * hazırdı ve hiçbiri çağrılmıyordu. Yalnız analizi bağlamak
 * yetmezdi: kullanıcı analiz edeceği dosyayı hiçbir yerde
 * göremiyordu.
 *
 * ANALİZ SONUCU ÖNERİDİR, KAYIT DEĞİL. Ekran bunu üç şekilde
 * söylüyor: güven skorunu gösteriyor, uçtan gelen uyarıları
 * listeliyor ve hiçbir alanı hakedişe otomatik taşımıyor. Düşük
 * güvenli bir okumayı sessizce "kesin bilgi" gibi sunmak, yanlış
 * tutarın hakedişe girmesine yol açardı.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function money(value: number | null) {
  if (value === null) return "—";

  return amount(value);
}

export default function HakedisFilesPage() {
  const { has } = usePermissions();
  const canUpload = has("hakedis.create");
  const canDelete = has("hakedis.delete");

  const [files, setFiles] = useState<HakedisFile[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [analysis, setAnalysis] = useState<HakedisAnalysis | null>(null);
  const [pendingDelete, setPendingDelete] = useState<HakedisFile | null>(null);

  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setFiles(await hakedisFileService.list());
    } catch (err) {
      setError(messageOf(err));
      setFiles([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  async function upload(file: File) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await hakedisFileService.upload(file);

      // Yükleme ucu analizi birlikte döndürüyor; ikinci bir çağrı
      // yapmak aynı işi iki kez yaptırırdı.
      setAnalysis(result.analysis);
      setNotice(`${result.file.originalName} yüklendi ve okundu.`);
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  async function analyze(file: HakedisFile) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      setAnalysis(await hakedisFileService.analyze(file.storedName));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(file: HakedisFile) {
    setBusy(true);
    setError("");

    try {
      const result = await hakedisFileService.remove(file.storedName);
      setNotice(result.message);
      setPendingDelete(null);
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
      title="Hakediş Dosyaları"
      description="Yüklenen hakediş dosyaları ve otomatik okuma sonuçları."
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/hakedis" className="hover:text-slate-900">
          Hakedişler
        </Link>
        <span>›</span>
        <strong className="text-slate-800">Dosyalar</strong>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {notice && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {notice}
        </div>
      )}

      {canUpload && (
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              Dosya yükle
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              PDF ya da Excel. Yüklenen dosya hemen okunur; çıkan bilgiler
              öneridir, hiçbir kayda otomatik işlenmez.
            </p>
          </CardHeader>

          <CardContent>
            <input
              ref={fileInput}
              type="file"
              disabled={busy}
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file) void upload(file);
              }}
              className="block w-full text-sm"
            />
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold text-slate-900">
              Yüklenen dosyalar
            </h2>
            <Badge>{files.length}</Badge>
          </div>
        </CardHeader>

        <CardContent className="p-0">
          {loading ? (
            <div className="py-10 text-center text-sm text-slate-500">
              Yükleniyor...
            </div>
          ) : files.length === 0 ? (
            <div className="p-6">
              <EmptyState
                title="Dosya yok"
                description="Henüz hakediş dosyası yüklenmemiş."
              />
            </div>
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Dosya</TableHead>
                    <TableHead className="text-right">Boyut</TableHead>
                    <TableHead>Yüklenme</TableHead>
                    <TableHead className="text-right">İşlem</TableHead>
                  </TableRow>
                </TableHeader>

                <TableBody>
                  {files.map((file) => (
                    <TableRow key={file.storedName}>
                      <TableCell className="font-medium">
                        {file.originalName}
                        <span className="ml-2 text-xs uppercase text-slate-400">
                          {file.extension.replace(".", "")}
                        </span>
                      </TableCell>

                      <TableCell className="text-right tabular-nums">
                        {formatSize(file.size)}
                      </TableCell>

                      <TableCell>{formatDateTime(file.uploadedAtUtc)}</TableCell>

                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-3">
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => void analyze(file)}
                            className="text-sm font-medium text-brand-700 underline disabled:opacity-50"
                          >
                            Oku
                          </button>

                          <a
                            href={hakedisFileUrl(file.storedName)}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-sm font-medium text-brand-700 underline"
                          >
                            İndir
                          </a>

                          {canDelete && (
                            <button
                              type="button"
                              disabled={busy}
                              onClick={() => setPendingDelete(file)}
                              className="text-sm font-medium text-red-600 disabled:opacity-50"
                            >
                              Sil
                            </button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      <Modal
        open={analysis !== null}
        title="Dosya okuma sonucu"
        description="Çıkan bilgiler öneridir; hakedişe elle girilir."
        size="lg"
        onClose={() => setAnalysis(null)}
        footer={
          <div className="flex justify-end">
            <Button variant="secondary" onClick={() => setAnalysis(null)}>
              Kapat
            </Button>
          </div>
        }
      >
        {analysis && (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge
                variant={
                  analysis.confidence >= 0.8
                    ? "success"
                    : analysis.confidence >= 0.5
                      ? "warning"
                      : "danger"
                }
              >
                Güven: %{Math.round(analysis.confidence * 100)}
              </Badge>

              {analysis.requiresOcr && (
                <Badge variant="warning">Taranmış belge (OCR gerekti)</Badge>
              )}
            </div>

            {analysis.warnings.length > 0 && (
              <ul className="space-y-1 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                {analysis.warnings.map((warning) => (
                  <li key={warning}>▲ {warning}</li>
                ))}
              </ul>
            )}

            <dl className="divide-y divide-slate-200 rounded-lg border border-slate-200 text-sm">
              <Field label="Proje" value={analysis.project} />
              <Field label="İşveren" value={analysis.employer} />
              <Field label="Hakediş no" value={analysis.progressPaymentNo} />
              <Field label="Dönem" value={analysis.period} />
              <Field
                label="Tutar (KDV hariç)"
                value={money(analysis.amountExcludingVat)}
              />
              <Field
                label="KDV oranı"
                value={
                  analysis.vatRate !== null ? `%${analysis.vatRate}` : null
                }
              />
              <Field label="KDV tutarı" value={money(analysis.vatAmount)} />
              <Field
                label="Önerilen stopaj"
                value={analysis.suggestedWithholding}
              />
            </dl>

            {analysis.extractedText && (
              <details className="rounded-lg border border-slate-200 p-3">
                <summary className="cursor-pointer text-sm font-medium text-slate-700">
                  Okunan ham metin
                </summary>
                <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap text-xs text-slate-600">
                  {analysis.extractedText}
                </pre>
              </details>
            )}
          </div>
        )}
      </Modal>

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Dosyayı sil"
        description={
          pendingDelete
            ? `"${pendingDelete.originalName}" kalıcı olarak silinecek.`
            : ""
        }
        confirmLabel="Sil"
        busy={busy}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => {
          if (pendingDelete) void remove(pendingDelete);
        }}
      />
    </ErpShell>
  );
}

function Field({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="flex items-center justify-between gap-4 px-3 py-2">
      <dt className="text-slate-600">{label}</dt>
      <dd className="text-right font-medium text-slate-900">
        {value || <span className="font-normal text-slate-400">Okunamadı</span>}
      </dd>
    </div>
  );
}
