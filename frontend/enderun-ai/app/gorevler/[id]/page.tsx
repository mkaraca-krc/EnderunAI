"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { AttachmentPanel, CommentThread } from "@/components/collaboration";
import { Badge, Button, ConfirmDialog, EmptyState } from "@/components/ui";
import { useCurrentUser } from "@/lib/use-current-user";
import { useModuleActions } from "@/lib/auth/module-actions";
import { date, dateTime } from "@/lib/format/turkish";
import { type WorkTask, workTaskService } from "@/services/work-task.service";

/**
 * GÖREV DETAYI.
 *
 * BU EKRAN ÖNCE YOKTU VE BU BİR ARIZAYDI: Yapılacaklar ekranı görev
 * satırlarını `/gorevler/{id}`'ye bağlıyordu ama o rota hiç
 * oluşturulmamıştı — kullanıcı bir göreve tıklayınca boş sayfa
 * görüyordu. Ayrıca çift adımlı kapanışın (Tamamlandı → Onaylandı /
 * İade) yapılabileceği bir yer de yoktu.
 *
 * "Yapılacaklar satırında onay düğmesi olmasın ama satır kaydın
 * DOĞRU YERİNE götürsün" kararının karşılığı burasıdır.
 */

/** Sunucudaki `WorkTaskStatus` ile aynı sıra. */
const DURUM_OPEN = 0;
const DURUM_IN_PROGRESS = 1;
const DURUM_COMPLETED = 2;
const DURUM_APPROVED = 3;
const DURUM_CANCELLED = 5;

function durumRengi(status: number) {
  if (status === DURUM_APPROVED) return "success" as const;
  if (status === DURUM_COMPLETED) return "warning" as const;
  if (status === DURUM_CANCELLED) return "danger" as const;
  return "default" as const;
}

const DURUM_ADLARI: Record<number, string> = {
  [DURUM_OPEN]: "Açık",
  [DURUM_IN_PROGRESS]: "Devam Ediyor",
  [DURUM_COMPLETED]: "Tamamlandı, onay bekliyor",
  [DURUM_APPROVED]: "Onaylandı",
  4: "İade Edildi",
  [DURUM_CANCELLED]: "İptal",
};

