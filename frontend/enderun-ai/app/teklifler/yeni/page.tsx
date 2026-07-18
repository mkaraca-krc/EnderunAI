"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import {
  Button,
  Card,
  CardContent,
  CardHeader,
  Input,
  Select,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  offerService,
  type OfferItemPayload,
} from "@/services/offer.service";

type Line = {
  positionNumber: string;
  description: string;
  manufacturerName: string;
  productCode: string;
  brand: string;
  model: string;
  quantity: string;
  unit: string;
  listPrice: string;
  discountRate: string;
  freightRate: string;
  wasteRate: string;
  financeRate: string;
  generalExpenseRate: string;
  profitRate: string;
  notes: string;
};

const today = new Date().toISOString().slice(0, 10);

function emptyLine(): Line {
  return {
    positionNumber: "",
    description: "",
    manufacturerName: "",
    productCode: "",
    brand: "",
    model: "",
    quantity: "1",
    unit: "Adet",
    listPrice: "0",
    discountRate: "0",
    freightRate: "0",
    wasteRate: "0",
    financeRate: "0",
    generalExpenseRate: "0",
    profitRate: "18",
    notes: "",
  };
}

function calculate(line: Line) {
  const quantity = Number(line.quantity) || 0;
  const listPrice = Number(line.listPrice) || 0;
  const discountRate = Number(line.discountRate) || 0;
  const freightRate = Number(line.freightRate) || 0;
  const wasteRate = Number(line.wasteRate) || 0;
  const financeRate = Number(line.financeRate) || 0;
  const generalExpenseRate = Number(line.generalExpenseRate) || 0;
  const profitRate = Number(line.profitRate) || 0;

  const netPurchasePrice = listPrice * (1 - discountRate / 100);
  const unitCost =
    netPurchasePrice *
    (1 +
      freightRate / 100 +
      wasteRate / 100 +
      financeRate / 100 +
      generalExpenseRate / 100);
  const unitSalesPrice = unitCost * (1 + profitRate / 100);

  return {
    quantity,
    listTotal: listPrice * quantity,
    netPurchasePrice,
    netTotal: netPurchasePrice * quantity,
    unitCost,
    unitSalesPrice,
    costTotal: unitCost * quantity,
    salesTotal: unitSalesPrice * quantity,
    profitTotal: (unitSalesPrice - unitCost) * quantity,
  };
}

