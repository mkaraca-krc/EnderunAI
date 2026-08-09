"use client";

import { useCallback, useEffect, useState } from "react";

import { usePermissions } from "@/lib/use-permissions";
import {
  personnelDocumentService,
  type PersonnelDocumentListItem,
  type PersonnelDocumentType,
} from "@/services/personnel-document.service";

/**
 * Özlük belge arşivi paneli.
 *
 * H8'de uçlar yazıldı ama ekran hiç yapılmamıştı: belgeler yalnızca
 * API'den erişilebiliyordu, yani pratikte kimse kullanamıyordu.
 *
 * "Aslı görüldü" işareti yüklemeden AYRI tutuluyor — belgenin sisteme
 * konmuş olması aslının görüldüğü anlamına gelmez ve özlük
 * denetiminde sorulan şey budur.
 */
export default function PersonnelDocumentsPanel({
  personnelId,
}: {
  personnelId: string;
}) {
  const { has } = usePermissions();

  const canView = has("personnel_document.view");
  const canManage = has("personnel_document.manage");

  const [documents, setDocuments] = useState<PersonnelDocumentListItem[]>([]);
  const [types, setTypes] = useState<PersonnelDocumentType[]>([]);

  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState("");
  const [uploading, setUploading] = useState(false);

  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [showForm, setShowForm] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [form, setForm] = useState({
    documentType: 0,
    title: "",
    documentNumber: "",
    issueDate: "",
    expiryDate: "",
    issuingInstitution: "",
    isMandatory: false,
    notes: "",
  });

  // Durum güncellemeleri ilk await'ten SONRA yapılıyor: efekt
  // gövdesinde eşzamanlı setState zincirleme render tetikliyor.
  const load = useCallback(async () => {
    if (!personnelId || !canView) return;

    try {
      const [documentResult, typeResult] = await Promise.all([
        personnelDocumentService.list(personnelId),
        personnelDocumentService.types(),
      ]);

      setDocuments(documentResult);
      setTypes(typeResult);
      setError("");
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Özlük belgeleri alınamadı."
      );
    }
  }, [personnelId, canView]);

  useEffect(() => {
    let active = true;

    void (async () => {
      await load();

      if (active) setLoading(false);
    })();

    return () => {
      active = false;
    };
  }, [load]);

  async function upload(event: React.FormEvent) {
    event.preventDefault();

    if (!file) {
      setError("Dosya seçilmedi.");
      return;
    }

    if (!form.title.trim()) {
      setError("Belge başlığı zorunludur.");
      return;
    }

    try {
      setUploading(true);
      setError("");
      setNotice("");

      await personnelDocumentService.upload({
        personnelId,
        documentType: form.documentType,
        title: form.title.trim(),
        file,
        documentNumber: form.documentNumber.trim() || undefined,
        issueDate: form.issueDate || undefined,
        expiryDate: form.expiryDate || undefined,
        issuingInstitution: form.issuingInstitution.trim() || undefined,
        isMandatory: form.isMandatory,
        notes: form.notes.trim() || undefined,
      });

      setNotice("Belge yüklendi.");
      setShowForm(false);
      setFile(null);
      setForm({
        documentType: 0,
        title: "",
        documentNumber: "",
        issueDate: "",
        expiryDate: "",
        issuingInstitution: "",
        isMandatory: false,
        notes: "",
      });

      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Belge yüklenemedi.");
    } finally {
      setUploading(false);
    }
  }

  async function toggleVerification(document: PersonnelDocumentListItem) {
    // İşareti kaldırmak denetim izini siler; onay isteniyor.
    if (
      document.isVerified &&
      !window.confirm(
        `"${document.title}" için aslı görüldü işareti kaldırılsın mı?`
      )
    ) {
      return;
    }

    try {
      setBusyId(document.id);
      setError("");
      setNotice("");

      const result = await personnelDocumentService.verify(
        document.id,
        !document.isVerified
      );

      setNotice(result.message);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşaret değiştirilemedi.");
    } finally {
      setBusyId("");
    }
  }

  async function remove(document: PersonnelDocumentListItem) {
    if (
      !window.confirm(
        `"${document.title}" silinsin mi? Dosya da depodan kaldırılır.`
      )
    ) {
      return;
    }

    try {
      setBusyId(document.id);
      setError("");
      setNotice("");

      await personnelDocumentService.remove(document.id);

      setNotice("Belge silindi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Belge silinemedi.");
    } finally {
      setBusyId("");
    }
  }

  async function download(document: PersonnelDocumentListItem) {
    try {
      setBusyId(document.id);
      setError("");

      await personnelDocumentService.download(
        document.id,
        document.originalName || `${document.title}.dosya`
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Belge indirilemedi.");
    } finally {
      setBusyId("");
    }
  }

  if (!canView) {
    return (
      <div style={box}>
        Özlük belgeleri kimlik fotokopisi ve adli sicil gibi kayıtlar
        içerdiği için ayrı bir yetkiyle korunuyor. Görüntüleme yetkiniz
        yok.
      </div>
    );
  }

  if (loading) return <div style={box}>Özlük belgeleri yükleniyor...</div>;

  return (
    <div style={{ display: "grid", gap: 14 }}>
      {error ? <div style={errorBox}>{error}</div> : null}
      {notice ? <div style={noticeBox}>{notice}</div> : null}

      {canManage ? (
        <div>
          <button
            type="button"
            style={primaryButton}
            onClick={() => {
              setShowForm((current) => !current);
              setError("");
              setNotice("");
            }}
          >
            {showForm ? "Vazgeç" : "Belge Yükle"}
          </button>
        </div>
      ) : null}

      {showForm && canManage ? (
        <form onSubmit={upload} style={formBox}>
          <div style={formGrid}>
            <label style={fieldLabel}>
              <span>Belge Türü</span>
              <select
                value={form.documentType}
                onChange={(event) =>
                  setForm((c) => ({
                    ...c,
                    documentType: Number(event.target.value),
                  }))
                }
                style={input}
              >
                {types.map((type) => (
                  <option key={type.value} value={type.value}>
                    {type.name}
                  </option>
                ))}
              </select>
            </label>

            <label style={fieldLabel}>
              <span>Başlık *</span>
              <input
                value={form.title}
                onChange={(event) =>
                  setForm((c) => ({ ...c, title: event.target.value }))
                }
                placeholder="Örn. İş Sözleşmesi 2026"
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              <span>Belge No</span>
              <input
                value={form.documentNumber}
                onChange={(event) =>
                  setForm((c) => ({
                    ...c,
                    documentNumber: event.target.value,
                  }))
                }
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              <span>Düzenleyen Kurum</span>
              <input
                value={form.issuingInstitution}
                onChange={(event) =>
                  setForm((c) => ({
                    ...c,
                    issuingInstitution: event.target.value,
                  }))
                }
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              <span>Düzenlenme Tarihi</span>
              <input
                type="date"
                value={form.issueDate}
                onChange={(event) =>
                  setForm((c) => ({ ...c, issueDate: event.target.value }))
                }
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              <span>Geçerlilik Bitişi</span>
              <input
                type="date"
                value={form.expiryDate}
                onChange={(event) =>
                  setForm((c) => ({ ...c, expiryDate: event.target.value }))
                }
                style={input}
              />
              <small style={hint}>
                Boş bırakılırsa belge süresiz sayılır.
              </small>
            </label>

            <label style={fieldLabel}>
              <span>Dosya *</span>
              <input
                type="file"
                onChange={(event) =>
                  setFile(event.target.files?.[0] ?? null)
                }
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              <span>Not</span>
              <input
                value={form.notes}
                onChange={(event) =>
                  setForm((c) => ({ ...c, notes: event.target.value }))
                }
                style={input}
              />
            </label>
          </div>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              marginTop: 12,
              fontSize: 13,
            }}
          >
            <input
              type="checkbox"
              checked={form.isMandatory}
              onChange={(event) =>
                setForm((c) => ({ ...c, isMandatory: event.target.checked }))
              }
            />
            <span>Özlük dosyasında bulunması zorunlu belge</span>
          </label>

          <div style={{ marginTop: 14 }}>
            <button type="submit" style={primaryButton} disabled={uploading}>
              {uploading ? "Yükleniyor..." : "Kaydet"}
            </button>
          </div>
        </form>
      ) : null}

      {documents.length === 0 ? (
        <div style={box}>Özlük belgesi bulunmuyor.</div>
      ) : (
        <div style={{ display: "grid", gap: 10 }}>
          {documents.map((document) => (
            <div key={document.id} style={row}>
              <div style={{ minWidth: 0 }}>
                <strong>{document.title}</strong>

                <div style={{ marginTop: 4, color: "#64748b", fontSize: 13 }}>
                  {document.documentTypeName}
                  {document.documentNumber
                    ? ` · No: ${document.documentNumber}`
                    : ""}
                  {document.issuingInstitution
                    ? ` · ${document.issuingInstitution}`
                    : ""}
                  {document.isMandatory ? " · Zorunlu" : ""}
                </div>

                <div
                  style={{
                    marginTop: 6,
                    display: "flex",
                    gap: 8,
                    flexWrap: "wrap",
                    fontSize: 12,
                  }}
                >
                  <span
                    style={{
                      ...badge,
                      background: document.isVerified ? "#dcfce7" : "#fef3c7",
                      color: document.isVerified ? "#166534" : "#92400e",
                    }}
                  >
                    {document.isVerified
                      ? "Aslı görüldü"
                      : "Aslı görülmedi"}
                  </span>

                  <span
                    style={{ ...badge, background: "#f1f5f9", color: "#334155" }}
                  >
                    {document.statusName}
                    {document.daysRemaining != null
                      ? ` · ${document.daysRemaining} gün`
                      : ""}
                  </span>

                  {document.originalName ? (
                    <span style={{ color: "#94a3b8" }}>
                      {document.originalName}
                    </span>
                  ) : null}
                </div>
              </div>

              <div
                style={{
                  display: "flex",
                  gap: 8,
                  alignItems: "center",
                  flexShrink: 0,
                }}
              >
                <button
                  type="button"
                  style={smallButton}
                  disabled={busyId === document.id}
                  onClick={() => void download(document)}
                >
                  İndir
                </button>

                {canManage ? (
                  <>
                    <button
                      type="button"
                      style={smallButton}
                      disabled={busyId === document.id}
                      onClick={() => void toggleVerification(document)}
                    >
                      {document.isVerified
                        ? "İşareti Kaldır"
                        : "Aslı Görüldü"}
                    </button>

                    <button
                      type="button"
                      style={dangerButton}
                      disabled={busyId === document.id}
                      onClick={() => void remove(document)}
                    >
                      Sil
                    </button>
                  </>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const box = {
  padding: 24,
  textAlign: "center",
  borderRadius: 12,
  background: "#f8fafc",
  border: "1px solid #e2e8f0",
  color: "#64748b",
} as const;

const errorBox = {
  padding: 13,
  borderRadius: 11,
  background: "#fef2f2",
  border: "1px solid #fecaca",
  color: "#b91c1c",
  fontWeight: 700,
} as const;

const noticeBox = {
  padding: 13,
  borderRadius: 11,
  background: "#ecfdf5",
  border: "1px solid #a7f3d0",
  color: "#065f46",
  fontWeight: 700,
} as const;

const row = {
  display: "flex",
  justifyContent: "space-between",
  gap: 16,
  padding: 13,
  border: "1px solid #e2e8f0",
  borderRadius: 11,
  background: "#f8fafc",
} as const;

const formBox = {
  padding: 16,
  border: "1px solid #e2e8f0",
  borderRadius: 12,
  background: "#fff",
} as const;

const formGrid = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit,minmax(220px,1fr))",
  gap: 14,
} as const;

const fieldLabel = {
  display: "grid",
  gap: 6,
  fontSize: 12,
  color: "#475569",
} as const;

const input = {
  height: 38,
  padding: "0 10px",
  borderRadius: 10,
  border: "1px solid #cbd5e1",
  background: "#fff",
  color: "#0f172a",
} as const;

const hint = { color: "#94a3b8", fontSize: 11 } as const;

const badge = {
  padding: "2px 8px",
  borderRadius: 999,
  fontWeight: 600,
} as const;

const primaryButton = {
  height: 38,
  padding: "0 16px",
  borderRadius: 10,
  border: "none",
  background: "#0f766e",
  color: "#fff",
  fontWeight: 700,
  cursor: "pointer",
} as const;

const smallButton = {
  height: 34,
  padding: "0 12px",
  borderRadius: 9,
  border: "1px solid #cbd5e1",
  background: "#fff",
  color: "#0f172a",
  fontWeight: 600,
  cursor: "pointer",
} as const;

const dangerButton = {
  ...smallButton,
  borderColor: "#fecaca",
  color: "#b91c1c",
} as const;
