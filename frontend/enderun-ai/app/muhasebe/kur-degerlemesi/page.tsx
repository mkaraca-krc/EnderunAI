"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { amount, money, number as formatNumber } from "@/lib/format/turkish";
import { Button } from "@/components/ui";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  currencyValuationService,
  type CurrencyValuationPreview,
} from "@/services/currency-valuation.service";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

export default function CurrencyValuationPage() {
  /*
   * POST currency-valuations -> accounting.manage
   * Kur değerlemesi defterde fiş üretiyor; ayrı bir yönetim yetkisi.
   */
  const actions = useModuleActions("accounting");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [valuationDate, setValuationDate] = useState(todayIso);

  const [preview, setPreview] = useState<CurrencyValuationPreview | null>(null);
  const [loading, setLoading] = useState(false);
  const [posting, setPosting] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const list = await companyService.getAll();
        if (cancelled) return;

        setCompanies(list);
        setCompanyId((current) => current || list[0]?.id || "");
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const loadPreview = useCallback(
    async (signal?: { cancelled: boolean }) => {
      if (!companyId) return;

      setLoading(true);
      setError("");

      try {
        const result = await currencyValuationService.preview(
          companyId,
          valuationDate
        );

        if (signal?.cancelled) return;

        setPreview(result);
      } catch (err) {
        if (signal?.cancelled) return;

        setPreview(null);
        setError(err instanceof Error ? err.message : "Önizleme alınamadı.");
      } finally {
        if (!signal?.cancelled) setLoading(false);
      }
    },
    [companyId, valuationDate]
  );

  useEffect(() => {
    // setState çağrıları async gövdenin içinde kalmalı; efekt
    // gövdesinden eşzamanlı çağrılırsa zincirleme render tetiklenir.
    const signal = { cancelled: false };

    void (async () => {
      await loadPreview(signal);
    })();

    return () => {
      signal.cancelled = true;
    };
  }, [loadPreview]);

  async function handlePost() {
    if (!companyId) return;

    setPosting(true);
    setError("");
    setMessage("");

    try {
      const result = await currencyValuationService.post(
        companyId,
        valuationDate
      );

      setMessage(
        `Değerleme fişi kesildi. Deftere yazılan net fark: ` +
          `${money(result.postedDifference)} (${result.lineCount} satır).`
      );

      await loadPreview();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiş kesilemedi.");
    } finally {
      setPosting(false);
    }
  }

  const postableLines =
    preview?.lines.filter((x) => x.postableDifference !== 0) ?? [];

  const canPost =
    !!preview &&
    !preview.alreadyPostedRunId &&
    postableLines.length > 0 &&
    !posting;

  /*
   * SÜTUNLAR VERİ OLARAK (F4n).
   *
   * "Bu Turda" sütununda alt toplam VAR: bu turda kesilecek fişin
   * toplam tutarı, kullanıcının fişi onaylamadan önce görmesi gereken
   * sayı. Diğer para sütunlarında yok — farklı para birimlerindeki
   * bakiyeleri toplamak anlamsız bir rakam üretirdi.
   */
  const valuationColumns: DataTableColumn<
    NonNullable<typeof preview>["lines"][number]
  >[] = [
    {
      key: "cari",
      header: "Cari",
      value: (row) => `${row.currentAccountTitle} (${row.currentAccountCode})`,
      render: (row) => (
        <>
          {row.currentAccountTitle}
          <small>{row.currentAccountCode}</small>
        </>
      ),
    },
    {
      key: "para",
      header: "Para Birimi",
      value: (row) => row.currencyCode,
      render: (row) => <strong>{row.currencyCode}</strong>,
    },
    {
      key: "bakiye",
      header: "Bakiye",
      numeric: true,
      value: (row) => amount(row.balance),
    },
    {
      key: "defter",
      header: "Defter (TL)",
      numeric: true,
      value: (row) => money(row.bookValueLocal),
    },
    {
      key: "kur",
      header: "Kur",
      numeric: true,
      value: (row) =>
        row.rateAvailable && row.valuationRate != null
          ? `${formatNumber(row.valuationRate, 4)} (${row.rateSource})`
          : "Kur yok",
      render: (row) =>
        row.rateAvailable && row.valuationRate != null ? (
          <>
            {formatNumber(row.valuationRate, 4)}
            <small>{row.rateSource}</small>
          </>
        ) : (
          <span className="erp-status red">Kur yok</span>
        ),
    },
    {
      key: "degerlenmis",
      header: "Değerlenmiş (TL)",
      numeric: true,
      value: (row) => (row.valuedLocal != null ? money(row.valuedLocal) : "—"),
    },
    {
      key: "fark",
      header: "Toplam Fark",
      numeric: true,
      value: (row) =>
        row.totalDifference != null ? money(row.totalDifference) : "—",
    },
    {
      key: "onceki",
      header: "Önce Yazılan",
      numeric: true,
      value: (row) => money(row.previouslyPosted),
    },
    {
      key: "bu-tur",
      header: "Bu Turda",
      numeric: true,
      value: (row) =>
        row.postableDifference !== 0
          ? `${money(row.postableDifference)} (${row.postableDifference > 0 ? "646" : "656"})`
          : row.message ?? "—",
      render: (row) =>
        row.postableDifference !== 0 ? (
          <strong>
            {money(row.postableDifference)}
            <small>{row.postableDifference > 0 ? "646" : "656"}</small>
          </strong>
        ) : (
          <small>{row.message ?? "—"}</small>
        ),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.postableDifference, 0)),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Kur Değerlemesi"
      description="Dövizli cari bakiyelerinin dönem sonu değerlemesi (646/656)"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void loadPreview()}>Yenile</Button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {message && <div className="erp-alert success">{message}</div>}

      <div className="erp-page-toolbar">
        <div
          style={{
            display: "flex",
            gap: 8,
            flexWrap: "wrap",
            alignItems: "flex-end",
          }}
        >
          <label>
            <span style={{ display: "block", fontSize: 11 }}>Şirket</span>
            <select
              value={companyId}
              onChange={(e) => setCompanyId(e.target.value)}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span style={{ display: "block", fontSize: 11 }}>
              Değerleme tarihi
            </span>
            <input
              type="date"
              value={valuationDate}
              onChange={(e) => setValuationDate(e.target.value)}
            />
          </label>
        </div>

        {actions.can("manage") && (
          <button
            type="button"
            className="erp-primary-button"
            disabled={!canPost}
            onClick={() => void handlePost()}
          >
            {posting ? "Kesiliyor..." : "Değerleme Fişini Kes"}
          </button>
        )}
      </div>

      <div className="erp-panel" style={{ marginBottom: 16 }}>
        <p style={{ margin: 0, fontSize: 13 }}>
          Cari bakiyeleri defterde, her hareketin kendi günündeki kurla TL
          karşılığıyla durur. Bu ekran o değeri seçtiğiniz tarihin kuruyla
          karşılaştırır ve farkı kambiyo kârı (646) ya da zararı (656)
          olarak yazar. Değerleme satırları TL kesilir; dövizin kendi
          bakiyesi değişmez. Sonraki değerlemede yalnızca aradaki
          <strong> değişim </strong> yazılır, aynı fark ikinci kez
          defterlenmez.
        </p>
      </div>

      {preview?.alreadyPostedRunId && (
        <div className="erp-alert warning">
          Bu tarih için zaten bir değerleme fişi kesilmiş. Yeniden kesmek
          için önce mevcut turu iptal edin.
        </div>
      )}

      {preview?.hasMissingRate && (
        <div className="erp-alert warning">
          Kuru bulunamayan döviz var. O satırlar fişe girmez ve toplam
          eksiktir; kur arşivi tamamlanmadan değerleme tam olmaz.
        </div>
      )}

      <div className="erp-quick-grid">
        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Kambiyo Kârı (646)
          </small>
          <strong>{money(preview?.totalGain ?? 0)}</strong>
        </div>
        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Kambiyo Zararı (656)
          </small>
          <strong>{money(preview?.totalLoss ?? 0)}</strong>
        </div>
        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>Net Fark</small>
          <strong>{money(preview?.netDifference ?? 0)}</strong>
        </div>
        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Yazılacak Satır
          </small>
          <strong>{postableLines.length}</strong>
        </div>
      </div>

      <div className="erp-table-card" style={{ marginTop: 16 }}>
        <div className="erp-table-header">
          <h2>Dövizli Cari Bakiyeleri</h2>
          <small>{preview?.lines.length ?? 0} satır</small>
        </div>

        {loading ? (
          <div className="erp-loading">Önizleme hesaplanıyor...</div>
        ) : !preview || preview.lines.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Dövizli cari bakiyesi yok</strong>
            <p>
              Değerlenecek bir şey bulunmuyor. Dövizli fatura veya tahsilat
              defterlendikçe bu liste dolar.
            </p>
          </div>
        ) : (
          <DataTable
            rows={preview.lines}
            columns={valuationColumns}
            rowKey={(row) => `${row.currentAccountId}-${row.currencyCode}`}
            title="Kur Değerlemesi Önizleme"
            /* Şirket ya da değerleme tarihi değişince sayfa 1'e döner. */
            resetKey={`${companyId}|${valuationDate}`}
          />
        )}
      </div>
    </ErpShell>
  );
}
