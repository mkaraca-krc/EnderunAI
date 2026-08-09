"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  corporateTaxRateService,
  taxService,
  type TaxObligation,
  type TaxOverview,
} from "@/services/tax.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const moneyDetailed = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function TaxPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [year, setYear] = useState(new Date().getFullYear());

  const [overview, setOverview] = useState<TaxOverview | null>(null);
  const [calendar, setCalendar] = useState<TaxObligation[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Kurumlar vergisi oranı yıl bazlı ve varsayılanı yok; tanımsız
  // yılda buradan girilir.
  const [rateInput, setRateInput] = useState("");
  const [savingRate, setSavingRate] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const result = await companyService.getAll();
        setCompanies(result);
        setCompanyId(result[0]?.id ?? "");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const [overviewResult, calendarResult] = await Promise.all([
        taxService.getOverview(companyId, year),
        taxService.getCalendar(companyId),
      ]);

      setOverview(overviewResult);
      setCalendar(calendarResult);
    } catch (err) {
      setOverview(null);
      setError(err instanceof Error ? err.message : "Vergi görünümü alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, year]);

  useEffect(() => {
    void load();
  }, [load]);

  async function saveRate() {
    if (!companyId) return;

    setSavingRate(true);
    setError("");
    setNotice("");

    try {
      await corporateTaxRateService.save(companyId, year, Number(rateInput));

      setRateInput("");
      setNotice(`${year} kurumlar vergisi oranı kaydedildi.`);

      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Oran kaydedilemedi.");
    } finally {
      setSavingRate(false);
    }
  }

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 5000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  /** Hareketi olan aylar; boş aylar tabloyu şişirmesin. */
  const vatRows = useMemo(
    () =>
      (overview?.vat ?? []).filter(
        (row) =>
          row.outputVat !== 0 ||
          row.inputVat !== 0 ||
          row.carryForwardIn !== 0 ||
          row.payableVat !== 0
      ),
    [overview]
  );

  const upcoming = useMemo(
    () => calendar.filter((item) => !item.isPaid),
    [calendar]
  );

  async function accrue(month: number) {
    if (!companyId) return;

    setSaving(true);
    setError("");

    try {
      const result = await taxService.accrueVat(companyId, year, month);
      setNotice(`${result.message} Fiş: ${result.voucherNumber}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Tahakkuk fişi kesilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function markPaid(item: TaxObligation) {
    if (!companyId) return;

    const input = window.prompt(
      `${item.kindName} — ${item.periodLabel}\n` +
        "Ödenen tutar (tahmini tutarı kullanmak için boş bırakın):",
      ""
    );

    if (input === null) return;

    const amount = input.trim() === "" ? null : Number(input.replace(",", "."));

    if (amount !== null && !(amount > 0)) {
      setError("Ödenen tutar sıfırdan büyük olmalıdır.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      await taxService.markPaid({
        companyId,
        kind: item.kind,
        periodYear: item.periodYear,
        periodNumber: item.periodNumber,
        amount,
      });

      setNotice(`${item.kindName} ${item.periodLabel} ödendi olarak işaretlendi.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşaretlenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function undoPayment(item: TaxObligation) {
    if (!companyId) return;

    if (!window.confirm(`${item.kindName} ${item.periodLabel} ödemesi geri alınsın mı?`))
      return;

    setSaving(true);

    try {
      await taxService.undoPayment(
        companyId,
        item.kind,
        item.periodYear,
        item.periodNumber
      );

      setNotice("Ödeme işareti geri alındı.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Geri alınamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Vergi Yükü"
      description="KDV, SGK, muhtasar ve geçici vergi — yönetim görünümü"
    >
      <div className="erp-alert warning">
        Bu ekran <strong>beyanname üretmez</strong>. Rakamlar defterden
        hesaplanır ve tahminler &quot;tahmini&quot; etiketlidir; kesin beyan
        müşavirinizde yapılır. Ekran, müşavirin beyanıyla mutabakat için
        hazırlanmıştır.
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-page-toolbar">
        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <select value={String(year)} onChange={(e) => setYear(Number(e.target.value))}>
            {[0, 1, 2].map((offset) => {
              const value = new Date().getFullYear() - offset;
              return (
                <option key={value} value={String(value)}>
                  {value}
                </option>
              );
            })}
          </select>
        </div>

        {overview && (
          <div style={{ textAlign: "right" }}>
            <strong>
              Yıllık tahmini kurumlar vergisi:{" "}
              {money.format(overview.estimatedAnnualCorporateTax)}
            </strong>
            <small style={{ display: "block" }}>
              {overview.corporateTaxRate === null
                ? "Oran tanımlı değil"
                : `Oran %${overview.corporateTaxRate}`}
            </small>
          </div>
        )}
      </div>

      {overview && overview.corporateTaxRate === null && (
        <div
          className="erp-panel"
          style={{
            border: "1px solid #fcd34d",
            background: "#fffbeb",
            marginBottom: "14px",
          }}
        >
          <strong>{year} kurumlar vergisi oranı tanımlanmadı</strong>
          <p style={{ margin: "6px 0 10px" }}>
            Oran girilmediği için geçici ve kurumlar vergisi tahmini
            üretilmedi. Oran mevzuatla değiştiği için koda gömülmedi; her yıl
            için ayrı girilir.
          </p>

          <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
            <input
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={rateInput}
              onChange={(event) => setRateInput(event.target.value)}
              placeholder="Örn. 25"
              style={{ width: "120px" }}
            />
            <button
              type="button"
              className="erp-primary-button"
              disabled={savingRate || rateInput.trim() === "" || !companyId}
              onClick={() => void saveRate()}
            >
              {savingRate ? "Kaydediliyor..." : `${year} oranını kaydet`}
            </button>
          </div>
        </div>
      )}

      {loading ? (
        <div className="erp-panel erp-loading">Vergi görünümü hesaplanıyor...</div>
      ) : !overview ? (
        <div className="erp-panel erp-empty-state">
          <strong>Veri bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Yaklaşan Vergi Ödemeleri</h2>
                <p>
                  Nakit akışta da bu tutarlar görünür. Ödenen dönemi
                  işaretlerseniz listeden ve nakit akıştan düşer.
                </p>
              </div>
            </div>

            {upcoming.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Bekleyen vergi ödemesi yok</strong>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tür</th>
                      <th>Dönem</th>
                      <th>Vade</th>
                      <th>Tahmini tutar</th>
                      <th>Durum</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {upcoming.map((item) => (
                      <tr key={`${item.kind}-${item.periodYear}-${item.periodNumber}`}>
                        <td>{item.kindName}</td>
                        <td>{item.periodLabel}</td>
                        <td>{dateFormat.format(new Date(item.dueDate))}</td>
                        <td>
                          <strong>{moneyDetailed.format(item.estimatedAmount)}</strong>
                        </td>
                        <td>
                          <span
                            className={`erp-status ${item.isOverdue ? "red" : "yellow"}`}
                          >
                            {item.isOverdue ? "Gecikti" : "Bekliyor"}
                          </span>
                        </td>
                        <td>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            disabled={saving}
                            onClick={() => void markPaid(item)}
                          >
                            Ödendi
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Aylık KDV Netleştirme</h2>
                <p>
                  Hesaplanan − indirilecek − devreden = ödenecek ya da yeni
                  devreden. Tahakkuk fişi dönemi muhasebeleştirir.
                </p>
              </div>
            </div>

            {vatRows.length === 0 ? (
              <div className="erp-empty-state">
                <strong>{year} yılında KDV hareketi yok</strong>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Dönem</th>
                      <th>Hesaplanan</th>
                      <th>İndirilecek</th>
                      <th>Devreden (önceki)</th>
                      <th>Ödenecek</th>
                      <th>Devreden (sonraki)</th>
                      <th>Sorumlu sıfatıyla</th>
                      <th>Tahakkuk</th>
                    </tr>
                  </thead>
                  <tbody>
                    {vatRows.map((row) => (
                      <tr key={row.month}>
                        <td>{row.label}</td>
                        <td>{money.format(row.outputVat)}</td>
                        <td>{money.format(row.inputVat)}</td>
                        <td>{money.format(row.carryForwardIn)}</td>
                        <td>
                          <strong
                            style={{ color: row.payableVat > 0 ? "#b91c1c" : "inherit" }}
                          >
                            {money.format(row.payableVat)}
                          </strong>
                        </td>
                        <td style={{ color: row.carryForwardOut > 0 ? "#15803d" : "inherit" }}>
                          {money.format(row.carryForwardOut)}
                        </td>
                        <td>
                          {row.reverseChargeVat > 0
                            ? money.format(row.reverseChargeVat)
                            : "—"}
                        </td>
                        <td>
                          {row.isAccrued ? (
                            <span className="erp-status green">
                              {row.accrualVoucherNumber}
                            </span>
                          ) : (
                            <button
                              type="button"
                              className="erp-secondary-button"
                              disabled={saving}
                              onClick={() => void accrue(row.month)}
                            >
                              Fiş Kes
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Bordro Kaynaklı Yük (SGK + Stopaj + Damga)</h2>
                <p>
                  Tahakkuk fişi kesilmiş dönemler defterden, kesilmemişler
                  onaylı bordrolardan okunur.
                </p>
              </div>
            </div>

            {overview.payroll.length === 0 ? (
              <div className="erp-empty-state">
                <strong>{year} yılında bordro kaydı yok</strong>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Dönem</th>
                      <th>Personel</th>
                      <th>Gelir vergisi</th>
                      <th>Damga</th>
                      <th>SGK (işçi+işveren)</th>
                      <th>Toplam</th>
                      <th>Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {overview.payroll.map((row) => (
                      <tr key={row.month}>
                        <td>{row.label}</td>
                        <td>{row.personnelCount}</td>
                        <td>{money.format(row.incomeTaxWithholding)}</td>
                        <td>{money.format(row.stampTax)}</td>
                        <td>{money.format(row.sgkTotal)}</td>
                        <td>
                          <strong>{money.format(row.totalBurden)}</strong>
                        </td>
                        <td>
                          <span
                            className={`erp-status ${row.isAccrued ? "green" : "yellow"}`}
                          >
                            {row.isAccrued ? "Tahakkuk edildi" : "Tahakkuk edilmedi"}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Geçici Vergi Tahmini</h2>
                <p>
                  Defterdeki ticari kâr üzerinden hesaplanan TAHMİNDİR; mali
                  kâr farkları müşavirde belirlenir.
                </p>
              </div>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Dönem</th>
                    <th>Gelir</th>
                    <th>Gider</th>
                    <th>Vergi öncesi kâr</th>
                    <th>Tahmini vergi</th>
                    <th>Ödeme tarihi</th>
                  </tr>
                </thead>
                <tbody>
                  {overview.advanceTax.map((row) => (
                    <tr key={row.quarter}>
                      <td>{row.label}</td>
                      <td>{money.format(row.revenue)}</td>
                      <td>{money.format(row.expense)}</td>
                      <td
                        style={{
                          color: row.profitBeforeTax < 0 ? "#b91c1c" : "inherit",
                        }}
                      >
                        {money.format(row.profitBeforeTax)}
                      </td>
                      <td>
                        <strong>{money.format(row.estimatedTax)}</strong>
                      </td>
                      <td>{dateFormat.format(new Date(row.dueDate))}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Hesap Varsayımları</h2>
                <p>Rakamların neye dayandığı.</p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              <ul style={{ margin: 0, paddingLeft: "18px" }}>
                {overview.assumptions.map((assumption, index) => (
                  <li key={index}>
                    <small>{assumption}</small>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