export default function WorkTaskDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params?.id ?? "";

  const { user } = useCurrentUser();
  const taskActions = useModuleActions("tasks");

  const [item, setItem] = useState<WorkTask | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [islem, setIslem] = useState(false);
  const [islemHatasi, setIslemHatasi] = useState<string | null>(null);

  const [tamamlaAcik, setTamamlaAcik] = useState(false);
  const [iadeAcik, setIadeAcik] = useState(false);

  const yukle = useCallback(async () => {
    if (!id) return;

    setLoading(true);
    setError(null);

    try {
      setItem(await workTaskService.getById(id));
    } catch (hata) {
      setError(hata instanceof Error ? hata.message : "Görev yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void yukle();
  }, [yukle]);

  /**
   * İŞLEM SONUCU DOĞRUDAN KULLANILIYOR, YENİDEN ÇEKİLMİYOR.
   *
   * Uçlar güncel kaydı adlarıyla döndürüyor. Yeniden çekmek fazladan
   * bir istek ve gözle görünür bir titreme demekti.
   */
  async function calistir(is: () => Promise<WorkTask>) {
    setIslem(true);
    setIslemHatasi(null);

    try {
      setItem(await is());
    } catch (hata) {
      setIslemHatasi(hata instanceof Error ? hata.message : "İşlem başarısız.");
    } finally {
      setIslem(false);
      setTamamlaAcik(false);
      setIadeAcik(false);
    }
  }

  const benim = user?.id ?? null;
  const yapan = item?.assignedToUserId === benim;
  const gonderen = item?.assignedByUserId === benim;
  const yonetebilir = taskActions.can("manage");

  return (
    <ErpShell
      design="redwood"
      title={item?.taskNumber ?? "Görev"}
      description={item?.title ?? "Görev detayı"}
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/yapilacaklar" className="hover:text-slate-900">
          Yapılacaklar
        </Link>
        <span>›</span>
        <Link href="/gorevler" className="hover:text-slate-900">
          Görev Yönetimi
        </Link>
        <span>›</span>
        <strong className="text-slate-800">{item?.taskNumber ?? "…"}</strong>
      </div>

      {loading && <p>Görev yükleniyor…</p>}

      {error && !loading && (
        <EmptyState
          title="Görev açılamadı"
          description={error}
          action={
            <Button variant="secondary" onClick={() => void yukle()}>
              Yeniden Dene
            </Button>
          }
        />
      )}

      {item && !loading && (
        <>
          <section className="erp-panel">
            <header className="erp-panel-header">
              <h2>{item.title}</h2>
            </header>

            <div className="mb-4 flex flex-wrap items-center gap-2">
              <Badge variant={durumRengi(item.status)}>
                {DURUM_ADLARI[item.status] ?? item.statusName}
              </Badge>

              {item.isOverdue && <Badge variant="danger">Termin geçti</Badge>}

              <Badge>{item.priorityName}</Badge>

              {item.returnCount > 0 && (
                <Badge variant="warning">{item.returnCount} kez iade edildi</Badge>
              )}
            </div>

            {/*
              YAPI CSS'İN BEKLEDİĞİ GİBİ: `erp-detail-grid` etiketi
              `span`, değeri `strong` olarak biçimlendiriyor.
              `dl/dt/dd` yazsaydım anlamsal olarak daha doğru ama
              GÖRSEL OLARAK BİÇİMSİZ olurdu — mevcut sözleşmeye
              uymak, tek ekran için yeni bir stil dalı açmaktan
              iyidir.
            */}
            <div className="erp-detail-grid">
              <div>
                <span>Yapacak</span>
                <strong>{item.assignedToName ?? "—"}</strong>
              </div>
              <div>
                <span>İsteyen</span>
                <strong>{item.assignedByName ?? "—"}</strong>
              </div>
              <div>
                <span>Başlangıç</span>
                <strong>{date(item.startDate)}</strong>
              </div>
              <div>
                <span>Termin</span>
                <strong>{date(item.dueDate)}</strong>
              </div>
              {item.completedAtUtc && (
                <div>
                  <span>Tamamlandı</span>
                  <strong>{dateTime(item.completedAtUtc)}</strong>
                </div>
              )}
              {item.approvedAtUtc && (
                <div>
                  <span>Onaylayan</span>
                  <strong>
                    {item.approvedByName ?? "—"} · {dateTime(item.approvedAtUtc)}
                  </strong>
                </div>
              )}
            </div>

            {item.description && (
              <p className="erp-comment-body mt-4">{item.description}</p>
            )}

            {item.completionNote && (
              <p className="mt-4">
                <strong>Tamamlama notu:</strong> {item.completionNote}
              </p>
            )}

            {item.returnReason && (
              <p className="erp-status red mt-4">
                <strong>İade gerekçesi:</strong> {item.returnReason}
              </p>
            )}

            {islemHatasi && (
              <p className="erp-status red" role="alert">
                {islemHatasi}
              </p>
            )}

            {yonetebilir && !taskActions.loading && (
              <div className="erp-comment-actions">
                {yapan && item.status === DURUM_OPEN && (
                  <Button
                    variant="secondary"
                    disabled={islem}
                    onClick={() => void calistir(() => workTaskService.start(item.id))}
                  >
                    Başla
                  </Button>
                )}

                {yapan &&
                  (item.status === DURUM_OPEN || item.status === DURUM_IN_PROGRESS) && (
                    <Button
                      variant="primary"
                      disabled={islem}
                      onClick={() => setTamamlaAcik(true)}
                    >
                      Tamamladım
                    </Button>
                  )}

                {gonderen && item.status === DURUM_COMPLETED && (
                  <>
                    <Button
                      variant="primary"
                      disabled={islem}
                      onClick={() =>
                        void calistir(() => workTaskService.approve(item.id))
                      }
                    >
                      Onayla
                    </Button>
                    <Button
                      variant="secondary"
                      disabled={islem}
                      onClick={() => setIadeAcik(true)}
                    >
                      İade Et
                    </Button>
                  </>
                )}
              </div>
            )}
          </section>

          <div className="mt-5 flex flex-col gap-5">
            <AttachmentPanel
              entityType="WorkTask"
              entityId={item.id}
              canUpload={yonetebilir}
            />

            <CommentThread
              entityType="WorkTask"
              entityId={item.id}
              currentUserId={benim}
            />
          </div>

          <ConfirmDialog
            open={tamamlaAcik}
            title="Görevi tamamla"
            description="Görev kapanmaz: isteyen kişiye onaya gider. Ne yaptığınızı yazmanız, onaylayanın işi görmeden karar vermesini önler."
            confirmLabel="Tamamladım"
            showReason
            reasonLabel="Tamamlama notu"
            busy={islem}
            onCancel={() => setTamamlaAcik(false)}
            onConfirm={(not) =>
              void calistir(() => workTaskService.complete(item.id, not))
            }
          />

          <ConfirmDialog
            open={iadeAcik}
            title="Görevi iade et"
            description="Görev yeniden açılır ve yapan kişiye döner. Termin korunur."
            confirmLabel="İade Et"
            requireReason
            reasonLabel="İade gerekçesi"
            busy={islem}
            onCancel={() => setIadeAcik(false)}
            onConfirm={(gerekce) =>
              void calistir(() => workTaskService.returnTask(item.id, gerekce))
            }
          />
        </>
      )}
    </ErpShell>
  );
}
