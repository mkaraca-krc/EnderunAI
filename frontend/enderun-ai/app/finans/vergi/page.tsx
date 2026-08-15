"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog, Modal } from "@/components/ui";
import { money, moneyWhole } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  corporateTaxRateService,
  taxService,
  type TaxObligation,
  type TaxOverview,
} from "@/services/tax.service";



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

  const [payingItem, setPayingItem] = useState<TaxObligation | null>(null);
  const [paidAmount, setPaidAmount] = useState("");
  const [undoingItem, setUndoingItem] = useState<TaxObligation | null>(null);
  const [showPaid, setShowPaid] = useState(false);

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

  /**
   * ÖDENENLER LİSTESİ YOKTU. Ödendi işaretlenen dönem listeden
   * tamamen düşüyordu; yanlışlıkla işaretlenen bir vergi ekrandan
   * geri alınamıyordu. Geri alma ucu ve servis çağrısı zaten vardı,
   * yalnızca hiçbir düğmeye bağlı değildi.
   */
  const paid = useMemo(
    () => calendar.filter((item) => item.isPaid),
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

  /**
   * ÖDEME İŞARETLEME window.prompt İLE YAPILIYORDU.
   *
   * Tarayıcı penceresi tutarı biçimlendiremiyor, hangi vergiye ait
   * olduğunu tek satır düz metinle söylüyor, tahmini tutarı
   * gösteremiyor ve "boş bırakırsanız tahmin kullanılır" kuralını
   * ancak metin içinde anlatabiliyordu. Girilen değer de doğrudan
   * uca gidiyordu: yalnızca virgül noktaya çevriliyor, "1.250,50"
   * gibi binlik ayıraçlı bir giriş sessizce 1.25 oluyordu.
   */
  function requestMarkPaid(item: TaxObligation) {
    setPayingItem(item);
    setPaidAmount("");
    setError("");
  }

  async function confirmMarkPaid() {
    if (!companyId || !payingItem) return;

    const raw = paidAmount.trim();
    const amount = raw === "" ? null : Number(raw);

    if (amount !== null && !(amount > 0)) {
      setError("Ödenen tutar sıfırdan büyük olmalıdır.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      await taxService.markPaid({
        companyId,
        kind: payingItem.kind,
        periodYear: payingItem.periodYear,
        periodNumber: payingItem.periodNumber,
        amount,
      });

      setNotice(
        `${payingItem.kindName} ${payingItem.periodLabel} ödendi olarak işaretlendi.`,
      );
      setPayingItem(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşaretlenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function confirmUndoPayment() {
    if (!companyId || !undoingItem) return;

    setSaving(true);

    try {
      await taxService.undoPayment(
        companyId,
        undoingItem.kind,
        undoingItem.periodYear,
        undoingItem.periodNumber
      );

      setNotice("Ödeme işareti geri alındı.");
      setUndoingItem(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Geri alınamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Vergi Yükü"
      description="KDV, SGK, muhtasar ve geçici vergi — yönetim görünümü"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

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
          <div className="num">
            <strong>
              Yıllık tahmini kurumlar vergisi:{" "}
              {moneyWhole(overview.estimatedAnnualCorporateTax)}
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
        <div className="erp-panel rw-panel-warning">
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

              {paid.length > 0 && (
                <label className="rw-check">
                  <input
                    type="checkbox"
                    checked={showPaid}
                    onChange={(event) => setShowPaid(event.target.checked)}
                  />
                  <span>Ödenenleri göster ({paid.length})</span>
                </label>
              )}
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
                      <th className="num">Tahmini tutar</th>
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
                        <td className="num">
                          <strong>{money(item.estimatedAmount)}</strong>
                        </td>
                        <td>
                          <span
                            className={`erp-status ${item.isOverdue ? "red" : "yellow"}`}
                          >
                            {item.isOverdue ? "Gecikti" : "Bekliyor"}
                          </span>
                        </td>
                        <td>
                          <div className="erp-actions">
                            <button
                              type="button"
                              disabled={saving}
                              onClick={() => requestMarkPaid(item)}
                            >
                              Ödendi
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}

                    {showPaid &&
                      paid.map((item) => (
                        <tr
                          key={`odendi-${item.kind}-${item.periodYear}-${item.periodNumber}`}
                        >
                          <td>{item.kindName}</td>
                          <td>{item.periodLabel}</td>
                          <td>{dateFormat.format(new Date(item.dueDate))}</td>
                          <td className="num">
                            <strong>{money(item.estimatedAmount)}</strong>
                          </td>
                          <td>
                            <span className="erp-status green">Ödendi</span>
                          </td>
                          <td>
                            <div className="erp-actions">
                              <button
                                type="button"
                                disabled={saving}
                                onClick={() => setUndoingItem(item)}
                              >
                                Geri Al
                              </button>
                            </div>
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
                        <td>{moneyWhole(row.outputVat)}</td>
                        <td>{moneyWhole(row.inputVat)}</td>
                        <td>{moneyWhole(row.carryForwardIn)}</td>
                        {/*
                          Ham hex yerine anlamsal sınıf: ödenecek KDV
                          "dikkat", devreden KDV "iyi haber". Renk
                          tokenlardan geliyor ki marka rengi
                          değiştiğinde bu iki hücre geride kalmasın.
                        */}
                        <td className="num">
                          <strong
                            className={row.payableVat > 0 ? "rw-value-danger" : ""}
                          >
                            {moneyWhole(row.payableVat)}
                          </strong>
                        </td>
                        <td
                          className={`num ${
                            row.carryForwardOut > 0 ? "rw-value-success" : ""
                          }`}
                        >
                          {moneyWhole(row.carryForwardOut)}
                        </td>
                        <td>
                          {row.reverseChargeVat > 0
                            ? moneyWhole(row.reverseChargeVat)
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
                        <td>{moneyWhole(row.incomeTaxWithholding)}</td>
                        <td>{moneyWhole(row.stampTax)}</td>
                        <td>{moneyWhole(row.sgkTotal)}</td>
                        <td>
                          <strong>{moneyWhole(row.totalBurden)}</strong>
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
                      <td className="num">{moneyWhole(row.revenue)}</td>
                      <td className="num">{moneyWhole(row.expense)}</td>
                      <td
                        className={`num ${
                          row.profitBeforeTax < 0 ? "rw-value-danger" : ""
                        }`}
                      >
                        {moneyWhole(row.profitBeforeTax)}
                      </td>
                      <td className="num">
                        <strong>{moneyWhole(row.estimatedTax)}</strong>
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

      <Modal
        open={payingItem !== null}
        title={
          payingItem
            ? `${payingItem.kindName} — ${payingItem.periodLabel}`
            : "Ödeme işaretle"
        }
        description="Ödenen tutarı yazın. Boş bırakırsanız tahmini tutar kullanılır."
        onClose={() => setPayingItem(null)}
        busy={saving}
        size="sm"
        footer={
          <div className="flex justify-end gap-3">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setPayingItem(null)}
              disabled={saving}
            >
              Vazgeç
            </Button>

            <Button onClick={() => void confirmMarkPaid()} loading={saving}>
              Ödendi olarak işaretle
            </Button>
          </div>
        }
      >
        <label className="rw-modal-field">
          <span>Ödenen tutar (₺)</span>
          <input
            type="number"
            min="0"
            step="0.01"
            value={paidAmount}
            onChange={(event) => setPaidAmount(event.target.value)}
            placeholder={
              payingItem ? `Tahmini: ${money(payingItem.estimatedAmount)}` : ""
            }
          />
        </label>

        {error && <div className="erp-alert error">{error}</div>}
      </Modal>

      <ConfirmDialog
        open={undoingItem !== null}
        title="Ödeme işareti geri alınsın mı?"
        description={
          undoingItem
            ? `${undoingItem.kindName} ${undoingItem.periodLabel} yeniden bekleyen ödemeler arasına döner ve nakit akışta tekrar görünür.`
            : ""
        }
        confirmLabel="Geri Al"
        busy={saving}
        onCancel={() => setUndoingItem(null)}
        onConfirm={() => void confirmUndoPayment()}
      />
    </ErpShell>
  );
}
