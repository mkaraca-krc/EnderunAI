"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  priceDifferenceService,
} from "@/services/price-difference.service";

export default function NewIndexPage() {
  const router = useRouter();

  const [saving, setSaving] =
    useState(false);

  const [error, setError] =
    useState("");

  const [form, setForm] = useState({
    year: 2026,
    month: 7,
    sourceName: "TÜİK",
    periodLabel: "",

    laborIndex: 0,
    fuelIndex: 0,
    materialIndex: 0,
    machineryIndex: 0,
    cementIndex: 0,
    otherIndex: 0,

    copperIndex: 0,
    steelIndex: 0,
    electricityIndex: 0,

    usdRate: 0,
    eurRate: 0,

    notes: "",
  });


  function update(
    key: keyof typeof form,
    value: string | number
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }));
  }


  async function save(
    event: React.FormEvent
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");

    try {

      await priceDifferenceService.createIndex({
        year:
          form.year,

        month:
          form.month,

        sourceName:
          form.sourceName,

        periodLabel:
          form.periodLabel,

        laborIndex:
          form.laborIndex,

        fuelIndex:
          form.fuelIndex,

        materialIndex:
          form.materialIndex,

        machineryIndex:
          form.machineryIndex,

        cementIndex:
          form.cementIndex,

        otherIndex:
          form.otherIndex,

        copperIndex:
          form.copperIndex,

        steelIndex:
          form.steelIndex,

        electricityIndex:
          form.electricityIndex,

        usdRate:
          form.usdRate,

        eurRate:
          form.eurRate,

        notes:
          form.notes,
      });


      router.push(
        "/fiyat-farki"
      );

    } catch (err) {

      setError(
        err instanceof Error
          ? err.message
          : "Endeks kaydedilemedi."
      );

    } finally {

      setSaving(false);

    }
  }


  const fields = [
    ["laborIndex","İşçilik"],
    ["fuelIndex","Akaryakıt"],
    ["materialIndex","Malzeme"],
    ["machineryIndex","Makine"],
    ["cementIndex","Çimento"],
    ["otherIndex","Diğer"],
    ["copperIndex","Bakır"],
    ["steelIndex","Çelik"],
    ["electricityIndex","Elektrik"],
    ["usdRate","USD"],
    ["eurRate","EUR"],
  ] as const;


  return (
    <ErpShell
      design="redwood"
      title="Yeni Endeks Dönemi"
      description="Fiyat farkı hesaplaması için aylık endeks girişi"
    >

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}


      <form
        className="erp-form-card"
        onSubmit={save}
      >

        <div className="erp-form-grid">

          <label>
            <span>Yıl</span>
            <input
              className="erp-input"
              type="number"
              value={form.year}
              onChange={(e)=>
                update(
                  "year",
                  Number(e.target.value)
                )
              }
            />
          </label>


          <label>
            <span>Ay</span>
            <input
              className="erp-input"
              type="number"
              min="1"
              max="12"
              value={form.month}
              onChange={(e)=>
                update(
                  "month",
                  Number(e.target.value)
                )
              }
            />
          </label>


          <label>
            <span>Kaynak</span>

            <input
              className="erp-input"
              value={form.sourceName}
              onChange={(e)=>
                update(
                  "sourceName",
                  e.target.value
                )
              }
            />

          </label>


          <label>
            <span>Dönem Açıklaması</span>

            <input
              className="erp-input"
              value={form.periodLabel}
              onChange={(e)=>
                update(
                  "periodLabel",
                  e.target.value
                )
              }
            />

          </label>


          {fields.map(
            ([key,label])=>(
              <label key={key}>

                <span>
                  {label}
                </span>

                <input
                  className="erp-input"
                  type="number"
                  step="0.000001"
                  value={form[key]}
                  onChange={(e)=>
                    update(
                      key,
                      Number(
                        e.target.value
                      )
                    )
                  }
                />

              </label>
            )
          )}


          <label className="span-2">

            <span>
              Notlar
            </span>

            <textarea
              value={form.notes}
              onChange={(e)=>
                update(
                  "notes",
                  e.target.value
                )
              }
            />

          </label>


        </div>


        <div className="erp-actions">

          <button
            type="submit"
            disabled={saving}
          >
            {saving
              ? "Kaydediliyor..."
              : "Endeksi Kaydet"}
          </button>

        </div>


      </form>

    </ErpShell>
  );
}
