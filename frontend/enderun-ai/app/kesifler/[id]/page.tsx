"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  projectBoqService,
  ProjectBoqItemType,
  ProjectBoqStatus,
  type ProjectBoqDetail,
} from "@/services/project-boq.service";

const statusLabels: Record<ProjectBoqStatus, string> = {
  [ProjectBoqStatus.Draft]: "Taslak",
  [ProjectBoqStatus.Approved]: "Onaylandı",
  [ProjectBoqStatus.Superseded]: "Eski Revizyon",
  [ProjectBoqStatus.Archived]: "Arşivlendi",
};

const itemTypeLabels: Record<ProjectBoqItemType, string> = {
  [ProjectBoqItemType.Mixed]: "Karma",
  [ProjectBoqItemType.Material]: "Malzeme",
  [ProjectBoqItemType.Labor]: "İşçilik",
};

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
  }).format(value);
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function ProjectBoqDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [item, setItem] = useState<ProjectBoqDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [actionError, setActionError] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result = await projectBoqService.getById(params.id);
      setItem(result);
    } catch (err) {
      setItem(null);
      setError(err instanceof Error ? err.message : "Keşif yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (params.id) {
      void load();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params.id]);

  async function approve() {
    setBusy(true);
    setActionError("");
    try {
      await projectBoqService.approve(params.id);
      await load();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Onaylanamadı.");
    } finally {
      setBusy(false);
    }
  }

  async function archive() {
    setBusy(true);
    setActionError("");
    try {
      await projectBoqService.archive(params.id);
      await load();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Arşivlenemedi.");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true);
    setActionError("");
    try {
      await projectBoqService.remove(params.id);
      router.push("/kesifler");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Silinemedi.");
      setBusy(false);
    }
  }

  return (
    <ErpShell
      title={item ? `${item.boqNumber} · ${item.name}` : "Keşif Detayı"}
      description={item ? `${item.projectCode} — ${item.projectName}` : "Keşif bilgileri yükleniyor"}
    >
      <div className="erp-project-breadcrumb">
        <Link href="/kesifler">Keşifler</Link>
        <span>›</span>
        <strong>{item?.boqNumber ?? "Keşif"}</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Keşif yükleniyor...</div>
      ) : !item ? (
        <div className="erp-panel erp-empty-state">
          <strong>Keşif bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Keşif Bilgileri</h2>
                <p>{item.revisionCode} · {statusLabels[item.status]}{item.isCurrentRevision ? " · Güncel" : ""}</p>
              </div>

              <div className="erp-actions">
                {item.status === ProjectBoqStatus.Draft && (
                  <>
                    <button type="button" disabled={busy} onClick={() => void approve()}>
                      Onayla
                    </button>
                    <button
                      type="button"
                      className="erp-button secondary"
                      disabled={busy}
                      onClick={() => void remove()}
                    >
                      Sil
                    </button>
                  </>
                )}

                {item.status !== ProjectBoqStatus.Archived && (
                  <button
                    type="button"
                    className="erp-button secondary"
                    disabled={busy}
                    onClick={() => void archive()}
                  >
                    Arşivle
                  </button>
                )}
              </div>
            </div>

            {actionError && <div className="erp-alert error">{actionError}</div>}

            <div className="erp-detail-grid">
              <div><span>Keşif No</span><strong>{item.boqNumber}</strong></div>
              <div><span>Adı</span><strong>{item.name}</strong></div>
              <div><span>Proje</span><strong>{item.projectCode} — {item.projectName}</strong></div>
              <div><span>Revizyon</span><strong>{item.revisionCode}</strong></div>
              <div><span>Para Birimi</span><strong>{item.currencyCode}</strong></div>
              <div><span>Toplam</span><strong>{money(item.totalAmount, item.currencyCode)}</strong></div>
              <div><span>Onay Tarihi</span><strong>{formatDate(item.approvedAtUtc)}</strong></div>
              <div><span>Oluşturulma</span><strong>{formatDate(item.createdAtUtc)}</strong></div>
              <div className="span-2">
                <span>Açıklama</span>
                <strong>{item.description || "—"}</strong>
              </div>
              <div className="span-2">
                <span>Notlar</span>
                <strong>{item.notes || "—"}</strong>
              </div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Keşif Kalemleri</h2>
                <p>{item.items.length} kalem</p>
              </div>
            </div>

            {item.items.length === 0 ? (
              <div className="erp-empty-state">Kalem bulunmuyor.</div>
            ) : (
              <div className="erp-table-card">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Poz</th>
                      <th>Açıklama</th>
                      <th>Birim</th>
                      <th>Miktar</th>
                      <th>Birim Fiyat</th>
                      <th>Tutar</th>
                      <th>Tip</th>
                    </tr>
                  </thead>
                  <tbody>
                    {item.items.map((line) => (
                      <tr key={line.id}>
                        <td><strong>{line.positionCode}</strong></td>
                        <td>{line.description}</td>
                        <td>{line.unit}</td>
                        <td>{line.contractQuantity}</td>
                        <td>{money(line.unitPrice, item.currencyCode)}</td>
                        <td><strong>{money(line.totalAmount, item.currencyCode)}</strong></td>
                        <td>{itemTypeLabels[line.itemType]}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </ErpShell>
  );
}
