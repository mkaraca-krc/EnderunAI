"use client";

import { FormEvent, useEffect, useState } from "react";

import { ApiError } from "@/lib/api/api-client";
import {
  CASH_PAYMENT_KINDS,
  personnelCashPaymentService,
  type CashPaymentSummary,
  type PersonnelCashPayment,
} from "@/services/personnel-cash-payment.service";
import type { PersonnelListItem } from "@/services/personnel.service";
import { money } from "@/lib/format/turkish";


const dateFormat = new Intl.DateTimeFormat("tr-TR");

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

/**
 * Elden ödeme kasası: fiilen ödenen tutarların defteri.
 *
 * Ek ödeme kartı aylık ne ödeneceğinin TANIMIdır; burası gerçekten ne
 * zaman ne kadar ödendiğidir. İkisi ayrı çünkü tanım olmadan da ödeme
 * yapılabiliyor (bir kerelik prim) ve tanım varken ödeme yapılmamış
 * olabiliyor — dönem özeti tam bu farkı gösteriyor.
 *
 * Bu kayıtlar muhasebe fişi, kasa hareketi ya da proje maliyet kaydı
 * ÜRETMEZ.
 */
export default function CashPaymentLedger({
  personnel,
  companyId,
}: {
  personnel: PersonnelListItem[];
  companyId: string;
}) {
  const today = new Date();

  const [entries, setEntries] = useState<PersonnelCashPayment[]>([]);
  const [summary, setSummary] = useState<CashPaymentSummary | null>(null);

  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);

  const [personnelId, setPersonnelId] = useState("");
  const [kind, setKind] = useState(0);
  const [amount, setAmount] = useState("");
  const [paymentDate, setPaymentDate] = useState(
    today.toISOString().slice(0, 10)
  );
  const [note, setNote] = useState("");

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  /** Değeri değiştikçe veri yeniden çekilir — kayıt/silme sonrası. */
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    if (!companyId) return;

    // Veri çekimi ve setState'ler async gövdenin içinde; efekt
    // gövdesinden eşzamanlı setState zincirleme render tetikler.
    let cancelled = false;

    void (async () => {
      try {
        const [list, periodSummary] = await Promise.all([
          personnelCashPaymentService.list({ companyId, year, month }),
          personnelCashPaymentService.getSummary(companyId, year, month),
        ]);

        if (cancelled) return;

        setEntries(list);
        setSummary(periodSummary);
        setError("");
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [companyId, year, month, reloadToken]);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    if (!personnelId) {
      setError("Personel seçin.");
      return;
    }

    const parsed = Number(amount.replace(",", "."));

    if (!Number.isFinite(parsed) || parsed <= 0) {
      setError("Tutar sıfırdan büyük olmalıdır.");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await personnelCashPaymentService.create({
        personnelId,
        kind,
        paymentDate,
        amount: parsed,
        // Aylık ücret dışındaki ödemeler dönemsiz olabilir; yine de
        // seçili dönemi yazıyoruz ki özet tutarlı kalsın.
        periodYear: year,
        periodMonth: month,
        note: note.trim() || null,
      });

      setAmount("");
      setNote("");
      setNotice("Ödeme kaydedildi. Bu kayıt muhasebeye yansımaz.");

      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: string) {
    try {
      await personnelCashPaymentService.remove(id);
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    }
  }

  return (
    <section className="erp-table-card" style={{ marginTop: 24 }}>
      <div className="erp-table-header">
        <h2>Elden Ödeme Kasası</h2>
        <small>
          Fiilen ödenen tutarlar — muhasebeye, kasaya ve proje maliyet
          defterine yazılmaz
        </small>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div
        style={{
          display: "flex",
          gap: 8,
          flexWrap: "wrap",
          alignItems: "flex-end",
          padding: "12px 16px",
        }}
      >
        <label>
          <span style={{ display: "block", fontSize: 11 }}>Yıl</span>
          <input
            type="number"
            value={year}
            min={2000}
            max={2100}
            onChange={(e) => setYear(Number(e.target.value))}
          />
        </label>

        <label>
          <span style={{ display: "block", fontSize: 11 }}>Ay</span>
          <select
            value={month}
            onChange={(e) => setMonth(Number(e.target.value))}
          >
            {MONTHS.map((name, index) => (
              <option key={name} value={index + 1}>
                {name}
              </option>
            ))}
          </select>
        </label>
      </div>

      {summary && (
        <div className="erp-quick-grid" style={{ padding: "0 16px 12px" }}>
          <div className="erp-panel">
            <small style={{ display: "block", marginBottom: 4 }}>
              Tanımlı Toplam
            </small>
            <strong>{money(summary.definedTotal)}</strong>
          </div>
          <div className="erp-panel">
            <small style={{ display: "block", marginBottom: 4 }}>
              Fiilen Ödenen
            </small>
            <strong>{money(summary.paidTotal)}</strong>
          </div>
          <div className="erp-panel">
            <small style={{ display: "block", marginBottom: 4 }}>
              Eksik Ödenen Personel
            </small>
            <strong>{summary.unpaidCount}</strong>
          </div>
        </div>
      )}

      <form
        onSubmit={handleSubmit}
        style={{
          display: "flex",
          gap: 8,
          flexWrap: "wrap",
          alignItems: "flex-end",
          padding: "0 16px 16px",
        }}
      >
        <label>
          <span style={{ display: "block", fontSize: 11 }}>Personel</span>
          <select
            value={personnelId}
            onChange={(e) => setPersonnelId(e.target.value)}
          >
            <option value="">Seçin</option>
            {personnel.map((person) => (
              <option key={person.id} value={person.id}>
                {person.firstName} {person.lastName}
              </option>
            ))}
          </select>
        </label>

        <label>
          <span style={{ display: "block", fontSize: 11 }}>Tür</span>
          <select value={kind} onChange={(e) => setKind(Number(e.target.value))}>
            {CASH_PAYMENT_KINDS.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>

        <label>
          <span style={{ display: "block", fontSize: 11 }}>Ödeme tarihi</span>
          <input
            type="date"
            value={paymentDate}
            onChange={(e) => setPaymentDate(e.target.value)}
          />
        </label>

        <label>
          <span style={{ display: "block", fontSize: 11 }}>Tutar</span>
          <input
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="0,00"
          />
        </label>

        <label style={{ flex: "1 1 200px" }}>
          <span style={{ display: "block", fontSize: 11 }}>Not</span>
          <input
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="opsiyonel"
          />
        </label>

        <button type="submit" className="erp-primary-button" disabled={saving}>
          {saving ? "Kaydediliyor..." : "Ödemeyi Kaydet"}
        </button>
      </form>

      {summary && summary.rows.length > 0 && (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Personel</th>
                <th>Tanımlı</th>
                <th>Ödenen</th>
                <th>Fark</th>
              </tr>
            </thead>
            <tbody>
              {summary.rows.map((row) => (
                <tr key={row.personnelId}>
                  <td>{row.personnelFullName}</td>
                  <td>{money(row.definedAmount)}</td>
                  <td>{money(row.paidAmount)}</td>
                  <td>
                    {row.difference === 0 ? (
                      <span className="erp-status green">Tam</span>
                    ) : (
                      <strong>{money(row.difference)}</strong>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="erp-table-header">
        <h2 style={{ fontSize: 14 }}>Dönem Hareketleri</h2>
        <small>{entries.length} kayıt</small>
      </div>

      {entries.length === 0 ? (
        <div className="erp-empty-state">
          <strong>Bu dönemde elden ödeme kaydı yok</strong>
        </div>
      ) : (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Tarih</th>
                <th>Personel</th>
                <th>Tür</th>
                <th>Tutar</th>
                <th>Not</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id}>
                  <td>{dateFormat.format(new Date(entry.paymentDate))}</td>
                  <td>{entry.personnelFullName}</td>
                  <td>{entry.kindName}</td>
                  <td>{money(entry.amount)}</td>
                  <td>{entry.note ?? "—"}</td>
                  <td>
                    <button
                      type="button"
                      className="erp-secondary-button"
                      onClick={() => void handleDelete(entry.id)}
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
