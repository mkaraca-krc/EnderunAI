"use client";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  sentetikSatir,
  sentetikSayisi,
} from "@/lib/denetim/sentetik-satir";

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
  /*
   * VARSAYILAN KAPALI — denetim ekranının varsayılanı EKSİKSİZ olmalı.
   * Gizlemeyi kullanıcı bilerek seçer (2026-09-16, Mehmet Bey).
   */
  const [sentetikGizle, setSentetikGizle] = useState(false);

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

  /*
   * GİZLEME YALNIZ GÖRÜNTÜLEMEDE. Kayıt eksiksiz; `total` hâlâ
   * SUNUCUDAKİ gerçek sayı ve öyle kalıyor — gizlenen satırlar o
   * sayıdan düşülmüyor. Düşseydi ekran, kütükte olmayan bir toplam
   * gösterirdi.
   */
  const gizlenen = useMemo(() => sentetikSayisi(events), [events]);
  const gorunenSatirlar = useMemo(
    () => (sentetikGizle ? events.filter((e) => !sentetikSatir(e.actorUsername)) : events),
    [events, sentetikGizle]
  );

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

            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={sentetikGizle}
                onChange={(event) => setSentetikGizle(event.target.checked)}
                className="h-4 w-4 rounded border-slate-300"
              />
              Sonda ve ısıtma satırlarını gizle
            </label>

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

        {/*
          * UYARI ÖLÇÜMLE DEĞİŞTİ (2026-09-16, Kural 94).
          *
          * Burada "IP alanı şu an güvenilmez" yazıyordu. VEKİL/1
          * düzeltmesi 2026-09-15 22:48 UTC'de yayınlandı ve aynı aletle
          * doğrulandı: iki farklı adresten iki istek, kayda İKİ FARKLI
          * IP düştü (önce ikisi de 127.0.0.1'di).
          *
          * Ama uyarı KALDIRILMADI, DARALTILDI: düzeltmeden ÖNCEKİ
          * satırlar hâlâ 127.0.0.1 taşıyor ve asıl yanıltıcı olan onlar.
          * "Alan güvenilir" demek, o satırları da güvenilir göstermek
          * olurdu.
          */}
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <strong>15.09.2026 22:48&apos;den ÖNCEKİ satırlarda IP alanı
          güvenilmez.</strong> O tarihe kadar uygulama proxy&apos;si
          istemci adresini backend&apos;e iletmiyordu; giriş dışındaki
          işlemler <code>127.0.0.1</code> olarak kaydedildi. Düzeltme
          yayınlandı ve ölçümle doğrulandı — <strong>o tarihten sonraki
          satırların IP&apos;si gerçektir.</strong> Eski satırlar
          silinmedi; IP&apos;ye dayanarak sonuç çıkarırken tarihe bakın.
        </div>

        {/*
          * GİZLENEN SAYISI HER ZAMAN YAZILIR — sessizce düşürülmez.
          * Kaç satırın gizlendiğini görmeyen kullanıcı, eksiksiz bir
          * liste gördüğünü sanır.
          */}
        {sentetikGizle && (
          <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-2 text-sm text-slate-600">
            Bu sayfada <strong>{gizlenen}</strong> sonda/ısıtma satırı
            gizlendi. Kayıt eksiksizdir; toplam sayı ({total}) gizlenen
            satırları da içerir.
          </div>
        )}

        <DataTable
          rows={gorunenSatirlar}
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
