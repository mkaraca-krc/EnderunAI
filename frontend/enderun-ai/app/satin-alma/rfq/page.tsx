"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
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
import {
  rfqService,
  type RfqListItem,
} from "@/services/rfq.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Gönderildi",
  2: "Teklif Geldi",
  3: "Kapandı",
  4: "Sonuçlandırıldı",
  5: "İptal",
};

function statusVariant(status: number) {
  if (status === 4) return "success" as const;
  if (status === 1 || status === 2) return "warning" as const;
  if (status === 5) return "danger" as const;
  return "default" as const;
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

export default function RfqListPage() {
  const [items, setItems] = useState<RfqListItem[]>([]);
  const [statusFilter, setStatusFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load(status?: string) {
    setLoading(true);
    setError("");

    try {
      const rows = await rfqService.getAll({
        status:
          status === "" || status === undefined
            ? undefined
            : Number(status),
      });

      setItems(rows);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const openCount = items.filter(
    (item) => ![3, 4, 5].includes(item.status)
  ).length;

  const waitingResponses = items.filter(
    (item) =>
      item.status === 1 &&
      item.responseCount < item.supplierCount
  ).length;

  const completed = items.filter(
    (item) => item.status === 4
  ).length;

  return (
    <ErpShell
      title="RFQ Yönetimi"
      description="Tedarikçi teklif talepleri ve karşılaştırmaları"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard
          title="Toplam RFQ"
          value={loading ? "…" : items.length}
          icon="⌑"
        />
        <StatCard
          title="Açık RFQ"
          value={loading ? "…" : openCount}
          icon="◷"
        />
        <StatCard
          title="Yanıt Bekleyen"
          value={loading ? "…" : waitingResponses}
          icon="?"
        />
        <StatCard
          title="Sonuçlanan"
          value={loading ? "…" : completed}
          icon="✓"
        />
      </div>

      <div className="mb-6 flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div className="w-full md:max-w-xs">
          <Select
            label="Durum"
            value={statusFilter}
            onChange={(event) => {
              const value = event.target.value;
              setStatusFilter(value);
              void load(value);
            }}
            placeholder="Tüm durumlar"
            options={Object.entries(statusLabels).map(
              ([value, label]) => ({
                value,
                label,
              })
            )}
          />
        </div>

        <Link
          href="/satin-alma"
          className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          Satın Alma Taleplerine Dön
        </Link>
      </div>

      <Card>
        <CardHeader>
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              RFQ Listesi
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Açık, gönderilmiş ve sonuçlandırılmış teklif talepleri
            </p>
          </div>
        </CardHeader>

        <CardContent>
          {loading ? (
            <div className="py-12 text-center text-sm text-slate-500">
              RFQ kayıtları yükleniyor...
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              title="RFQ bulunamadı"
              description="Onaylı bir satın alma talebinden RFQ oluşturabilirsiniz."
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>RFQ</TableHead>
                  <TableHead>Kaynak Talep</TableHead>
                  <TableHead>Tarih</TableHead>
                  <TableHead>Termin</TableHead>
                  <TableHead>Kalem</TableHead>
                  <TableHead>Tedarikçi</TableHead>
                  <TableHead>Yanıt</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">
                    İşlem
                  </TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <strong className="text-slate-900">
                        {item.rfqNumber}
                      </strong>
                      <span className="mt-1 block text-xs text-slate-500">
                        {item.title}
                      </span>
                    </TableCell>

                    <TableCell>
                      <Link
                        href={`/satin-alma/${item.purchaseRequestId}`}
                        className="font-medium text-slate-700 hover:text-slate-950"
                      >
                        {item.purchaseRequestNumber}
                      </Link>
                    </TableCell>

                    <TableCell>
                      {formatDate(item.issueDate)}
                    </TableCell>

                    <TableCell>
                      {formatDate(item.responseDeadline)}
                    </TableCell>

                    <TableCell>{item.itemCount}</TableCell>
                    <TableCell>{item.supplierCount}</TableCell>

                    <TableCell>
                      {item.responseCount} / {item.supplierCount}
                    </TableCell>

                    <TableCell>
                      <Badge variant={statusVariant(item.status)}>
                        {statusLabels[item.status]}
                      </Badge>
                    </TableCell>

                    <TableCell className="text-right">
                      <Link
                        href={`/satin-alma/rfq/${item.id}`}
                        className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-3 text-sm font-medium text-slate-700 hover:bg-slate-50"
                      >
                        RFQ Aç
                      </Link>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </ErpShell>
  );
}
