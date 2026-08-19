"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { FormEvent, Suspense, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { money } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import { projectService, type ProjectListItem } from "@/services/project.service";
import { Button } from "@/components/ui";
import {
  TOOL_SERVICE_DECISIONS,
  TOOL_SERVICE_STATUSES,
  TOOL_SERVICE_URGENCIES,
  ToolAssetStatus,
  ToolServiceDecision,
  ToolServiceStatus,
  toolAssetService,
  toolServiceRequestService,
  type ToolAsset,
  type ToolServiceRequest,
} from "@/services/tool-asset.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

function labelOf(list: [number, string][], value: number) {
  return list.find(([key]) => key === value)?.[1] ?? "—";
}

function statusClass(status: number) {
  if (status === ToolServiceStatus.Scrapped) return "erp-status red";
  if (status === ToolServiceStatus.Completed) return "erp-status green";
  if (status === ToolServiceStatus.Cancelled) return "erp-status gray";
  return "erp-status orange";
}

/**
 * Bir talebin bulunduğu durumdan geçebileceği durumlar.
 *
 * Backend'deki ToolServiceTransitions ile AYNI kural. Burada
 * tekrarlanmasının sebebi yalnızca hangi düğmenin gösterileceği;
 * gerçek kontrol her zaman uçta yapılır ve geçersiz geçiş orada
 * reddedilir.
 */
function nextStates(status: number): [number, string][] {
  if (status === ToolServiceStatus.Requested) {
    return [
      [ToolServiceStatus.Transferred, "Merkeze Geldi"],
      [ToolServiceStatus.Completed, "Yerinde Çözüldü"],
      [ToolServiceStatus.Cancelled, "İptal"],
    ];
  }

  if (status === ToolServiceStatus.Transferred) {
    return [
      [ToolServiceStatus.InService, "Servise Verildi"],
      [ToolServiceStatus.Scrapped, "Hurdaya Ayır"],
      [ToolServiceStatus.Cancelled, "İptal"],
    ];
  }

  if (status === ToolServiceStatus.InService) {
    return [
      [ToolServiceStatus.Completed, "Onarıldı, Döndü"],
      [ToolServiceStatus.Scrapped, "Onarılamadı, Hurda"],
    ];
  }

  return [];
}

