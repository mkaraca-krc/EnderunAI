"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import { Badge, Button, Card, CardContent, Input } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  payrollSettingsService,
  type PayrollSettings,
  type PayrollTaxBracket,
} from "@/services/payroll-settings.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

/** Sayı alanları: number tipini korurken boş girişi de tolere eder. */
function numeric(value: string) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

type Props = { companyId: string | null };

export default function PayrollSettingsCard({ companyId }: Props) {
  const [settings, setSettings] = useState<PayrollSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [verifying, setVerifying] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [verificationNote, setVerificationNote] = useState("");

  const load = useCallback(async () => {
    if (!companyId) {
      setSettings(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      setSettings(await payrollSettingsService.get(companyId));
    } catch (err) {
      setSettings(null);
      setError(getErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    void load();
  }, [load]);

  function patch(changes: Partial<PayrollSettings>) {
    setSettings((current) => (current ? { ...current, ...changes } : current));
  }

  function patchBracket(index: number, changes: Partial<PayrollTaxBracket>) {
    setSettings((current) => {
      if (!current) return current;

      const taxBrackets = current.taxBrackets.map((bracket, i) =>
        i === index ? { ...bracket, ...changes } : bracket
      );

      return { ...current, taxBrackets };
    });
  }

  function addBracket() {
    setSettings((current) => {
      if (!current) return current;

      const last = current.taxBrackets[current.taxBrackets.length - 1];

      // Yeni dilim, son dilimin bittiği yerden başlar; son dilim üst sınırı
      // boş olmak zorunda olduğu için ondan önceye eklenir.
      const boundary = last?.lowerBound ?? 0;

      const brackets: PayrollTaxBracket[] = [
        ...current.taxBrackets.slice(0, -1),
        { ...last, upperBound: boundary, id: last?.id ?? "" },
        {
          id: "",
          order: current.taxBrackets.length + 1,
          lowerBound: boundary,
          upperBound: null,
          rate: last?.rate ?? 0,
        },
      ].map((bracket, index) => ({ ...bracket, order: index + 1 }));

      return { ...current, taxBrackets: brackets };
    });
  }

  function removeBracket(index: number) {
    setSettings((current) => {
      if (!current || current.taxBrackets.length <= 1) return current;

      const brackets = current.taxBrackets
        .filter((_, i) => i !== index)
        .map((bracket, i, all) => ({
          ...bracket,
          order: i + 1,
          // Son dilimin üst sınırı her zaman boş olmalı.
          upperBound: i === all.length - 1 ? null : bracket.upperBound,
        }));

      return { ...current, taxBrackets: brackets };
    });
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!companyId || !settings) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const updated = await payrollSettingsService.update(companyId, {
        minimumWageGross: settings.minimumWageGross,
        minimumWageNet: settings.minimumWageNet,
        sgkBaseFloor: settings.sgkBaseFloor,
        sgkBaseCeiling: settings.sgkBaseCeiling,
        sgkEmployeeRate: settings.sgkEmployeeRate,
        unemploymentEmployeeRate: settings.unemploymentEmployeeRate,
        sgkEmployerRate: settings.sgkEmployerRate,
        unemploymentEmployerRate: settings.unemploymentEmployerRate,
        sgkEmployerDiscountEnabled: settings.sgkEmployerDiscountEnabled,
        sgkEmployerDiscountPoints: settings.sgkEmployerDiscountPoints,
        stampTaxPerMille: settings.stampTaxPerMille,
        minimumWageIncomeTaxExemptionEnabled:
          settings.minimumWageIncomeTaxExemptionEnabled,
        minimumWageStampTaxExemptionEnabled:
          settings.minimumWageStampTaxExemptionEnabled,
        severanceCeiling: settings.severanceCeiling,
        severanceCeilingPeriodNote: settings.severanceCeilingPeriodNote,
        taxBrackets: settings.taxBrackets.map((bracket) => ({
          order: bracket.order,
          lowerBound: bracket.lowerBound,
          upperBound: bracket.upperBound,
          rate: bracket.rate,
        })),
      });

      setSettings(updated);
      setNotice(
        "Parametreler kaydedildi. Değişiklik sonrası doğrulama sıfırlandı — " +
          "bordro kesilebilmesi için yeniden onaylamanız gerekiyor."
      );
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function verify() {
    if (!companyId) return;

    setVerifying(true);
    setError("");
    setNotice("");

    try {
      setSettings(
        await payrollSettingsService.verify(
          companyId,
          verificationNote.trim() || null
        )
      );
      setVerificationNote("");
      setNotice("Parametreler doğrulandı. Bordro artık kesinleştirilebilir.");
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setVerifying(false);
    }
  }

  return (
    <Card className="lg:col-span-2">
      <CardContent className="p-6">
        <div className="mb-1 flex flex-wrap items-center gap-3">
          <h3 className="font-semibold text-slate-950">Bordro Parametreleri</h3>
          {settings && (
            <Badge variant={settings.isVerified ? "success" : "warning"}>
              {settings.isVerified ? "Doğrulandı" : "Doğrulanmadı"}
            </Badge>
          )}
          {settings && (
            <span className="text-sm text-slate-500">{settings.year} yılı</span>
          )}
        </div>

        <p className="mb-4 text-sm text-slate-500">
          Asgari ücret, SGK taban/tavan, prim oranları, gelir vergisi dilimleri
          ve damga vergisi. Bu değerler her yıl mevzuatla değiştiği için koda
          gömülmez. Sistemle gelen değerler yalnızca başlangıç değeridir;
          yürürlükteki SGK ve GİB tebliğleriyle karşılaştırıp doğrulamadan
          bordro kesinleştirilemez.
        </p>

        {error && (
          <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}

        {notice && (
          <div className="mb-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        {loading ? (
          <div className="py-10 text-center text-sm text-slate-500">
            Bordro parametreleri yükleniyor...
          </div>
        ) : !settings ? (
          <div className="py-10 text-center text-sm text-slate-500">
            Bu yıl için bordro parametresi tanımlı değil.
          </div>
        ) : (
          <form onSubmit={save} className="space-y-5">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Input
                label="Brüt Asgari Ücret (TL)"
                type="number"
                min={0}
                step={0.01}
                value={String(settings.minimumWageGross)}
                onChange={(e) =>
                  patch({ minimumWageGross: numeric(e.target.value) })
                }
              />
              <Input
                label="Net Asgari Ücret (TL)"
                type="number"
                min={0}
                step={0.01}
                value={String(settings.minimumWageNet)}
                onChange={(e) =>
                  patch({ minimumWageNet: numeric(e.target.value) })
                }
              />
              <Input
                label="SGK Tabanı (TL)"
                type="number"
                min={0}
                step={0.01}
                value={String(settings.sgkBaseFloor)}
                onChange={(e) => patch({ sgkBaseFloor: numeric(e.target.value) })}
              />
              <Input
                label="SGK Tavanı (TL)"
                type="number"
                min={0}
                step={0.01}
                value={String(settings.sgkBaseCeiling)}
                onChange={(e) =>
                  patch({ sgkBaseCeiling: numeric(e.target.value) })
                }
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
              <Input
                label="İşçi SGK (%)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.sgkEmployeeRate)}
                onChange={(e) =>
                  patch({ sgkEmployeeRate: numeric(e.target.value) })
                }
              />
              <Input
                label="İşçi İşsizlik (%)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.unemploymentEmployeeRate)}
                onChange={(e) =>
                  patch({ unemploymentEmployeeRate: numeric(e.target.value) })
                }
              />
              <Input
                label="İşveren SGK (%)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.sgkEmployerRate)}
                onChange={(e) =>
                  patch({ sgkEmployerRate: numeric(e.target.value) })
                }
              />
              <Input
                label="İşveren İşsizlik (%)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.unemploymentEmployerRate)}
                onChange={(e) =>
                  patch({ unemploymentEmployerRate: numeric(e.target.value) })
                }
              />
              <Input
                label="Damga Vergisi (binde)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.stampTaxPerMille)}
                onChange={(e) =>
                  patch({ stampTaxPerMille: numeric(e.target.value) })
                }
              />
              <Input
                label="İşveren Prim İndirimi (puan)"
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={String(settings.sgkEmployerDiscountPoints)}
                onChange={(e) =>
                  patch({ sgkEmployerDiscountPoints: numeric(e.target.value) })
                }
              />
              <Input
                label="Kıdem Tazminatı Tavanı (TL)"
                type="number"
                min={0}
                step={0.01}
                value={String(settings.severanceCeiling)}
                onChange={(e) =>
                  patch({ severanceCeiling: numeric(e.target.value) })
                }
              />
              <Input
                label="Kıdem Tavanı Dönemi"
                value={settings.severanceCeilingPeriodNote ?? ""}
                placeholder="01.01.2026-30.06.2026"
                onChange={(e) =>
                  patch({ severanceCeilingPeriodNote: e.target.value })
                }
              />
            </div>

            <p className="text-xs text-slate-500">
              Kıdem tazminatı tavanı memur maaş katsayısına bağlı olduğu için
              yılda iki kez (Ocak ve Temmuz) değişir; dönem alanı hangi
              döneme ait olduğunu kayda geçirir.
            </p>

            <div className="grid gap-3 sm:grid-cols-3">
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input
                  type="checkbox"
                  checked={settings.sgkEmployerDiscountEnabled}
                  onChange={(e) =>
                    patch({ sgkEmployerDiscountEnabled: e.target.checked })
                  }
                />
                İşveren prim indirimi uygulansın
              </label>
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input
                  type="checkbox"
                  checked={settings.minimumWageIncomeTaxExemptionEnabled}
                  onChange={(e) =>
                    patch({
                      minimumWageIncomeTaxExemptionEnabled: e.target.checked,
                    })
                  }
                />
                Asgari ücret gelir vergisi istisnası
              </label>
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input
                  type="checkbox"
                  checked={settings.minimumWageStampTaxExemptionEnabled}
                  onChange={(e) =>
                    patch({
                      minimumWageStampTaxExemptionEnabled: e.target.checked,
                    })
                  }
                />
                Asgari ücret damga vergisi istisnası
              </label>
            </div>

            <div>
              <div className="mb-2 flex items-center justify-between">
                <h4 className="text-sm font-semibold text-slate-800">
                  Gelir Vergisi Dilimleri
                </h4>
                <Button type="button" variant="secondary" onClick={addBracket}>
                  + Dilim Ekle
                </Button>
              </div>

              <p className="mb-3 text-xs text-slate-500">
                Dilimler kümülatif vergi matrahına uygulanır ve aralarında
                boşluk olamaz: her dilimin üst sınırı bir sonrakinin alt
                sınırına eşit olmalı, son dilimin üst sınırı boş kalmalıdır.
              </p>

              <div className="space-y-2">
                {settings.taxBrackets.map((bracket, index) => {
                  const isLast = index === settings.taxBrackets.length - 1;

                  return (
                    <div
                      key={`${bracket.order}-${index}`}
                      className="grid gap-3 sm:grid-cols-[auto_1fr_1fr_1fr_auto] sm:items-end"
                    >
                      <span className="text-sm font-medium text-slate-600">
                        {bracket.order}.
                      </span>
                      <Input
                        label="Alt Sınır (TL)"
                        type="number"
                        min={0}
                        step={0.01}
                        value={String(bracket.lowerBound)}
                        onChange={(e) =>
                          patchBracket(index, {
                            lowerBound: numeric(e.target.value),
                          })
                        }
                      />
                      <Input
                        label={isLast ? "Üst Sınır (sınırsız)" : "Üst Sınır (TL)"}
                        type="number"
                        min={0}
                        step={0.01}
                        disabled={isLast}
                        value={isLast ? "" : String(bracket.upperBound ?? "")}
                        onChange={(e) =>
                          patchBracket(index, {
                            upperBound: numeric(e.target.value),
                          })
                        }
                      />
                      <Input
                        label="Oran (%)"
                        type="number"
                        min={0}
                        max={100}
                        step={0.01}
                        value={String(bracket.rate)}
                        onChange={(e) =>
                          patchBracket(index, { rate: numeric(e.target.value) })
                        }
                      />
                      <Button
                        type="button"
                        variant="secondary"
                        disabled={settings.taxBrackets.length <= 1}
                        onClick={() => removeBracket(index)}
                      >
                        Sil
                      </Button>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <Button type="submit" loading={saving}>
                Parametreleri Kaydet
              </Button>
            </div>

            <div className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <h4 className="mb-1 text-sm font-semibold text-slate-800">
                Mevzuat Doğrulaması
              </h4>
              <p className="mb-3 text-xs text-slate-500">
                {settings.isVerified
                  ? `Doğrulandı: ${
                      settings.verifiedAtUtc
                        ? new Date(settings.verifiedAtUtc).toLocaleString("tr-TR")
                        : ""
                    }${
                      settings.verificationNote
                        ? ` — ${settings.verificationNote}`
                        : ""
                    }`
                  : "Yukarıdaki değerleri yürürlükteki SGK ve GİB tebliğleriyle " +
                    "karşılaştırın. Doğrulanana kadar bordro kesinleştirilemez."}
              </p>

              <div className="flex flex-wrap items-end gap-3">
                <div className="min-w-[16rem] flex-1">
                  <Input
                    label="Doğrulama Notu (tebliğ no / tarih)"
                    value={verificationNote}
                    onChange={(e) => setVerificationNote(e.target.value)}
                    placeholder="Örn: 2026 SGK genelgesi ile karşılaştırıldı"
                  />
                </div>
                <Button
                  type="button"
                  onClick={verify}
                  loading={verifying}
                  disabled={settings.isVerified}
                >
                  {settings.isVerified ? "Doğrulandı" : "Doğrulandı Olarak İşaretle"}
                </Button>
              </div>
            </div>

            <p className="text-xs text-slate-500">
              Örnek: brüt asgari ücret {money.format(settings.minimumWageGross)},
              SGK tavanı {money.format(settings.sgkBaseCeiling)}.
            </p>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
