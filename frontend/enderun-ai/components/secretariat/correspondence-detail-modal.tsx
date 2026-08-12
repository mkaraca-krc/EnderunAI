"use client";

import { useCallback, useEffect, useRef, useState } from "react";

import { Badge, Button, ConfirmDialog, Input, Modal, Select } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import {
  CORRESPONDENCE_WORKFLOW_ACTIONS,
  correspondenceAttachmentUrl,
  correspondenceService,
  type CorrespondenceAttachment,
  type CorrespondenceDetail,
  type CorrespondenceDirection,
} from "@/services/correspondence.service";

/**
 * Evrak detayı: akış geçmişi + ekler.
 *
 * Bu uçlar (workflow, attachments, download, delete) aylardır
 * backend'de hazırdı ama hiçbir ekrandan çağrılmıyordu — sekreterya
 * modülünün yarısı erişilemez durumdaydı.
 *
 * AKIŞ GEÇMİŞİ SALT OKUNUR bir kayıt: adım eklenir, var olan adım
 * düzenlenmez ya da silinmez. Evrakın kim tarafından ne zaman nereye
 * havale edildiği düzeltilebilir olsaydı, kaydın kanıt değeri
 * kalmazdı.
 *
 * Yetki: görüntüleme `documents.view`, akış adımı `documents.edit`,
 * ek yükleme `documents.create`, ek silme `documents.delete`. Dördü
 * ayrı; yetkisi olmayana çalışmayan düğme göstermiyoruz.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function formatDateTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** Dosya boyutu — bayt yerine okunur birim. */
