"use client";

import { useIstemciYili } from "@/lib/use-istemci-zamani";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import {
  money,
  moneyWhole,
  percent,
  quantity,
  unitPrice,
} from "@/lib/format/turkish";
import {
  projectProfitService,
  type BoqLineProfit,
  type ProjectProfitBreakdown,
} from "@/services/project-profit.service";

/**
 * Sayfa başındaki toplu bakış — kuruşsuz.
 *
 * Proje geneli için büyüklük okunur, kuruş okunmaz. SATIR TABLOSUNDA
 * KULLANILMAZ: orada tek bir pozun sözleşme tutarı ve kârı yazıyor ve
 * o rakamlar sözleşmeyle karşılaştırılıyor.
 */
const summaryMoney = moneyWhole;

/**
 * Kâr tarafında artı iyi, eksi kötü — maliyet tablosunun tersi.
 *
 * Renk artık tokendan geliyor: sayfa ham hex yazdığında marka rengi
 * değiştiğinde bu hücreler geride kalıyordu.
 */
function profitClass(value: number) {
  if (value > 0) return "rw-value-success";
  if (value < 0) return "rw-value-danger";
  return undefined;
}

/**
 * Referans fiyatlar kurum kurum, yan yana. Toplanmaz, ortalaması
 * alınmaz: hangi kurumun kitabında ne yazdığı ayrı bir bilgidir.
 */
function ReferenceCell({ line }: { line: BoqLineProfit }) {
  if (line.references.length === 0) {
    return <small>—</small>;
  }

  return (
    <>
      {line.references.map((reference) => (
        <small key={reference.institutionName} style={{ display: "block" }}>
          {reference.institutionName}
          {reference.year ? ` ${reference.year}` : ""}:{" "}
          {reference.unitPrice == null
            ? "—"
            : unitPrice(reference.unitPrice)}
        </small>
      ))}
    </>
  );
}

/**
 * Şirket ortalaması ya bir rakamdır ya da neden hesaplanamadığının
 * gerekçesi. Yetersiz veriden ortalama üretmek, sonraki teklifleri
 * yanlış fiyatlandırır.
 */
function CompanyAverageCell({ line }: { line: BoqLineProfit }) {
  const average = line.companyAverage;

  if (!average.hasEnoughData) {
    return (
      <small className="rw-value-muted" title={average.explanation}>
        Ölçüm yetersiz
      </small>
    );
  }

  return (
    <>
      <strong>{unitPrice(average.averageUnitCost)}</strong>
      <small style={{ display: "block" }}>
        {average.projectCount} proje · {unitPrice(average.minUnitCost)}{" "}
        – {unitPrice(average.maxUnitCost)}
      </small>
    </>
  );
}

