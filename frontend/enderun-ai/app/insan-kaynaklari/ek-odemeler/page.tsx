"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ApiError } from "@/lib/api/api-client";

import {
  PersonnelListItem,
  personnelService,
} from "@/services/personnel.service";

import { extraPaymentService, type ExtraPayment } from "@/services/termination.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

function money(value: number) {
  return value.toLocaleString("tr-TR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

/**
 * Ek ödemeler (elden) — resmi bordroda görünmeyen tutarlar.
 *
 * Sayfanın uçları extra_payment.* izniyle korunur; yetkisi olmayan
 * kullanıcı listeyi çekemez ve uyarı görür. Bu tutarlar resmi
 * muhasebeye hiçbir kayıt üretmez.
 */
export default function ExtraPaymentsPage() {
  const [records, setRecords] = useState<ExtraPayment[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);

  const [personnelId, setPersonnelId] = useState("");
  const [amount, setAmount] = useState("");
  const [startDate, setStartDate] = useState(new Date().toISOString().slice(0, 10));
  const [endDate, setEndDate] = useState("");
  const [note, setNote] = useState("");

  const [denied, setDenied] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const refresh = useCallback(async () => {
    try {
      setRecords(await extraPaymentService.list());
      setDenied(false);
    } catch (loadError) {
      if (loadError instanceof ApiError && loadError.status === 403) {
        setDenied(true);
        return;
      }
      setError(getErrorMessage(loadError));
    }
  }, []);

  useEffect(() => {
    void refresh();

    personnelService
      .getAll()
      .then(setPersonnel)
      .catch(() => {
        // Personel listesi okunamazsa form boş kalır; hata yukarıda çıkar.
      });
  }, [refresh]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (saving || !personnelId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await extraPaymentService.create({
        personnelId,
        monthlyAmount: Number(amount || 0),
        effectiveStartDate: startDate,
        effectiveEndDate: endDate || null,
        note: note || null,
      });

      setNotice(result.message);
      setAmount("");
      setNote("");
      await refresh();
    } catch (createError) {
      setError(getErrorMessage(createError));
    } finally {
      setSaving(false);
    }
  }

  if (denied) {
    return (
      <ErpShell title="Ek Ödemeler" description="Elden ödenen aylık tutarlar">
        <p className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Bu sayfayı görüntüleme yetkiniz yok. Ek ödeme bilgileri yalnızca
          Genel Müdür, Finans ve Muhasebe rollerine açıktır.
        </p>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title="Ek Ödemeler"
      description="Elden ödenen aylık tutarlar — resmi bordroda görünmez"
    >
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <p className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
          Buradaki tutarlar resmi kasa, banka ve muhasebe defterinden
          tamamen izoledir; hiçbir yevmiye kaydı üretmez. Yalnızca gerçek
          maliyetin ve gerçek tazminat yükümlülüğünün hesaplanabilmesi
          için tutulur.
        </p>

        <form onSubmit={submit} className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <label className="text-xs font-bold text-slate-600">
            Personel
            <select
              value={personnelId}
              onChange={(e) => setPersonnelId(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            >
              <option value="">Seçin...</option>
              {personnel.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.firstName} {person.lastName}
                </option>
              ))}
            </select>
          </label>

          <label className="text-xs font-bold text-slate-600">
            Aylık Tutar (TL)
            <input
              type="number"
              min={0}
              step={0.01}
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>

          <label className="text-xs font-bold text-slate-600">
            Başlangıç
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>

          <label className="text-xs font-bold text-slate-600">
            Bitiş (boşsa sürüyor)
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>

          <label className="text-xs font-bold text-slate-600">
            Not
            <input
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>

          <div className="sm:col-span-2 lg:col-span-5">
            <button
              type="submit"
              disabled={saving || !personnelId}
              className="rounded-xl bg-cyan-700 px-4 py-2.5 text-sm font-bold text-white hover:bg-cyan-800 disabled:opacity-60"
            >
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </button>
          </div>
        </form>

        {error && (
          <p className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            {error}
          </p>
        )}

        {notice && (
          <p className="mt-3 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
            {notice}
          </p>
        )}
      </section>

      <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="text-sm font-bold text-slate-900">Tanımlı Ek Ödemeler</h2>

        {records.length === 0 ? (
          <p className="mt-2 text-sm text-slate-500">Kayıt yok.</p>
        ) : (
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 text-left text-xs uppercase text-slate-500">
                  <th className="py-2">Personel</th>
                  <th className="py-2 text-right">Aylık Tutar</th>
                  <th className="py-2">Başlangıç</th>
                  <th className="py-2">Bitiş</th>
                  <th className="py-2">Not</th>
                </tr>
              </thead>
              <tbody>
                {records.map((row) => (
                  <tr key={row.id} className="border-b border-slate-100">
                    <td className="py-2 text-slate-800">{row.personnelFullName}</td>
                    <td className="py-2 text-right tabular-nums text-slate-800">
                      {money(row.monthlyAmount)}
                    </td>
                    <td className="py-2 text-slate-600">
                      {new Date(row.effectiveStartDate).toLocaleDateString("tr-TR")}
                    </td>
                    <td className="py-2 text-slate-600">
                      {row.effectiveEndDate
                        ? new Date(row.effectiveEndDate).toLocaleDateString("tr-TR")
                        : "Sürüyor"}
                    </td>
                    <td className="py-2 text-slate-500">{row.note ?? "-"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ErpShell>
  );
}