function ServiceRequestsContent() {
  /**
   * Düğme -> uç -> izin (ToolServiceRequestsController):
   *   POST tool-service-requests                        -> personnel.create
   *   POST tool-service-requests/{id}/decide            -> personnel.edit
   *   POST tool-service-requests/{id}/advance           -> personnel.edit
   *   POST tool-service-requests/{id}/replacement-request
   *        -> PURCHASING-REQUESTS.create
   *
   * SON DÜĞME BAŞKA MODÜLDE: hurdaya ayrılan aletin yerine SATIN ALMA
   * TALEBİ açıyor. Servis ekranında olması onu personnel.* yapmıyor.
   */
  const actions = useModuleActions("personnel");
  const purchasingActions = useModuleActions("purchasing-requests");

  const searchParams = useSearchParams();
  const presetAssetId = searchParams.get("assetId") ?? "";

  const [assets, setAssets] = useState<ToolAsset[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [requests, setRequests] = useState<ToolServiceRequest[]>([]);

  const [openOnly, setOpenOnly] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);

  // Yeni talep
  const [assetId, setAssetId] = useState(presetAssetId);
  const [projectId, setProjectId] = useState("");
  const [fault, setFault] = useState("");
  const [urgency, setUrgency] = useState(1);

  // Karar
  const [decidingId, setDecidingId] = useState<string | null>(null);
  const [decision, setDecision] = useState<number>(
    ToolServiceDecision.ExternalPaid
  );
  const [decisionNote, setDecisionNote] = useState("");
  const [providerName, setProviderName] = useState("");
  const [serviceCost, setServiceCost] = useState("");

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const [assetList, projectList] = await Promise.all([
          toolAssetService.getAll({}),
          projectService.getAll(),
        ]);

        if (cancelled) return;

        setAssets(assetList);
        setProjects(projectList);
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const list = await toolServiceRequestService.getAll({
          openOnly: openOnly || undefined,
        });

        if (!cancelled) {
          setRequests(list);
          setError("");
        }
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [openOnly, reloadToken]);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();

    if (!assetId) {
      setError("Alet seçin.");
      return;
    }

    if (!fault.trim()) {
      setError("Arıza tanımı zorunludur.");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await toolServiceRequestService.create({
        toolAssetId: assetId,
        // Proje seçilmezse merkez talebidir ve maliyet hiçbir projeye
        // yazılmaz.
        projectId: projectId || null,
        projectSiteId: null,
        faultDescription: fault.trim(),
        urgency,
      });

      setFault("");
      setNotice("Servis talebi açıldı. Alet serviste; zimmet açık kalıyor.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleDecide(event: FormEvent) {
    event.preventDefault();

    if (!decidingId) return;

    if (!decisionNote.trim()) {
      setError("Karar gerekçesi zorunludur.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      await toolServiceRequestService.decide(decidingId, {
        decision,
        decisionNote: decisionNote.trim(),
        serviceProviderName: providerName.trim() || null,
        // Garanti kararında bedel sıfır olmak zorunda; uç da bunu
        // reddediyor.
        serviceCost:
          decision === ToolServiceDecision.ExternalWarranty
            ? 0
            : Number(serviceCost || 0),
      });

      setDecidingId(null);
      setDecisionNote("");
      setProviderName("");
      setServiceCost("");
      setNotice("Servis kararı kaydedildi.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleAdvance(id: string, status: number) {
    setError("");

    try {
      const result = await toolServiceRequestService.advance(id, status);

      setNotice(
        result.costWritten
          ? "Durum güncellendi ve servis maliyeti projeye işlendi."
          : "Durum güncellendi."
      );

      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    }
  }

  async function handleReplacement(id: string) {
    setError("");

    try {
      const result = await toolServiceRequestService.createReplacement(id);
      setNotice(
        `Yerine alım talebi taslak açıldı: ${result.requestNumber}`
      );
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    }
  }

  const serviceable = assets.filter(
    (x) => x.status !== ToolAssetStatus.Scrapped
  );


  /* Eylem sütunu duruma, karara ve İKİ AYRI yetkiye bağlı. */
  /*
   * SÜTUNLAR HER RENDER'DA KURULUYOR — bilerek.
   *
   * `useMemo` ile belleğe almak, eylem işleyicilerini bağımlılıktan
   * çıkarmayı gerektiriyordu; o da BAYAT KAPANIŞ demek: düğme eski
   * durumu görüp yanlış kayıt üzerinde çalışabilirdi. Sütun dizisi
   * ucuz bir nesne; doğruluğu hıza tercih ediyoruz.
   */
  const columns: DataTableColumn<ToolServiceRequest>[] = [
      {
        key: "talep",
        header: "Talep No",
        value: (request) => request.requestNumber,
        render: (request) => (
          <>
            {request.requestNumber}
            <small>
              {dateFormat.format(new Date(request.requestDate))}
            </small>
          </>
        ),
      },
      {
        key: "alet",
        header: "Alet",
        value: (request) => `${request.assetCode} ${request.assetName}`,
        render: (request) => (
          <>
            {request.assetCode}
            <small>{request.assetName}</small>
          </>
        ),
      },
      { key: "ariza", header: "Arıza", value: (r) => r.faultDescription },
      { key: "proje", header: "Proje", value: (r) => r.projectCode ?? "Merkez" },
      {
        key: "durum",
        header: "Durum",
        value: (request) => labelOf(TOOL_SERVICE_STATUSES, request.status),
        render: (request) => (
          <span className={statusClass(request.status)}>
            {labelOf(TOOL_SERVICE_STATUSES, request.status)}
          </span>
        ),
      },
      {
        key: "karar",
        header: "Karar",
        value: (request) =>
          request.decision === ToolServiceDecision.Pending
            ? "—"
            : labelOf(TOOL_SERVICE_DECISIONS, request.decision),
      },
      {
        key: "bedel",
        header: "Bedel",
        numeric: true,
        value: (request) => (request.serviceCost > 0 ? request.serviceCost : ""),
        render: (request) =>
          request.serviceCost > 0 ? money(request.serviceCost) : "—",
      },
      {
        key: "islem",
        header: "",
        value: () => "",
        render: (request) => (
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            {request.decision === ToolServiceDecision.Pending &&
              request.status !== ToolServiceStatus.Completed &&
              request.status !== ToolServiceStatus.Scrapped &&
              actions.can("edit") && (
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => {
                    setDecidingId(request.id);
                    setNotice("");
                  }}
                >
                  Karar Ver
                </button>
              )}

            {actions.can("edit") &&
              nextStates(request.status).map(([value, label]) => (
                <button
                  key={value}
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => void handleAdvance(request.id, value)}
                >
                  {label}
                </button>
              ))}

            {request.status === ToolServiceStatus.Scrapped &&
              !request.replacementPurchaseRequestId &&
              purchasingActions.can("create") && (
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => void handleReplacement(request.id)}
                >
                  Yerine Talep Aç
                </button>
              )}
          </div>
        ),
      },
  ];


  return (
    <ErpShell
      design="redwood"
      title="Alet Servis Talepleri"
      description="Şantiyeden talep, merkez kararı, servis takibi"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-page-toolbar">
        {/* Servis talepleri sahadan açılıyor. */}
        <Button variant="secondary" disabled={saving} onClick={() => setReloadToken((value) => value + 1)}>Yenile</Button>

        <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <input
            type="checkbox"
            checked={openOnly}
            onChange={(e) => setOpenOnly(e.target.checked)}
          />
          <span style={{ fontSize: 12 }}>Yalnızca açık talepler</span>
        </label>

        <Link className="erp-secondary-button" href="/demirbas">
          Demirbaş Listesi
        </Link>
      </div>

      <section className="erp-panel" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Yeni Servis Talebi</h2>

        <form
          onSubmit={handleCreate}
          style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}
        >
          <label>
            <span style={{ display: "block", fontSize: 11 }}>Alet *</span>
            <select value={assetId} onChange={(e) => setAssetId(e.target.value)}>
              <option value="">Seçin</option>
              {serviceable.map((asset) => (
                <option key={asset.id} value={asset.id}>
                  {asset.code} — {asset.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span style={{ display: "block", fontSize: 11 }}>Proje / şantiye</span>
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
              <option value="">Merkez (projeye yazılmaz)</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
            <small className="rw-value-muted" style={{ display: "block" }}>
              Ücretli servis bu projeye yazılır
            </small>
          </label>

          <label style={{ flex: "1 1 240px" }}>
            <span style={{ display: "block", fontSize: 11 }}>Arıza *</span>
            <input
              value={fault}
              onChange={(e) => setFault(e.target.value)}
              placeholder="Şarj tutmuyor"
            />
          </label>

          <label>
            <span style={{ display: "block", fontSize: 11 }}>Aciliyet</span>
            <select
              value={urgency}
              onChange={(e) => setUrgency(Number(e.target.value))}
            >
              {TOOL_SERVICE_URGENCIES.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          {actions.can("create") && (
            <button type="submit" className="erp-primary-button" disabled={saving}>
              {saving ? "Açılıyor..." : "Talep Aç"}
            </button>
          )}
        </form>
      </section>

      {decidingId && (
        <section className="erp-panel" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Servis Kararı</h2>

          <form
            onSubmit={handleDecide}
            style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}
          >
            <label>
              <span style={{ display: "block", fontSize: 11 }}>Karar *</span>
              <select
                value={decision}
                onChange={(e) => setDecision(Number(e.target.value))}
              >
                {TOOL_SERVICE_DECISIONS.map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Servis firması</span>
              <input
                value={providerName}
                onChange={(e) => setProviderName(e.target.value)}
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Bedel</span>
              <input
                type="number"
                step="0.01"
                value={
                  decision === ToolServiceDecision.ExternalWarranty
                    ? "0"
                    : serviceCost
                }
                disabled={decision === ToolServiceDecision.ExternalWarranty}
                onChange={(e) => setServiceCost(e.target.value)}
              />
              {decision === ToolServiceDecision.ExternalWarranty && (
                <small className="rw-value-muted" style={{ display: "block" }}>
                  Garantide bedel olmaz
                </small>
              )}
            </label>

            <label style={{ flex: "1 1 240px" }}>
              <span style={{ display: "block", fontSize: 11 }}>Gerekçe *</span>
              <input
                value={decisionNote}
                onChange={(e) => setDecisionNote(e.target.value)}
                placeholder="Motor sargısı yanmış"
              />
            </label>

            <div style={{ display: "flex", gap: 8 }}>
              {actions.can("edit") && (
                <button type="submit" className="erp-primary-button" disabled={saving}>
                  Kararı Kaydet
                </button>
              )}
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setDecidingId(null)}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </section>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Talepler</h2>
          <small>{requests.length} kayıt</small>
        </div>

        {requests.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Servis talebi yok</strong>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <DataTable
              rows={requests}
              columns={columns}
              rowKey={(request) => request.id}
              title="Servis Talepleri"
              emptyText="Servis talebi bulunmuyor."
              /* FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER. Sayfalama F4'te eklendi
                 ama bu bağ kurulmamıştı: kullanıcı 7. sayfadayken filtreyi
                 daraltınca son sayfada kalıyordu. */
              resetKey={`${projectId}`}
            />
          </div>
        )}
      </div>
    </ErpShell>
  );
}

export default function ToolServiceRequestsPage() {
  return (
    <Suspense fallback={<div className="erp-loading">Yükleniyor...</div>}>
      <ServiceRequestsContent />
    </Suspense>
  );
}
