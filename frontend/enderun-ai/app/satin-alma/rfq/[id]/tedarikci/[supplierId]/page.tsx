"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { currencyMoney } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  rfqService,
  type RfqDetail,
  type RfqSupplier,
  type SaveQuotationItemPayload,
} from "@/services/rfq.service";
import { requestedBrandLabel } from "@/lib/purchasing/requested-brand";

type QuotationLine = SaveQuotationItemPayload & {
  materialDescription: string;
  unit: string;

  /**
   * Talep edenin istediği marka — yalnız GÖSTERİM için. Tekliflenen
   * markaya (brand) kopyalanmaz: tedarikçinin ne verdiğini tedarikçi
   * söyler, sistem onun yerine doldurmaz.
   */
  requestedBrand?: string | null;
  brandIrrelevant?: boolean;
};

function today() {
  return new Date().toISOString().slice(0, 10);
}

function numberValue(value: string) {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? parsed : 0;
}

function formatMoney(value: number, currency: string) {
  return currencyMoney(value, currency);
}

export default function SupplierQuotationPage() {
  /**
   * Düğme -> uç -> izin (RfqController):
   *   POST rfq/{id}/suppliers/{supplierId}/quotation -> purchasing-rfq.edit
   *
   * "Teklifi Kaydet" ekranda İKİ YERDE (üstte ve altta); ikisi de aynı
   * uca gidiyor, ikisi de kapılandı.
   */
  const actions = useModuleActions("purchasing-rfq");

  const params = useParams<{
    id: string;
    supplierId: string;
  }>();

  const router = useRouter();

  const [rfq, setRfq] = useState<RfqDetail | null>(null);
  const [supplier, setSupplier] = useState<RfqSupplier | null>(null);
  const [lines, setLines] = useState<QuotationLine[]>([]);

  const [supplierQuotationNumber, setSupplierQuotationNumber] =
    useState("");
  const [quotationDate, setQuotationDate] = useState(today());
  const [validUntil, setValidUntil] = useState("");
  const [currency, setCurrency] = useState("TRY");
  const [exchangeRate, setExchangeRate] = useState("1");
  const [deliveryDays, setDeliveryDays] = useState("");
  const [paymentTerm, setPaymentTerm] = useState("");
  const [notes, setNotes] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    if (!params.id || !params.supplierId) return;

    setLoading(true);
    setError("");

    try {
      const detail = await rfqService.getById(params.id);
      const selectedSupplier = detail.suppliers.find(
        (row) => row.id === params.supplierId
      );

      if (!selectedSupplier) {
        throw new Error("RFQ tedarikçisi bulunamadı.");
      }

      setRfq(detail);
      setSupplier(selectedSupplier);
      setCurrency(detail.currency || "TRY");

      setLines(
        detail.items.map((item) => ({
          rfqItemId: item.id,
          materialDescription: item.materialDescription,
          unit: item.unit,
          requestedBrand: item.requestedBrand,
          brandIrrelevant: item.brandIrrelevant,
          quantity: item.quantity,
          unitPrice: 0,
          discountRate: 0,
          brand: "",
          model: "",
          deliveryDays: null,
          notes: "",
        }))
      );

      if (selectedSupplier.deliveryDays !== null &&
          selectedSupplier.deliveryDays !== undefined) {
        setDeliveryDays(String(selectedSupplier.deliveryDays));
      }

      if (selectedSupplier.paymentTerm) {
        setPaymentTerm(selectedSupplier.paymentTerm);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Teklif ekranı yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id, params.supplierId]);

  useEffect(() => {
    void load();
  }, [load]);

  function updateLine(
    index: number,
    field: keyof QuotationLine,
    value: string | number | null
  ) {
    setLines((current) =>
      current.map((line, lineIndex) =>
        lineIndex === index
          ? { ...line, [field]: value }
          : line
      )
    );
  }

  const subtotal = useMemo(
    () =>
      lines.reduce(
        (sum, line) => sum + line.quantity * line.unitPrice,
        0
      ),
    [lines]
  );

  const grandTotal = useMemo(
    () =>
      lines.reduce((sum, line) => {
        const netUnitPrice =
          line.unitPrice * (1 - line.discountRate / 100);

        return sum + line.quantity * netUnitPrice;
      }, 0),
    [lines]
  );

  const discountTotal = subtotal - grandTotal;

  async function saveQuotation() {
    if (!rfq || !supplier) return;

    if (!quotationDate) {
      setError("Teklif tarihi zorunludur.");
      return;
    }

    if (!currency.trim()) {
      setError("Para birimi zorunludur.");
      return;
    }

    if (numberValue(exchangeRate) <= 0) {
      setError("Kur sıfırdan büyük olmalıdır.");
      return;
    }

    if (lines.some((line) => line.quantity <= 0)) {
      setError("Tüm miktarlar sıfırdan büyük olmalıdır.");
      return;
    }

    if (lines.some((line) => line.unitPrice < 0)) {
      setError("Birim fiyat negatif olamaz.");
      return;
    }

    if (
      lines.some(
        (line) =>
          line.discountRate < 0 ||
          line.discountRate > 100
      )
    ) {
      setError("İskonto oranı 0 ile 100 arasında olmalıdır.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      await rfqService.saveQuotation(
        rfq.id,
        supplier.id,
        {
          supplierQuotationNumber:
            supplierQuotationNumber.trim() || null,
          quotationDate,
          validUntil: validUntil || null,
          currency: currency.trim().toUpperCase(),
          exchangeRate: numberValue(exchangeRate),
          deliveryDays: deliveryDays
            ? numberValue(deliveryDays)
            : null,
          paymentTerm: paymentTerm.trim() || null,
          notes: notes.trim() || null,
          items: lines.map((line) => ({
            rfqItemId: line.rfqItemId,
            quantity: line.quantity,
            unitPrice: line.unitPrice,
            discountRate: line.discountRate,
            brand: line.brand?.trim() || null,
            model: line.model?.trim() || null,
            deliveryDays:
              line.deliveryDays === null ||
              line.deliveryDays === undefined
                ? null
                : Number(line.deliveryDays),
            notes: line.notes?.trim() || null,
          })),
        }
      );

      router.push(`/satin-alma/rfq/${rfq.id}`);
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Tedarikçi teklifi kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title={supplier?.supplierTitle ?? "Tedarikçi Teklifi"}
      description={
        rfq
          ? `${rfq.rfqNumber} teklif girişi`
          : "Teklif bilgileri yükleniyor"
      }
    >
      <div className="mb-5 flex flex-wrap items-center gap-2 text-sm text-slate-500">
        <Link
          href="/satin-alma/rfq"
          className="hover:text-slate-900"
        >
          RFQ Yönetimi
        </Link>

        <span>›</span>

        <Link
          href={`/satin-alma/rfq/${params.id}`}
          className="hover:text-slate-900"
        >
          {rfq?.rfqNumber ?? "RFQ"}
        </Link>

        <span>›</span>

        <strong className="text-slate-800">
          {supplier?.supplierTitle ?? "Teklif"}
        </strong>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {loading ? (
        <Card>
          <CardContent className="py-12 text-center text-sm text-slate-500">
            Teklif ekranı yükleniyor...
          </CardContent>
        </Card>
      ) : !rfq || !supplier ? (
        <EmptyState
          title="Teklif kaydı bulunamadı"
          description="RFQ veya tedarikçi bilgisi bulunamadı."
        />
      ) : (
        <>
          <Card className="mb-6">
            <CardContent className="py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="info">
                      {rfq.rfqNumber}
                    </Badge>

                    {supplier.quotationId && (
                      <Badge variant="success">
                        Mevcut Teklif Güncelleniyor
                      </Badge>
                    )}
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    {supplier.supplierTitle}
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    {supplier.supplierCode}
                    {supplier.contactName
                      ? ` · ${supplier.contactName}`
                      : ""}
                  </p>
                </div>

                <div className="flex gap-3">
                  <Link
                    href={`/satin-alma/rfq/${rfq.id}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Vazgeç
                  </Link>

                  {actions.can("edit") && (
                    <Button
                      loading={saving}
                      onClick={saveQuotation}
                    >
                      Teklifi Kaydet
                    </Button>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Teklif Bilgileri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Tedarikçi teklifinin genel koşulları
                </p>
              </div>
            </CardHeader>

            <CardContent>
              <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-4">
                <Input
                  label="Tedarikçi Teklif No"
                  value={supplierQuotationNumber}
                  onChange={(event) =>
                    setSupplierQuotationNumber(event.target.value)
                  }
                />

                <Input
                  label="Teklif Tarihi"
                  type="date"
                  required
                  value={quotationDate}
                  onChange={(event) =>
                    setQuotationDate(event.target.value)
                  }
                />

                <Input
                  label="Geçerlilik Tarihi"
                  type="date"
                  value={validUntil}
                  onChange={(event) =>
                    setValidUntil(event.target.value)
                  }
                />

                <Input
                  label="Para Birimi"
                  required
                  value={currency}
                  onChange={(event) =>
                    setCurrency(event.target.value.toUpperCase())
                  }
                />

                <Input
                  label="Kur"
                  type="number"
                  min="0.0001"
                  step="0.0001"
                  required
                  value={exchangeRate}
                  onChange={(event) =>
                    setExchangeRate(event.target.value)
                  }
                />

                <Input
                  label="Genel Termin (Gün)"
                  type="number"
                  min="0"
                  value={deliveryDays}
                  onChange={(event) =>
                    setDeliveryDays(event.target.value)
                  }
                />

                <Input
                  label="Ödeme Koşulu"
                  value={paymentTerm}
                  onChange={(event) =>
                    setPaymentTerm(event.target.value)
                  }
                />

                <Input
                  label="Genel Not"
                  value={notes}
                  onChange={(event) =>
                    setNotes(event.target.value)
                  }
                />
              </div>
            </CardContent>
          </Card>

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Teklif Kalemleri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Fiyat, iskonto, marka, model ve termin bilgileri
                </p>
              </div>
            </CardHeader>

            <CardContent>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Malzeme</TableHead>
                      <TableHead className="min-w-28">
                        Miktar
                      </TableHead>
                      <TableHead>Birim</TableHead>
                      <TableHead className="min-w-32">
                        Birim Fiyat
                      </TableHead>
                      <TableHead className="min-w-28">
                        İskonto %
                      </TableHead>
                      <TableHead className="min-w-36">
                        Marka
                      </TableHead>
                      <TableHead className="min-w-36">
                        Model
                      </TableHead>
                      <TableHead className="min-w-28">
                        Termin
                      </TableHead>
                      <TableHead className="text-right">
                        Toplam
                      </TableHead>
                    </TableRow>
                  </TableHeader>

                  <TableBody>
                    {lines.map((line, index) => {
                      const netUnitPrice =
                        line.unitPrice *
                        (1 - line.discountRate / 100);

                      const lineTotal =
                        line.quantity * netUnitPrice;

                      return (
                        <TableRow key={line.rfqItemId}>
                          <TableCell>
                            <strong className="block min-w-56 text-slate-900">
                              {line.materialDescription}
                            </strong>

                            {/* İstenen marka teklifi girenin gözü
                                önünde dursun; teklif markası ayrı
                                sütunda serbestçe girilir. */}
                            <span className="mt-1 block text-xs text-slate-500">
                              İstenen: {requestedBrandLabel(line)}
                            </span>
                          </TableCell>

                          <TableCell>
                            <input
                              type="number"
                              min="0.0001"
                              step="0.0001"
                              value={line.quantity}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "quantity",
                                  numberValue(event.target.value)
                                )
                              }
                              className="h-10 w-28 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell>{line.unit}</TableCell>

                          <TableCell>
                            <input
                              type="number"
                              min="0"
                              step="0.0001"
                              value={line.unitPrice}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "unitPrice",
                                  numberValue(event.target.value)
                                )
                              }
                              className="h-10 w-32 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell>
                            <input
                              type="number"
                              min="0"
                              max="100"
                              step="0.01"
                              value={line.discountRate}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "discountRate",
                                  numberValue(event.target.value)
                                )
                              }
                              className="h-10 w-24 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell>
                            <input
                              value={line.brand ?? ""}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "brand",
                                  event.target.value
                                )
                              }
                              className="h-10 w-36 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell>
                            <input
                              value={line.model ?? ""}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "model",
                                  event.target.value
                                )
                              }
                              className="h-10 w-36 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell>
                            <input
                              type="number"
                              min="0"
                              value={line.deliveryDays ?? ""}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "deliveryDays",
                                  event.target.value === ""
                                    ? null
                                    : numberValue(event.target.value)
                                )
                              }
                              className="h-10 w-24 rounded-lg border border-slate-300 px-3 text-sm outline-none focus:border-slate-600"
                            />
                          </TableCell>

                          <TableCell className="text-right font-semibold">
                            {formatMoney(
                              lineTotal,
                              currency || "TRY"
                            )}
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Teklif Özeti
                </h2>
              </div>
            </CardHeader>

            <CardContent>
              <div className="ml-auto grid max-w-lg gap-3">
                <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                  <span className="text-sm text-slate-500">
                    Ara Toplam
                  </span>
                  <strong className="text-slate-900">
                    {formatMoney(subtotal, currency || "TRY")}
                  </strong>
                </div>

                <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                  <span className="text-sm text-slate-500">
                    Toplam İskonto
                  </span>
                  <strong className="text-slate-900">
                    {formatMoney(
                      discountTotal,
                      currency || "TRY"
                    )}
                  </strong>
                </div>

                <div className="flex items-center justify-between pt-2">
                  <span className="text-base font-semibold text-slate-900">
                    Genel Toplam
                  </span>
                  <strong className="text-xl text-slate-950">
                    {formatMoney(
                      grandTotal,
                      currency || "TRY"
                    )}
                  </strong>
                </div>

                <div className="mt-5 flex justify-end gap-3">
                  <Link
                    href={`/satin-alma/rfq/${rfq.id}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Vazgeç
                  </Link>

                  {actions.can("edit") && (
                    <Button
                      loading={saving}
                      onClick={saveQuotation}
                    >
                      Teklifi Kaydet
                    </Button>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>
        </>
      )}
    </ErpShell>
  );
}
