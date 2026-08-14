"use client";

import { useCallback, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { Badge, Button, Card, CardContent } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  accessRequestService,
  type AccessRequestListItem,
} from "@/services/access-request.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

const dateTimeFormat = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});

function statusBadge(status: AccessRequestListItem["status"]) {
  if (status === 1) return <Badge variant="success">Onaylandı</Badge>;
  if (status === 2) return <Badge variant="danger">Reddedildi</Badge>;
  return <Badge variant="warning">Bekliyor</Badge>;
}

export default function AccessRequestsPage() {
  const [items, setItems] = useState<AccessRequestListItem[]>([]);
  const [includeDecided, setIncludeDecided] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [processingId, setProcessingId] = useState<string | null>(null);
  const [durationDrafts, setDurationDrafts] = useState<Record<string, number>>({});
  const [rejectionDrafts, setRejectionDrafts] = useState<Record<string, string>>({});

  const load = useCallback(async (withDecided: boolean) => {
    setLoading(true);
    setError("");
    try {
      const result = await accessRequestService.getAll(withDecided);
      setItems(result);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load(includeDecided);
  }, [load, includeDecided]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  async function approve(id: string) {
    setProcessingId(id);
    setError("");
    try {
      const result = await accessRequestService.approve(id, durationDrafts[id]);
      setNotice(result.message);
      await load(includeDecided);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setProcessingId(null);
    }
  }

  async function reject(id: string) {
    setProcessingId(id);
    setError("");
    try {
      const result = await accessRequestService.reject(id, rejectionDrafts[id]);
      setNotice(result.message);
      await load(includeDecided);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setProcessingId(null);
    }
  }

  const pendingCount = items.filter((item) => item.status === 0).length;

  return (
    <ErpShell
      design="redwood"
      title="Erişim Talepleri"
      description="Mesai saati dışında girmek isteyen kullanıcıların gerekçeli talepleri"
    >
      <div className="space-y-6">
        {/* Talepler mesai dışında geliyor; bekleyen liste tazelenmeden eskiyordu. */}
        <div className="flex justify-end">
          <button
            type="button"
            className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
            onClick={() => void load(includeDecided)}
          >
            Yenile
          </button>
        </div>

        {error && (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        <div className="flex items-center justify-between">
          <p className="text-sm text-slate-500">
            {pendingCount > 0
              ? `${pendingCount} bekleyen talep`
              : "Bekleyen talep yok"}
          </p>
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              checked={includeDecided}
              onChange={(e) => setIncludeDecided(e.target.checked)}
            />
            Karara bağlanmış talepleri de göster
          </label>
        </div>

        {loading ? (
          <div className="rounded-xl border border-slate-200 bg-white py-16 text-center text-sm text-slate-500">
            Yükleniyor...
          </div>
        ) : items.length === 0 ? (
          <div className="rounded-xl border border-slate-200 bg-white py-16 text-center text-sm text-slate-500">
            Gösterilecek talep yok.
          </div>
        ) : (
          <div className="grid gap-4">
            {items.map((item) => (
              <Card key={item.id}>
                <CardContent className="p-5">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <strong className="text-slate-950">{item.fullName}</strong>
                        <span className="text-xs text-slate-400">
                          @{item.username}
                        </span>
                        {statusBadge(item.status)}
                      </div>
                      <p className="mt-1.5 text-sm text-slate-600">{item.reason}</p>
                      <p className="mt-1.5 text-xs text-slate-400">
                        Talep: {dateTimeFormat.format(new Date(item.createdAtUtc))}
                        {item.decidedAtUtc &&
                          ` · Karar: ${dateTimeFormat.format(new Date(item.decidedAtUtc))}`}
                        {item.status === 1 &&
                          item.grantedDurationMinutes != null &&
                          ` · ${item.grantedDurationMinutes} dakika erişim tanındı`}
                        {item.status === 2 &&
                          item.rejectionReason &&
                          ` · Gerekçe: ${item.rejectionReason}`}
                      </p>
                    </div>

                    {item.status === 0 && (
                      <div className="flex flex-wrap items-center gap-2">
                        <input
                          type="number"
                          min={5}
                          placeholder="120"
                          className="w-20 rounded-lg border border-slate-300 px-2 py-1.5 text-xs"
                          value={durationDrafts[item.id] ?? ""}
                          onChange={(e) =>
                            setDurationDrafts((current) => ({
                              ...current,
                              [item.id]: Number(e.target.value) || 0,
                            }))
                          }
                        />
                        <span className="text-xs text-slate-400">dk</span>
                        <Button
                          type="button"
                          loading={processingId === item.id}
                          onClick={() => void approve(item.id)}
                          className="text-xs"
                        >
                          Onayla
                        </Button>
                        <input
                          type="text"
                          placeholder="Ret gerekçesi (ops.)"
                          className="w-40 rounded-lg border border-slate-300 px-2 py-1.5 text-xs"
                          value={rejectionDrafts[item.id] ?? ""}
                          onChange={(e) =>
                            setRejectionDrafts((current) => ({
                              ...current,
                              [item.id]: e.target.value,
                            }))
                          }
                        />
                        <button
                          type="button"
                          disabled={processingId === item.id}
                          onClick={() => void reject(item.id)}
                          className="text-xs text-red-600 hover:underline disabled:opacity-50"
                        >
                          Reddet
                        </button>
                      </div>
                    )}
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </div>
    </ErpShell>
  );
}
