"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
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
import { offerService, type OfferListItem } from "@/services/offer.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onaya Gönderildi",
  2: "Onaylandı",
  3: "Reddedildi",
  4: "Kazanıldı",
  5: "Kaybedildi",
  6: "İptal",
};

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function statusVariant(status: number) {
  if (status === 2 || status === 4) return "success" as const;
  if (status === 1) return "warning" as const;
  if (status === 3 || status === 5 || status === 6) return "danger" as const;
  return "default" as const;
}

export default function OffersPage() {
  const [items, setItems] = useState<OfferListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");
  const [companyFilter, setCompanyFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      setItems(
        await offerService.getAll({
          companyId: companyFilter || undefined,
          status: statusFilter === "" ? undefined : Number(statusFilter),
          search: search.trim() || undefined,
        })
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Teklifler yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void (async () => {
      try {
        const [companyRows, offerRows] = await Promise.all([
          companyService.getAll(),
          offerService.getAll(),
        ]);

        setCompanies(companyRows);
        setItems(offerRows);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Teklif merkezi yüklenemedi.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const openCount = items.filter((x) => [0, 1, 2].includes(x.status)).length;
  const wonCount = items.filter((x) => x.status === 4).length;
  const totalValue = items.reduce((sum, x) => sum + x.grandTotal, 0);
  const totalProfit = items.reduce((sum, x) => sum + x.profitTotal, 0);

  function submitSearch(event: FormEvent) {
    event.preventDefault();
    void load();
  }

  return (
    <ErpShell
      title="Teklif Merkezi"
      description="Teklifleri, maliyetleri, iskontoları ve kârlılığı yönetin"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Toplam Teklif" value={loading ? "…" : items.length} icon="◫" />
        <StatCard title="Açık Teklif" value={loading ? "…" : openCount} icon="◷" />
        <StatCard title="Kazanılan" value={loading ? "…" : wonCount} icon="✓" />
        <StatCard title="Toplam Kâr" value={loading ? "…" : money(totalProfit)} icon="₺" />
      </div>

      <div className="mb-6 flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
        <form
          onSubmit={submitSearch}
          className="flex flex-1 flex-col gap-3 md:flex-row"
        >
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Teklif no, başlık veya proje ara"
          />
          <Select
            value={companyFilter}
            onChange={(event) => setCompanyFilter(event.target.value)}
            placeholder="Tüm şirketler"
            options={companies.map((x) => ({
              label: `${x.code} · ${x.name}`,
              value: x.id,
            }))}
          />
          <Select
            value={statusFilter}
            onChange={(event) => setStatusFilter(event.target.value)}
            placeholder="Tüm durumlar"
            options={Object.entries(statusLabels).map(([value, label]) => ({
              value,
              label,
            }))}
          />
          <Button type="submit" variant="secondary">
            Ara
          </Button>
        </form>

        <Link href="/teklifler/yeni">
          <Button>+ Yeni Teklif</Button>
        </Link>
      </div>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Teklif Listesi
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Taslak, onaylı ve sonuçlanan teklifler
              </p>
            </div>
            <Badge variant="info">{items.length} kayıt</Badge>
          </div>
        </CardHeader>

        <CardContent>
          {loading ? (
            <div className="py-12 text-center text-sm text-slate-500">
              Teklifler yükleniyor...
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              title="Teklif bulunamadı"
              description="İlk teklifinizi oluşturarak başlayın."
              action={
                <Link href="/teklifler/yeni">
                  <Button>Yeni Teklif</Button>
                </Link>
              }
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Teklif</TableHead>
                  <TableHead>Proje</TableHead>
                  <TableHead>Kalem</TableHead>
                  <TableHead>Maliyet</TableHead>
                  <TableHead>Kâr</TableHead>
                  <TableHead>Toplam</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {items.map((item) => {
                  const profitRate =
                    item.costTotal > 0
                      ? (item.profitTotal / item.costTotal) * 100
                      : 0;

                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <strong className="text-slate-900">
                          {item.offerNumber}
                        </strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {item.title}
                        </span>
                      </TableCell>
                      <TableCell>{item.projectName || "—"}</TableCell>
                      <TableCell>{item.itemCount}</TableCell>
                      <TableCell>{money(item.costTotal, item.currency)}</TableCell>
                      <TableCell>
                        <strong>{money(item.profitTotal, item.currency)}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          %{profitRate.toLocaleString("tr-TR", {
                            maximumFractionDigits: 2,
                          })}
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong>{money(item.grandTotal, item.currency)}</strong>
                      </TableCell>
                      <TableCell>
                        <Badge variant={statusVariant(item.status)}>
                          {statusLabels[item.status]}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-right">
                        <Link
                          href={`/teklifler/${item.id}`}
                          className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-3 text-sm font-medium text-slate-700"
                        >
                          Teklifi Aç
                        </Link>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <div className="mt-6 text-right text-sm text-slate-500">
        Toplam teklif hacmi:{" "}
        <strong className="text-slate-800">{money(totalValue)}</strong>
      </div>
    </ErpShell>
  );
}
