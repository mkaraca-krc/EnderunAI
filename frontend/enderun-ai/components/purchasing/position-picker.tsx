"use client";

import { useEffect, useState } from "react";

import { Button, Input } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import {
  purchaseRequestService,
  type PurchasePositionOption,
} from "@/services/purchase-request.service";
import { customPositionService } from "@/services/engineering-position.service";

type Props = {
  companyId: string;
  /** Seçili poz — kaldırılabilsin diye özet burada gösteriliyor. */
  selected: { id: string; code: string; name: string; isCustom: boolean } | null;
  /** Poz seçilince ad ve birim de taşınır; talep kalemi ikisini doldurur. */
  onSelect: (
    position: { id: string; code: string; name: string; isCustom: boolean },
    unit: string
  ) => void;
  onClear: () => void;
};

/**
 * Talep kalemi için poz seçici.
 *
 * Şirketin 23 binin üzerinde pozu var; talep bugüne kadar bunlardan
 * hiçbirine bağlanamıyordu. Arama en az iki harften sonra çalışır —
 * kütüphanenin tamamını dökmek istemciyi kilitler.
 *
 * ÖZEL POZ KALICI: listede yoksa buradan açılan poz şirket
 * kütüphanesine yazılır ve bir sonraki talepte aramada çıkar. Tek
 * talebe özel geçici kayıt üretilmiyor — aynı kalem ikinci kez
 * yazılmasın.
 */
