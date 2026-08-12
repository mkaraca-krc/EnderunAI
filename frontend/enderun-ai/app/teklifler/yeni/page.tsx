"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import CostBreakdownModal from "@/components/offers/cost-breakdown-modal";
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
import {
  engineeringPositionService,
  type EngineeringPositionListItem,
} from "@/services/engineering-position.service";
import {
  engineeringRecipeService,
} from "@/services/engineering-recipe.service";
import {
  offerCostingService,
  type EstimatedMaterialCost,
} from "@/services/offer-costing.service";

type Line = {
  engineeringPositionId: string;
  recipeId: string;
  recipeVersion: string;
  recipeSummary: string;
  estimatedLaborHours: string;
  estimatedMaterialCost: string;
  estimatedLaborCost: string;
  estimatedMachineCost: string;
  pricedMaterialCount: string;
  unpricedMaterialCount: string;
  costMaterials: EstimatedMaterialCost[];
  costWarnings: string[];
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
    engineeringPositionId: "",
    recipeId: "",
    recipeVersion: "",
    recipeSummary: "",
    estimatedLaborHours: "0",
    estimatedMaterialCost: "0",
    estimatedLaborCost: "0",
    estimatedMachineCost: "0",
    pricedMaterialCount: "0",
    unpricedMaterialCount: "0",
    costMaterials: [],
    costWarnings: [],
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
  const [positions, setPositions] = useState<EngineeringPositionListItem[]>([]);
  const [loadingRecipeLine, setLoadingRecipeLine] = useState<number | null>(null);
  const [laborHourRate, setLaborHourRate] = useState("500");
  const [machineHourRate, setMachineHourRate] = useState("750");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  // Maliyet kirilimi paneli: hangi satirin kirilimi aciliyor.
  const [breakdown, setBreakdown] = useState<{
    request: {
      listPrice: number;
      discountRate: number;
      freightRate: number;
      wasteRate: number;
      financeRate: number;
      generalExpenseRate: number;
      profitRate: number;
    };
    localSalesPrice: number;
  } | null>(null);

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
        const [companyRows, projectRows, positionRows] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          engineeringPositionService.getAll({ status: 1 }),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);
        setPositions(positionRows);

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

  async function selectEngineeringPosition(
    index: number,
    positionId: string
  ) {
    if (!positionId) {


      setForm((current) => ({
        ...current,
        items: current.items.map((line, lineIndex) =>
          lineIndex === index
            ? {
                ...emptyLine(),
                quantity: line.quantity,
                profitRate: line.profitRate,
                generalExpenseRate: line.generalExpenseRate,
                financeRate: line.financeRate,
                freightRate: line.freightRate,
              }
            : line
        ),
      }));
      return;
    }

    const position = positions.find((item) => item.id === positionId);

    if (!position) {
      setError("Seçilen mühendislik pozu bulunamadı.");
      return;
    }

    setLoadingRecipeLine(index);
    setError("");

    try {
      const recipes =
        await engineeringRecipeService.getByPosition(positionId);

      const selectedRecipe =
        recipes.find((item) => item.isDefault) ?? recipes[0] ?? null;

      let recipeId = "";
      let recipeVersion = "";
      let recipeSummary = "Bu poz için kayıtlı reçete bulunmuyor.";
      let laborHours = position.totalLaborHours ?? 0;
      let wasteRate = 0;

      if (selectedRecipe) {
        const detail =
          await engineeringRecipeService.getById(selectedRecipe.id);

        recipeId = detail.id;
        recipeVersion = `V${detail.version}`;

        laborHours = detail.labors.reduce(
          (sum, labor) =>
            sum + Number(labor.personCount) * Number(labor.hours),
          0
        );

        const materialWasteRates = detail.materials
          .map((material) => Number(material.wastePercent || 0))
          .filter((value) => Number.isFinite(value));

        wasteRate =
          materialWasteRates.length > 0
            ? materialWasteRates.reduce((sum, value) => sum + value, 0) /
              materialWasteRates.length
            : 0;

        const machineHours = detail.machines.reduce(
          (sum, machine) =>
            sum + Number(machine.quantity) * Number(machine.hours),
          0
        );

        recipeSummary = [
          `Reçete ${recipeVersion}`,
          `${detail.materials.length} malzeme`,
          `${detail.labors.length} işçilik`,
          `${detail.machines.length} makine`,
          `${laborHours.toLocaleString("tr-TR", {
            maximumFractionDigits: 2,
          })} adam/saat`,
          `${machineHours.toLocaleString("tr-TR", {
            maximumFractionDigits: 2,
          })} makine/saat`,
        ].join(" · ");
      }

      let costing = null;

      if (form.companyId && selectedRecipe) {
        costing = await offerCostingService.estimatePosition({
          companyId: form.companyId,
          engineeringPositionId: position.id,
          currency: form.currency,
          laborHourRate: Number(laborHourRate || 0),
          machineHourRate: Number(machineHourRate || 0),
        });
      }

      const costWarnings: string[] = [];

      if (costing) {
        if (costing.unpricedMaterialCount > 0) {
          costWarnings.push(
            `${costing.unpricedMaterialCount} malzemenin güncel fiyatı bulunamadı.`
          );
        }

        if (
          costing.pricedMaterialCount === 0 &&
          costing.materials.length > 0
        ) {
          costWarnings.push(
            "Hiçbir reçete malzemesi üretici fiyat listesiyle eşleşmedi."
          );
        }

        const highWasteMaterials = costing.materials.filter(
          (material) => Number(material.wastePercent) > 3
        );

        if (highWasteMaterials.length > 0) {
          costWarnings.push(
            `${highWasteMaterials.length} malzemede fire oranı %3'ün üzerinde.`
          );
        }

        if (
          costing.materialCost > 0 &&
          costing.laborCost / costing.materialCost > 0.5
        ) {
          costWarnings.push(
            "İşçilik maliyeti malzeme maliyetinin %50'sinden yüksek."
          );
        }

        if (
          costing.machineCost > costing.laborCost &&
          costing.machineCost > 0
        ) {
          costWarnings.push(
            "Makine maliyeti işçilik maliyetinden yüksek; ekipman süresini kontrol edin."
          );
        }

        if (costing.unpricedMaterialCount === 0) {
          costWarnings.push(
            "Tüm reçete malzemeleri aktif fiyat listeleriyle eşleşti."
          );
        }
      }

      setForm((current) => ({
        ...current,
        items: current.items.map((line, lineIndex) =>
          lineIndex === index
            ? {
                ...line,
                engineeringPositionId: position.id,
                recipeId,
                recipeVersion,
                recipeSummary,
                estimatedLaborHours: String(
                  costing?.laborHours ?? laborHours
                ),
                estimatedMaterialCost: String(
                  costing?.materialCost ?? 0
                ),
                estimatedLaborCost: String(
                  costing?.laborCost ?? 0
                ),
                estimatedMachineCost: String(
                  costing?.machineCost ?? 0
                ),
                pricedMaterialCount: String(
                  costing?.pricedMaterialCount ?? 0
                ),
                unpricedMaterialCount: String(
                  costing?.unpricedMaterialCount ?? 0
                ),
                costMaterials: costing?.materials ?? [],
                costWarnings,
                positionNumber: position.code,
                description: position.name,
                unit: position.unit,
                listPrice: String(
                  costing?.unitCost ?? line.listPrice
                ),
                wasteRate: String(
                  Number(wasteRate.toFixed(2))
                ),
                manufacturerName:
                  costing?.materials.find(
                    (material) => material.priceFound
                  )?.manufacturer ??
                  line.manufacturerName,
                notes: costing
                  ? [
                      recipeSummary,
                      `Malzeme: ${costing.materialCost.toLocaleString("tr-TR")}`,
                      `İşçilik: ${costing.laborCost.toLocaleString("tr-TR")}`,
                      `Makine: ${costing.machineCost.toLocaleString("tr-TR")}`,
                      `Fiyatlanan: ${costing.pricedMaterialCount}`,
                      `Eksik fiyat: ${costing.unpricedMaterialCount}`,
                    ].join(" · ")
                  : recipeSummary,
              }
            : line
        ),
      }));
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Poz reçetesi teklif satırına aktarılamadı."
      );
    } finally {
      setLoadingRecipeLine(null);
    }
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
        engineeringPositionId:
          line.engineeringPositionId || null,
        engineeringRecipeId:
          line.recipeId || null,
        recipeVersion:
          Number(
            line.recipeVersion.replace(/^V/i, "")
          ) || null,
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

        <Card className="mb-6">
          <CardHeader>
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Maliyet Motoru Parametreleri
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Reçete işçilik ve makine maliyetlerinde kullanılacak saat ücretleri
              </p>
            </div>
          </CardHeader>

          <CardContent>
            <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-4">
              <Input
                label="İşçilik Saat Ücreti"
                type="number"
                min="0"
                step="0.01"
                value={laborHourRate}
                onChange={(event) =>
                  setLaborHourRate(event.target.value)
                }
              />

              <Input
                label="Makine Saat Ücreti"
                type="number"
                min="0"
                step="0.01"
                value={machineHourRate}
                onChange={(event) =>
                  setMachineHourRate(event.target.value)
                }
              />

              <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                <span className="text-xs text-slate-500">
                  Hesaplama Para Birimi
                </span>
                <strong className="mt-1 block text-lg text-slate-900">
                  {form.currency}
                </strong>
              </div>

              <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                <span className="text-xs text-slate-500">
                  Maliyet Kaynağı
                </span>
                <strong className="mt-1 block text-lg text-slate-900">
                  Reçete + Fiyat Listesi
                </strong>
              </div>
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
                <table className="min-w-[1850px] border-separate border-spacing-0 text-sm">
                  <thead>
                    <tr className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                      <th className="border-b px-3 py-3">Poz Seç</th>
                      <th className="border-b px-3 py-3">Reçete</th>
                      <th className="border-b px-3 py-3">Adam/Saat</th>
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
                            <select
                              value={line.engineeringPositionId}
                              onChange={(event) =>
                                void selectEngineeringPosition(
                                  index,
                                  event.target.value
                                )
                              }
                              className="w-64 rounded-lg border border-slate-300 bg-white px-2 py-2"
                            >
                              <option value="">Poz seçin...</option>
                              {positions.map((position) => (
                                <option value={position.id} key={position.id}>
                                  {position.code} · {position.name}
                                </option>
                              ))}
                            </select>

                            {line.positionNumber && (
                              <small className="mt-1 block text-slate-500">
                                {line.positionNumber}
                              </small>
                            )}
                          </td>

                          <td className="border-b p-2">
                            <div className="w-40">
                              <strong className="block text-slate-800">
                                {loadingRecipeLine === index
                                  ? "Yükleniyor..."
                                  : line.recipeVersion || "Reçete yok"}
                              </strong>
                              <small
                                className="mt-1 block text-slate-500"
                                title={line.recipeSummary}
                              >
                                {line.recipeSummary || "Poz seçilmedi"}
                              </small>

                              {line.recipeId && (
                                <div className="mt-2 space-y-1 text-xs text-slate-500">
                                  <div>
                                    Malzeme:{" "}
                                    {money(
                                      Number(line.estimatedMaterialCost || 0),
                                      form.currency
                                    )}
                                  </div>
                                  <div>
                                    İşçilik:{" "}
                                    {money(
                                      Number(line.estimatedLaborCost || 0),
                                      form.currency
                                    )}
                                  </div>
                                  <div>
                                    Makine:{" "}
                                    {money(
                                      Number(line.estimatedMachineCost || 0),
                                      form.currency
                                    )}
                                  </div>
                                  <div>
                                    Fiyatlanan: {line.pricedMaterialCount} ·
                                    Eksik: {line.unpricedMaterialCount}
                                  </div>

                                  {line.costWarnings.length > 0 && (
                                    <div className="mt-2 space-y-1">
                                      {line.costWarnings.map((warning, warningIndex) => (
                                        <div
                                          key={warningIndex}
                                          className={
                                            warning.includes("Tüm reçete")
                                              ? "rounded bg-emerald-50 px-2 py-1 text-emerald-700"
                                              : "rounded bg-amber-50 px-2 py-1 text-amber-700"
                                          }
                                        >
                                          {warning}
                                        </div>
                                      ))}
                                    </div>
                                  )}

                                  {line.costMaterials.length > 0 && (
                                    <details className="mt-3">
                                      <summary className="cursor-pointer font-medium text-slate-700">
                                        Malzeme maliyet dökümü
                                      </summary>

                                      <div className="mt-2 max-h-72 min-w-[520px] overflow-auto rounded-lg border border-slate-200 bg-white">
                                        <table className="w-full text-xs">
                                          <thead className="sticky top-0 bg-slate-50">
                                            <tr>
                                              <th className="px-2 py-2 text-left">
                                                Malzeme
                                              </th>
                                              <th className="px-2 py-2 text-left">
                                                Üretici
                                              </th>
                                              <th className="px-2 py-2 text-right">
                                                Efektif Miktar
                                              </th>
                                              <th className="px-2 py-2 text-right">
                                                Birim Fiyat
                                              </th>
                                              <th className="px-2 py-2 text-right">
                                                Toplam
                                              </th>
                                            </tr>
                                          </thead>

                                          <tbody>
                                            {line.costMaterials.map((material) => (
                                              <tr
                                                key={material.recipeMaterialId}
                                                className="border-t border-slate-100"
                                              >
                                                <td className="px-2 py-2">
                                                  <strong className="block">
                                                    {material.materialName}
                                                  </strong>
                                                  <span className="text-slate-500">
                                                    {material.materialCode || "Kod yok"}
                                                    {" · "}
                                                    Fire %{material.wastePercent}
                                                  </span>
                                                </td>

                                                <td className="px-2 py-2">
                                                  {material.priceFound ? (
                                                    <>
                                                      <strong className="block">
                                                        {material.manufacturer || "Üretici"}
                                                      </strong>
                                                      <span className="text-slate-500">
                                                        {material.brand ||
                                                          material.productCode ||
                                                          "Ürün"}
                                                      </span>
                                                    </>
                                                  ) : (
                                                    <span className="font-medium text-amber-700">
                                                      Fiyat bulunamadı
                                                    </span>
                                                  )}
                                                </td>

                                                <td className="px-2 py-2 text-right">
                                                  {Number(
                                                    material.effectiveQuantity
                                                  ).toLocaleString("tr-TR", {
                                                    maximumFractionDigits: 4,
                                                  })}
                                                </td>

                                                <td className="px-2 py-2 text-right">
                                                  {money(
                                                    Number(material.unitPrice),
                                                    material.currency
                                                  )}
                                                </td>

                                                <td className="px-2 py-2 text-right font-semibold">
                                                  {money(
                                                    Number(material.totalPrice),
                                                    material.currency
                                                  )}
                                                </td>
                                              </tr>
                                            ))}
                                          </tbody>
                                        </table>
                                      </div>
                                    </details>
                                  )}
                                </div>
                              )}
                            </div>
                          </td>

                          <td className="border-b px-3 py-3 text-right">
                            <strong className="text-slate-900">
                              {(
                                Number(line.estimatedLaborHours || 0) *
                                Number(line.quantity || 0)
                              ).toLocaleString("tr-TR", {
                                maximumFractionDigits: 2,
                              })}
                            </strong>
                            <small className="mt-1 block text-slate-500">
                              Birim:{" "}
                              {Number(
                                line.estimatedLaborHours || 0
                              ).toLocaleString("tr-TR", {
                                maximumFractionDigits: 2,
                              })}
                            </small>
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
                                value={(() => {
                                  const inputValue =
                                    line[key as keyof Line];

                                  return typeof inputValue === "string" ||
                                    typeof inputValue === "number"
                                    ? inputValue
                                    : "";
                                })()}
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
                            <div className="flex items-center gap-1">
                              {/* Kirilim UCTAN gelir; ekranin canli
                                  hesabi maliyeti tek rakama katliyor. */}
                              <Button
                                type="button"
                                size="sm"
                                variant="ghost"
                                onClick={() =>
                                  setBreakdown({
                                    request: {
                                      listPrice: Number(line.listPrice) || 0,
                                      discountRate: Number(line.discountRate) || 0,
                                      freightRate: Number(line.freightRate) || 0,
                                      wasteRate: Number(line.wasteRate) || 0,
                                      financeRate: Number(line.financeRate) || 0,
                                      generalExpenseRate:
                                        Number(line.generalExpenseRate) || 0,
                                      profitRate: Number(line.profitRate) || 0,
                                    },
                                    localSalesPrice: result.unitSalesPrice,
                                  })
                                }
                              >
                                Kırılım
                              </Button>

                              <Button
                                type="button"
                                size="sm"
                                variant="ghost"
                                disabled={form.items.length === 1}
                                onClick={() => removeLine(index)}
                              >
                                Sil
                              </Button>
                            </div>
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

      <CostBreakdownModal
        open={breakdown !== null}
        request={breakdown?.request ?? null}
        currency={form.currency}
        localSalesPrice={breakdown?.localSalesPrice ?? 0}
        onClose={() => setBreakdown(null)}
      />
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
