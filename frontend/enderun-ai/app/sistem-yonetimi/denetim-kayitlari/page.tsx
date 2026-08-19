"use client";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  Input,
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

  // SANİYE DAHİL: denetim kaydında iki işlemin sırası saniyeyle
  // ayrılabiliyor. Paylaşılan `dateTime` dakika bazlı olduğu için
  // burada kullanılmıyor — biçim bilinçli olarak farklı.
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
/**
 * SÜTUNLAR — dosyaya giden değer ekrandaki rozet/açılır detaydan ayrı.
 *
 * Denetim kütüğünün dışa aktarılması gerçek bir ihtiyaç: bir olayı
 * incelerken kayıtları başka bir yere taşımak isteniyor.
 */
const columns: DataTableColumn<SecurityAuditEvent>[] = [
  {
    key: "zaman",
    header: "Zaman",
    value: (event) => formatDateTime(event.occurredAtUtc),
  },
  {
    key: "kullanici",
    header: "Kullanıcı",
    value: (event) => event.actorUsername || "Sistem",
    render: (event) =>
      event.actorUsername || (
        <span className="font-normal text-slate-400">Sistem</span>
      ),
  },
  {
    key: "eylem",
    header: "Eylem",
    value: (event) => event.action,
    render: (event) => <Badge variant="info">{event.action}</Badge>,
  },
  {
    key: "varlik",
    header: "Varlık",
    value: (event) => event.entityType || "—",
  },
  {
    key: "ip",
    header: "IP",
    value: (event) => event.ipAddress || "—",
  },
  {
    key: "detay",
    header: "Detay",
    // Dosyada ham JSON tek hücrede; ekranda açılır kapanır.
    value: (event) => event.detailsJson || "",
    render: (event) =>
      event.detailsJson ? (
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
      ),
  },
];

export default function SecurityAuditPage() {
  const [events, setEvents] = useState<SecurityAuditEvent[]>([]);
  const [entityType, setEntityType] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  /* Kütüphanedeki gerçek kayıt sayısı — listelenen kayıt sayısı DEĞİL. */
  const [total, setTotal] = useState(0);
  /* Sayfa sunucuda atlanıyor: kütük yalnız büyür. */
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const result = await securityAuditService.getEvents({
        entityType: entityType.trim() || undefined,
        take: pageSize,
        page,
      });

      setEvents(result.items);
      setTotal(result.total);
    } catch (err) {
      setError(messageOf(err));
      setEvents([]);
      setTotal(0);
    } finally {
      setLoading(false);
    }
  }, [entityType, pageSize, page]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  return (
    <ErpShell
      design="redwood"
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
                onChange={(event) => {
                  // Filtre değişince sayfa 1'e döner; yoksa uçtan boş
                  // sayfa gelir.
                  setEntityType(event.target.value);
                  setPage(1);
                }}
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

        <DataTable
          rows={events}
          columns={columns}
          rowKey={(event) => event.id}
          loading={loading}
          title="Denetim Kayıtları"
          emptyText="Bu filtreyle eşleşen denetim kaydı bulunmuyor."
          server={{
            total,
            page,
            pageSize,
            onChange: (nextPage, nextSize) => {
              setPage(nextPage);
              setPageSize(nextSize);
            },
          }}
          resetKey={entityType}
        />
      </div>
    </ErpShell>
  );
}