export default function ProjectProfitPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [breakdown, setBreakdown] = useState<ProjectProfitBreakdown | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  /*
   * YIL ÇİZİMDE OKUNMAZ.
   *
   * Sunucu geçişi derleme anında, istemci geçişi açılışta koşuyor;
   * yılbaşını geçen bir yayında ikisi farklı yıl yazar ve hidrasyon
   * uyuşmazlığı doğar. Bağlanma sonrası dolduruluyor.
   */
  const istemciYili = useIstemciYili();
  const [secilenYil, setSecilenYil] = useState<number | undefined>(undefined);

  const currentYear = istemciYili ?? 0;
  const referenceYear = secilenYil ?? istemciYili ?? undefined;
  const setReferenceYear = setSecilenYil;
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const fetchBreakdown = useCallback(
    () => projectProfitService.get(projectId, referenceYear),
    [projectId, referenceYear]
  );

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchBreakdown();
        if (cancelled) return;

        setBreakdown(data);
        setError("");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Kâr analizi alınamadı.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchBreakdown, reloadToken]);

  const years = currentYear
    ? [currentYear, currentYear - 1, currentYear - 2]
    : [];

  return (
    <ErpShell
      design="redwood"
      title="Poz Kâr Analizi"
      description="İcmal satırında dört fiyat: sözleşme, referans, şirket gerçekleşmesi ve anlık maliyet"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-page-toolbar">
        <div>
          {breakdown && (
            <>
              <strong>
                Sözleşme {summaryMoney(breakdown.contractTotal)} · Maliyet{" "}
                {summaryMoney(breakdown.actualCostTotal)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                Kâr{" "}
                <strong className={profitClass(breakdown.profit)}>
                  {summaryMoney(breakdown.profit)}
                </strong>{" "}
                · Marj {percent(breakdown.profitMarginPercent)}
              </small>
              <small style={{ display: "block", marginTop: "2px" }}>
                Maliyetin {summaryMoney(breakdown.measuredCostTotal)} tutarı
                ölçülmüş, {summaryMoney(breakdown.allocatedCostTotal)} tutarı
                dağıtılmış (tahmin).
              </small>
            </>
          )}
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          {/* Maliyet fişleri ve referans fiyatlar dışarıdan
              güncelleniyor; yılı değiştirmeden tazelemek gerekiyor. */}
          <Button variant="secondary" disabled={loading} onClick={() => setReloadToken((value) => value + 1)}>Yenile</Button>

          <label htmlFor="referenceYear">
            <small>Referans yılı</small>
          </label>
          <select
            id="referenceYear"
            value={referenceYear ?? ""}
            onChange={(event) =>
              setReferenceYear(
                event.target.value ? Number(event.target.value) : undefined
              )
            }
          >
            <option value="">En güncel</option>
            {years.map((year) => (
              <option key={year} value={year}>
                {year}
              </option>
            ))}
          </select>

          <Link className="erp-secondary-button" href={`/projeler/${projectId}`}>
            Projeye Dön
          </Link>
        </div>
      </div>

      {loading ? (
        <div className="erp-panel erp-loading">Kâr analizi hesaplanıyor...</div>
      ) : !breakdown ? (
        <div className="erp-panel erp-empty-state">
          <strong>Analiz bulunamadı</strong>
        </div>
      ) : (
        <>
          {breakdown.unassignedCost > 0 && (
            <div className="erp-alert warning">
              {summaryMoney(breakdown.unassignedCost)} tutarındaki maliyet hiçbir
              kısma ya da poza bağlanamadı; satır kârlarına yansımıyor. Proje kârı
              bu tutar kadar iyimser görünüyor.
            </div>
          )}

          {!breakdown.includesExtraPayments && (
            <div className="erp-alert warning">
              İşçilik yalnızca resmi bordro maliyetidir; elden ödemeler yetkiniz
              olmadığı için dahil edilmemiştir. Gerçek kâr burada görünenden
              düşüktür.
            </div>
          )}

          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Satır Bazında Dört Fiyat</h2>
                <p>
                  Kâr her zaman sözleşme eksi gerçekleşen maliyettir. Referans ve
                  şirket ortalaması karara yardımcı bilgilerdir, kâr hesabına
                  girmez.
                </p>
              </div>
            </div>

            {breakdown.lines.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Sözleşme icmali yok</strong>
                <p>
                  Bu projede güncel revizyon icmal bulunmuyor; kâr karşılaştırması
                  yapılamaz.
                </p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Poz</th>
                      <th>Miktar</th>
                      <th>Sözleşme BF</th>
                      <th>Referans BF</th>
                      <th>Şirket ort. maliyet</th>
                      <th>Sözleşme tutarı</th>
                      <th>Anlık maliyet</th>
                      <th>Kâr</th>
                      <th>Marj</th>
                    </tr>
                  </thead>
                  <tbody>
                    {breakdown.lines.map((line) => (
                      <tr key={line.boqItemId}>
                        <td>
                          <strong>{line.positionCode}</strong>
                          <small style={{ display: "block" }}>
                            {line.description}
                          </small>
                        </td>
                        <td>
                          {quantity(line.contractQuantity)} {line.unit}
                        </td>
                        <td>
                          <strong>
                            {unitPrice(line.contractUnitPrice)} TL
                          </strong>
                          {(line.contractMaterialUnitPrice > 0 ||
                            line.contractLaborUnitPrice > 0) && (
                            <small style={{ display: "block" }}>
                              malzeme{" "}
                              {unitPrice(line.contractMaterialUnitPrice)} ·
                              montaj{" "}
                              {unitPrice(line.contractLaborUnitPrice)}
                            </small>
                          )}
                        </td>
                        <td>
                          <ReferenceCell line={line} />
                        </td>
                        <td>
                          <CompanyAverageCell line={line} />
                        </td>
                        <td>{money(line.contractTotal)}</td>
                        <td>
                          <strong>{money(line.actualCost)}</strong>
                          <small style={{ display: "block" }}>
                            ölçülmüş {money(line.measuredCost)} · dağıtılmış{" "}
                            {money(line.allocatedCost)}
                          </small>
                        </td>
                        <td className={profitClass(line.profit)}>
                          <strong>{money(line.profit)}</strong>
                        </td>
                        <td className={profitClass(line.profit)}>
                          {percent(line.profitMarginPercent)}
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
                <h2>Hesap Varsayımları</h2>
                <p>Rakamların neye dayandığı — okurken bilinmesi gerekenler.</p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              <ul style={{ margin: 0, paddingLeft: "18px" }}>
                {breakdown.assumptions.map((assumption, index) => (
                  <li key={index}>
                    <small>{assumption}</small>
                  </li>
                ))}
                <li>
                  <small>
                    Şirket ortalaması yalnızca poza etiketlenmiş maliyetten ve en
                    az iki ayrı projeden hesaplanır; dağıtılmış tutarlar
                    ortalamaya girmez.
                  </small>
                </li>
              </ul>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
