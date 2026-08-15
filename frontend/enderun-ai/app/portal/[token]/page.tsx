"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import { percent } from "@/lib/format/turkish";

import {
  publicPortalService,
  type PortalProgress,
  type PortalProject,
  type PortalReport,
} from "@/services/employer-portal.service";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

function defaultFrom() {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return d.toISOString().slice(0, 10);
}

function defaultTo() {
  return new Date().toISOString().slice(0, 10);
}

export default function EmployerPortalPage() {
  const params = useParams<{ token: string }>();
  const token = params.token;

  const [project, setProject] = useState<PortalProject | null>(null);
  const [progress, setProgress] = useState<PortalProgress | null>(null);
  const [reports, setReports] = useState<PortalReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [from, setFrom] = useState(defaultFrom());
  const [to, setTo] = useState(defaultTo());
  const [siteId, setSiteId] = useState("");
  const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);

  useEffect(() => {
    async function loadProject() {
      try {
        setProject(await publicPortalService.getProject(token));
      } catch (err) {
        setError(err instanceof Error ? err.message : "Portal bulunamadı.");
      }
    }

    if (token) void loadProject();
  }, [token]);

  useEffect(() => {
    if (!token) return;

    // İlerleme ayrı çağrı: icmali olmayan projede bu uç "yok" der ve
    // raporlar yine gösterilir.
    void publicPortalService
      .getProgress(token)
      .then(setProgress)
      .catch(() => setProgress(null));
  }, [token]);

  useEffect(() => {
    document.title = project?.projectName
      ? `${project.projectName} - Saha Takip Portalı`
      : "Enderun ERP - İşveren Portalı";
  }, [project]);

  useEffect(() => {
    async function loadReports() {
      setLoading(true);
      setError("");

      try {
        const data = await publicPortalService.getReports(
          token,
          from || undefined,
          to || undefined,
          siteId || undefined
        );
        setReports(data);
      } catch (err) {
        setReports([]);
        setError(err instanceof Error ? err.message : "Raporlar yüklenemedi.");
      } finally {
        setLoading(false);
      }
    }

    if (token) void loadReports();
  }, [token, from, to, siteId]);

  const chartData = useMemo(() => {
    const byDate = new Map<string, number>();
    for (const report of reports) {
      const key = report.reportDate.slice(0, 10);
      const total =
        report.engineerCount +
        report.foremanCount +
        report.craftsmanCount +
        report.workerCount +
        report.otherCount;
      byDate.set(key, (byDate.get(key) ?? 0) + total);
    }

    return Array.from(byDate.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .slice(-30);
  }, [reports]);

  const maxHeadcount = Math.max(1, ...chartData.map(([, count]) => count));
  const peakDate = chartData.length > 0
    ? chartData.reduce((peak, entry) => (entry[1] > peak[1] ? entry : peak))[0]
    : null;

  if (error && !project) {
    return (
      <div className="portal-page">
        <div className="portal-container">
          <div className="portal-panel">
            <p className="portal-empty">{error}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="portal-page">
      <div className="portal-header">
        <div className="portal-header-inner">
          <img
            src={project?.companyLogoUrl || "/logo-full-white.png"}
            alt="Şirket logosu"
            className="portal-header-logo"
          />
          <div>
            <h1>{project?.projectName ?? "Yükleniyor..."}</h1>
            <p>İşveren Portalı · {project?.projectCode}</p>
          </div>
        </div>
      </div>

      <div className="portal-container">
        {progress?.hasProgress && (
          <div className="portal-panel">
            <h2>İş İlerlemesi</h2>

            <div style={{ marginBottom: "18px" }}>
              <div
                style={{
                  display: "flex",
                  alignItems: "baseline",
                  justifyContent: "space-between",
                  marginBottom: "6px",
                }}
              >
                <strong style={{ fontSize: "28px" }}>
                  {percent(progress.completionRate)}
                </strong>
                <span>proje geneli</span>
              </div>

              <div
                style={{
                  height: "14px",
                  borderRadius: "999px",
                  background: "#e4ebec",
                  overflow: "hidden",
                }}
              >
                <div
                  style={{
                    height: "100%",
                    width: `${Math.min(100, progress.completionRate)}%`,
                    background: "#18797c",
                  }}
                />
              </div>
            </div>

            {progress.sections.map((section) => (
              <div key={section.name} style={{ marginBottom: "12px" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "14px",
                    marginBottom: "4px",
                  }}
                >
                  <span>{section.name}</span>
                  <span>
                    {percent(section.completionRate)}
                    {" · "}
                    {section.completedItemCount}/{section.itemCount} kalem
                  </span>
                </div>

                <div
                  style={{
                    height: "8px",
                    borderRadius: "999px",
                    background: "#eef2f3",
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      height: "100%",
                      width: `${Math.min(100, section.completionRate)}%`,
                      background: "#5cd2d6",
                    }}
                  />
                </div>
              </div>
            ))}

            <p style={{ fontSize: "13px", color: "#5c6b68", marginTop: "12px" }}>
              Yüzdeler onaylanmış saha raporlarındaki fiziksel imalat
              miktarlarından hesaplanır.
            </p>
          </div>
        )}

        <div className="portal-panel">
          <h2>Tarih Aralığı</h2>
          <div className="portal-filter-row">
            <label>
              Başlangıç
              <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            </label>
            <label>
              Bitiş
              <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </label>
            <label>
              Şantiye
              <select value={siteId} onChange={(e) => setSiteId(e.target.value)}>
                <option value="">Tüm şantiyeler</option>
                {project?.sites.map((site) => (
                  <option key={site.id} value={site.id}>
                    {site.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>

        {chartData.length > 0 && (
          <div className="portal-panel">
            <h2>Personel Sayısı (Son 30 Gün)</h2>
            <div className="portal-chart">
              {chartData.map(([date, count]) => (
                <div className="portal-chart-bar-wrap" key={date}>
                  <div
                    className={`portal-chart-bar${date === peakDate ? " peak" : ""}`}
                    style={{ height: `${Math.max(4, (count / maxHeadcount) * 100)}px` }}
                    title={`${formatDate(date)}: ${count} personel`}
                  />
                  <span className="portal-chart-label">{formatDate(date)}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        <div className="portal-panel">
          <h2>Günlük Raporlar</h2>

          {loading ? (
            <p className="portal-empty">Yükleniyor...</p>
          ) : error ? (
            <p className="portal-empty">{error}</p>
          ) : reports.length === 0 ? (
            <p className="portal-empty">Seçilen aralıkta rapor bulunmuyor.</p>
          ) : (
            reports.map((report) => (
              <div className="portal-report-card" key={report.id}>
                <div className="portal-report-card-header">
                  <strong>
                    {formatDate(report.reportDate)} · {report.siteName}
                  </strong>
                  <span>{report.weatherCondition || "—"}</span>
                </div>

                <div className="portal-headcount-grid">
                  <div>
                    <strong>{report.engineerCount}</strong>
                    <div>Mühendis</div>
                  </div>
                  <div>
                    <strong>{report.foremanCount}</strong>
                    <div>Formen</div>
                  </div>
                  <div>
                    <strong>{report.craftsmanCount}</strong>
                    <div>Usta</div>
                  </div>
                  <div>
                    <strong>{report.workerCount}</strong>
                    <div>İşçi</div>
                  </div>
                  <div>
                    <strong>{report.otherCount}</strong>
                    <div>Diğer</div>
                  </div>
                </div>

                {report.workItems.length > 0 && (
                  <ul className="portal-work-items">
                    {report.workItems.map((item, index) => (
                      <li key={index}>
                        {item.description}
                        {item.quantity ? ` — ${item.quantity} ${item.unit ?? ""}` : ""}
                      </li>
                    ))}
                  </ul>
                )}

                {report.notes && <p>{report.notes}</p>}

                {report.photos.length > 0 && (
                  <div className="portal-photo-grid">
                    {report.photos.map((photo) => {
                      const url = publicPortalService.photoUrl(token, photo.id);
                      return (
                        <img
                          key={photo.id}
                          src={url}
                          alt={photo.caption || "Şantiye fotoğrafı"}
                          onClick={() => setLightboxUrl(url)}
                        />
                      );
                    })}
                  </div>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {lightboxUrl && (
        <div className="portal-lightbox" onClick={() => setLightboxUrl(null)}>
          <img src={lightboxUrl} alt="Şantiye fotoğrafı" />
        </div>
      )}
    </div>
  );
}
