"use client";

import { useCallback, useEffect, useState } from "react";

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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  securityAuditService,
  type SecurityAuditEvent,
} from "@/services/security-audit.service";

/**
 * Güvenlik denetim kayıtları.
 *
 * Uç hazırdı, ekranı yoktu: kimin neyi ne zaman değiştirdiği hiçbir
 * yerden görünmüyordu.
 *
 * IP ALANI HAKKINDA AÇIK NOT VAR. Genel proxy `X-Forwarded-For`'u
 * iletmediği için login dışındaki işlemler 127.0.0.1 olarak
 * kaydediliyor. Ekran alanı olduğu gibi gösteriyor ve bunun
 * güvenilmez olduğunu söylüyor; sessizce doğruymuş gibi sunmak,
 * denetim yapan kişiyi yanıltırdı.
 *
 * Detay alanı serbest biçimli JSON; ayrıştırılmadan gösteriliyor.
 * Biçimlendirmeye çalışmak, yapısı değiştiğinde ekranı bozardı.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Kayıtlar yüklenemedi.";
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
    second: "2-digit",
  });
}

/** Uç 200'ü aşan değeri zaten kırpıyor; seçenekler onun içinde. */
const TAKE_OPTIONS = [
  { label: "Son 50", value: "50" },
  { label: "Son 100", value: "100" },
  { label: "Son 200", value: "200" },
];

export default function SecurityAuditPage() {
  const [events, setEvents] = useState<SecurityAuditEvent[]>([]);
  const [entityType, setEntityType] = useState("");
  const [take, setTake] = useState("50");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setEvents(
        await securityAuditService.getEvents({
          entityType: entityType.trim() || undefined,
          take: Number(take),
        })
      );
    } catch (err) {
      setError(messageOf(err));
      setEvents([]);
    } finally {
      setLoading(false);
    }
  }, [entityType, take]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  return (
    <ErpShell
      title="Güvenlik Denetim Kayıtları"
      description="Kimin neyi ne zaman değiştirdiği; salt okunur."
    >
      <div className="space-y-6">
        <Card>
          <CardContent className="flex flex-wrap items-end gap-4">
            <div className="min-w-56 flex-1">
              <Input
                label="Varlık türü"
                placeholder="Örn. WorkHourAccess (boş = tümü)"
                value={entityType}
                onChange={(event) => setEntityType(event.target.value)}
              />
            </div>

            <div className="w-40">
              <Select
                label="Kayıt sayısı"
                value={take}
                onChange={(event) => setTake(event.target.value)}
                options={TAKE_OPTIONS}
              />
            </div>

            <Button onClick={() => void load()} disabled={loading}>
              {loading ? "Yükleniyor..." : "Yenile"}
            </Button>
          </CardContent>
        </Card>

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}

        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <strong>IP adresi alanı şu an güvenilmez.</strong> Uygulama
          proxy&apos;si istemci adresini backend&apos;e iletmediği için giriş
          dışındaki işlemler <code>127.0.0.1</code> olarak kaydediliyor.
          Alan olduğu gibi gösteriliyor; düzeltilene kadar IP&apos;ye
          dayanarak sonuç çıkarmayın.
        </div>

        {loading ? (
          <div className="py-10 text-center text-sm text-slate-500">
            Yükleniyor...
          </div>
        ) : events.length === 0 ? (
          <EmptyState
            title="Kayıt yok"
            description="Bu filtreyle eşleşen denetim kaydı bulunmuyor."
          />
        ) : (
          <Card>
            <CardHeader>
              <div className="flex items-center gap-3">
                <h2 className="text-sm font-semibold text-slate-900">
                  Denetim kayıtları
                </h2>
                <Badge>{events.length}</Badge>
              </div>
            </CardHeader>

            <CardContent className="p-0 overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Zaman</TableHead>
                    <TableHead>Kullanıcı</TableHead>
                    <TableHead>Eylem</TableHead>
                    <TableHead>Varlık</TableHead>
                    <TableHead>IP</TableHead>
                    <TableHead>Detay</TableHead>
                  </TableRow>
                </TableHeader>

                <TableBody>
                  {events.map((event) => (
                    <TableRow key={event.id}>
                      <TableCell className="whitespace-nowrap text-sm">
                        {formatDateTime(event.occurredAtUtc)}
                      </TableCell>

                      <TableCell className="font-medium">
                        {event.actorUsername || (
                          <span className="font-normal text-slate-400">
                            Sistem
                          </span>
                        )}
                      </TableCell>

                      <TableCell>
                        <Badge variant="info">{event.action}</Badge>
                      </TableCell>

                      <TableCell className="text-sm text-slate-600">
                        {event.entityType || "—"}
                      </TableCell>

                      <TableCell className="font-mono text-xs text-slate-500">
                        {event.ipAddress || "—"}
                      </TableCell>

                      <TableCell className="max-w-md">
                        {event.detailsJson ? (
                          <details>
                            <summary className="cursor-pointer text-sm text-brand-700">
                              Göster
                            </summary>
                            <pre className="mt-1 max-h-40 overflow-auto whitespace-pre-wrap text-xs text-slate-600">
                              {event.detailsJson}
                            </pre>
                          </details>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </div>
    </ErpShell>
  );
}