function money(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

export default function NewOfferPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const [form, setForm] = useState({
    companyId: "",
    projectId: "",
    title: "",
    offerDate: today,
    validUntil: "",
    currency: "TRY",
    exchangeRate: "1",
    description: "",
    notes: "",
    items: [emptyLine()],
  });

  useEffect(() => {
    void (async () => {
      try {
        const [companyRows, projectRows] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);

        if (companyRows.length === 1) {
          setForm((current) => ({
            ...current,
            companyId: companyRows[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Form verileri yüklenemedi."
        );
      }
    })();
  }, []);

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) => !form.companyId || project.companyId === form.companyId
      ),
    [projects, form.companyId]
  );

  const totals = useMemo(() => {
    const calculations = form.items.map(calculate);

    return calculations.reduce(
      (acc, current) => ({
        listTotal: acc.listTotal + current.listTotal,
        netTotal: acc.netTotal + current.netTotal,
        costTotal: acc.costTotal + current.costTotal,
        salesTotal: acc.salesTotal + current.salesTotal,
        profitTotal: acc.profitTotal + current.profitTotal,
      }),
      {
        listTotal: 0,
        netTotal: 0,
        costTotal: 0,
        salesTotal: 0,
        profitTotal: 0,
      }
    );
  }, [form.items]);

  function updateLine(index: number, key: keyof Line, value: string) {
    setForm((current) => ({
      ...current,
      items: current.items.map((line, lineIndex) =>
        lineIndex === index ? { ...line, [key]: value } : line
      ),
    }));
  }

  function addLine() {
    setForm((current) => ({
      ...current,
      items: [...current.items, emptyLine()],
    }));
  }

  function removeLine(index: number) {
    setForm((current) => ({
      ...current,
      items:
        current.items.length === 1
          ? current.items
          : current.items.filter((_, lineIndex) => lineIndex !== index),
    }));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");

    try {
      const items: OfferItemPayload[] = form.items.map((line) => ({
        positionNumber: line.positionNumber || null,
        description: line.description,
        manufacturerPriceListItemId: null,
        manufacturerName: line.manufacturerName || null,
        productCode: line.productCode || null,
        brand: line.brand || null,
        model: line.model || null,
        quantity: Number(line.quantity),
        unit: line.unit,
        listPrice: Number(line.listPrice),
        discountRate: Number(line.discountRate),
        freightRate: Number(line.freightRate),
        wasteRate: Number(line.wasteRate),
        financeRate: Number(line.financeRate),
        generalExpenseRate: Number(line.generalExpenseRate),
        profitRate: Number(line.profitRate),
        notes: line.notes || null,
      }));

      const result = await offerService.create({
        companyId: form.companyId,
        projectId: form.projectId || null,
        customerId: null,
        title: form.title,
        offerDate: form.offerDate,
        validUntil: form.validUntil || null,
        currency: form.currency,
        exchangeRate: Number(form.exchangeRate),
        description: form.description || null,
        notes: form.notes || null,
        items,
      });

      router.push(`/teklifler/${result.id}`);
      router.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Teklif oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Yeni Teklif"
      description="İskonto ve kâr oranına göre canlı maliyet hesabı"
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/teklifler" className="hover:text-slate-900">
          Teklif Merkezi
        </Link>
        <span>›</span>
        <strong className="text-slate-800">Yeni Teklif</strong>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <form onSubmit={submit}>
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              Teklif Bilgileri
            </h2>
          </CardHeader>
          <CardContent>
            <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-4">
              <Select
                label="Şirket"
                required
                value={form.companyId}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                    projectId: "",
                  }))
                }
                placeholder="Şirket seçin"
                options={companies.map((company) => ({
                  label: `${company.code} · ${company.name}`,
                  value: company.id,
                }))}
              />

              <Select
                label="Proje"
                value={form.projectId}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    projectId: event.target.value,
                  }))
                }
                placeholder="Proje seçmeden devam et"
                options={filteredProjects.map((project) => ({
                  label: `${project.code} · ${project.name}`,
                  value: project.id,
                }))}
              />

              <Input
                label="Teklif No"
                value="Otomatik oluşturulacak"
                disabled
              />

              <Input
                label="Teklif Başlığı"
                required
                value={form.title}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    title: event.target.value,
                  }))
                }
              />

              <Input
                label="Teklif Tarihi"
                type="date"
                required
                value={form.offerDate}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    offerDate: event.target.value,
                  }))
                }
              />

              <Input
                label="Geçerlilik Tarihi"
                type="date"
                value={form.validUntil}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    validUntil: event.target.value,
                  }))
                }
              />

              <Select
                label="Para Birimi"
                value={form.currency}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    currency: event.target.value,
                  }))
                }
                options={[
                  { value: "TRY", label: "TRY" },
                  { value: "USD", label: "USD" },
                  { value: "EUR", label: "EUR" },
                  { value: "GBP", label: "GBP" },
                ]}
              />

              <Input
                label="Kur"
                type="number"
                min="0.000001"
                step="0.000001"
                value={form.exchangeRate}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    exchangeRate: event.target.value,
                  }))
                }
              />
            </div>
          </CardContent>
        </Card>

        <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Teklif Kalemleri
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    İskonto ve kâr oranlarını satır bazında yönetin
                  </p>
                </div>
                <Button type="button" variant="secondary" onClick={addLine}>
                  + Satır Ekle
                </Button>
              </div>
            </CardHeader>

            <CardContent>
              <div className="overflow-x-auto">
                <table className="min-w-[1500px] border-separate border-spacing-0 text-sm">
                  <thead>
                    <tr className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                      <th className="border-b px-3 py-3">Poz</th>
                      <th className="border-b px-3 py-3">Açıklama</th>
                      <th className="border-b px-3 py-3">Marka</th>
                      <th className="border-b px-3 py-3">Miktar</th>
                      <th className="border-b px-3 py-3">Birim</th>
                      <th className="border-b px-3 py-3">Liste</th>
                      <th className="border-b px-3 py-3">İsk. %</th>
                      <th className="border-b px-3 py-3">Net Alış</th>
                      <th className="border-b px-3 py-3">Nak. %</th>
                      <th className="border-b px-3 py-3">Fire %</th>
                      <th className="border-b px-3 py-3">Fin. %</th>
                      <th className="border-b px-3 py-3">G.G. %</th>
                      <th className="border-b px-3 py-3">Kâr %</th>
                      <th className="border-b px-3 py-3">Satış</th>
                      <th className="border-b px-3 py-3">Toplam</th>
                      <th className="border-b px-3 py-3"></th>
                    </tr>
                  </thead>

                  <tbody>
                    {form.items.map((line, index) => {
                      const result = calculate(line);

                      return (
                        <tr key={index} className="align-top">
                          <td className="border-b p-2">
                            <input
                              value={line.positionNumber}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "positionNumber",
                                  event.target.value
                                )
                              }
                              className="w-28 rounded-lg border border-slate-300 px-2 py-2"
                            />
                          </td>
                          <td className="border-b p-2">
                            <input
                              required
                              value={line.description}
                              onChange={(event) =>
                                updateLine(index, "description", event.target.value)
                              }
                              className="w-64 rounded-lg border border-slate-300 px-2 py-2"
                            />
                          </td>
                          <td className="border-b p-2">
                            <input
                              value={line.manufacturerName}
                              onChange={(event) =>
                                updateLine(
                                  index,
                                  "manufacturerName",
                                  event.target.value
                                )
                              }
                              className="w-32 rounded-lg border border-slate-300 px-2 py-2"
                            />
                          </td>
                          <td className="border-b p-2">
                            <input
                              type="number"
                              min="0.0001"
                              step="0.0001"
                              required
                              value={line.quantity}
                              onChange={(event) =>
                                updateLine(index, "quantity", event.target.value)
                              }
                              className="w-24 rounded-lg border border-slate-300 px-2 py-2 text-right"
                            />
                          </td>
                          <td className="border-b p-2">
                            <input
                              required
                              value={line.unit}
                              onChange={(event) =>
                                updateLine(index, "unit", event.target.value)
                              }
                              className="w-20 rounded-lg border border-slate-300 px-2 py-2"
                            />
                          </td>
                          {[
                            "listPrice",
                            "discountRate",
                            "freightRate",
                            "wasteRate",
                            "financeRate",
                            "generalExpenseRate",
                            "profitRate",
                          ].map((key) => (
                            <td key={key} className="border-b p-2">
                              <input
                                type="number"
                                min="0"
                                step="0.01"
                                value={line[key as keyof Line]}
                                onChange={(event) =>
                                  updateLine(
                                    index,
                                    key as keyof Line,
                                    event.target.value
                                  )
                                }
                                className="w-20 rounded-lg border border-slate-300 px-2 py-2 text-right"
                              />
                            </td>
                          ))}
                          <td className="border-b px-3 py-3 text-right font-medium text-slate-800">
                            {money(result.netPurchasePrice, form.currency)}
                          </td>
                          <td className="border-b px-3 py-3 text-right font-medium text-slate-800">
                            {money(result.unitSalesPrice, form.currency)}
                          </td>
                          <td className="border-b px-3 py-3 text-right font-semibold text-slate-950">
                            {money(result.salesTotal, form.currency)}
                          </td>
                          <td className="border-b p-2">
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              disabled={form.items.length === 1}
                              onClick={() => removeLine(index)}
                            >
                              Sil
                            </Button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>

          <Card className="h-fit">
            <CardHeader>
              <h2 className="text-lg font-semibold text-slate-900">
                Canlı Teklif Özeti
              </h2>
            </CardHeader>
            <CardContent>
              <div className="space-y-4 text-sm">
                <SummaryRow
                  label="Liste Toplamı"
                  value={money(totals.listTotal, form.currency)}
                />
                <SummaryRow
                  label="Net Alış"
                  value={money(totals.netTotal, form.currency)}
                />
                <SummaryRow
                  label="Toplam Maliyet"
                  value={money(totals.costTotal, form.currency)}
                />
                <SummaryRow
                  label="Beklenen Kâr"
                  value={money(totals.profitTotal, form.currency)}
                />
                <div className="border-t border-slate-200 pt-4">
                  <SummaryRow
                    label="Teklif Toplamı"
                    value={money(totals.salesTotal, form.currency)}
                    strong
                  />
                </div>
                <div className="rounded-lg bg-slate-50 p-3">
                  <span className="text-xs text-slate-500">Ortalama Kâr Oranı</span>
                  <strong className="mt-1 block text-xl text-slate-900">
                    %
                    {(
                      totals.costTotal > 0
                        ? (totals.profitTotal / totals.costTotal) * 100
                        : 0
                    ).toLocaleString("tr-TR", {
                      maximumFractionDigits: 2,
                    })}
                  </strong>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Link href="/teklifler">
            <Button type="button" variant="secondary">
              Vazgeç
            </Button>
          </Link>
          <Button type="submit" loading={saving}>
            Taslak Teklifi Kaydet
          </Button>
        </div>
      </form>
    </ErpShell>
  );
}

function SummaryRow({
  label,
  value,
  strong = false,
}: {
  label: string;
  value: string;
  strong?: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="text-slate-500">{label}</span>
      <strong className={strong ? "text-lg text-slate-950" : "text-slate-800"}>
        {value}
      </strong>
    </div>
  );
}
