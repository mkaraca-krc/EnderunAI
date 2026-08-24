"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { ConfirmDialog } from "@/components/ui";
import { money, quantity, unitPrice } from "@/lib/format/turkish";

import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentDetail,
} from "@/services/progress-payment.service";

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

const date = new Intl.DateTimeFormat("tr-TR");

export default function ProgressPaymentDetailPage() {
  /**
   * Düğme -> uç -> izin (ProgressPaymentsController):
   *   POST   progress-payments/{id}/submit  -> hakedis.edit
   *   POST   progress-payments/{id}/approve -> hakedis.approve
   *   POST   progress-payments/{id}/post    -> hakedis.approve
   *   DELETE progress-payments/{id}         -> hakedis.delete
   *   POST   progress-payments/{id}/cancel  -> hakedis.DELETE
   *
   * "Kesinleştir ve Fişleştir" ONAYLAMAYLA AYNI yetkide: uç ikisini de
   * hakedis.approve ile koruyor. Fişleştirme muhasebe kaydı üretse de
   * izin hakediş modülünde kalıyor.
   */
  const actions = useModuleActions("hakedis");

  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [item, setItem] =
    useState<ProgressPaymentDetail | null>(null);

  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState<"sil" | "iptal" | null>(null);

  const id = params.id;

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result =
        await progressPaymentService.getById(id);

      setItem(result);
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

  async function cancelProgressPayment(reason: string) {
    if (!item) {
      return;
    }

    setWorking(true);
    setMessage("");
    setError("");

    try {
      setConfirming(null);

      const result =
        await progressPaymentService.cancel(
          item.id,
          reason
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
      <ErpShell design="redwood" title="Hakediş Detayı">
        <div className="erp-form-card">
          Hakediş yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell design="redwood" title="Hakediş Detayı">
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
      design="redwood"
      title={`Hakediş ${item.progressPaymentNumber}`}
      description={`${item.projectCode} · ${item.projectName}`}
    >
      <div className="erp-toolbar">
        <div className="erp-actions">
          {/* NATURA formatında, logo antetli çıktı; PDF tarayıcının
              yazdırma penceresinden alınır.

              SUNUCU TARAFI PDF YOK: yanında duran "Hakediş PDF İndir"
              düğmesi /api/reports/... çağırıyordu, o uç hiç yazılmamış
              (backend'de api/reports rotası ve PDF kütüphanesi yok).
              Düğme kaldırıldı; PDF yeteneği bu yazdırma sayfasıyla
              zaten sağlanıyor. Bkz. TEMIZLIK-TARAMASI.md. */}
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

        {/*
          ONAY ÇAPASI.

          Yapılacaklar ekranındaki satır buraya götürüyor: kullanıcı
          sayfanın başında değil, KARARI VERECEĞİ yerde açılıyor.
          Bir tık daha var ama o tık, bakmadan onaylamayı engelleyen
          tık — bilinçli bedel.
        */}
        <div className="erp-actions" id="onay">
          <Link href="/hakedis">
            Listeye Dön
          </Link>

          {item.status ===
            ProgressPaymentStatus.Draft && (
            <>
              <Link href={`/hakedis/${item.id}/duzenle`}>
                Düzenle
              </Link>

              {actions.can("delete") && (
                <button
                  type="button"
                  disabled={working}
                  onClick={() => setConfirming("sil")}
                >
                  Sil
                </button>
              )}

              {actions.can("edit") && (
                <button
                  type="button"
                  disabled={working}
                  onClick={() =>
                    void runAction("submit")
                  }
                >
                  Onaya Gönder
                </button>
              )}
            </>
          )}

          {item.status ===
            ProgressPaymentStatus.PendingApproval &&
            actions.can("approve") && (
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
            ProgressPaymentStatus.Approved &&
            actions.can("approve") && (
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
              ProgressPaymentStatus.Cancelled &&
            actions.can("delete") && (
              <button
                type="button"
                disabled={working}
                onClick={() =>
                  setConfirming("iptal")
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
            value={money(item.contractAmount)}
          />

          <Info
            label="KDV Oranı"
            value={`%${quantity(item.vatRate)}`}
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
                    {quantity(
                      line.contractQuantity
                    )}
                  </td>

                  <td>
                    {quantity(
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
                      ? quantity(line.fieldQuantity)
                      : "—"}
                  </td>

                  <td>
                    {quantity(
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
                        {quantity(line.fieldDifference)}
                      </span>
                    )}
                  </td>

                  <td>
                    {quantity(
                      line.cumulativeQuantity
                    )}
                  </td>

                  <td>
                    {unitPrice(line.unitPrice)}
                  </td>

                  <td>
                    <strong>
                      {money(
                        line.currentAmount
                      )}
                    </strong>
                  </td>

                  <td>
                    %{quantity(
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
                  %{quantity(deduction.rate)}
                </td>
                <td>
                  {money(
                    deduction.baseAmount
                  )}
                </td>
                <td>
                  <strong>
                    {money(
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
      <ConfirmDialog
        open={confirming === "sil"}
        title="Taslak hakediş silinsin mi?"
        description={
          item
            ? `${item.progressPaymentNumber} numaralı taslak kalıcı olarak silinir.`
            : ""
        }
        confirmLabel="Sil"
        busy={working}
        onCancel={() => setConfirming(null)}
        onConfirm={() => void remove()}
      />

      {/*
        İPTAL GEREKÇESİ ZORUNLU: eskiden prompt boş geçilebiliyor ve
        hata ancak iki pencere kapandıktan sonra yazılıyordu.
      */}
      <ConfirmDialog
        open={confirming === "iptal"}
        title="Hakediş iptal edilsin mi?"
        description={
          item
            ? `${item.progressPaymentNumber} numaralı hakediş iptal edilir; gerekçe kayda geçer.`
            : ""
        }
        confirmLabel="İptal Et"
        requireReason
        reasonLabel="İptal gerekçesi (zorunlu)"
        busy={working}
        onCancel={() => setConfirming(null)}
        onConfirm={(reason) => void cancelProgressPayment(reason)}
      />

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
        {money(value)}
      </div>
    </div>
  );
}