function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function CorrespondenceDetailModal({
  documentId,
  direction,
  onClose,
  onChanged,
}: {
  documentId: string | null;
  direction: CorrespondenceDirection;
  onClose: () => void;
  /** Ek/akış değişince listedeki sayaç tazelensin. */
  onChanged?: () => void;
}) {
  const { has } = usePermissions();
  const canEdit = has("documents.edit");
  const canCreate = has("documents.create");
  const canDelete = has("documents.delete");

  const [detail, setDetail] = useState<CorrespondenceDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [action, setAction] = useState(
    String(CORRESPONDENCE_WORKFLOW_ACTIONS[0].value)
  );
  const [toUserName, setToUserName] = useState("");
  const [workflowNote, setWorkflowNote] = useState("");

  const [file, setFile] = useState<File | null>(null);
  const [fileDescription, setFileDescription] = useState("");
  const fileInput = useRef<HTMLInputElement>(null);

  const [pendingDelete, setPendingDelete] =
    useState<CorrespondenceAttachment | null>(null);

  const load = useCallback(async () => {
    if (!documentId) return;

    setLoading(true);
    setError("");

    try {
      setDetail(await correspondenceService.getById(documentId, direction));
    } catch (err) {
      setError(messageOf(err));
      setDetail(null);
    } finally {
      setLoading(false);
    }
  }, [documentId, direction]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  async function addWorkflow() {
    if (!documentId || busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const updated = await correspondenceService.addWorkflow(
        documentId,
        direction,
        {
          action: Number(action),
          toUserName: toUserName || null,
          description: workflowNote || null,
        }
      );

      setDetail(updated);
      setToUserName("");
      setWorkflowNote("");
      setNotice("Akış adımı eklendi.");
      onChanged?.();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function upload() {
    if (!documentId || !file || busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      await correspondenceService.addAttachment(
        documentId,
        direction,
        file,
        fileDescription
      );

      setFile(null);
      setFileDescription("");
      if (fileInput.current) fileInput.current.value = "";

      setNotice("Ek dosya yüklendi.");
      await load();
      onChanged?.();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function removeAttachment(attachment: CorrespondenceAttachment) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      await correspondenceService.deleteAttachment(attachment.id);
      setPendingDelete(null);
      setNotice("Ek dosya silindi.");
      await load();
      onChanged?.();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Modal
        open={documentId !== null}
        title="Evrak detayı"
        description="Akış geçmişi ve ek dosyalar."
        size="lg"
        busy={busy}
        onClose={onClose}
        footer={
          <div className="flex justify-end">
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Kapat
            </Button>
          </div>
        }
      >
        {loading && <p className="text-sm text-slate-500">Yükleniyor...</p>}

        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        {notice && (
          <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
            {notice}
          </div>
        )}

        {detail && (
          <div className="space-y-6">
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
              <div className="font-semibold text-slate-900">
                {detail.document.documentNumber} · {detail.document.subject}
              </div>
              <div className="mt-1 text-sm text-slate-600">
                {detail.document.institutionName || "Kurum belirtilmemiş"} ·{" "}
                {detail.document.statusName}
              </div>
            </div>

            {/* --- AKIŞ GEÇMİŞİ --- */}
            <section>
              <h3 className="mb-2 text-sm font-semibold text-slate-900">
                Akış geçmişi
              </h3>

              {detail.workflow.length === 0 ? (
                <p className="text-sm text-slate-500">
                  Henüz akış adımı yok.
                </p>
              ) : (
                <ol className="space-y-2 border-l-2 border-slate-200 pl-4">
                  {detail.workflow.map((step) => (
                    <li key={step.id} className="text-sm">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info">{step.actionName}</Badge>
                        <span className="text-slate-500">
                          {formatDateTime(step.actionAtUtc)}
                        </span>
                      </div>

                      <div className="mt-1 text-slate-700">
                        {step.fromUserName && <>{step.fromUserName}</>}
                        {step.toUserName && <> → {step.toUserName}</>}
                      </div>

                      {step.description && (
                        <div className="mt-0.5 text-slate-600">
                          {step.description}
                        </div>
                      )}
                    </li>
                  ))}
                </ol>
              )}

              {canEdit && (
                <div className="mt-4 space-y-3 rounded-lg border border-slate-200 p-3">
                  <div className="grid gap-3 sm:grid-cols-2">
                    <Select
                      label="Eylem"
                      value={action}
                      onChange={(event) => setAction(event.target.value)}
                      options={CORRESPONDENCE_WORKFLOW_ACTIONS.map((item) => ({
                        label: item.label,
                        value: String(item.value),
                      }))}
                    />

                    <Input
                      label="Havale edilen kişi"
                      placeholder="İsteğe bağlı"
                      value={toUserName}
                      onChange={(event) => setToUserName(event.target.value)}
                    />
                  </div>

                  <Input
                    label="Açıklama"
                    placeholder="İsteğe bağlı"
                    value={workflowNote}
                    onChange={(event) => setWorkflowNote(event.target.value)}
                  />

                  <div className="flex justify-end">
                    <Button onClick={() => void addWorkflow()} disabled={busy}>
                      {busy ? "Ekleniyor..." : "Akışa ekle"}
                    </Button>
                  </div>

                  <p className="text-xs text-slate-500">
                    Eklenen adım sonradan düzeltilemez ya da silinemez —
                    kaydın kanıt değeri buna bağlı.
                  </p>
                </div>
              )}
            </section>

            {/* --- EKLER --- */}
            <section>
              <h3 className="mb-2 text-sm font-semibold text-slate-900">
                Ek dosyalar ({detail.attachments.length})
              </h3>

              {detail.attachments.length === 0 ? (
                <p className="text-sm text-slate-500">Ek dosya yok.</p>
              ) : (
                <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200">
                  {detail.attachments.map((attachment) => (
                    <li
                      key={attachment.id}
                      className="flex flex-wrap items-center justify-between gap-3 px-3 py-2 text-sm"
                    >
                      <div className="min-w-0">
                        <div className="truncate font-medium text-slate-900">
                          {attachment.fileName}
                        </div>
                        <div className="text-xs text-slate-500">
                          {formatSize(attachment.fileSize)} ·{" "}
                          {formatDateTime(attachment.createdAtUtc)}
                          {attachment.description && ` · ${attachment.description}`}
                        </div>
                      </div>

                      <div className="flex shrink-0 items-center gap-3">
                        {/* Doğrudan bağlantı: dosya akışını JavaScript'e
                            taşımak indirmeyi bozuyor, tarayıcı kendi
                            indirme akışını kullanmalı. */}
                        <a
                          href={correspondenceAttachmentUrl(attachment.id)}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="font-medium text-brand-700 underline"
                        >
                          İndir
                        </a>

                        {canDelete && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setPendingDelete(attachment)}
                            className="font-medium text-red-600 disabled:opacity-50"
                          >
                            Sil
                          </button>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}

              {canCreate && (
                <div className="mt-4 space-y-3 rounded-lg border border-slate-200 p-3">
                  <div>
                    <label className="mb-1.5 block text-sm font-medium text-slate-700">
                      Dosya
                    </label>
                    <input
                      ref={fileInput}
                      type="file"
                      onChange={(event) =>
                        setFile(event.target.files?.[0] ?? null)
                      }
                      className="block w-full text-sm"
                    />
                  </div>

                  <Input
                    label="Açıklama"
                    placeholder="İsteğe bağlı"
                    value={fileDescription}
                    onChange={(event) => setFileDescription(event.target.value)}
                  />

                  <div className="flex justify-end">
                    <Button
                      onClick={() => void upload()}
                      disabled={busy || !file}
                    >
                      {busy ? "Yükleniyor..." : "Yükle"}
                    </Button>
                  </div>
                </div>
              )}
            </section>
          </div>
        )}
      </Modal>

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Ek dosyayı sil"
        description={
          pendingDelete
            ? `"${pendingDelete.fileName}" kalıcı olarak silinecek.`
            : ""
        }
        confirmLabel="Sil"
        busy={busy}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => {
          if (pendingDelete) void removeAttachment(pendingDelete);
        }}
      />
    </>
  );
}
