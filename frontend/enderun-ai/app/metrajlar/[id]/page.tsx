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
import { ConfirmDialog } from "@/components/ui";
import { currencyMoney, quantity } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";

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
  return currencyMoney(amount, currencyCode);
}

function formatQuantity(value: number) {
  return quantity(value);
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

/**
 * Metraj üzerinde yapılabilen geri alınamaz işlemler.
 *
 * Soru metni, düğme etiketi ve gerekçe kuralı TEK YERDE duruyor:
 * dört ayrı işlevde dağınık dururken iptal gerekçesi zorunluyken
 * silme gerekçesiz gidiyordu ve bunu görmek için dört gövdeyi birden
 * okumak gerekiyordu.
 */
type MeasurementAction = "submit" | "approve" | "cancel" | "remove";

const ACTIONS: Record<
  MeasurementAction,
  {
    title: string;
    confirmLabel: string;
    description: (measurementNumber: string) => string;
    requireReason?: boolean;
  }
> = {
  submit: {
    title: "Metrajı Onaya Gönder",
    confirmLabel: "Onaya Gönder",
    description: (no) =>
      `${no} numaralı metraj onaya gönderilecek. Onaya giden metraj artık düzenlenemez.`,
  },
  approve: {
    title: "Metrajı Onayla",
    confirmLabel: "Metrajı Onayla",
    description: (no) =>
      `${no} numaralı metraj onaylanacak. Onaylanan metraj hakedişe aktarılabilir hâle gelir.`,
  },
  cancel: {
    title: "Metrajı İptal Et",
    confirmLabel: "Metrajı İptal Et",
    description: (no) =>
      `${no} numaralı metraj iptal edilecek. İptal geri alınamaz.`,
    // Gerekçe ZORUNLU: iptal edilmiş bir metrajın nedeni aylar sonra
    // hakediş uyuşmazlığında sorulan ilk şey.
    requireReason: true,
  },
  remove: {
    title: "Taslak Metrajı Sil",
    confirmLabel: "Taslağı Sil",
    description: (no) =>
      `${no} numaralı taslak metraj kalıcı olarak silinecek. Bu işlem geri alınamaz.`,
  },
};

export default function ProjectMeasurementDetailPage() {
  const params = useParams();
  const router = useRouter();

  const id = String(params.id ?? "");

  /*
   * Aksiyon izinleri UÇLARDAN (ProjectMeasurementsController):
   *   POST   {id}/submit  -> hakedis.create
   *   POST   {id}/approve -> hakedis.approve
   *   POST   {id}/cancel  -> hakedis.EDIT   (delete değil!)
   *   DELETE {id}         -> hakedis.delete
   *
   * İPTAL EDIT'E BAĞLI, silmeye değil. Yıkıcı bir işlem için zayıf bir
   * yetki; ama karar ucun, arayüz onu izler. Aksi hâlde edit yetkili
   * kullanıcı düğmeyi göremez ama API'den yine iptal edebilirdi —
   * "gizli ama izinli" sapması. Tutarsızlık TEMIZLIK'e yazıldı.
   */
  const actions = useModuleActions("hakedis");

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

  /**
   * Onay bekleyen işlem.
   *
   * Dört eylemin dördü de tarayıcı diyaloğuyla soruluyordu; iptal
   * ayrıca `prompt` + `confirm` diye ÜST ÜSTE İKİ pencere açıyordu.
   * Tarayıcı penceresi gerekçeyi zorunlu tutamıyor, boş metni kabul
   * ediyor ve hata mesajını kendi içinde gösteremiyor: iptal gerekçesi
   * boş bırakılabiliyordu.
   */
  const [pendingAction, setPendingAction] =
    useState<MeasurementAction | null>(null);

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

  async function runPendingAction(reason: string) {
    if (!item || !pendingAction) {
      return;
    }

    setProcessing(true);
    setError("");
    setMessage("");

    try {
      if (pendingAction === "remove") {
        await projectMeasurementService.remove(item.id);

        // Kayıt artık yok: listeye dönülür, bu sayfada kalınmaz.
        router.push("/metrajlar");
        router.refresh();
        return;
      }

      const result =
        pendingAction === "submit"
          ? await projectMeasurementService.submit(item.id)
          : pendingAction === "approve"
            ? await projectMeasurementService.approve(item.id)
            : await projectMeasurementService.cancel(item.id, reason);

      setMessage(result.message);

      // Diyalog BAŞARIDA kapanır, hatada açık kalır: hata mesajı
      // diyaloğun içinde görünür ve kullanıcı tekrar deneyebilir.
      setPendingAction(null);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İşlem tamamlanamadı."
      );
    } finally {
      setProcessing(false);
    }
  }

  if (loading) {
    return (
      <ErpShell design="redwood" title="Metraj Detayı">
        <div className="erp-form-card">
          Metraj yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell design="redwood" title="Metraj Detayı">
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
      design="redwood"
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
            {actions.can("create") && (
              <button
                type="button"
                disabled={processing}
                onClick={() =>
                  setPendingAction("submit")
                }
              >
                Onaya Gönder
              </button>
            )}

            {actions.can("delete") && (
              <button
                type="button"
                disabled={processing}
                onClick={() =>
                  setPendingAction("remove")
                }
              >
                Taslağı Sil
              </button>
            )}
          </>
        )}

        {item.status ===
          ProjectMeasurementStatus.PendingApproval && (
          <>
            {actions.can("approve") && (
              <button
                type="button"
                disabled={processing}
                onClick={() =>
                  setPendingAction("approve")
                }
              >
                Metrajı Onayla
              </button>
            )}

            {actions.can("edit") && actions.can("edit") && (
              <button
                type="button"
                disabled={processing}
                onClick={() =>
                  setPendingAction("cancel")
                }
              >
                Metrajı İptal Et
              </button>
            )}
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
                setPendingAction("cancel")
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

      {/* key: her açılışta gerekçe alanı temiz başlasın — bileşen
          yeniden kurulur, önceki işlemin metni taşınmaz. */}
      {pendingAction && (
        <ConfirmDialog
          key={pendingAction}
          open
          title={ACTIONS[pendingAction].title}
          description={ACTIONS[pendingAction].description(
            item.measurementNumber
          )}
          confirmLabel={ACTIONS[pendingAction].confirmLabel}
          requireReason={ACTIONS[pendingAction].requireReason}
          busy={processing}
          error={error}
          onCancel={() => setPendingAction(null)}
          onConfirm={(reason) => void runPendingAction(reason)}
        />
      )}
    </ErpShell>
  );
}
