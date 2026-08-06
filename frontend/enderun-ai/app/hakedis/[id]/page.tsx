"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentDetail,
} from "@/services/progress-payment.service";

import {
  priceDifferenceService,
  type PriceDifferenceCalculation,
  type PriceDifferenceIndexPeriod,
  type PriceDifferenceProfile,
} from "@/services/price-difference.service";

import {
  reportService,
} from "@/services/report.service";

const statusLabels: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "Taslak",
  [ProgressPaymentStatus.PendingApproval]: "Onay Bekliyor",
  [ProgressPaymentStatus.Approved]: "Onaylandı",
  [ProgressPaymentStatus.Posted]: "Kesinleşti",
  [ProgressPaymentStatus.Cancelled]: "İptal",
};

const statusClasses: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "gray",
  [ProgressPaymentStatus.PendingApproval]: "yellow",
  [ProgressPaymentStatus.Approved]: "blue",
  [ProgressPaymentStatus.Posted]: "green",
  [ProgressPaymentStatus.Cancelled]: "red",
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const number = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 4,
});

const date = new Intl.DateTimeFormat("tr-TR");

export default function ProgressPaymentDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [item, setItem] =
    useState<ProgressPaymentDetail | null>(null);

  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const [priceProfiles, setPriceProfiles] =
    useState<PriceDifferenceProfile[]>([]);

  const [priceIndexes, setPriceIndexes] =
    useState<PriceDifferenceIndexPeriod[]>([]);

  const [selectedProfileId, setSelectedProfileId] =
    useState("");

  const [selectedBaseIndexId, setSelectedBaseIndexId] =
    useState("");

  const [selectedCurrentIndexId, setSelectedCurrentIndexId] =
    useState("");

  const [priceCalculation, setPriceCalculation] =
    useState<PriceDifferenceCalculation | null>(null);

  const [priceWorking, setPriceWorking] =
    useState(false);

  const id = params.id;

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result =
        await progressPaymentService.getById(id);

      setItem(result);

      const [
        profileResult,
        indexResult,
      ] = await Promise.all([
        priceDifferenceService.getProfiles({
          companyId: result.companyId,
          projectId: result.projectId,
        }),

        priceDifferenceService.getIndexes(),
      ]);

      setPriceProfiles(profileResult);
      setPriceIndexes(indexResult);

      const defaultProfile =
        profileResult.find(
          (x) => x.isDefault
        ) ?? profileResult[0];

      if (defaultProfile) {
        setSelectedProfileId(
          defaultProfile.id
        );
      }

      if (indexResult.length > 0) {
        const sortedIndexes =
          [...indexResult].sort(
            (a, b) =>
              b.year * 100 +
              b.month -
              (a.year * 100 +
                a.month)
          );

        setSelectedCurrentIndexId(
          sortedIndexes[0].id
        );

        setSelectedBaseIndexId(
          sortedIndexes[
            sortedIndexes.length - 1
          ].id
        );
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hakediş detayı yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [id]);

  /**
   * NATURA formatındaki Excel çıktısı — üzerinde çalışılabilir hâli.
   * Yazdırma sayfası PDF'i verir, bu dosya hesabı verir.
   */
  async function downloadExcel() {

    if (!item) {
      return;
    }

    try {

      const response = await fetch(
        `/api/backend/hakedis-export/${item.id}/excel`,
        { credentials: "include" }
      );

      if (!response.ok) {
        throw new Error("Excel oluşturulamadı.");
      }

      const blob = await response.blob();
      const objectUrl = window.URL.createObjectURL(blob);

      const link = document.createElement("a");
      link.href = objectUrl;
      link.download = `Hakedis-${item.progressPaymentNumber}.xlsx`;

      document.body.appendChild(link);
      link.click();
      link.remove();

      window.URL.revokeObjectURL(objectUrl);

    } catch (err) {

      setError(
        err instanceof Error
          ? err.message
          : "Excel indirilemedi."
      );

    }

  }


  async function downloadPdf() {

    if (!item) {
      return;
    }

    try {

      await reportService
        .downloadProgressPaymentPdf(
          item.id
        );

    } catch (err) {

      setError(
        err instanceof Error
          ? err.message
          : "PDF indirilemedi."
      );

    }

  }



  async function downloadProgressPdf() {

    if (!item) return;

    await reportService
      .downloadProgressPaymentPdf(
        item.id
      );
  }


  async function downloadPricePdf() {

    if (!item) return;

    await reportService
      .downloadPriceDifferencePdf(
        item.id
      );
  }


  async function downloadDeductionPdf() {

    if (!item) return;

    await reportService
      .downloadDeductionPdf(
        item.id
      );
  }



  async function calculatePriceDifference() {
    if (!item) {
      return;
    }

    if (
      !selectedProfileId ||
      !selectedBaseIndexId ||
      !selectedCurrentIndexId
    ) {
      setError(
        "Fiyat farkı profili ve endeks dönemleri seçilmelidir."
      );
      return;
    }

    setPriceWorking(true);
    setError("");
    setMessage("");

    try {
      const result =
        await priceDifferenceService.calculate({
          progressPaymentId: item.id,
          priceDifferenceProfileId:
            selectedProfileId,
          baseIndexPeriodId:
            selectedBaseIndexId,
          currentIndexPeriodId:
            selectedCurrentIndexId,
          baseAmount:
            item.currentAmount,
          notes:
            "Hakediş ekranından hesaplandı.",
        });

      setPriceCalculation(result);

      setMessage(
        "Fiyat farkı hesabı başarıyla oluşturuldu."
      );

      await load();

    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Fiyat farkı hesaplanamadı."
      );
    } finally {
      setPriceWorking(false);
    }
  }


  const withholdingRate = useMemo(() => {
    if (!item || item.withholdingDenominator <= 0) {
      return "0/10";
    }

    return `${item.withholdingNumerator}/${item.withholdingDenominator}`;
  }, [item]);

  async function remove() {
    if (!item) {
      return;
    }

    const confirmed = window.confirm(
      `${item.progressPaymentNumber} numaralı taslak hakediş silinsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      await progressPaymentService.remove(item.id);
      router.push("/hakedis");
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hakediş silinemedi."
      );
    } finally {
      setWorking(false);
    }
  }

  async function cancelProgressPayment() {
    if (!item) {
      return;
    }

    const reason = window.prompt(
      "Hakediş iptal gerekçesini yazın:"
    );

    if (reason === null) {
      return;
    }

    if (!reason.trim()) {
      setError("İptal gerekçesi zorunludur.");
      return;
    }

    const confirmed = window.confirm(
      `${item.progressPaymentNumber} numaralı hakediş iptal edilsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      const result =
        await progressPaymentService.cancel(
          item.id,
          reason.trim()
        );

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hakediş iptal edilemedi."
      );
    } finally {
      setWorking(false);
    }
  }

  async function runAction(
    action: "submit" | "approve" | "post"
  ) {
    if (!item) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      const result =
        action === "submit"
          ? await progressPaymentService.submit(item.id)
          : action === "approve"
            ? await progressPaymentService.approve(item.id)
            : await progressPaymentService.post(item.id);

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İşlem tamamlanamadı."
      );
    } finally {
      setWorking(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Hakediş Detayı">
        <div className="erp-form-card">
          Hakediş yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell title="Hakediş Detayı">
        {error && (
          <div className="erp-alert error">
            {error}
          </div>
        )}

        <Link href="/hakedis">
          Hakediş listesine dön
        </Link>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title={`Hakediş ${item.progressPaymentNumber}`}
      description={`${item.projectCode} · ${item.projectName}`}
    >
      <div className="erp-toolbar">
        <div className="erp-actions">
          {/* NATURA formatında, logo antetli çıktı; PDF tarayıcının
              yazdırma penceresinden alınır. */}
          <Link href={`/hakedis/${item.id}/yazdir`}>
            NATURA Çıktısı
          </Link>

          <Link href={`/hakedis/${item.id}/kar-marji`}>
            Kâr Marjı
          </Link>

          <button
            type="button"
            onClick={() => void downloadExcel()}
          >
            Excel İndir
          </button>

          <button
            type="button"
            onClick={() => void downloadPdf()}
          >
            Hakediş PDF İndir
          </button>
        </div>


        <div>
          <strong>
            {item.progressPaymentNumber}
          </strong>

          <small>
            {item.periodNumber}. dönem ·{" "}
            {date.format(
              new Date(item.progressPaymentDate)
            )}
          </small>
        </div>

        <div className="erp-actions">
          <Link href="/hakedis">
            Listeye Dön
          </Link>

          {item.status ===
            ProgressPaymentStatus.Draft && (
            <>
              <Link href={`/hakedis/${item.id}/duzenle`}>
                Düzenle
              </Link>

              <button
                type="button"
                disabled={working}
                onClick={() => void remove()}
              >
                Sil
              </button>

              <button
                type="button"
                disabled={working}
                onClick={() =>
                  void runAction("submit")
                }
              >
                Onaya Gönder
              </button>
            </>
          )}

          {item.status ===
            ProgressPaymentStatus.PendingApproval && (
            <button
              type="button"
              disabled={working}
              onClick={() =>
                void runAction("approve")
              }
            >
              Onayla
            </button>
          )}

          {item.status ===
            ProgressPaymentStatus.Approved && (
            <button
              type="button"
              disabled={working}
              onClick={() =>
                void runAction("post")
              }
              title="Kesinleştirir ve gelir fişini otomatik oluşturur (120 Alıcılar borç / 600 Satışlar + 391 Hesaplanan KDV alacak)"
            >
              Kesinleştir ve Fişleştir
            </button>
          )}

          {item.status !==
            ProgressPaymentStatus.Posted &&
            item.status !==
              ProgressPaymentStatus.Cancelled && (
              <button
                type="button"
                disabled={working}
                onClick={() =>
                  void cancelProgressPayment()
                }
              >
                Hakedişi İptal Et
              </button>
            )}
        </div>
      </div>

      {message && (
        <div className="erp-alert success">
          {message}
        </div>
      )}

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <Info
            label="Şirket / Proje"
            value={`${item.projectCode} — ${item.projectName}`}
          />

          <Info
            label="Durum"
            value={
              <span
                className={`erp-status ${
                  statusClasses[item.status]
                }`}
              >
                {statusLabels[item.status]}
              </span>
            }
          />

          <Info
            label="Hakediş No"
            value={item.progressPaymentNumber}
          />

          <Info
            label="Dönem"
            value={String(item.periodNumber)}
          />

          <Info
            label="Dönem Başlangıcı"
            value={
              item.periodStartDate
                ? date.format(
                    new Date(item.periodStartDate)
                  )
                : "—"
            }
          />

          <Info
            label="Dönem Bitişi"
            value={
              item.periodEndDate
                ? date.format(
                    new Date(item.periodEndDate)
                  )
                : "—"
            }
          />

          <Info
            label="Hakediş Tarihi"
            value={date.format(
              new Date(item.progressPaymentDate)
            )}
          />

          <Info
            label="Para Birimi"
            value={item.currencyCode}
          />

          <Info
            label="Sözleşme Bedeli"
            value={money.format(item.contractAmount)}
          />

          <Info
            label="KDV Oranı"
            value={`%${number.format(item.vatRate)}`}
          />

          <Info
            label="Tevkifat"
            value={withholdingRate}
          />

          <Info
            label="Poz Sayısı"
            value={String(item.items.length)}
          />

          <Info
            label="Açıklama"
            value={item.description || "—"}
            wide
          />

          <Info
            label="Notlar"
            value={item.notes || "—"}
            wide
          />
        </div>
      </div>

      <div
        className="erp-form-card"
        style={{ marginTop: 16 }}
      >
        <h3>Finansal Özet</h3>

        <div className="erp-form-grid">
          <Summary
            label="Önceki Hakediş"
            value={item.previousAmount}
          />

          <Summary
            label="Bu Dönem"
            value={item.currentAmount}
          />

          <Summary
            label="Kümülatif"
            value={item.cumulativeAmount}
          />

          <Summary
            label="Fiyat Farkı"
            value={item.priceDifferenceAmount}
          />

          <Summary
            label="KDV"
            value={item.vatAmount}
          />

          <Summary
            label="Tevkifat"
            value={item.withholdingAmount}
          />

          <Summary
            label="Diğer Kesintiler"
            value={
              item.totalDeductionAmount -
              item.withholdingAmount
            }
          />

          <Summary
            label="Brüt Ödeme"
            value={item.grossPayableAmount}
          />

          <Summary
            label="Net Ödenecek"
            value={item.netPayableAmount}
            strong
          />
        </div>

        {item.accountingVoucherNumber && (
          <div
            className="erp-alert success"
            style={{ marginTop: 12 }}
          >
            Gelir fişi otomatik oluşturuldu ve kesinleştirildi:{" "}
            <strong>{item.accountingVoucherNumber}</strong> — 120 Alıcılar
            (borç) / 600 Yurtiçi Satışlar + 391 Hesaplanan KDV (alacak).
          </div>
        )}
      </div>

      <div
        className="erp-form-card"
        style={{ marginTop: 16 }}
      >
        <div className="erp-toolbar">
          <div>
            <strong>Fiyat Farkı Hesabı</strong>
            <small>
              Hakediş tutarı üzerinden fiyat farkı hesaplama
            </small>
          </div>

          <button
            type="button"
            disabled={priceWorking}
            onClick={() =>
              void calculatePriceDifference()
            }
          >
            {priceWorking
              ? "Hesaplanıyor..."
              : "Fiyat Farkını Hesapla"}
          </button>
        </div>

        <div className="erp-form-grid">

          <label>
            <span>Fiyat Farkı Profili</span>

            <select
              value={selectedProfileId}
              onChange={(event) =>
                setSelectedProfileId(
                  event.target.value
                )
              }
            >
              <option value="">
                Profil seçin
              </option>

              {priceProfiles.map((profile) => (
                <option
                  key={profile.id}
                  value={profile.id}
                >
                  {profile.profileName}
                </option>
              ))}
            </select>
          </label>


          <label>
            <span>Baz Dönem</span>

            <select
              value={selectedBaseIndexId}
              onChange={(event) =>
                setSelectedBaseIndexId(
                  event.target.value
                )
              }
            >
              <option value="">
                Baz dönem seçin
              </option>

              {priceIndexes.map((index) => (
                <option
                  key={index.id}
                  value={index.id}
                >
                  {index.month
                    .toString()
                    .padStart(2, "0")}
                  /{index.year}
                  {" "}
                  {index.sourceName}
                </option>
              ))}
            </select>
          </label>


          <label>
            <span>Cari Dönem</span>

            <select
              value={selectedCurrentIndexId}
              onChange={(event) =>
                setSelectedCurrentIndexId(
                  event.target.value
                )
              }
            >
              <option value="">
                Cari dönem seçin
              </option>

              {priceIndexes.map((index) => (
                <option
                  key={index.id}
                  value={index.id}
                >
                  {index.month
                    .toString()
                    .padStart(2, "0")}
                  /{index.year}
                  {" "}
                  {index.sourceName}
                </option>
              ))}
            </select>
          </label>

        </div>


        {priceCalculation && (
          <div
            className="erp-form-card"
            style={{
              marginTop: 16,
              background: "#f8fbff"
            }}
          >

            <div className="erp-form-grid">

              <div>
                <span>Pn</span>

                <strong>
                  {number.format(
                    priceCalculation.pn
                  )}
                </strong>
              </div>


              <div>
                <span>Delta</span>

                <strong>
                  {number.format(
                    priceCalculation.delta
                  )}
                </strong>
              </div>


              <div>
                <span>Esas Tutar</span>

                <strong>
                  {money.format(
                    priceCalculation.baseAmount
                  )}
                </strong>
              </div>


              <div>
                <span>Fiyat Farkı</span>

                <strong>
                  {money.format(
                    priceCalculation
                      .priceDifferenceAmount
                  )}
                </strong>
              </div>

            </div>

          </div>
        )}

      </div>


      <div
        className="erp-table-card"
        style={{ marginTop: 16 }}
      >
        <div className="erp-toolbar">
          <div>
            <strong>Poz ve Metraj Detayları</strong>
            <small>
              {item.items.length} satır
            </small>
          </div>
        </div>

        <div style={{ overflowX: "auto" }}>
          <table className="erp-table">
            <thead>
              <tr>
                <th>Sıra</th>
                <th>Poz</th>
                <th>Birim</th>
                <th>Sözleşme Miktarı</th>
                <th>Önceki</th>
                <th title="Hakediş hazırlanırken dondurulan saha miktarı">
                  Sahaya Göre
                </th>
                <th>Bu Dönem</th>
                <th title="Bu dönem − sahaya göre; eksi ise devreden iş">
                  Fark
                </th>
                <th>Kümülatif</th>
                <th>Birim Fiyat</th>
                <th>Bu Dönem Tutar</th>
                <th>İlerleme</th>
              </tr>
            </thead>

            <tbody>
              {item.items.map((line) => (
                <tr key={line.id}>
                  <td>{line.lineNumber}</td>

                  <td>
                    <strong>
                      {line.positionCode}
                    </strong>
                    <small>
                      {line.description}
                    </small>
                  </td>

                  <td>{line.unit}</td>

                  <td>
                    {number.format(
                      line.contractQuantity
                    )}
                  </td>

                  <td>
                    {number.format(
                      line.previousQuantity
                    )}
                  </td>

                  {/*
                    Saha gerçekleşmesi ve işveren kabulü bilerek ayrı
                    tutulur; ikisi arasındaki fark devreden iştir.
                    İcmale bağlı olmayan satırda saha rakamı yoktur.
                  */}
                  <td>
                    {line.projectBoqItemId
                      ? number.format(line.fieldQuantity)
                      : "—"}
                  </td>

                  <td>
                    {number.format(
                      line.currentQuantity
                    )}
                  </td>

                  <td>
                    {!line.projectBoqItemId ? (
                      "—"
                    ) : Math.abs(line.fieldDifference) < 0.0001 ? (
                      <span className="erp-status green">Aynı</span>
                    ) : (
                      <span
                        className={`erp-status ${
                          line.fieldDifference < 0 ? "yellow" : "blue"
                        }`}
                        title={
                          line.fieldDifference < 0
                            ? "Sahada yapıldı, bu dönem kabul edilmedi"
                            : "Sahadan fazla kabul edildi"
                        }
                      >
                        {line.fieldDifference > 0 ? "+" : ""}
                        {number.format(line.fieldDifference)}
                      </span>
                    )}
                  </td>

                  <td>
                    {number.format(
                      line.cumulativeQuantity
                    )}
                  </td>

                  <td>
                    {money.format(
                      line.unitPrice
                    )}
                  </td>

                  <td>
                    <strong>
                      {money.format(
                        line.currentAmount
                      )}
                    </strong>
                  </td>

                  <td>
                    %{number.format(
                      line.completionRate
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div
        className="erp-table-card"
        style={{ marginTop: 16 }}
      >
        <div className="erp-toolbar">
          <div>
            <strong>Kesintiler</strong>
            <small>
              {item.deductions.length} satır
            </small>
          </div>
        </div>

        <table className="erp-table">
          <thead>
            <tr>
              <th>Sıra</th>
              <th>Açıklama</th>
              <th>Oran</th>
              <th>Matrah</th>
              <th>Tutar</th>
              <th>Hesaplama</th>
            </tr>
          </thead>

          <tbody>
            {item.deductions.length === 0 && (
              <tr>
                <td colSpan={6}>
                  Kesinti kaydı bulunmuyor.
                </td>
              </tr>
            )}

            {item.deductions.map((deduction) => (
              <tr key={deduction.id}>
                <td>{deduction.lineNumber}</td>
                <td>{deduction.description}</td>
                <td>
                  %{number.format(deduction.rate)}
                </td>
                <td>
                  {money.format(
                    deduction.baseAmount
                  )}
                </td>
                <td>
                  <strong>
                    {money.format(
                      deduction.amount
                    )}
                  </strong>
                </td>
                <td>
                  {deduction.isManualAmount
                    ? "Manuel"
                    : "Oransal"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </ErpShell>
  );
}

function Info({
  label,
  value,
  wide = false,
}: {
  label: string;
  value: React.ReactNode;
  wide?: boolean;
}) {
  return (
    <div className={wide ? "span-2" : undefined}>
      <span>{label}</span>
      <div
        style={{
          marginTop: 6,
          fontWeight: 600,
        }}
      >
        {value}
      </div>
    </div>
  );
}

function Summary({
  label,
  value,
  strong = false,
}: {
  label: string;
  value: number;
  strong?: boolean;
}) {
  return (
    <div>
      <span>{label}</span>

      <div
        style={{
          marginTop: 6,
          fontSize: strong ? 22 : 18,
          fontWeight: strong ? 800 : 600,
        }}
      >
        {money.format(value)}
      </div>
    </div>
  );
}