export default function PositionPicker({
  companyId,
  selected,
  onSelect,
  onClear,
}: Props) {
  const [term, setTerm] = useState("");
  const [results, setResults] = useState<PurchasePositionOption[]>([]);
  const [searching, setSearching] = useState(false);
  const [error, setError] = useState("");

  // Özel poz KÜTÜPHANEYE kalıcı satır yazar; kapı uçta dar tutuldu
  // (engineering.manage). Talep açabilen herkes yazabilseydi 23
  // binlik kütüphane mükerrer kalemlerle dolardı. Yetkisi olmayan
  // kullanıcı kalemi serbest metinle açmaya devam eder.
  const { has } = usePermissions();
  const canCreateCustom = has("engineering.manage");

  const [showCustom, setShowCustom] = useState(false);
  const [customName, setCustomName] = useState("");
  const [customUnit, setCustomUnit] = useState("AD");
  const [customNotes, setCustomNotes] = useState("");
  const [customPrice, setCustomPrice] = useState("");
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    const trimmed = term.trim();

    if (!companyId || trimmed.length < 2) {
      const timer = window.setTimeout(() => setResults([]), 0);
      return () => window.clearTimeout(timer);
    }

    const timer = window.setTimeout(() => {
      void (async () => {
        setSearching(true);

        try {
          setResults(
            await purchaseRequestService.searchPositions(companyId, trimmed)
          );
          setError("");
        } catch (err) {
          setResults([]);
          setError(err instanceof Error ? err.message : "Poz aranamadı.");
        } finally {
          setSearching(false);
        }
      })();
    }, 300);

    return () => window.clearTimeout(timer);
  }, [companyId, term]);

  async function createCustom() {
    setCreating(true);
    setError("");

    try {
      if (!customName.trim()) throw new Error("Poz tanımı zorunludur.");

      const price = customPrice.trim() ? Number(customPrice) : null;

      if (price !== null && (!Number.isFinite(price) || price < 0)) {
        throw new Error("Tahmini fiyat geçersiz.");
      }

      const created = await customPositionService.create({
        companyId,
        name: customName.trim(),
        unit: customUnit.trim() || "AD",
        discipline: 99,
        notes: customNotes.trim() || null,
        unitPrice: price,
      });

      onSelect(
        {
          id: created.id,
          code: created.code,
          name: created.name,
          isCustom: true,
        },
        created.unit
      );

      setShowCustom(false);
      setCustomName("");
      setCustomNotes("");
      setCustomPrice("");
      setTerm("");
      setResults([]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Özel poz açılamadı.");
    } finally {
      setCreating(false);
    }
  }

  if (selected) {
    return (
      <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm">
        <span
          className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${
            selected.isCustom
              ? "border-violet-200 bg-violet-50 text-violet-700"
              : "border-emerald-200 bg-emerald-50 text-emerald-700"
          }`}
        >
          {selected.isCustom ? "Özel" : "ÇŞG"}
        </span>

        <span className="font-mono text-xs text-slate-500">{selected.code}</span>
        <span className="truncate text-slate-700">{selected.name}</span>

        <button
          type="button"
          onClick={onClear}
          className="ml-auto text-xs text-slate-500 underline"
        >
          Kaldır
        </button>
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-slate-200 bg-white p-3">
      <Input
        label="Poz ara (kod / ad)"
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        placeholder="En az 2 harf yazın"
      />

      {error && (
        <p className="mt-2 text-xs text-red-700">{error}</p>
      )}

      {searching && (
        <p className="mt-2 text-xs text-slate-500">Aranıyor…</p>
      )}

      {!searching && term.trim().length >= 2 && results.length === 0 && (
        <p className="mt-2 text-xs text-slate-500">
          Eşleşen poz yok. Listede olmayan kalem için özel poz açın.
        </p>
      )}

      {results.length > 0 && (
        <ul className="mt-2 max-h-48 divide-y divide-slate-100 overflow-y-auto rounded border border-slate-200">
          {results.map((option) => (
            <li key={option.id}>
              <button
                type="button"
                onClick={() =>
                  onSelect(
                    {
                      id: option.id,
                      code: option.code,
                      name: option.name,
                      isCustom: option.isCustom,
                    },
                    option.unit
                  )
                }
                className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-xs hover:bg-slate-50"
              >
                <span
                  className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${
                    option.isCustom
                      ? "border-violet-200 bg-violet-50 text-violet-700"
                      : "border-emerald-200 bg-emerald-50 text-emerald-700"
                  }`}
                >
                  {option.isCustom ? "Özel" : option.sourceName}
                </span>
                <span className="font-mono text-slate-500">{option.code}</span>
                <span className="truncate text-slate-700">{option.name}</span>
                <span className="ml-auto text-slate-400">{option.unit}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-2">
        {canCreateCustom ? (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={() => setShowCustom((current) => !current)}
          >
            {showCustom ? "Vazgeç" : "Listede yok — özel poz aç"}
          </Button>
        ) : (
          <p className="text-xs text-slate-500">
            Listede yoksa kalemi aşağıya serbest metin olarak yazın; poz
            tanımını teknik ofis açar. Talep beklemez.
          </p>
        )}
      </div>

      {showCustom && canCreateCustom && (
        <div className="mt-2 grid gap-2 rounded-lg border border-violet-200 bg-violet-50 p-3">
          <p className="text-xs text-violet-900">
            Açılan poz şirket kütüphanesine KALICI olarak eklenir ve
            sonraki taleplerde aramada çıkar; aynı kalem ikinci kez
            yazılmaz.
          </p>

          <Input
            label="Poz tanımı"
            value={customName}
            onChange={(event) => setCustomName(event.target.value)}
          />

          <div className="grid gap-2 md:grid-cols-2">
            <Input
              label="Birim"
              value={customUnit}
              onChange={(event) => setCustomUnit(event.target.value)}
            />

            <Input
              label="Tahmini birim fiyat (opsiyonel)"
              type="number"
              min="0"
              step="0.01"
              value={customPrice}
              onChange={(event) => setCustomPrice(event.target.value)}
            />
          </div>

          <Input
            label="Ölçü / açıklama"
            value={customNotes}
            onChange={(event) => setCustomNotes(event.target.value)}
          />

          <div>
            <Button
              type="button"
              size="sm"
              disabled={creating}
              onClick={() => void createCustom()}
            >
              {creating ? "Açılıyor…" : "Özel Pozu Aç ve Seç"}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
