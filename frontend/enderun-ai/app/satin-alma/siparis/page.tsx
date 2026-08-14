"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney } from "@/lib/format/turkish";
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
  purchaseOrderService,
  type PurchaseOrderListItem,
} from "@/services/purchase-order.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Kısmi Teslim",
  4: "Tamamlandı",
  5: "İptal",
  6: "Reddedildi",
};

function statusVariant(status: number) {
  if (status === 2 || status === 4) return "success" as const;
  if (status === 1 || status === 3) return "warning" as const;
  if (status === 5 || status === 6) return "danger" as const;
  return "default" as const;
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

function formatMoney(value: number, currency: string) {
  return currencyMoney(value, currency);
}

export default function PurchaseOrderListPage() {
  const [items, setItems] = useState<PurchaseOrderListItem[]>([]);
  const [statusFilter, setStatusFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");
  const [supplierFilter, setSupplierFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result = await purchaseOrderService.getAll({
        status:
          statusFilter === ""
            ? undefined
            : Number(statusFilter),
      });

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Satın alma siparişleri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [statusFilter]);

  const projectOptions = useMemo(() => {
    const map = new Map<string, string>();

    for (const item of items) {
      map.set(
        item.projectId,
        `${item.projectCode} · ${item.projectName}`
      );
    }

    return Array.from(map.entries()).map(([value, label]) => ({
      value,
      label,
    }));
  }, [items]);

  const supplierOptions = useMemo(() => {
    const map = new Map<string, string>();

    for (const item of items) {
      map.set(
        item.supplierCurrentAccountId,
        `${item.supplierCode} · ${item.supplierTitle}`
      );
    }

    return Array.from(map.entries()).map(([value, label]) => ({
      value,
      label,
    }));
  }, [items]);

  const filteredItems = useMemo(
    () =>
      items.filter((item) => {
        if (
          projectFilter &&
          item.projectId !== projectFilter
        ) {
          return false;
        }

        if (
          supplierFilter &&
          item.supplierCurrentAccountId !== supplierFilter
        ) {
          return false;
        }

        return true;
      }),
    [items, projectFilter, supplierFilter]
  );

  const pendingApprovalCount = items.filter(
    (item) => item.status === 1
  ).length;

  const approvedCount = items.filter(
    (item) => item.status === 2
  ).length;

  const completedCount = items.filter(
    (item) => item.status === 4
  ).length;

  return (
    <ErpShell
      design="redwood"
      title="Satın Alma Siparişleri"
      description="Kazanan tedarikçi tekliflerinden oluşturulan siparişler"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard
          title="Toplam Sipariş"
          value={loading ? "…" : items.length}
          icon="▤"
        />

        <StatCard
          title="Onay Bekleyen"
          value={loading ? "…" : pendingApprovalCount}
          icon="◷"
        />

        <StatCard
          title="Onaylanan"
          value={loading ? "…" : approvedCount}
          icon="✓"
        />

        <StatCard
          title="Tamamlanan"
          value={loading ? "…" : completedCount}
          icon="■"
        />
      </div>

      <Card className="mb-6">
        <CardHeader>
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              Filtreler
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Siparişleri durum, proje ve tedarikçiye göre filtreleyin
            </p>
          </div>
        </CardHeader>

        <CardContent>
          <div className="grid gap-4 md:grid-cols-3">
            <Select
              label="Durum"
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(event.target.value)
              }
              placeholder="Tüm durumlar"
              options={Object.entries(statusLabels).map(
                ([value, label]) => ({
                  value,
                  label,
                })
              )}
            />

            <Select
              label="Proje"
              value={projectFilter}
              onChange={(event) =>
                setProjectFilter(event.target.value)
              }
              placeholder="Tüm projeler"
              options={projectOptions}
            />

            <Select
              label="Tedarikçi"
              value={supplierFilter}
              onChange={(event) =>
                setSupplierFilter(event.target.value)
              }
              placeholder="Tüm tedarikçiler"
              options={supplierOptions}
            />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              Sipariş Listesi
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Toplam {filteredItems.length} kayıt
            </p>
          </div>

          <Link
            href="/satin-alma/rfq"
            className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            RFQ Yönetimine Dön
          </Link>
        </CardHeader>

        <CardContent>
          {loading ? (
            <div className="py-12 text-center text-sm text-slate-500">
              Siparişler yükleniyor...
            </div>
          ) : filteredItems.length === 0 ? (
            <EmptyState
              title="Satın alma siparişi bulunamadı"
              description="Award edilmiş bir RFQ üzerinden sipariş oluşturabilirsiniz."
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Sipariş</TableHead>
                  <TableHead>RFQ</TableHead>
                  <TableHead>Proje</TableHead>
                  <TableHead>Tedarikçi</TableHead>
                  <TableHead>Tarih</TableHead>
                  <TableHead>Beklenen Teslim</TableHead>
                  <TableHead>Kalem</TableHead>
                  <TableHead className="text-right">
                    Toplam
                  </TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">
                    İşlem
                  </TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {filteredItems.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <strong className="text-slate-900">
                        {item.orderNumber}
                      </strong>
                    </TableCell>

                    <TableCell>
                      <Link
                        href={`/satin-alma/rfq/${item.rfqId}`}
                        className="font-medium text-slate-700 hover:text-slate-950"
                      >
                        {item.rfqNumber}
                      </Link>
                    </TableCell>

                    <TableCell>
                      <strong className="block text-slate-900">
                        {item.projectCode}
                      </strong>
                      <span className="mt-1 block text-xs text-slate-500">
                        {item.projectName}
                      </span>
                    </TableCell>

                    <TableCell>
                      <strong className="block text-slate-900">
                        {item.supplierTitle}
                      </strong>
                      <span className="mt-1 block text-xs text-slate-500">
                        {item.supplierCode}
                      </span>
                    </TableCell>

                    <TableCell>
                      {formatDate(item.orderDate)}
                    </TableCell>

                    <TableCell>
                      {formatDate(item.expectedDeliveryDate)}
                    </TableCell>

                    <TableCell>{item.itemCount}</TableCell>

                    <TableCell className="text-right font-semibold">
                      {formatMoney(
                        item.grandTotal,
                        item.currency
                      )}
                    </TableCell>

                    <TableCell>
                      <Badge variant={statusVariant(item.status)}>
                        {statusLabels[item.status]}
                      </Badge>
                    </TableCell>

                    <TableCell className="text-right">
                      <Link
                        href={`/satin-alma/siparis/${item.id}`}
                        className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-3 text-sm font-medium text-slate-700 hover:bg-slate-50"
                      >
                        Siparişi Aç
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
