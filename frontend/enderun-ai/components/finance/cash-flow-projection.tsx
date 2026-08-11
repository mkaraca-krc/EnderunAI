"use client";

import { useCallback, useEffect, useState } from "react";

import { Button, ConfirmDialog, Input, Modal, Select } from "@/components/ui";
import {
  cashFlowService,
  type CashFlowProjection,
  type EstimatedExpense,
} from "@/services/cash-flow.service";
import type { ProjectListItem } from "@/services/project.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const moneyExact = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

const emptyExpense = {
  description: "",
  amount: "",
  startYear: String(new Date().getFullYear()),
  startMonth: String(new Date().getMonth() + 1),
  recurrenceCount: "6",
  paymentDay: "1",
  projectId: "",
};

/**
 * Likidite takvimi.
 *
 * Kova görünümü "önümüzdeki 60 günde ne olur" diyor ama iki tahsilat
 * arasındaki çukuru gizliyor. Burada bakiye GÜN GÜN yürüyor: hangi
 * gün açığa düşüldüğü ve çukurun dibinde ne kadar para gerektiği
 * doğrudan okunuyor.
 *
 * KESİN ↔ TAHMİNİ ayrı rozetle: tahmini bir gecikmeyi kesin bir borç
 * gibi göstermek yanlış karar verdirir.
 */
