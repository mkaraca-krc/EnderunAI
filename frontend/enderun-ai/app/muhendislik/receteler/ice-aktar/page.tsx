"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Badge, Button, Card, CardContent, CardHeader } from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  positionImportService,
  type SpreadsheetInspection,
} from "@/services/position-import.service";
import {
  RecipeImportAction,
  recipeImportService,
  type RecipeImportPreview,
} from "@/services/recipe-import.service";
import { foldTurkish } from "@/lib/search/fold";

function ColumnSelect({
  label,
  value,
  headers,
  optional,
  onChange,
}: {
  label: string;
  value: number;
  headers: string[];
  optional?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-slate-700">
        {label}
        {optional ? " (ops.)" : " *"}
      </span>
      <select
        className="erp-input"
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      >
        <option value={0}>— eşlenmedi —</option>
        {headers.map((header, index) => (
          <option key={index} value={index + 1}>
            {index + 1}. {header || "(başlıksız)"}
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * Reçete toplu içe aktarma.
 *
 * Sütun düzeni varsayılmıyor — poz kitabı aktarımındaki gerekçenin
 * aynısı: her firmanın reçete tablosu farklı. Kullanıcı sütunları
 * eşler, önizlemede ne olacağını görür, sonra aktarır.
 *
 * Dosya incelemesi poz aktarımının ucundan yapılır: dosya biçimi aynı,
 * aynı iş iki yerde tutulmuyor.
 */
export default function RecipeImportPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [file, setFile] = useState<File | null>(null);
  const [inspection, setInspection] = useState<SpreadsheetInspection | null>(null);
  const [preview, setPreview] = useState<RecipeImportPreview | null>(null);

  const [headerRow, setHeaderRow] = useState(1);
  const [positionCodeColumn, setPositionCodeColumn] = useState(0);
  const [materialCodeColumn, setMaterialCodeColumn] = useState(0);
  const [materialNameColumn, setMaterialNameColumn] = useState(0);
  const [quantityColumn, setQuantityColumn] = useState(0);
  const [unitColumn, setUnitColumn] = useState(0);
  const [wasteColumn, setWasteColumn] = useState(0);

  const [createItems, setCreateItems] = useState(true);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const data = await companyService.getAll();
        setCompanies(data);

        if (data.length === 1) setCompanyId(data[0].id);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler yüklenemedi.");
      }
    })();
  }, []);

  const headers = inspection?.headers ?? [];

  const mapping = {
    sheetName: inspection?.sheetName ?? null,
    headerRowIndex: headerRow,
    positionCodeColumn,
    materialNameColumn,
    quantityColumn,
    unitColumn,
    materialCodeColumn: materialCodeColumn || null,
    wastePercentColumn: wasteColumn || null,
    notesColumn: null,
  };

  const options = { companyId, createMissingInventoryItems: createItems };

  const mappingComplete =
    companyId !== "" &&
    positionCodeColumn > 0 &&
    materialNameColumn > 0 &&
    quantityColumn > 0 &&
    unitColumn > 0 &&
    (!createItems || materialCodeColumn > 0);

  async function handleInspect(selected: File, sheet?: string, row?: number) {
    setBusy(true);
    setError("");
    setNotice("");
    setPreview(null);

    try {
      const result = await positionImportService.inspect(selected, sheet, row);

      setInspection(result);
      setHeaderRow(result.headerRowIndex);

      // Başlıklardan makul bir ilk eşleme önerilir; kullanıcı görüp
      // değiştirebilir. Sessizce varsayılmaz.
      const guess = (patterns: string[]) => {
        const index = result.headers.findIndex((header) =>
          patterns.some((pattern) =>
            foldTurkish(header).includes(pattern)
          )
        );

        return index >= 0 ? index + 1 : 0;
      };

      setPositionCodeColumn(guess(["poz"]));
      setMaterialCodeColumn(guess(["malzeme kod", "stok kod", "ürün kod"]));
      setMaterialNameColumn(
        guess(["malzeme ad", "malzeme", "ürün", "tanım", "açıklama"])
      );
      setQuantityColumn(guess(["miktar", "sarfiyat", "tüketim"]));
      setUnitColumn(guess(["birim"]));
      setWasteColumn(guess(["fire", "zayiat"]));
    } catch (err) {
      setInspection(null);
      setError(err instanceof Error ? err.message : "Dosya okunamadı.");
    } finally {
      setBusy(false);
    }
  }

  async function handlePreview() {
    if (!file) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      setPreview(await recipeImportService.preview(file, mapping, options));
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "Önizleme alınamadı.");
    } finally {
      setBusy(false);
    }
  }

  async function handleCommit() {
    if (!file) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await recipeImportService.commit(file, mapping, options);

      setNotice(result.message);
      setPreview(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Aktarım başarısız.");
    } finally {
      setBusy(false);
    }
  }

  const skipped = preview?.rows.filter(
    (row) => row.action === RecipeImportAction.Skip
  );

  return (
    <ErpShell
      design="redwood"
      title="Reçete İçe Aktarma"
      description="Excel'den toplu poz reçetesi (poz → malzeme + miktar) aktarımı"
    >
      <div className="mb-5">
        <Link
          href="/muhendislik/receteler"
          className="text-sm text-brand-700 hover:underline"
        >
          ← Reçeteler
        </Link>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {notice && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {notice}
        </div>
      )}

      <Card className="mb-6">
        <CardHeader>
          <h2 className="text-lg font-semibold text-slate-900">1. Dosya ve şirket</h2>
        </CardHeader>

        <CardContent>
          <div className="grid gap-4 md:grid-cols-2">
            <label className="block">
              <span className="mb-1.5 block text-sm font-medium text-slate-700">
                Şirket *
              </span>
              <select
                className="erp-input"
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
              >
                <option value="">— seçin —</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.code} · {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="block">
              <span className="mb-1.5 block text-sm font-medium text-slate-700">
                Excel dosyası (.xlsx) *
              </span>
              <input
                type="file"
                accept=".xlsx"
                className="erp-input"
                onChange={(event) => {
                  const selected = event.target.files?.[0] ?? null;
                  setFile(selected);

                  if (selected) void handleInspect(selected);
                }}
              />
            </label>
          </div>

          {inspection && (
            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <label className="block">
                <span className="mb-1.5 block text-sm font-medium text-slate-700">
                  Sayfa
                </span>
                <select
                  className="erp-input"
                  value={inspection.sheetName}
                  onChange={(event) => {
                    if (file) void handleInspect(file, event.target.value);
                  }}
                >
                  {inspection.sheetNames.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="mb-1.5 block text-sm font-medium text-slate-700">
                  Başlık satırı
                </span>
                <input
                  type="number"
                  min={1}
                  className="erp-input"
                  value={headerRow}
                  onChange={(event) => {
                    const row = Number(event.target.value);
                    setHeaderRow(row);

                    if (file) {
                      void handleInspect(file, inspection.sheetName, row);
                    }
                  }}
                />
              </label>
            </div>
          )}
        </CardContent>
      </Card>

      {inspection && (
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              2. Sütun eşleme
            </h2>
          </CardHeader>

          <CardContent>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <ColumnSelect
                label="Poz kodu"
                value={positionCodeColumn}
                headers={headers}
                onChange={setPositionCodeColumn}
              />
              <ColumnSelect
                label="Malzeme adı"
                value={materialNameColumn}
                headers={headers}
                onChange={setMaterialNameColumn}
              />
              <ColumnSelect
                label="Malzeme kodu"
                value={materialCodeColumn}
                headers={headers}
                optional={!createItems}
                onChange={setMaterialCodeColumn}
              />
              <ColumnSelect
                label="Miktar"
                value={quantityColumn}
                headers={headers}
                onChange={setQuantityColumn}
              />
              <ColumnSelect
                label="Birim"
                value={unitColumn}
                headers={headers}
                onChange={setUnitColumn}
              />
              <ColumnSelect
                label="Fire %"
                value={wasteColumn}
                headers={headers}
                optional
                onChange={setWasteColumn}
              />
            </div>

            <label className="mt-4 flex items-start gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                checked={createItems}
                onChange={(event) => setCreateItems(event.target.checked)}
              />
              <span>
                Tanınmayan malzeme için stok kartı aç.
                <span className="mt-1 block text-xs text-slate-500">
                  Kapatırsanız kartı olmayan satırlar aktarılmaz. Depo mevcudu
                  ve açık talep yalnız stok kartı üzerinden düşülebildiği için
                  kartsız malzeme eksik hesabına giremez.
                </span>
              </span>
            </label>

            {inspection.sampleRows.length > 0 && (
              <div className="mt-5 overflow-x-auto">
                <table className="min-w-full text-xs">
                  <thead>
                    <tr className="bg-slate-100 text-slate-600">
                      {headers.map((header, index) => (
                        <th key={index} className="px-2 py-1 text-left">
                          {index + 1}. {header || "(başlıksız)"}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {inspection.sampleRows.map((row, rowIndex) => (
                      <tr key={rowIndex} className="border-t border-slate-200">
                        {row.map((cell, cellIndex) => (
                          <td key={cellIndex} className="px-2 py-1 text-slate-700">
                            {cell}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="mt-5 flex justify-end">
              <Button
                onClick={handlePreview}
                disabled={!mappingComplete}
                loading={busy}
              >
                Önizle
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {preview && (
        <Card>
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              3. Önizleme — hiçbir şey yazılmadı
            </h2>
          </CardHeader>

          <CardContent>
            <div className="mb-5 grid gap-3 md:grid-cols-3 xl:grid-cols-5">
              {[
                ["Toplam satır", preview.totalRows],
                ["Aktarılacak", preview.validRows],
                ["Atlanacak", preview.invalidRows],
                ["Poz sayısı", preview.positionCount],
                ["Açılacak stok kartı", preview.newInventoryItemCount],
              ].map(([label, value]) => (
                <div
                  key={String(label)}
                  className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2"
                >
                  <div className="text-xs text-slate-500">{label}</div>
                  <div className="text-lg font-semibold tabular-nums text-slate-900">
                    {value}
                  </div>
                </div>
              ))}
            </div>

            {preview.missingPositionCount > 0 && (
              <div className="mb-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                {preview.missingPositionCount} poz kodu sistemde bulunamadı;
                bu satırlar aktarılmayacak. Poz kitabında olmayan pozları önce
                tanımlayın.
              </div>
            )}

            {preview.inheritedPositionCodeCount > 0 && (
              <div className="mb-4 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                {preview.inheritedPositionCodeCount} satırda poz kodu yazmıyordu,
                üstteki satırdan devralındı. Aşağıdaki listede
                &quot;devralındı&quot; olarak işaretli — yanlış poza yazılmadığını
                doğrulayın.
              </div>
            )}

            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="bg-slate-100 text-slate-600">
                    <th className="px-3 py-2 text-left">Satır</th>
                    <th className="px-3 py-2 text-left">Poz</th>
                    <th className="px-3 py-2 text-left">Malzeme</th>
                    <th className="px-3 py-2 text-right">Miktar</th>
                    <th className="px-3 py-2 text-left">Birim</th>
                    <th className="px-3 py-2 text-right">Fire</th>
                    <th className="px-3 py-2 text-left">Sonuç</th>
                  </tr>
                </thead>

                <tbody>
                  {preview.rows.map((row) => (
                    <tr key={row.rowNumber} className="border-t border-slate-200">
                      <td className="px-3 py-2 tabular-nums text-slate-500">
                        {row.rowNumber}
                      </td>

                      <td className="px-3 py-2">
                        <span className="block text-slate-900">
                          {row.positionCode || "—"}
                        </span>
                        {row.positionName && (
                          <span className="block text-xs text-slate-500">
                            {row.positionName}
                          </span>
                        )}
                        {row.positionCodeInherited && (
                          <span className="block text-xs text-amber-700">
                            devralındı
                          </span>
                        )}
                      </td>

                      <td className="px-3 py-2">
                        <span className="block text-slate-900">
                          {row.materialName || "—"}
                        </span>
                        {row.materialCode && (
                          <span className="block text-xs text-slate-500">
                            {row.materialCode}
                          </span>
                        )}
                      </td>

                      <td className="px-3 py-2 text-right tabular-nums">
                        {row.quantity ?? "—"}
                      </td>
                      <td className="px-3 py-2">{row.unit || "—"}</td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        %{row.wastePercent}
                      </td>

                      <td className="px-3 py-2">
                        <Badge
                          variant={
                            row.action === RecipeImportAction.Skip
                              ? "danger"
                              : row.action === RecipeImportAction.CreateItem
                                ? "warning"
                                : "success"
                          }
                        >
                          {row.actionName}
                        </Badge>
                        {row.error && (
                          <span className="mt-1 block text-xs text-red-600">
                            {row.error}
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-5 flex items-center justify-between gap-4">
              <p className="text-sm text-slate-500">
                {skipped && skipped.length > 0
                  ? `${skipped.length} satır atlanacak; gerekçeleri yukarıda satır satır yazıyor.`
                  : "Tüm satırlar aktarılabilir."}
              </p>

              <Button
                onClick={handleCommit}
                disabled={preview.validRows === 0}
                loading={busy}
              >
                Aktar ({preview.validRows} satır)
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
    </ErpShell>
  );
}
