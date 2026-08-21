"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
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
  /**
   * BU EKRAN ÜÇ FARKLI MODÜLÜN İZNİNİ İSTİYOR — düğme başına ucun
   * kendi RequirePermission'ı:
   *   PUT    kurumlar-vergisi-oranlari -> company-settings.edit
   *   POST   tax/vat-accrual           -> accounting.manage
   *   POST   tax/payments              -> accounting.edit
   *   DELETE tax/payments              -> accounting.delete
   *
   * "Ekranın modülü" diye tek bir anahtara bağlamak yanlış olurdu:
   * kurumlar vergisi oranı muhasebe değil şirket ayarı yetkisiyle
   * korunuyor.
   */
  const actions = useModuleActions("accounting");
  const settingsActions = useModuleActions("company-settings");
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

  /*
   * SÜTUNLAR VERİ OLARAK (F4g).
   *
   * VERGİ TAKVİMİ TEK DİZİ: ekran daha önce aynı `tbody` içinde iki
   * ayrı küme basıyordu (bekleyenler + isteğe bağlı ödenenler). Bileşen
   * tek dizi alıyor; ayrım zaten satırdaki `isPaid` alanında olduğu için
   * ayırt edici bir alan uydurmaya gerek kalmadı.
   */
  const calendarRows = showPaid ? [...upcoming, ...paid] : upcoming;

  const calendarColumns: DataTableColumn<TaxObligation>[] = [
    { key: "tur", header: "Tür", value: (row) => row.kindName },
    { key: "donem", header: "Dönem", value: (row) => row.periodLabel },
    {
      key: "vade",
      header: "Vade",
      value: (row) => dateFormat.format(new Date(row.dueDate)),
    },
    {
      key: "tutar",
      header: "Tahmini tutar",
      numeric: true,
      value: (row) => money(row.estimatedAmount),
      render: (row) => <strong>{money(row.estimatedAmount)}</strong>,
      // Alt toplam TÜM satırlar üzerinden: görünen sayfa yanıltırdı.
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.estimatedAmount, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) =>
        row.isPaid ? "Ödendi" : row.isOverdue ? "Gecikti" : "Bekliyor",
      render: (row) =>
        row.isPaid ? (
          <span className="erp-status green">Ödendi</span>
        ) : (
          <span className={`erp-status ${row.isOverdue ? "red" : "yellow"}`}>
            {row.isOverdue ? "Gecikti" : "Bekliyor"}
          </span>
        ),
    },
    {
      key: "islem",
      header: "",
      value: () => "",
      render: (row) => (
        <div className="erp-actions">
          {row.isPaid
            ? actions.can("delete") && (
                <button
                  type="button"
                  disabled={saving}
                  onClick={() => setUndoingItem(row)}
                >
                  Geri Al
                </button>
              )
            : actions.can("edit") && (
                <button
                  type="button"
                  disabled={saving}
                  onClick={() => requestMarkPaid(row)}
                >
                  Ödendi
                </button>
              )}
        </div>
      ),
    },
  ];

  const vatColumns: DataTableColumn<(typeof vatRows)[number]>[] = [
    { key: "donem", header: "Dönem", value: (row) => row.label },
    {
      key: "hesaplanan",
      header: "Hesaplanan",
      numeric: true,
      value: (row) => moneyWhole(row.outputVat),
    },
    {
      key: "indirilecek",
      header: "İndirilecek",
      numeric: true,
      value: (row) => moneyWhole(row.inputVat),
    },
    {
      key: "devreden-onceki",
      header: "Devreden (önceki)",
      numeric: true,
      value: (row) => moneyWhole(row.carryForwardIn),
    },
    {
      key: "odenecek",
      header: "Ödenecek",
      numeric: true,
      value: (row) => moneyWhole(row.payableVat),
      /*
       * Ham hex yerine anlamsal sınıf: ödenecek KDV "dikkat",
       * devreden KDV "iyi haber". Renk tokenlardan geliyor ki marka
       * rengi değiştiğinde bu iki hücre geride kalmasın.
       */
      render: (row) => (
        <strong className={row.payableVat > 0 ? "rw-value-danger" : ""}>
          {moneyWhole(row.payableVat)}
        </strong>
      ),
      footer: (rows) =>
        moneyWhole(rows.reduce((sum, row) => sum + row.payableVat, 0)),
    },
    {
      key: "devreden-sonraki",
      header: "Devreden (sonraki)",
      numeric: true,
      value: (row) => moneyWhole(row.carryForwardOut),
      render: (row) => (
        <span className={row.carryForwardOut > 0 ? "rw-value-success" : ""}>
          {moneyWhole(row.carryForwardOut)}
        </span>
      ),
    },
    {
      key: "sorumlu",
      header: "Sorumlu sıfatıyla",
      numeric: true,
      value: (row) =>
        row.reverseChargeVat > 0 ? moneyWhole(row.reverseChargeVat) : "—",
    },
    {
      key: "tahakkuk",
      header: "Tahakkuk",
      value: (row) => (row.isAccrued ? row.accrualVoucherNumber ?? "edildi" : "—"),
      render: (row) =>
        row.isAccrued ? (
          <span className="erp-status green">{row.accrualVoucherNumber}</span>
        ) : (
          actions.can("manage") && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={saving}
              onClick={() => void accrue(row.month)}
            >
              Fiş Kes
            </button>
          )
        ),
    },
  ];

  const payrollColumns: DataTableColumn<
    NonNullable<TaxOverview>["payroll"][number]
  >[] = [
    { key: "donem", header: "Dönem", value: (row) => row.label },
    {
      key: "personel",
      header: "Personel",
      numeric: true,
      value: (row) => row.personnelCount,
    },
    {
      key: "gelir",
      header: "Gelir vergisi",
      numeric: true,
      value: (row) => moneyWhole(row.incomeTaxWithholding),
    },
    {
      key: "damga",
      header: "Damga",
      numeric: true,
      value: (row) => moneyWhole(row.stampTax),
    },
    {
      key: "sgk",
      header: "SGK (işçi+işveren)",
      numeric: true,
      value: (row) => moneyWhole(row.sgkTotal),
    },
    {
      key: "toplam",
      header: "Toplam",
      numeric: true,
      value: (row) => moneyWhole(row.totalBurden),
      render: (row) => <strong>{moneyWhole(row.totalBurden)}</strong>,
      footer: (rows) =>
        moneyWhole(rows.reduce((sum, row) => sum + row.totalBurden, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => (row.isAccrued ? "Tahakkuk edildi" : "Tahakkuk edilmedi"),
      render: (row) => (
        <span className={`erp-status ${row.isAccrued ? "green" : "yellow"}`}>
          {row.isAccrued ? "Tahakkuk edildi" : "Tahakkuk edilmedi"}
        </span>
      ),
    },
  ];

  const advanceTaxColumns: DataTableColumn<
    NonNullable<TaxOverview>["advanceTax"][number]
  >[] = [
    { key: "donem", header: "Dönem", value: (row) => row.label },
    {
      key: "gelir",
      header: "Gelir",
      numeric: true,
      value: (row) => moneyWhole(row.revenue),
    },
    {
      key: "gider",
      header: "Gider",
      numeric: true,
      value: (row) => moneyWhole(row.expense),
    },
    {
      key: "kar",
      header: "Vergi öncesi kâr",
      numeric: true,
      value: (row) => moneyWhole(row.profitBeforeTax),
      render: (row) => (
        <span className={row.profitBeforeTax < 0 ? "rw-value-danger" : ""}>
          {moneyWhole(row.profitBeforeTax)}
        </span>
      ),
    },
    {
      key: "vergi",
      header: "Tahmini vergi",
      numeric: true,
      value: (row) => moneyWhole(row.estimatedTax),
      render: (row) => <strong>{moneyWhole(row.estimatedTax)}</strong>,
      footer: (rows) =>
        moneyWhole(rows.reduce((sum, row) => sum + row.estimatedTax, 0)),
    },
    {
      key: "vade",
      header: "Vade",
      value: (row) => dateFormat.format(new Date(row.dueDate)),
    },
  ];

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
            {settingsActions.can("edit") && (
              <button
                type="button"
                className="erp-primary-button"
                disabled={savingRate || rateInput.trim() === "" || !companyId}
                onClick={() => void saveRate()}
              >
                {savingRate ? "Kaydediliyor..." : `${year} oranını kaydet`}
              </button>
            )}
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
                <DataTable
                  rows={calendarRows}
                  columns={calendarColumns}
                  rowKey={(row) =>
                    `${row.kind}-${row.periodYear}-${row.periodNumber}`
                  }
                  title="Vergi Takvimi"
                  resetKey={`${companyId}|${year}|${showPaid}`}
                />
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
                <DataTable
                  rows={vatRows}
                  columns={vatColumns}
                  rowKey={(row) => String(row.month)}
                  title="Aylık KDV Netleştirme"
                  resetKey={`${companyId}|${year}`}
                />
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
                <DataTable
                  rows={overview.payroll}
                  columns={payrollColumns}
                  rowKey={(row) => String(row.month)}
                  title="Bordro Kaynaklı Yük"
                  resetKey={`${companyId}|${year}`}
                />
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
              <DataTable
                rows={overview.advanceTax}
                columns={advanceTaxColumns}
                rowKey={(row) => String(row.quarter)}
                title="Geçici Vergi Tahmini"
                resetKey={`${companyId}|${year}`}
              />
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

            {actions.can("edit") && (
              <Button onClick={() => void confirmMarkPaid()} loading={saving}>
                Ödendi olarak işaretle
              </Button>
            )}
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
