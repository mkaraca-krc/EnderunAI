"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { currencyMoney, decimalRange, percent, quantity, unitPrice } from "@/lib/format/turkish";
import {
  Badge,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import OfferChainPanel from "@/components/offers/offer-chain-panel";
import {
  OFFER_STATUS_LABELS,
  offerService,
  type OfferDetail,
} from "@/services/offer.service";

const statusLabels: Record<number, string> = {
  ...OFFER_STATUS_LABELS,
};

function statusVariant(status: number) {
  if (status === 2 || status === 4) return "success" as const;
  if (status === 1) return "warning" as const;
  if ([3, 5, 6].includes(status)) return "danger" as const;
  return "default" as const;
}

function money(value: number, currency: string) {
  return currencyMoney(value, currency);
}

function date(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function OfferDetailPage() {
  /**
   * Düğme -> uç -> izin:
   *   POST offers/{id}/icmale-aktar -> ENGINEERING.manage
   *
   * Teklif ekranında ama izni MÜHENDİSLİK modülünde: icmal (poz/metraj
   * defteri) mühendislik verisi. offer_tracking.* demek yanlış olurdu —
   * o, teklifin ticari takibi.
   */
  const actions = useModuleActions("engineering");

  const params = useParams<{ id: string }>();
  const offerId = params.id;
  const [item, setItem] = useState<OfferDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [transferring, setTransferring] = useState(false);
  const [transferMessage, setTransferMessage] = useState("");
  const [transferWarnings, setTransferWarnings] = useState<string[]>([]);

  /**
   * Teklifi keşif icmaline aktarır. Aktarım TEK YÖNLÜdür: sonrasında
   * teklifte yapılan düzeltme icmali değiştirmez, çünkü icmal
   * hakedişin referansıdır.
   */
  async function handleTransfer() {
    setTransferring(true);
    setTransferMessage("");
    setTransferWarnings([]);

    try {
      const result = await offerService.transferToBoq(offerId);

      setTransferMessage(
        `İcmal oluşturuldu: ${result.boqNumber} — ${result.itemCount} kalem.`
      );
      setTransferWarnings(result.warnings);
    } catch (err) {
      setError(err instanceof Error ? err.message : "İcmale aktarılamadı.");
    } finally {
      setTransferring(false);
    }
  }

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      setItem(await offerService.getById(params.id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Teklif yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const profitRate =
    item && item.costTotal > 0
      ? (item.profitTotal / item.costTotal) * 100
      : 0;

  return (
    <ErpShell
      design="redwood"
      title={item?.offerNumber ?? "Teklif"}
      description={item?.title ?? "Teklif detayları"}
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/teklifler" className="hover:text-slate-900">
          Teklif Merkezi
        </Link>
        <span>›</span>
        <strong className="text-slate-800">
          {item?.offerNumber ?? "Teklif"}
        </strong>

        <span style={{ marginLeft: "auto", display: "flex", gap: 8 }}>
          <Link className="erp-secondary-button" href={`/teklifler/${offerId}/yazdir`}>
            Yazdır
          </Link>
          {actions.can("manage") && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={transferring}
              onClick={() => void handleTransfer()}
            >
              {transferring ? "Aktarılıyor..." : "İcmale Aktar"}
            </button>
          )}
        </span>
      </div>

      {transferMessage && (
        <div className="erp-alert success">{transferMessage}</div>
      )}

      {transferWarnings.map((warning) => (
        <div key={warning} className="erp-alert warning">
          {warning}
        </div>
      ))}

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {loading ? (
        <Card>
          <CardContent className="py-12 text-center text-sm text-slate-500">
            Teklif yükleniyor...
          </CardContent>
        </Card>
      ) : !item ? (
        <EmptyState
          title="Teklif bulunamadı"
          description="Kayıt silinmiş veya erişiminiz olmayabilir."
        />
      ) : (
        <>
          <Card className="mb-6">
            <CardContent className="py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <Badge variant={statusVariant(item.status)}>
                    {statusLabels[item.status]}
                  </Badge>
                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    {item.offerNumber}
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    {item.title}
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  <Link
                    href="/teklifler/fiyatlar"
                    className="inline-flex h-10 items-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700"
                  >
                    Fiyat Karşılaştır
                  </Link>
                </div>
              </div>
            </CardContent>
          </Card>

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard
              title="Liste Toplamı"
              value={money(item.subtotal, item.currency)}
              icon="L"
            />
            <StatCard
              title="Maliyet"
              value={money(item.costTotal, item.currency)}
              icon="M"
            />
            <StatCard
              title="Beklenen Kâr"
              value={money(item.profitTotal, item.currency)}
              icon="%"
            />
            <StatCard
              title="Teklif Toplamı"
              value={money(item.grandTotal, item.currency)}
              icon="₺"
            />
          </div>

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader>
                <h2 className="text-lg font-semibold text-slate-900">
                  Teklif Bilgileri
                </h2>
              </CardHeader>
              <CardContent>
                <div className="grid gap-5 md:grid-cols-2">
                  <Info label="Şirket" value={item.companyName} />
                  <Info label="Proje" value={item.projectName || "—"} />
                  <Info label="Teklif Tarihi" value={date(item.offerDate)} />
                  <Info
                    label="Geçerlilik Tarihi"
                    value={date(item.validUntil)}
                  />
                  <Info label="Para Birimi" value={item.currency} />
                  <Info
                    label="Kur"
                    value={decimalRange(item.exchangeRate, 4, 4)}
                  />
                  <div className="md:col-span-2">
                    <Info label="Açıklama" value={item.description || "—"} />
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <h2 className="text-lg font-semibold text-slate-900">
                  Kârlılık
                </h2>
              </CardHeader>
              <CardContent>
                <div className="space-y-4">
                  <Info
                    label="İskonto Toplamı"
                    value={money(item.discountTotal, item.currency)}
                  />
                  <Info
                    label="Beklenen Kâr"
                    value={money(item.profitTotal, item.currency)}
                  />
                  <Info
                    label="Kâr Oranı"
                    value={percent(profitRate, 2)}
                  />
                </div>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Teklif Kalemleri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Poz, üretici, iskonto ve satış fiyatları
                </p>
              </div>
            </CardHeader>

            <CardContent>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>No</TableHead>
                      <TableHead>Poz</TableHead>
                      <TableHead>Açıklama</TableHead>
                      <TableHead>Üretici</TableHead>
                      <TableHead>Miktar</TableHead>
                      <TableHead>Liste</TableHead>
                      <TableHead>İskonto</TableHead>
                      <TableHead>Net Alış</TableHead>
                      <TableHead>Maliyet</TableHead>
                      <TableHead>Satış</TableHead>
                      <TableHead>Toplam</TableHead>
                    </TableRow>
                  </TableHeader>

                  <TableBody>
                    {item.items.map((line) => (
                      <TableRow key={line.id}>
                        <TableCell>{line.lineNumber}</TableCell>
                        <TableCell>{line.positionNumber || "—"}</TableCell>
                        <TableCell>
                          <strong className="text-slate-900">
                            {line.description}
                          </strong>
                          {line.productCode && (
                            <span className="mt-1 block text-xs text-slate-500">
                              {line.productCode}
                            </span>
                          )}
                        </TableCell>
                        <TableCell>
                          {line.manufacturerName || line.brand || "—"}
                        </TableCell>
                        <TableCell>
                          {quantity(line.quantity)} {line.unit}
                        </TableCell>
                        <TableCell>
                          {unitPrice(line.listPrice, item.currency)}
                        </TableCell>
                        <TableCell>%{line.discountRate}</TableCell>
                        <TableCell>
                          {money(line.netPurchasePrice, item.currency)}
                        </TableCell>
                        <TableCell>
                          {money(line.unitCost, item.currency)}
                        </TableCell>
                        <TableCell>
                          {money(line.unitSalesPrice, item.currency)}
                        </TableCell>
                        <TableCell>
                          <strong>
                            {money(line.salesTotal, item.currency)}
                          </strong>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>

          <OfferChainPanel offerId={item.id} />
        </>
      )}
    </ErpShell>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="text-sm text-slate-500">{label}</span>
      <strong className="mt-1 block text-slate-900">{value}</strong>
    </div>
  );
}
