"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import {
  purchaseOrderService,
  type PurchaseOrderDetail,
} from "@/services/purchase-order.service";

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "-";
}

function formatNumber(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value);
}

function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

export default function PurchaseOrderPrintPage() {
  const params = useParams<{ id: string }>();

  const [order, setOrder] =
    useState<PurchaseOrderDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const result = await purchaseOrderService.getById(
        params.id
      );

      setOrder(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Sipariş belgesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <main className="min-h-screen bg-slate-100 p-8">
        <div className="mx-auto max-w-5xl rounded-lg bg-white p-12 text-center text-sm text-slate-500">
          Sipariş belgesi hazırlanıyor...
        </div>
      </main>
    );
  }

  if (error || !order) {
    return (
      <main className="min-h-screen bg-slate-100 p-8">
        <div className="mx-auto max-w-5xl rounded-lg border border-red-200 bg-red-50 p-8 text-red-700">
          {error || "Sipariş bulunamadı."}
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-slate-100 py-8 print:bg-white print:py-0">
      <div className="mx-auto mb-5 flex max-w-[210mm] justify-between px-2 print:hidden">
        <Link
          href={`/satin-alma/siparis/${order.id}`}
          className="inline-flex h-10 items-center rounded-lg border border-slate-300 bg-white px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          Siparişe Dön
        </Link>

        <button
          type="button"
          onClick={() => window.print()}
          className="inline-flex h-10 items-center rounded-lg bg-slate-900 px-5 text-sm font-medium text-white hover:bg-slate-800"
        >
          PDF Kaydet / Yazdır
        </button>
      </div>

      <article className="mx-auto min-h-[297mm] w-[210mm] bg-white px-[14mm] py-[12mm] text-[11px] text-slate-900 shadow-xl print:min-h-0 print:w-full print:px-[10mm] print:py-[8mm] print:shadow-none">
        <header className="border-b-2 border-slate-900 pb-5">
          <div className="flex items-start justify-between gap-8">
            <div>
              <h1 className="text-2xl font-bold tracking-wide">
                ENDERUN ENERJİ
              </h1>

              <p className="mt-1 text-[10px] text-slate-600">
                Elektrik Üretim Enerji A.Ş.
              </p>

              <p className="mt-1 max-w-md text-[9px] leading-4 text-slate-500">
                1122. Cadde, İvedik OSB, Maxi İvedik
                Ticaret Merkezi, 5. Kat, No:28, Ankara
              </p>

              <p className="mt-1 text-[9px] text-slate-500">
                Tel: +90 312 241 72 59
              </p>
            </div>

            <div className="text-right">
              <h2 className="text-xl font-bold">
                SATIN ALMA SİPARİŞİ
              </h2>

              <p className="mt-3 text-lg font-semibold">
                {order.orderNumber}
              </p>

              <p className="mt-1 text-slate-600">
                Tarih: {formatDate(order.orderDate)}
              </p>
            </div>
          </div>
        </header>

        <section className="mt-5 grid grid-cols-2 gap-5">
          <div className="rounded border border-slate-300">
            <h3 className="border-b border-slate-300 bg-slate-100 px-3 py-2 font-bold">
              TEDARİKÇİ BİLGİLERİ
            </h3>

            <dl className="space-y-2 p-3">
              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Firma
                </dt>
                <dd className="font-bold">
                  {order.supplierTitle}
                </dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Cari Kod
                </dt>
                <dd>{order.supplierCode}</dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Yetkili
                </dt>
                <dd>
                  {order.supplierAuthorizedPerson || "-"}
                </dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Telefon / E-posta
                </dt>
                <dd>
                  {order.supplierPhone || "-"}
                  {order.supplierEmail
                    ? ` / ${order.supplierEmail}`
                    : ""}
                </dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Adres
                </dt>
                <dd className="whitespace-pre-wrap">
                  {order.supplierAddress || "-"}
                </dd>
              </div>
            </dl>
          </div>

          <div className="rounded border border-slate-300">
            <h3 className="border-b border-slate-300 bg-slate-100 px-3 py-2 font-bold">
              SİPARİŞ BİLGİLERİ
            </h3>

            <dl className="grid grid-cols-2 gap-x-4 gap-y-2 p-3">
              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Proje
                </dt>
                <dd className="font-semibold">
                  {order.projectCode}
                </dd>
                <dd>{order.projectName}</dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Beklenen Teslim
                </dt>
                <dd>
                  {formatDate(
                    order.expectedDeliveryDate
                  )}
                </dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  RFQ
                </dt>
                <dd>{order.rfqNumber}</dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Satın Alma Talebi
                </dt>
                <dd>{order.purchaseRequestNumber}</dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Para Birimi
                </dt>
                <dd>{order.currency}</dd>
              </div>

              <div>
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Kur
                </dt>
                <dd>{formatNumber(order.exchangeRate)}</dd>
              </div>

              <div className="col-span-2">
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Ödeme Koşulu
                </dt>
                <dd>{order.paymentTerm || "-"}</dd>
              </div>

              <div className="col-span-2">
                <dt className="text-[9px] font-semibold uppercase text-slate-500">
                  Teslim Adresi
                </dt>
                <dd className="whitespace-pre-wrap">
                  {order.deliveryAddress ||
                    `${order.projectCode} - ${order.projectName}`}
                </dd>
              </div>
            </dl>
          </div>
        </section>

        <section className="mt-5">
          <table className="w-full border-collapse text-[9px]">
            <thead>
              <tr className="bg-slate-900 text-white">
                <th className="border border-slate-900 px-2 py-2 text-center">
                  No
                </th>
                <th className="border border-slate-900 px-2 py-2 text-left">
                  Malzeme Açıklaması
                </th>
                <th className="border border-slate-900 px-2 py-2 text-left">
                  Marka / Model
                </th>
                <th className="border border-slate-900 px-2 py-2 text-right">
                  Miktar
                </th>
                <th className="border border-slate-900 px-2 py-2">
                  Birim
                </th>
                <th className="border border-slate-900 px-2 py-2 text-right">
                  Birim Fiyat
                </th>
                <th className="border border-slate-900 px-2 py-2 text-right">
                  İskonto
                </th>
                <th className="border border-slate-900 px-2 py-2 text-right">
                  Net Fiyat
                </th>
                <th className="border border-slate-900 px-2 py-2 text-right">
                  Toplam
                </th>
              </tr>
            </thead>

            <tbody>
              {order.items.map((item) => (
                <tr
                  key={item.id}
                  className="break-inside-avoid"
                >
                  <td className="border border-slate-300 px-2 py-2 text-center">
                    {item.lineNumber}
                  </td>

                  <td className="border border-slate-300 px-2 py-2">
                    <strong>
                      {item.materialDescription}
                    </strong>

                    {item.notes && (
                      <div className="mt-1 text-[8px] text-slate-500">
                        {item.notes}
                      </div>
                    )}
                  </td>

                  <td className="border border-slate-300 px-2 py-2">
                    {item.brand || "-"}
                    {item.model ? ` / ${item.model}` : ""}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-right">
                    {formatNumber(item.quantity)}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-center">
                    {item.unit}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-right">
                    {formatMoney(
                      item.unitPrice,
                      order.currency
                    )}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-right">
                    %{formatNumber(item.discountRate)}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-right">
                    {formatMoney(
                      item.netUnitPrice,
                      order.currency
                    )}
                  </td>

                  <td className="border border-slate-300 px-2 py-2 text-right font-semibold">
                    {formatMoney(
                      item.totalPrice,
                      order.currency
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section className="mt-5 flex justify-end">
          <div className="w-[85mm] rounded border border-slate-300">
            <div className="flex justify-between border-b border-slate-300 px-4 py-2">
              <span>Ara Toplam</span>
              <strong>
                {formatMoney(
                  order.subtotal,
                  order.currency
                )}
              </strong>
            </div>

            <div className="flex justify-between border-b border-slate-300 px-4 py-2">
              <span>Toplam İskonto</span>
              <strong>
                {formatMoney(
                  order.discountTotal,
                  order.currency
                )}
              </strong>
            </div>

            <div className="flex justify-between bg-slate-900 px-4 py-3 text-white">
              <span className="font-bold">
                GENEL TOPLAM
              </span>
              <strong className="text-sm">
                {formatMoney(
                  order.grandTotal,
                  order.currency
                )}
              </strong>
            </div>
          </div>
        </section>

        {(order.description || order.notes) && (
          <section className="mt-5 rounded border border-slate-300 p-3">
            <h3 className="font-bold">
              AÇIKLAMA VE NOTLAR
            </h3>

            {order.description && (
              <p className="mt-2 whitespace-pre-wrap leading-5">
                {order.description}
              </p>
            )}

            {order.notes && (
              <p className="mt-2 whitespace-pre-wrap leading-5 text-slate-600">
                {order.notes}
              </p>
            )}
          </section>
        )}

        <section className="mt-12 grid grid-cols-4 gap-5 text-center">
          {[
            "Hazırlayan",
            "Satın Alma Onayı",
            "Finans Onayı",
            "Genel Müdür",
          ].map((title) => (
            <div key={title}>
              <div className="h-16 border-b border-slate-500" />
              <p className="mt-2 font-semibold">
                {title}
              </p>
              <p className="mt-1 text-[8px] text-slate-500">
                Ad Soyad / Tarih / İmza
              </p>
            </div>
          ))}
        </section>

        <footer className="mt-10 border-t border-slate-300 pt-3 text-center text-[8px] text-slate-500">
          Bu belge Enderun AI Yönetim Sistemi
          tarafından oluşturulmuştur.
        </footer>
      </article>

      <style jsx global>{`
        @page {
          size: A4;
          margin: 0;
        }

        @media print {
          html,
          body {
            background: white !important;
          }

          body {
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
          }
        }
      `}</style>
    </main>
  );
}
