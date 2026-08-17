"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { quantity } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  Input,
  Modal,
} from "@/components/ui";
import {
  defaultSelection,
  isSelectable,
  toRequestLines,
} from "@/lib/purchasing/material-requirement-selection";
import {
  projectMaterialRequirementService,
  type ProjectMaterialRequirement,
  type ProjectMaterialRequirementLine,
} from "@/services/project-material-requirement.service";

/**
 * PROJE MALZEME İHTİYACI
 *
 *   eksik = ihtiyaç − depo mevcudu − açık talepler
 *
 * Ekran hesap YAPMAZ; dört sayı da sunucudan gelir. Burada yeniden
 * hesaplansaydı ekranla sunucu zamanla ayrışır ve kullanıcı talep
 * ederken başka, kayıtta başka miktar görürdü.
 *
 * Talep otomatik açılmaz: liste bir ÖNERİ. Kullanıcı satır seçer,
 * miktarı değiştirebilir; sunucu istenen miktarı güncel eksikle
 * sınırlar ve kırptıysa söyler.
 */
export default function ProjectMaterialRequirementPage() {
  /**
   * Düğme -> uç -> izin:
   *   POST projects/{id}/material-requirement/create-request
   *     -> purchasing-requests.create
   *
   * Proje ekranında ama izni SATIN ALMA TALEBİ modülünde: ürettiği şey
   * bir satın alma talebi. projects.* demek yanlış olurdu.
   */
  const actions = useModuleActions("purchasing-requests");

  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [data, setData] = useState<ProjectMaterialRequirement | null>(null);
  const [includeCentral, setIncludeCentral] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [selected, setSelected] = useState<Record<string, string>>({});
  const [formOpen, setFormOpen] = useState(false);
  const [requestedByName, setRequestedByName] = useState("");
  const [neededByDate, setNeededByDate] = useState("");
  const [priority, setPriority] = useState("1");
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setData(
        await projectMaterialRequirementService.get(projectId, includeCentral)
      );
    } catch (err) {
      setData(null);
      setError(
        err instanceof Error ? err.message : "Malzeme ihtiyacı yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [projectId, includeCentral]);

  useEffect(() => {
    void load();
  }, [load]);

  const shortageLines = useMemo(
    () => data?.lines.filter((line) => line.shortageQuantity > 0) ?? [],
    [data]
  );

  const selectedCount = Object.keys(selected).length;

  function toggle(line: ProjectMaterialRequirementLine) {
    if (!line.inventoryItemId || !isSelectable(line)) return;

    const id = line.inventoryItemId;

    setSelected((current) => {
      const next = { ...current };

      if (id in next) {
        delete next[id];
      } else {
        next[id] = String(line.shortageQuantity);
      }

      return next;
    });
  }

  function selectAll() {
    setSelected(defaultSelection(data?.lines ?? []));
  }

  async function submit() {
    if (!requestedByName.trim()) {
      setError("Talep eden kişi girilmelidir.");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await projectMaterialRequirementService.createRequest(
        projectId,
        {
          requestedByName: requestedByName.trim(),
          neededByDate: neededByDate || null,
          priority: Number(priority),
          lines: toRequestLines(selected),
        }
      );

      setNotice(
        `${result.requestNumber} numaralı taslak talep oluşturuldu ` +
          `(${result.itemCount} kalem).` +
          (result.adjustments.length > 0
            ? ` ${result.adjustments.join(" ")}`
            : "")
      );

      setSelected({});
      setFormOpen(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Talep oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Proje Malzeme İhtiyacı"
      description="İcmal ve reçetelerden çıkan ihtiyaç, depo mevcudu ve eksik"
    >
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <Link
          href={`/projeler/${projectId}`}
          className="text-sm text-brand-700 hover:underline"
        >
          ← Proje
        </Link>

        {/* Depo mevcudu ve icmal dışarıdan değişiyor; eksik listesi
            tazelenmeden eskiyordu. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <label className="flex items-center gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
            checked={includeCentral}
            onChange={(event) => setIncludeCentral(event.target.checked)}
          />
          Merkez depoyu da say
        </label>
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

      {data?.warnings.map((warning) => (
        <div
          key={warning}
          className="mb-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
        >
          {warning}
        </div>
      ))}

      {data && (
        <div className="mb-6 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {[
            ["Kaynak icmal", data.boqNumber ?? "—"],
            ["İcmal kalemi", data.positionLineCount],
            ["Reçetesiz poz", data.positionsWithoutRecipe],
            ["Eksik malzeme", shortageLines.length],
          ].map(([label, value]) => (
            <div
              key={String(label)}
              className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3"
            >
              <div className="text-xs text-slate-500">{label}</div>
              <div className="text-lg font-semibold text-slate-900">{value}</div>
            </div>
          ))}
        </div>
      )}

      <Card className="mb-6">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="text-lg font-semibold text-slate-900">
              Malzeme listesi
            </h2>

            <div className="flex gap-2">
              <Button
                variant="secondary"
                onClick={selectAll}
                disabled={shortageLines.length === 0}
              >
                Eksikleri seç
              </Button>

              {actions.can("create") && (
                <Button
                  onClick={() => setFormOpen(true)}
                  disabled={selectedCount === 0}
                >
                  Talep Oluştur ({selectedCount})
                </Button>
              )}
            </div>
          </div>
        </CardHeader>

        <CardContent>
          {loading ? (
            <p className="py-8 text-center text-slate-500">Yükleniyor…</p>
          ) : !data || data.lines.length === 0 ? (
            <div className="py-8 text-center text-slate-500">
              <strong className="block text-slate-700">
                Hesaplanacak malzeme yok
              </strong>
              <p className="mt-1 text-sm">
                İcmal kalemlerinin poza bağlı ve pozların reçeteli olması
                gerekiyor.
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="bg-slate-100 text-slate-600">
                    <th className="px-3 py-2 text-left">Seç</th>
                    <th className="px-3 py-2 text-left">Malzeme</th>
                    <th className="px-3 py-2 text-right">İhtiyaç</th>
                    <th className="px-3 py-2 text-right">Depo</th>
                    <th className="px-3 py-2 text-right">Açık talep</th>
                    <th className="px-3 py-2 text-right">Eksik</th>
                    <th className="px-3 py-2 text-left">Birim</th>
                    <th className="px-3 py-2 text-left">Durum</th>
                  </tr>
                </thead>

                <tbody>
                  {data.lines.map((line) => {
                    const id = line.inventoryItemId ?? "";
                    const isSelected = id !== "" && id in selected;

                    return (
                      <tr
                        key={`${line.materialCode}-${line.unit}`}
                        className="border-t border-slate-200"
                      >
                        <td className="px-3 py-2">
                          <input
                            type="checkbox"
                            className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                            checked={isSelected}
                            disabled={!isSelectable(line)}
                            onChange={() => toggle(line)}
                          />
                        </td>

                        <td className="px-3 py-2">
                          <span className="block font-medium text-slate-900">
                            {line.materialName}
                          </span>
                          <span className="block text-xs text-slate-500">
                            {line.materialCode || "kodsuz"} ·{" "}
                            {line.sourceLineCount} icmal kaleminden
                          </span>
                        </td>

                        <td className="px-3 py-2 text-right tabular-nums">
                          {quantity(line.requiredQuantity)}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums text-slate-600">
                          {quantity(line.stockQuantity)}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums text-slate-600">
                          {quantity(line.openRequestedQuantity)}
                        </td>

                        <td className="px-3 py-2 text-right tabular-nums font-semibold text-slate-900">
                          {isSelected ? (
                            <input
                              className="h-9 w-28 rounded-lg border border-slate-300 px-2 text-right text-sm"
                              value={selected[id]}
                              onChange={(event) =>
                                setSelected((current) => ({
                                  ...current,
                                  [id]: event.target.value,
                                }))
                              }
                            />
                          ) : (
                            quantity(line.shortageQuantity)
                          )}
                        </td>

                        <td className="px-3 py-2">{line.unit}</td>

                        <td className="px-3 py-2">
                          {!line.canRequest ? (
                            <Badge variant="warning">Stok kartı yok</Badge>
                          ) : line.shortageQuantity > 0 ? (
                            <Badge variant="danger">Eksik</Badge>
                          ) : (
                            <Badge variant="success">Karşılanıyor</Badge>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {data && data.missingRecipes.length > 0 && (
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              Reçetesi olmayan pozlar
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Bu pozlar ihtiyaca <strong>sıfır</strong> katkı verdi — malzemeleri
              hesaba girmedi. Reçete tanımlanınca ihtiyaç artacaktır.
            </p>
          </CardHeader>

          <CardContent>
            <ul className="space-y-2 text-sm">
              {data.missingRecipes.map((issue) => (
                <li
                  key={`${issue.lineNumber}-${issue.positionCode}`}
                  className="flex flex-wrap items-center gap-2 border-b border-slate-100 pb-2"
                >
                  <span className="font-medium text-slate-900">
                    {issue.positionCode}
                  </span>
                  <span className="text-slate-600">{issue.positionName}</span>
                  <span className="text-xs text-slate-400">
                    icmal satırı {issue.lineNumber}
                  </span>
                </li>
              ))}
            </ul>

            <div className="mt-4">
              <Link
                href="/muhendislik/receteler/ice-aktar"
                className="text-sm text-brand-700 hover:underline"
              >
                Toplu reçete aktarımına git →
              </Link>
            </div>
          </CardContent>
        </Card>
      )}

      {data && data.unitConflicts.length > 0 && (
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              Birim çakışmaları
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Aynı malzeme farklı birimlerle geçiyor; miktarlar
              <strong> toplanmadı</strong>, ayrı satır olarak duruyor.
            </p>
          </CardHeader>

          <CardContent>
            <ul className="space-y-2 text-sm text-slate-700">
              {data.unitConflicts.map((issue, index) => (
                <li key={index}>{issue.reason}</li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title="Malzeme Talebi Oluştur"
      >
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            {selectedCount} malzeme için <strong>taslak</strong> talep
            açılacak; normal onay sürecine girecek. İstenen miktar, kayıt
            anındaki güncel eksiği aşamaz.
          </p>

          <Input
            label="Talep eden"
            value={requestedByName}
            onChange={(event) => setRequestedByName(event.target.value)}
          />

          <Input
            label="İhtiyaç tarihi"
            type="date"
            value={neededByDate}
            onChange={(event) => setNeededByDate(event.target.value)}
          />

          <label className="block">
            <span className="mb-1.5 block text-sm font-medium text-slate-700">
              Öncelik
            </span>
            <select
              className="erp-input"
              value={priority}
              onChange={(event) => setPriority(event.target.value)}
            >
              <option value="0">Düşük</option>
              <option value="1">Normal</option>
              <option value="2">Yüksek</option>
              <option value="3">Kritik</option>
            </select>
          </label>

          <div className="flex justify-end gap-3">
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Vazgeç
            </Button>
            {actions.can("create") && (
              <Button onClick={submit} loading={saving}>
                Taslak Talep Oluştur
              </Button>
            )}
          </div>
        </div>
      </Modal>
    </ErpShell>
  );
}
