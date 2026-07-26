"use client";

import Link from "next/link";
import {
  useEffect,
  useState,
} from "react";
import {
  useParams,
  useRouter,
} from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  projectMeasurementService,
  ProjectMeasurementStatus,
  type ProjectMeasurementDetail,
} from "@/services/project-measurement.service";

const statusLabels: Record<
  ProjectMeasurementStatus,
  string
> = {
  [ProjectMeasurementStatus.Draft]:
    "Taslak",

  [ProjectMeasurementStatus.PendingApproval]:
    "Onay Bekliyor",

  [ProjectMeasurementStatus.Approved]:
    "Onaylandı",

  [ProjectMeasurementStatus.TransferredToProgressPayment]:
    "Hakedişe Aktarıldı",

  [ProjectMeasurementStatus.Cancelled]:
    "İptal Edildi",
};

function formatMoney(
  amount: number,
  currencyCode: string
) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: currencyCode || "TRY",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value);
}

function formatDate(
  value?: string | null
) {
  if (!value) {
    return "—";
  }

  return new Intl.DateTimeFormat(
    "tr-TR",
    {
      dateStyle: "short",
      timeStyle: "short",
    }
  ).format(new Date(value));
}

export default function ProjectMeasurementDetailPage() {
  const params = useParams();
  const router = useRouter();

  const id = String(params.id ?? "");

  const [item, setItem] =
    useState<ProjectMeasurementDetail | null>(
      null
    );

  const [loading, setLoading] =
    useState(true);

  const [processing, setProcessing] =
    useState(false);

  const [error, setError] =
    useState("");

  const [message, setMessage] =
    useState("");

  async function load() {
    if (!id) {
      return;
    }

    setLoading(true);
    setError("");

    try {
      const result =
        await projectMeasurementService.getById(
          id
        );

      setItem(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj detayı yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [id]);

  async function submitForApproval() {
    if (!item) {
      return;
    }

    const confirmed = window.confirm(
      `${item.measurementNumber} numaralı metraj onaya gönderilsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setProcessing(true);
    setError("");
    setMessage("");

    try {
      const result =
        await projectMeasurementService.submit(
          item.id
        );

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj onaya gönderilemedi."
      );
    } finally {
      setProcessing(false);
    }
  }

  async function approve() {
    if (!item) {
      return;
    }

    const confirmed = window.confirm(
      `${item.measurementNumber} numaralı metraj onaylansın mı?`
    );

    if (!confirmed) {
      return;
    }

    setProcessing(true);
    setError("");
    setMessage("");

    try {
      const result =
        await projectMeasurementService.approve(
          item.id
        );

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj onaylanamadı."
      );
    } finally {
      setProcessing(false);
    }
  }

  async function cancelMeasurement() {
    if (!item) {
      return;
    }

    const reason = window.prompt(
      "Metraj iptal gerekçesini yazın:"
    );

    if (!reason?.trim()) {
      return;
    }

    const confirmed = window.confirm(
      `${item.measurementNumber} numaralı metraj iptal edilsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setProcessing(true);
    setError("");
    setMessage("");

    try {
      const result =
        await projectMeasurementService.cancel(
          item.id,
          reason.trim()
        );

      setMessage(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj iptal edilemedi."
      );
    } finally {
      setProcessing(false);
    }
  }

  async function remove() {
    if (!item) {
      return;
    }

    const confirmed = window.confirm(
      `${item.measurementNumber} numaralı taslak metraj kalıcı olarak silinsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setProcessing(true);
    setError("");

    try {
      await projectMeasurementService.remove(
        item.id
      );

      router.push("/metrajlar");
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj silinemedi."
      );

      setProcessing(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Metraj Detayı">
        <div className="erp-form-card">
          Metraj yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell title="Metraj Detayı">
        {error && (
          <div className="erp-alert error">
            {error}
          </div>
        )}

        <Link href="/metrajlar">
          Metraj listesine dön
        </Link>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title={`Metraj ${item.measurementNumber}`}
      description={`${item.projectCode} — ${item.projectName}`}
    >
      <div className="erp-toolbar">
        <div>
          <strong>
            Metraj Detayı
          </strong>

          <small>
            Keşif: {item.boqNumber} ·{" "}
            Durum:{" "}
            {statusLabels[item.status]}
          </small>
        </div>

        <Link href="/metrajlar">
          Metraj Listesine Dön
        </Link>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      {message && (
        <div className="erp-alert success">
          {message}
        </div>
      )}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <div>
            <span>Metraj No</span>
            <strong>
              {item.measurementNumber}
            </strong>
          </div>

          <div>
            <span>Durum</span>
            <strong>
              {statusLabels[item.status]}
            </strong>
          </div>

          <div>
            <span>Proje</span>
            <strong>
              {item.projectCode} —{" "}
              {item.projectName}
            </strong>
          </div>

          <div>
            <span>Keşif No</span>
            <strong>
              {item.boqNumber}
            </strong>
          </div>

          <div>
            <span>Metraj Tarihi</span>
            <strong>
              {formatDate(
                item.measurementDate
              )}
            </strong>
          </div>

          <div>
            <span>Para Birimi</span>
            <strong>
              {item.currencyCode}
            </strong>
          </div>

          <div>
            <span>Bu Dönem Tutarı</span>
            <strong>
              {formatMoney(
                item.totalAmount,
                item.currencyCode
              )}
            </strong>
          </div>

          <div>
            <span>Kalem Sayısı</span>
            <strong>
              {item.items.length}
            </strong>
          </div>

          <div className="span-2">
            <span>Açıklama</span>
            <strong>
              {item.description || "—"}
            </strong>
          </div>

          <div className="span-2">
            <span>Notlar</span>
            <strong>
              {item.notes || "—"}
            </strong>
          </div>

          {item.cancellationReason && (
            <div className="span-2">
              <span>İptal Gerekçesi</span>
              <strong>
                {item.cancellationReason}
              </strong>
            </div>
          )}
        </div>
      </div>

      <div
        className="erp-actions"
        style={{ marginTop: 16 }}
      >
        {item.status ===
          ProjectMeasurementStatus.Draft && (
          <>
            <button
              type="button"
              disabled={processing}
              onClick={() =>
                void submitForApproval()
              }
            >
              Onaya Gönder
            </button>

            <button
              type="button"
              disabled={processing}
              onClick={() =>
                void remove()
              }
            >
              Taslağı Sil
            </button>
          </>
        )}

        {item.status ===
          ProjectMeasurementStatus.PendingApproval && (
          <>
            <button
              type="button"
              disabled={processing}
              onClick={() =>
                void approve()
              }
            >
              Metrajı Onayla
            </button>

            <button
              type="button"
              disabled={processing}
              onClick={() =>
                void cancelMeasurement()
              }
            >
              Metrajı İptal Et
            </button>
          </>
        )}

        {item.status ===
          ProjectMeasurementStatus.Approved && (
          <>
            <Link
              href={`/hakedis/yeni?measurementId=${item.id}`}
            >
              Hakediş Oluştur
            </Link>

            <button
              type="button"
              disabled={processing}
              onClick={() =>
                void cancelMeasurement()
              }
            >
              Metrajı İptal Et
            </button>
          </>
        )}
      </div>

      <div
        className="erp-table-card"
        style={{ marginTop: 16 }}
      >
        <div className="erp-toolbar">
          <div>
            <strong>
              Metraj Kalemleri
            </strong>

            <small>
              {item.items.length} kalem
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
                <th>Keşif</th>
                <th>Önceki</th>
                <th>Bu Dönem</th>
                <th>Kümülatif</th>
                <th>Kalan</th>
                <th>İlerleme</th>
                <th>Birim Fiyat</th>
                <th>Bu Dönem Tutarı</th>
                <th>Mahall</th>
              </tr>
            </thead>

            <tbody>
              {item.items.map(
                (line) => (
                  <tr key={line.id}>
                    <td>
                      {line.lineNumber}
                    </td>

                    <td
                      style={{
                        minWidth: 280,
                      }}
                    >
                      <strong>
                        {line.positionCode}
                      </strong>

                      <div>
                        {line.description}
                      </div>
                    </td>

                    <td>
                      {line.unit}
                    </td>

                    <td>
                      {formatQuantity(
                        line.contractQuantity
                      )}
                    </td>

                    <td>
                      {formatQuantity(
                        line.previousQuantity
                      )}
                    </td>

                    <td>
                      <strong>
                        {formatQuantity(
                          line.currentQuantity
                        )}
                      </strong>
                    </td>

                    <td>
                      {formatQuantity(
                        line.cumulativeQuantity
                      )}
                    </td>

                    <td>
                      {formatQuantity(
                        line.remainingQuantity
                      )}
                    </td>

                    <td>
                      %{formatQuantity(
                        line.completionRate
                      )}
                    </td>

                    <td>
                      {formatMoney(
                        line.unitPrice,
                        item.currencyCode
                      )}
                    </td>

                    <td>
                      <strong>
                        {formatMoney(
                          line.currentAmount,
                          item.currencyCode
                        )}
                      </strong>
                    </td>

                    <td>
                      {[
                        line.location,
                        line.block,
                        line.floor,
                        line.room,
                      ]
                        .filter(Boolean)
                        .join(" / ") || "—"}
                    </td>
                  </tr>
                )
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div
        className="erp-form-card"
        style={{ marginTop: 16 }}
      >
        <div className="erp-form-grid">
          <div>
            <span>Onaya Gönderildi</span>
            <strong>
              {formatDate(
                item.submittedAtUtc
              )}
            </strong>
          </div>

          <div>
            <span>Onaylandı</span>
            <strong>
              {formatDate(
                item.approvedAtUtc
              )}
            </strong>
          </div>

          <div>
            <span>Hakedişe Aktarıldı</span>
            <strong>
              {formatDate(
                item.transferredAtUtc
              )}
            </strong>
          </div>

          <div>
            <span>Hakediş Bağlantısı</span>

            {item.progressPaymentId ? (
              <Link
                href={`/hakedis/${item.progressPaymentId}`}
              >
                Hakedişi Aç
              </Link>
            ) : (
              <strong>—</strong>
            )}
          </div>
        </div>
      </div>
    </ErpShell>
  );
}
