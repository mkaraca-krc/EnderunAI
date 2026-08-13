"use client";

import { Input } from "@/components/ui";
import {
  requestedBrandLabel,
  type RequestedBrandFields,
} from "@/lib/purchasing/requested-brand";

type Props = {
  brand: string;
  irrelevant: boolean;
  onBrandChange: (value: string) => void;
  onIrrelevantChange: (value: boolean) => void;
};

/**
 * Talep kaleminde İSTENEN MARKA girişi.
 *
 * Marka zorunlu ama esnek: ya bir marka yazılır ya da "farketmez /
 * muadil kabul" işaretlenir. İkisi birden de olabilir — o zaman marka
 * TERCİH olur ve zincirde öyle taşınır; yazılan marka silinmez, çünkü
 * "muadil olur ama Schneider iyi olur" gerçek bir taleptir.
 *
 * Seçimin ne anlama geldiği kullanıcıya anında yazılır; üç durum
 * arasındaki fark ancak böyle görünür olur.
 */
export default function RequestedBrandField({
  brand,
  irrelevant,
  onBrandChange,
  onIrrelevantChange,
}: Props) {
  const fields: RequestedBrandFields = {
    requestedBrand: brand,
    brandIrrelevant: irrelevant,
  };

  const invalid = !brand.trim() && !irrelevant;

  return (
    <div className="space-y-2">
      <Input
        label="İstenen Marka"
        value={brand}
        placeholder="Örn. Schneider"
        onChange={(e) => onBrandChange(e.target.value)}
      />

      <label className="flex items-center gap-2 text-sm text-slate-700">
        <input
          type="checkbox"
          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
          checked={irrelevant}
          onChange={(e) => onIrrelevantChange(e.target.checked)}
        />
        Marka farketmez / muadil kabul
      </label>

      <p
        className={
          invalid
            ? "text-xs font-medium text-red-600"
            : "text-xs text-slate-500"
        }
      >
        {invalid
          ? "Marka girin ya da \"marka farketmez / muadil kabul\" işaretleyin."
          : requestedBrandLabel(fields)}
      </p>
    </div>
  );
}
