"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import { usePermissions } from "@/lib/use-permissions";
import {
  projectService,
  type ProjectDeletionImpact,
} from "@/services/project.service";

type Props = {
  projectId: string;
  projectCode: string;
  isArchived: boolean;
  onChanged?: () => void;
};

function formatSize(bytes: number) {
  if (bytes <= 0) return "0 KB";
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;

  return `${(bytes / (1024 * 1024)).toLocaleString("tr-TR", {
    maximumFractionDigits: 1,
  })} MB`;
}

/**
 * Proje silme/arşivleme bölümü.
 *
 * İki kademeli güvenlik burada görünür hale gelir: kesinleşmiş kayıt
 * varsa kalıcı silme düğmesi hiç açılmaz ve neden açılmadığı kalem
 * kalem yazılır. Silme onayı için proje kodunun elle yazılması gerekir —
 * yanlışlıkla tıklamayla silme mümkün değil.
 */
export default function ProjectDangerZone({
  projectId,
  projectCode,
  isArchived,
  onChanged,
}: Props) {
  const router = useRouter();
  const { has } = usePermissions();
  const canDelete = has("projects.delete");

  const [open, setOpen] = useState(false);
  const [impact, setImpact] = useState<ProjectDeletionImpact | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  const [archiveReason, setArchiveReason] = useState("");
  const [confirmCode, setConfirmCode] = useState("");
  const [confirmStage, setConfirmStage] = useState(false);

  const loadImpact = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setImpact(await projectService.getDeletionImpact(projectId));
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Silme etkisi hesaplanamadı."
      );
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    if (open && !impact && !loading) void loadImpact();
  }, [open, impact, loading, loadImpact]);

  if (!canDelete) return null;

  const handleArchive = async () => {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = isArchived
        ? await projectService.unarchive(projectId)
        : await projectService.archive(projectId, archiveReason);

      setNotice(result.message);
      onChanged?.();
      router.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem tamamlanamadı.");
    } finally {
      setBusy(false);
    }
  };

  const handleDelete = async () => {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      await projectService.remove(projectId, confirmCode);
      router.push("/projeler");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Proje silinemedi.");
      setBusy(false);
    }
  };

  return (
    <section className="erp-panel" style={{ borderColor: "#e0b4b4" }}>
      <div className="erp-panel-header">
        <div>
          <h3>Proje Silme / Arşivleme</h3>
          <p>
            Kalıcı silme geri alınamaz. Bağlı kesinleşmiş kaydı olan projeler
            yalnızca arşive alınabilir.
          </p>
        </div>
        <button
          type="button"
          className="erp-btn ghost"
          onClick={() => setOpen((value) => !value)}
        >
          {open ? "Kapat" : "Aç"}
        </button>
      </div>

      {open && (
        <div className="erp-panel-body">
          {error && <div className="erp-alert error">{error}</div>}
          {notice && <div className="erp-alert success">{notice}</div>}

          {loading && <div className="erp-loading">Bağlı kayıtlar taranıyor...</div>}

          {impact && (
            <>
              {impact.blockers.length > 0 ? (
                <div className="erp-alert warning">
                  <strong>
                    Bu projede {impact.totalBlockingRecords} kesinleşmiş kayıt var
                    — kalıcı silme kapalı.
                  </strong>
                  <ul style={{ marginTop: 8 }}>
                    {impact.blockers.map((blocker) => (
                      <li key={blocker.key}>
                        {blocker.label}: <strong>{blocker.count}</strong> —{" "}
                        {blocker.reason}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                <div className="erp-alert info">
                  Kesinleşmiş kayıt bulunmadı. Proje kalıcı olarak silinebilir.
                </div>
              )}

              <h4 style={{ marginTop: 16 }}>Silinecek bağlı kayıtlar</h4>
              {impact.dependencies.length === 0 ? (
                <p>Projeye bağlı başka kayıt yok.</p>
              ) : (
                <ul>
                  {impact.dependencies.map((dependency) => (
                    <li key={dependency.key}>
                      {dependency.label}: <strong>{dependency.count}</strong>
                    </li>
                  ))}
                </ul>
              )}

              {impact.documentCount > 0 && (
                <p style={{ marginTop: 8 }}>
                  Yüklenen dosyalar: <strong>{impact.documentCount}</strong> adet
                  ({formatSize(impact.documentSizeBytes)}) — kalıcı silmede
                  diskten de kaldırılır.
                </p>
              )}

              <hr style={{ margin: "20px 0" }} />

              <h4>{isArchived ? "Arşivden çıkar" : "Arşive al"}</h4>
              <p>
                {isArchived
                  ? "Proje aktif listelere geri döner."
                  : "Veriler durur, proje aktif listelerden ve seçim kutularından düşer; mali raporlar kayıtları göstermeye devam eder."}
              </p>

              {!isArchived && (
                <input
                  className="erp-input"
                  placeholder="Arşiv gerekçesi (opsiyonel)"
                  value={archiveReason}
                  onChange={(event) => setArchiveReason(event.target.value)}
                  maxLength={500}
                />
              )}

              <button
                type="button"
                className="erp-btn"
                style={{ marginTop: 8 }}
                disabled={busy}
                onClick={handleArchive}
              >
                {isArchived ? "Arşivden Çıkar" : "Arşive Al"}
              </button>

              {impact.canHardDelete && (
                <>
                  <hr style={{ margin: "20px 0" }} />

                  <h4>Kalıcı sil</h4>
                  <p>
                    Proje ve yukarıdaki bağlı kayıtları veritabanından
                    tamamen silinir. Bu işlem geri alınamaz ve denetim kaydına
                    yazılır.
                  </p>

                  {!confirmStage ? (
                    <button
                      type="button"
                      className="erp-btn danger"
                      onClick={() => setConfirmStage(true)}
                    >
                      Kalıcı Sil
                    </button>
                  ) : (
                    <div>
                      <p>
                        Onaylamak için proje kodunu yazın:{" "}
                        <strong>{projectCode}</strong>
                      </p>
                      <input
                        className="erp-input"
                        placeholder="Proje kodu"
                        value={confirmCode}
                        onChange={(event) => setConfirmCode(event.target.value)}
                        autoComplete="off"
                      />
                      <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
                        <button
                          type="button"
                          className="erp-btn danger"
                          disabled={
                            busy ||
                            confirmCode.trim().toLocaleUpperCase("tr-TR") !==
                              projectCode.toLocaleUpperCase("tr-TR")
                          }
                          onClick={handleDelete}
                        >
                          {busy ? "Siliniyor..." : "Onaylıyorum, kalıcı sil"}
                        </button>
                        <button
                          type="button"
                          className="erp-btn ghost"
                          disabled={busy}
                          onClick={() => {
                            setConfirmStage(false);
                            setConfirmCode("");
                          }}
                        >
                          Vazgeç
                        </button>
                      </div>
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}
