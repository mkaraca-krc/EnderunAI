"use client";

import { useCallback, useEffect, useState } from "react";

import { Badge, Button, Modal } from "@/components/ui";
import { unitPrice } from "@/lib/format/turkish";
import {
  pricingService,
  type CalculateOfferPriceRequest,
  type CalculateOfferPriceResponse,
} from "@/services/pricing.service";

/**
 * Teklif satırının maliyet kırılımı.
 *
 * Teklif ekranı yazarken canlı geri bildirim için birim maliyeti
 * kendisi hesaplıyor, ama nakliye/zaiyat/finansman/genel gideri tek
 * rakama katlıyor. Bu panel kırılımı UÇTAN alıyor — aynı formülü
 * üçüncü kez yazmamak için.
 *
 * EKRANIN CANLI HESABIYLA UÇ ARASINDA FARK VARSA GÖSTERİLİYOR. İki
 * uygulamanın zamanla ayrışması gerçek bir risk; sessiz kalmak, satış
 * fiyatının hangisinden geldiğini kimsenin bilmediği bir durum
 * bırakırdı. Uç doğruluk kaynağıdır.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Kırılım alınamadı.";
}

export default function CostBreakdownModal({
  open,
  request,
  currency,
  /** Ekranın canlı hesabındaki birim satış fiyatı — karşılaştırma için. */
  localSalesPrice,
  onClose,
}: {
  open: boolean;
  request: CalculateOfferPriceRequest | null;
  currency: string;
  localSalesPrice: number;
  onClose: () => void;
}) {
  const [result, setResult] = useState<CalculateOfferPriceResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  /**
   * Kırılımın tamamı BİRİM ölçeğinde: liste fiyatı → iskonto → net alış
   * → nakliye/fire/finansman/genel gider → maliyet → kâr → satış. Eski
   * biçim hepsini iki haneye kırpıyordu; oysa liste ve net alış fiyatı
   * veritabanında numeric(18,6) ve teklif bu rakamdan türüyor.
   */
  const money = useCallback(
    (value: number) => unitPrice(value, currency),
    [currency]
  );

  const load = useCallback(async () => {
    if (!open || !request) return;

    setLoading(true);
    setError("");

    try {
      setResult(await pricingService.calculateOffer(request));
    } catch (err) {
      setError(messageOf(err));
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [open, request]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  // Kuruş altı fark yuvarlamadan gelir; anlamlı sapma eşiği 1 kuruş.
  const drift =
    result !== null ? Math.abs(result.salesPrice - localSalesPrice) : 0;
  const hasDrift = drift > 0.01;

  return (
    <Modal
      open={open}
      title="Maliyet kırılımı"
      description="Birim maliyetin hangi kalemden ne kadar geldiği."
      onClose={onClose}
      footer={
        <div className="flex justify-end">
          <Button variant="secondary" onClick={onClose}>
            Kapat
          </Button>
        </div>
      }
    >
      {loading && <p className="text-sm text-slate-500">Hesaplanıyor...</p>}

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </div>
      )}

      {result && !loading && (
        <div className="space-y-4">
          <dl className="divide-y divide-slate-200 rounded-lg border border-slate-200 text-sm">
            <Row label="Liste fiyatı" value={money(result.listPrice)} />
            <Row
              label={`İskonto (%${result.discountRate})`}
              value={`− ${money(result.listPrice - result.netPurchasePrice)}`}
            />
            <Row
              label="Net alış"
              value={money(result.netPurchasePrice)}
              strong
            />
            <Row label="Nakliye" value={money(result.freightAmount)} />
            <Row label="Zaiyat" value={money(result.wasteAmount)} />
            <Row label="Finansman" value={money(result.financeAmount)} />
            <Row
              label="Genel gider"
              value={money(result.generalExpenseAmount)}
            />
            <Row label="Birim maliyet" value={money(result.costPrice)} strong />
            <Row label="Kâr" value={money(result.profitAmount)} />
            <Row
              label="Birim satış fiyatı"
              value={money(result.salesPrice)}
              strong
            />
          </dl>

          {hasDrift && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
              <div className="flex items-center gap-2">
                <Badge variant="warning">Fark var</Badge>
                <span className="font-semibold">
                  {money(drift)} sapma
                </span>
              </div>
              <p className="mt-1">
                Ekrandaki canlı hesap {money(localSalesPrice)} gösteriyor.
                Doğruluk kaynağı sunucudur; farkı bildirin.
              </p>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

function Row({
  label,
  value,
  strong = false,
}: {
  label: string;
  value: string;
  strong?: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-4 px-3 py-2">
      <dt className={strong ? "font-semibold text-slate-900" : "text-slate-600"}>
        {label}
      </dt>
      <dd
        className={
          "tabular-nums " +
          (strong ? "font-semibold text-slate-900" : "text-slate-800")
        }
      >
        {value}
      </dd>
    </div>
  );
}
