"use client";

import { useCallback, useEffect, useState } from "react";

import {
  Badge,
  Card,
  CardContent,
  CardHeader,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  supplierQualityService,
  type SupplierQualityReport,
  type SupplierQualityRow,
} from "@/services/supplier-quality.service";

/**
 * Tedarikçi kalite karnesi kartı — satın alma dashboard'una eklenir.
 *
 * Uç aylardır hazırdı, ekranı yoktu: hangi tedarikçiden gelen malın
 * sürekli reddedildiği hiçbir yerde görünmüyordu.
 *
 * HİÇBİR ORAN BURADA HESAPLANMIYOR. Red oranı miktar üzerinden
 * (teslimat sayısı üzerinden değil) ve yalnızca kesinleşmiş mal
 * kabullerden hesaplanıyor; ikisi de backend kuralı. Ekranda
 * yeniden hesaplansa, taslak kabuller de sayılıp tedarikçiye
 * haksız bir karne çıkabilirdi.
 *
 * "Sorunlu tedarikçi" eşiği de backend'de: kart yalnızca
 * `problemSupplierCount` sayısını gösteriyor, eşiği kendisi
 * uygulamıyor.
 */

const PERIODS = [
  { label: "Son 3 ay", value: "3" },
  { label: "Son 6 ay", value: "6" },
  { label: "Son 12 ay", value: "12" },
  { label: "Son 24 ay", value: "24" },
];

/** Kaç satır gösterilecek — kart, tam liste değil özet. */
const VISIBLE_ROWS = 8;

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Karne yüklenemedi.";
}

function formatQuantity(value: number) {
  return value.toLocaleString("tr-TR", { maximumFractionDigits: 2 });
}

function formatDate(value: string | null) {
  if (!value) return "—";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleDateString("tr-TR");
}

export default function SupplierQualityCard({
  companyId,
}: {
  companyId?: string;
}) {
  const [months, setMonths] = useState("12");
  const [report, setReport] = useState<SupplierQualityReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setReport(await supplierQualityService.get(companyId, Number(months)));
    } catch (err) {
      setError(messageOf(err));
      setReport(null);
    } finally {
      setLoading(false);
    }
  }, [companyId, months]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const rows = report?.rows ?? [];

  return (
    <Card className="mb-6">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              Tedarikçi Kalite Karnesi
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Red ve hasar oranı, kesinleşmiş mal kabullerden hesaplanır.
            </p>
          </div>

          <div className="flex items-center gap-3">
            {report && report.problemSupplierCount > 0 && (
              <Badge variant="danger">
                {report.problemSupplierCount} sorunlu tedarikçi
              </Badge>
            )}

            <div className="w-40">
              <Select
                value={months}
                onChange={(event) => setMonths(event.target.value)}
                options={PERIODS}
              />
            </div>
          </div>
        </div>
      </CardHeader>

      <CardContent>
        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        {loading && !error && (
          <div className="py-8 text-center text-sm text-slate-500">
            Yükleniyor...
          </div>
        )}

        {!loading && !error && rows.length === 0 && (
          <div className="py-8 text-center text-sm text-slate-500">
            Bu dönemde kesinleşmiş mal kabul yok.
          </div>
        )}

        {!loading && !error && rows.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Tedarikçi</TableHead>
                    <TableHead className="text-right">Kabul</TableHead>
                    <TableHead className="text-right">Sorunlu</TableHead>
                    <TableHead className="text-right">Gelen</TableHead>
                    <TableHead className="text-right">Red + hasar</TableHead>
                    <TableHead className="text-right">Red oranı</TableHead>
                    <TableHead className="text-right">Geciken sipariş</TableHead>
                    <TableHead>Son sorun</TableHead>
                  </TableRow>
                </TableHeader>

                <TableBody>
                  {rows.slice(0, VISIBLE_ROWS).map((row, index) => (
                    <QualityRow
                      key={row.supplierCurrentAccountId}
                      row={row}
                      // EŞİK BURADA TEKRARLANMIYOR: uç satırları
                      // sorunlu olan başta sıralıyor ve kaç tanesinin
                      // eşiği aştığını söylüyor. Yüzde 5'i istemcide
                      // yazsaydım, eşik backend'de değişince ekran
                      // sessizce yanlış renk gösterirdi.
                      isProblem={index < (report?.problemSupplierCount ?? 0)}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>

            {rows.length > VISIBLE_ROWS && (
              <p className="mt-3 text-xs text-slate-500">
                {rows.length} tedarikçiden en sorunlu {VISIBLE_ROWS} tanesi
                gösteriliyor.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function QualityRow({
  row,
  isProblem,
}: {
  row: SupplierQualityRow;
  /** Uç bu tedarikçiyi eşiği aşanlar arasında saydı mı. */
  isProblem: boolean;
}) {
  return (
    <TableRow>
      <TableCell className="font-medium">{row.supplierTitle}</TableCell>

      <TableCell className="text-right tabular-nums">
        {row.receiptCount}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {row.problemReceiptCount > 0 ? (
          <Badge variant="warning">{row.problemReceiptCount}</Badge>
        ) : (
          <span className="text-slate-400">0</span>
        )}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {formatQuantity(row.deliveredQuantity)}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {formatQuantity(row.rejectedQuantity + row.damagedQuantity)}
      </TableCell>

      <TableCell className="text-right tabular-nums font-semibold">
        {row.rejectionRatePercent > 0 ? (
          <Badge variant={isProblem ? "danger" : "warning"}>
            %{formatQuantity(row.rejectionRatePercent)}
          </Badge>
        ) : (
          <span className="text-slate-400">%0</span>
        )}
      </TableCell>

      <TableCell className="text-right tabular-nums">
        {row.lateOrderCount > 0 ? (
          <Badge variant="danger">{row.lateOrderCount}</Badge>
        ) : (
          <span className="text-slate-400">0</span>
        )}
      </TableCell>

      <TableCell className="text-sm text-slate-600">
        {formatDate(row.lastProblemDate)}
      </TableCell>
    </TableRow>
  );
}