export default function CashFlowProjectionPanel({
  companyId,
  projects,
}: {
  companyId: string;
  projects: ProjectListItem[];
}) {
  const [months, setMonths] = useState(6);
  const [targetDate, setTargetDate] = useState("");
  const [data, setData] = useState<CashFlowProjection | null>(null);
  const [expenses, setExpenses] = useState<EstimatedExpense[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [showExpenses, setShowExpenses] = useState(false);
  const [expenseForm, setExpenseForm] = useState(emptyExpense);
  const [expenseError, setExpenseError] = useState("");
  const [saving, setSaving] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<EstimatedExpense | null>(null);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      const [projection, expenseRows] = await Promise.all([
        cashFlowService.getProjection({
          companyId,
          months,
          targetDate: targetDate || undefined,
        }),
        cashFlowService.listEstimatedExpenses(companyId).catch(() => []),
      ]);

      setData(projection);
      setExpenses(expenseRows);
    } catch (err) {
      setData(null);
      setError(err instanceof Error ? err.message : "Projeksiyon alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, months, targetDate]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  async function saveExpense() {
    setSaving(true);
    setExpenseError("");

    try {
      const amount = Number(expenseForm.amount);

      if (!expenseForm.description.trim()) {
        throw new Error("Gider açıklaması zorunludur.");
      }

      if (!Number.isFinite(amount) || amount <= 0) {
        throw new Error("Tutar sıfırdan büyük olmalıdır.");
      }

      await cashFlowService.createEstimatedExpense({
        companyId,
        description: expenseForm.description.trim(),
        amount,
        startYear: Number(expenseForm.startYear),
        startMonth: Number(expenseForm.startMonth),
        recurrenceCount: Number(expenseForm.recurrenceCount),
        paymentDay: Number(expenseForm.paymentDay),
        projectId: expenseForm.projectId || null,
      });

      setExpenseForm(emptyExpense);
      setNotice("Tahmini gider eklendi.");
      await load();
    } catch (err) {
      setExpenseError(err instanceof Error ? err.message : "Kayıt başarısız.");
    } finally {
      setSaving(false);
    }
  }

  async function removeExpense() {
    if (!removeTarget) return;

    setSaving(true);
    setExpenseError("");

    try {
      await cashFlowService.deleteEstimatedExpense(removeTarget.id);

      setRemoveTarget(null);
      setNotice("Tahmini gider kaldırıldı.");
      await load();
    } catch (err) {
      setExpenseError(err instanceof Error ? err.message : "Silme başarısız.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="grid gap-4">
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {notice && (
        <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {notice}
        </div>
      )}

      <div className="flex flex-wrap items-end gap-2">
        <div className="w-40">
          <Select
            label="Ufuk"
            value={String(months)}
            onChange={(event) => setMonths(Number(event.target.value))}
            options={[
              { value: "3", label: "3 ay" },
              { value: "6", label: "6 ay" },
              { value: "12", label: "12 ay" },
            ]}
          />
        </div>

        <div className="w-48">
          <Input
            label="Hedef tarih"
            type="date"
            value={targetDate}
            onChange={(event) => setTargetDate(event.target.value)}
          />
        </div>

        <Button
          type="button"
          variant="secondary"
          onClick={() => setShowExpenses(true)}
        >
          Tahmini Giderler ({expenses.length})
        </Button>
      </div>

      {/* Tablonun neyi göstermediğini söyleyen notlar. Eksikliği
          sessizce taşımak, bakiyeyi olduğundan iyimser okutur. */}
      {data?.notes?.map((note) => (
        <div
          key={note}
          className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
        >
          {note}
        </div>
      ))}

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-6 text-sm text-slate-500">
          Takvim hesaplanıyor…
        </div>
      ) : !data ? null : (
        <>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {[
              ["Bugünkü bakiye", money.format(data.openingBalance), ""],
              [
                "Dönem sonu",
                money.format(data.closingBalance),
                `${formatDate(data.toDate)}`,
              ],
              [
                "İlk açık günü",
                data.shortfall
                  ? formatDate(data.shortfall.firstNegativeDate)
                  : "Yok",
                data.shortfall
                  ? money.format(data.shortfall.firstNegativeBalance)
                  : "Bakiye pozitif seyrediyor",
              ],
              [
                "Gereken finansman",
                data.shortfall
                  ? money.format(data.shortfall.requiredFinancing)
                  : "—",
                data.shortfall
                  ? `En derin: ${formatDate(data.shortfall.peakDate)}`
                  : "",
              ],
            ].map(([label, value, hint]) => (
              <article
                key={label}
                className={`rounded-xl border p-4 shadow-sm ${
                  label === "Gereken finansman" && data.shortfall
                    ? "border-red-200 bg-red-50"
                    : "border-slate-200 bg-white"
                }`}
              >
                <span className="text-xs font-bold text-slate-500">{label}</span>
                <strong className="mt-2 block text-2xl tabular-nums text-slate-800">
                  {value}
                </strong>
                {hint && (
                  <span className="mt-1 block text-xs text-slate-500">{hint}</span>
                )}
              </article>
            ))}
          </div>

          {data.target && (
            <div className="rounded-xl border border-brand-200 bg-brand-50 p-4">
              <strong className="text-sm text-brand-900">
                {formatDate(data.target.targetDate)} tarihine kadar
              </strong>

              <div className="mt-2 grid gap-3 text-sm md:grid-cols-4">
                <span>
                  Giriş:{" "}
                  <strong className="tabular-nums">
                    {moneyExact.format(data.target.inflow)}
                  </strong>
                </span>
                <span>
                  Çıkış:{" "}
                  <strong className="tabular-nums">
                    {moneyExact.format(data.target.outflow)}
                  </strong>
                </span>
                <span>
                  Bakiye:{" "}
                  <strong className="tabular-nums">
                    {moneyExact.format(data.target.closingBalance)}
                  </strong>
                </span>
                <span>
                  Gereken finansman:{" "}
                  <strong className="tabular-nums">
                    {moneyExact.format(data.target.requiredFinancing)}
                  </strong>
                </span>
              </div>
            </div>
          )}

          {/* AYLIK ÖZET: üstte kaba resim, altta günün gününe döküm. */}
          <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
            <table className="w-full min-w-[720px] text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50 text-xs font-bold text-slate-500">
                <tr>
                  <th className="p-3">Ay</th>
                  <th className="p-3 text-right">Giriş</th>
                  <th className="p-3 text-right">Çıkış</th>
                  <th className="p-3 text-right">Net</th>
                  <th className="p-3 text-right">Ay sonu</th>
                  <th className="p-3 text-right">En düşük</th>
                </tr>
              </thead>
              <tbody>
                {data.monthlySummary.map((month) => (
                  <tr
                    key={`${month.year}-${month.month}`}
                    className="border-b border-slate-100 last:border-0"
                  >
                    <td className="p-3 font-semibold text-slate-800">
                      {month.label}
                    </td>
                    <td className="p-3 text-right tabular-nums text-emerald-700">
                      {money.format(month.inflow)}
                    </td>
                    <td className="p-3 text-right tabular-nums text-red-700">
                      {money.format(month.outflow)}
                    </td>
                    <td className="p-3 text-right tabular-nums">
                      {money.format(month.net)}
                    </td>
                    <td
                      className={`p-3 text-right font-semibold tabular-nums ${
                        month.closingBalance < 0 ? "text-red-700" : "text-slate-800"
                      }`}
                    >
                      {money.format(month.closingBalance)}
                    </td>
                    <td
                      className={`p-3 text-right tabular-nums ${
                        month.lowestBalance < 0 ? "text-red-700" : "text-slate-500"
                      }`}
                    >
                      {money.format(month.lowestBalance)}
                      <span className="block text-[11px] text-slate-400">
                        {formatDate(month.lowestBalanceDate)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {data.days.length === 0 ? (
            <div className="rounded-xl border border-slate-200 bg-white p-6 text-sm text-slate-500">
              Seçilen ufukta hareket yok.
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
              <table className="w-full min-w-[860px] text-left text-sm">
                <thead className="border-b border-slate-200 bg-slate-50 text-xs font-bold text-slate-500">
                  <tr>
                    <th className="p-3">Tarih</th>
                    <th className="p-3">Hareket</th>
                    <th className="p-3 text-right">Tutar</th>
                    <th className="p-3 text-right">Gün sonu bakiye</th>
                  </tr>
                </thead>
                <tbody>
                  {data.days.map((day) => (
                    <tr
                      key={day.date}
                      className={`border-b border-slate-100 align-top last:border-0 ${
                        day.runningBalance < 0 ? "bg-red-50" : ""
                      }`}
                    >
                      <td className="p-3 whitespace-nowrap font-semibold text-slate-700">
                        {formatDate(day.date)}
                        {data.shortfall?.peakDate === day.date && (
                          <span className="mt-1 block rounded-full border border-red-300 bg-red-100 px-2 py-0.5 text-[10px] font-bold text-red-800">
                            EN DERİN
                          </span>
                        )}
                      </td>

                      <td className="p-3">
                        <div className="grid gap-1">
                          {day.items.map((item, index) => (
                            <div
                              key={`${item.kind}-${index}`}
                              className="flex flex-wrap items-center gap-2 text-xs"
                            >
                              {/* Üç durum: Kesin / Tahmini / Nakit değil.
                                  Nakit-dışı kalem (barter alacağı) bakiyeye
                                  girmiyor; aynı renkte gösterilseydi nakit
                                  sanılırdı. */}
                              <span
                                className={`rounded-full border px-2 py-0.5 font-semibold ${
                                  item.certainty === 0
                                    ? "border-slate-300 bg-slate-50 text-slate-700"
                                    : item.certainty === 2
                                      ? "border-violet-300 bg-violet-50 text-violet-800"
                                      : "border-amber-300 bg-amber-50 text-amber-800"
                                }`}
                              >
                                {item.certaintyName}
                              </span>

                              <span className="font-medium text-slate-700">
                                {item.kindName}
                              </span>

                              <span className="truncate text-slate-500">
                                {item.title}
                                {item.projectCode ? ` · ${item.projectCode}` : ""}
                              </span>

                              <span
                                className={`ml-auto tabular-nums ${
                                  item.isInflow ? "text-emerald-700" : "text-red-700"
                                }`}
                              >
                                {item.isInflow ? "+" : "−"}
                                {moneyExact.format(item.amount)}
                              </span>
                            </div>
                          ))}
                        </div>
                      </td>

                      <td className="p-3 text-right tabular-nums">
                        <span className="block text-emerald-700">
                          +{money.format(day.inflow)}
                        </span>
                        <span className="block text-red-700">
                          −{money.format(day.outflow)}
                        </span>
                      </td>

                      <td
                        className={`p-3 text-right font-bold tabular-nums ${
                          day.runningBalance < 0 ? "text-red-700" : "text-slate-800"
                        }`}
                      >
                        {moneyExact.format(day.runningBalance)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {/* TAHMİNİ GİDER: gider merkezi gelene kadar geçici. */}
      <Modal
        open={showExpenses}
        title="Tekrarlayan tahmini giderler"
        description="Gider merkezi modülü kurulana kadar kira, elektrik gibi düzenli giderler buradan takvime girer. Satırlar takvimde TAHMİNİ olarak işaretlenir."
        onClose={() => setShowExpenses(false)}
        busy={saving}
        size="lg"
      >
        <div className="grid gap-3">
          <div className="grid gap-2 md:grid-cols-2">
            <Input
              label="Açıklama"
              value={expenseForm.description}
              onChange={(event) =>
                setExpenseForm({ ...expenseForm, description: event.target.value })
              }
            />

            <Input
              label="Aylık tutar"
              type="number"
              min="0"
              step="0.01"
              value={expenseForm.amount}
              onChange={(event) =>
                setExpenseForm({ ...expenseForm, amount: event.target.value })
              }
            />

            <Select
              label="Başlangıç ayı"
              value={expenseForm.startMonth}
              onChange={(event) =>
                setExpenseForm({ ...expenseForm, startMonth: event.target.value })
              }
              options={MONTHS.map((name, index) => ({
                value: String(index + 1),
                label: name,
              }))}
            />

            <Input
              label="Başlangıç yılı"
              type="number"
              value={expenseForm.startYear}
              onChange={(event) =>
                setExpenseForm({ ...expenseForm, startYear: event.target.value })
              }
            />

            <Input
              label="Kaç ay tekrar"
              type="number"
              min="1"
              max="24"
              value={expenseForm.recurrenceCount}
              onChange={(event) =>
                setExpenseForm({
                  ...expenseForm,
                  recurrenceCount: event.target.value,
                })
              }
            />

            <Input
              label="Ayın kaçında"
              type="number"
              min="1"
              max="31"
              value={expenseForm.paymentDay}
              onChange={(event) =>
                setExpenseForm({ ...expenseForm, paymentDay: event.target.value })
              }
            />

            <div className="md:col-span-2">
              <Select
                label="Proje (boşsa merkez gideri)"
                value={expenseForm.projectId}
                onChange={(event) =>
                  setExpenseForm({ ...expenseForm, projectId: event.target.value })
                }
                options={[
                  { value: "", label: "Şirket geneli" },
                  ...projects.map((project) => ({
                    value: project.id,
                    label: `${project.code} — ${project.name}`,
                  })),
                ]}
              />
            </div>
          </div>

          {expenseError && (
            <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              {expenseError}
            </p>
          )}

          <div>
            <Button
              type="button"
              disabled={saving}
              onClick={() => void saveExpense()}
            >
              {saving ? "Ekleniyor…" : "Gider Ekle"}
            </Button>
          </div>

          {expenses.length > 0 && (
            <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
              {expenses.map((expense) => (
                <li
                  key={expense.id}
                  className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm"
                >
                  <span className="font-medium text-slate-700">
                    {expense.description}
                  </span>

                  <span className="text-slate-500">
                    {MONTHS[expense.startMonth - 1]} {expense.startYear} ·{" "}
                    {expense.recurrenceCount} ay · ayın {expense.paymentDay}&apos;i
                    {expense.projectCode ? ` · ${expense.projectCode}` : ""}
                  </span>

                  <strong className="ml-auto tabular-nums">
                    {moneyExact.format(expense.amount)}
                  </strong>

                  <button
                    type="button"
                    onClick={() => setRemoveTarget(expense)}
                    className="text-xs text-red-700 underline"
                  >
                    Kaldır
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </Modal>

      <ConfirmDialog
        key={removeTarget?.id ?? "kapali"}
        open={removeTarget !== null}
        title="Tahmini gideri kaldır"
        description={
          removeTarget
            ? `"${removeTarget.description}" takvimden çıkarılacak; bakiye bu tutar kadar iyileşecek.`
            : ""
        }
        confirmLabel="Kaldır"
        busy={saving}
        error={expenseError}
        onCancel={() => setRemoveTarget(null)}
        onConfirm={() => void removeExpense()}
      />
    </div>
  );
}
