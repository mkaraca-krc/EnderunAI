"use client";

import { FormEvent, useMemo, useState } from "react";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import PriceListManager from "@/components/pricing/price-list-manager";
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
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  manufacturerPriceListService,
  type ManufacturerPriceProduct,
} from "@/services/manufacturer-price-list.service";
import { useEffect } from "react";

function money(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 4,
  }).format(value);
}

export default function PriceSearchPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [search, setSearch] = useState("");
  const [discountRate, setDiscountRate] = useState("0");
  const [items, setItems] = useState<ManufacturerPriceProduct[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void companyService
      .getAll()
      .then((rows) => {
        setCompanies(rows);
        if (rows.length === 1) setCompanyId(rows[0].id);
      })
      .catch((err) =>
        setError(err instanceof Error ? err.message : "Şirketler yüklenemedi.")
      );
  }, []);

  const sortedItems = useMemo(() => {
    const rate = Number(discountRate) || 0;

    return [...items]
      .map((item) => ({
        ...item,
        netPrice: item.listPrice * (1 - rate / 100),
      }))
      .sort((a, b) => a.netPrice - b.netPrice);
  }, [items, discountRate]);

  async function submit(event: FormEvent) {
    event.preventDefault();

    if (!companyId || !search.trim()) {
      setError("Şirket ve ürün arama metni zorunludur.");
      return;
    }

    setLoading(true);
    setError("");

    try {
      setItems(
        await manufacturerPriceListService.searchProducts({
          companyId,
          search: search.trim(),
          take: 200,
        })
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiyatlar aranamadı.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <ErpShell
      title="Üretici Fiyat Karşılaştırma"
      description="Aynı ürünün farklı üretici listelerini iskonto sonrası karşılaştırın"
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/teklifler" className="hover:text-slate-900">
          Teklif Merkezi
        </Link>
        <span>›</span>
        <strong className="text-slate-800">Fiyat Karşılaştırma</strong>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Fiyat listeleri: uclari vardi, ekrani yoktu. Aramanin
          hangi listelerden beslendigi burada gorunuyor. */}
      <PriceListManager companyId={companyId} />

      <Card className="mb-6">
        <CardHeader>
          <h2 className="text-lg font-semibold text-slate-900">
            Ürün ve İskonto
          </h2>
        </CardHeader>
        <CardContent>
          <form
            onSubmit={submit}
            className="grid gap-4 md:grid-cols-4"
          >
            <label className="block space-y-2">
              <span className="text-sm font-medium text-slate-700">
                Şirket
              </span>
              <select
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
                className="h-10 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm"
              >
                <option value="">Şirket seçin</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.code} · {company.name}
                  </option>
                ))}
              </select>
            </label>

            <div className="md:col-span-2">
              <Input
                label="Ürün Ara"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Örn. N2XH 5x10, NYY 5x16 veya ürün kodu"
              />
            </div>

            <Input
              label="Uygulanacak İskonto %"
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={discountRate}
              onChange={(event) => setDiscountRate(event.target.value)}
            />

            <div className="md:col-span-4 flex justify-end">
              <Button type="submit" loading={loading}>
                Fiyatları Karşılaştır
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Karşılaştırma Sonuçları
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Net fiyatlar teklif verenin girdiği iskonto oranına göre hesaplanır.
              </p>
            </div>
            <Badge variant="info">{sortedItems.length} fiyat</Badge>
          </div>
        </CardHeader>
        <CardContent>
          {loading ? (
            <div className="py-12 text-center text-sm text-slate-500">
              Fiyatlar aranıyor...
            </div>
          ) : sortedItems.length === 0 ? (
            <EmptyState
              title="Henüz fiyat aranmadı"
              description="Ürün adı veya ürün kodu ile üretici fiyat listelerini karşılaştırın."
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Üretici</TableHead>
                  <TableHead>Ürün</TableHead>
                  <TableHead>Liste</TableHead>
                  <TableHead>İskonto</TableHead>
                  <TableHead>Net Fiyat</TableHead>
                  <TableHead>Liste Tarihi</TableHead>
                  <TableHead>Durum</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sortedItems.map((item, index) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <strong>{item.manufacturer}</strong>
                      <span className="mt-1 block text-xs text-slate-500">
                        {item.listName}
                      </span>
                    </TableCell>
                    <TableCell>
                      <strong>{item.productDescription}</strong>
                      <span className="mt-1 block text-xs text-slate-500">
                        {item.productCode} · {item.unit}
                      </span>
                    </TableCell>
                    <TableCell>
                      {money(item.listPrice, item.currency)}
                    </TableCell>
                    <TableCell>%{Number(discountRate) || 0}</TableCell>
                    <TableCell>
                      <strong>{money(item.netPrice, item.currency)}</strong>
                    </TableCell>
                    <TableCell>
                      {new Date(item.listDate).toLocaleDateString("tr-TR")}
                    </TableCell>
                    <TableCell>
                      {index === 0 ? (
                        <Badge variant="success">En Avantajlı</Badge>
                      ) : (
                        <Badge variant="default">Alternatif</Badge>
                      )}
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
