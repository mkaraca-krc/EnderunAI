"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  amount,
  decimal,
  percent,
  quantity,
  unitPrice,
} from "@/lib/format/turkish";
import { Button, Modal, Select } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  progressPaymentService,
  type ProgressPaymentListItem,
  type ProjectHakedisSection,
} from "@/services/progress-payment.service";
import {
  projectDocumentService,
  type ProjectDocumentListItem,
} from "@/services/project-document.service";
import {
  DeviationImpact,
  ExtraWorkApprovalStatus,
  extraWorkService,
  ProjectContractType,
  progressTrackingService,
  type ProgressTracking,
  type ProjectExtraWork,
  type TrackingItem,
  type TransferableExtraWork,
} from "@/services/progress-tracking.service";



function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "Metraj takibi yüklenemedi.";
}

/** Sapmanın anlamı → renk ve etiket. */
function impactStyle(impact: number): {
  className: string;
  label: string;
  hint: string;
} {
  switch (impact) {
    case DeviationImpact.Opportunity:
      return {
        className: "green",
        label: "İlave iş fırsatı",
        hint: "Birim fiyatlı: yapılan iş kadar ödenir, hakedişe eklenebilir",
      };
    case DeviationImpact.ProfitErosion:
      return {
        className: "red",
        label: "Kâr erozyonu",
        hint: "Anahtar teslim: bedel sabit, bu tutar doğrudan kârdan gider",
      };
    case DeviationImpact.Saving:
      return { className: "green", label: "Tasarruf", hint: "Keşfin altında kalındı" };
    case DeviationImpact.Information:
      return { className: "gray", label: "Bilgi", hint: "Hakediş de o kadar az olur" };
    case DeviationImpact.Undetermined:
      return {
        className: "gray",
        label: "Yorumlanmadı",
        hint: "Sözleşme tipi belirlenmemiş",
      };
    default:
      return { className: "gray", label: "-", hint: "" };
  }
}

/**
 * Keşif vs Gerçekleşen ekranı.
 *
 * Aynı sapma birim fiyatlı işte fırsat, anahtar teslimde zarardır —
 * renk kodu bu ayrımdan çıkar ve sözleşme tipi seçilmeden hiçbir yorum
 * yapılmaz.
 */
export default function MetrajTakipPage() {
  const params = useParams<{ id: string }>();

  const [data, setData] = useState<ProgressTracking | null>(null);
  const [extraWorks, setExtraWorks] = useState<ProjectExtraWork[]>([]);
  const [sections, setSections] = useState<ProjectHakedisSection[]>([]);
  const [documents, setDocuments] = useState<ProjectDocumentListItem[]>([]);

  // Hakedişe aktarım: hangi ilave işlerin aktarılabileceğini UÇ
  // söylüyor (sözleşme türü kuralı orada), hedef hakediş listesi de
  // projeye ait hakedişlerden geliyor.
  const [transferable, setTransferable] = useState<TransferableExtraWork[]>([]);
  const [payments, setPayments] = useState<ProgressPaymentListItem[]>([]);
  const [transferTarget, setTransferTarget] =
    useState<ProjectExtraWork | null>(null);
  const [transferPaymentId, setTransferPaymentId] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  // İlave iş giriş formu
  const [form, setForm] = useState({
    sectionId: "",
    positionCode: "",
    description: "",
    unit: "",
    quantity: "",
    unitPrice: "",
    workDate: new Date().toISOString().slice(0, 10),
    notes: "",
  });

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const [
        tracking,
        works,
        sectionRows,
        documentRows,
        transferableRows,
        paymentRows,
      ] = await Promise.all([
        progressTrackingService.get(params.id),
        extraWorkService.list(params.id).catch(() => []),
        progressPaymentService.getProjectSections(params.id).catch(() => []),
        projectDocumentService.getAll(params.id).catch(() => []),
        extraWorkService.transferable(params.id).catch(() => []),
        progressPaymentService
          .getAll({ projectId: params.id })
          .catch(() => []),
      ]);

      setData(tracking);
      setExtraWorks(works);
      setSections(sectionRows.filter((x) => x.isActive));
      setDocuments(documentRows);
      setTransferable(transferableRows);
      setPayments(paymentRows);
    } catch (loadError) {
      setError(getErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function submitExtraWork(event: FormEvent) {
    event.preventDefault();
    if (!params.id || busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await extraWorkService.create({
        projectId: params.id,
        projectHakedisSectionId: form.sectionId || null,
        positionCode: form.positionCode.trim(),
        description: form.description.trim(),
        unit: form.unit.trim(),
        quantity: Number(form.quantity || 0),
        unitPrice: Number(form.unitPrice || 0),
        workDate: form.workDate,
        notes: form.notes.trim() || null,
      });

      setNotice(result.message);
      setForm((current) => ({
        ...current,
        positionCode: "",
        description: "",
        unit: "",
        quantity: "",
        unitPrice: "",
        notes: "",
      }));

      await load();
    } catch (submitError) {
      setError(getErrorMessage(submitError));
    } finally {
      setBusy(false);
    }
  }

  /**
   * Onay. Anahtar teslimde belge zorunlu — sunucu da belgesiz onayı
   * reddediyor, buradaki kontrol yalnızca kullanıcıyı erken uyarmak için.
   */
  async function approve(work: ProjectExtraWork, documentId: string) {
    if (busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await extraWorkService.approve(
        work.id,
        documentId || null
      );
      setNotice(result.message);
      await load();
    } catch (approveError) {
      setError(getErrorMessage(approveError));
    } finally {
      setBusy(false);
    }
  }

  async function reject(work: ProjectExtraWork) {
    if (busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await extraWorkService.reject(work.id);
      setNotice(result.message);
      await load();
    } catch (rejectError) {
      setError(getErrorMessage(rejectError));
    } finally {
      setBusy(false);
    }
  }

  function openTransfer(work: ProjectExtraWork) {
    setTransferTarget(work);

    // Tek hakediş varsa önceden seç; birden fazlaysa seçim zorunlu
    // kalsın — yanlış hakedişe aktarım uçta geri alınamıyor.
    setTransferPaymentId(payments.length === 1 ? payments[0].id : "");
    setError("");
    setNotice("");
  }

  async function confirmTransfer() {
    if (!transferTarget || !transferPaymentId || busy) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await extraWorkService.transfer(
        transferTarget.id,
        transferPaymentId
      );

      setNotice(result.message);
      setTransferTarget(null);
      setTransferPaymentId("");
      await load();
    } catch (transferError) {
      // Hata modalda kalıyor: kullanıcı neyi aktarmaya çalıştığını
      // görürken mesajı okumalı.
      setError(getErrorMessage(transferError));
    } finally {
      setBusy(false);
    }
  }

  if (loading) {
    return (
      <ErpShell design="redwood" title="Metraj Takip" description="">
        <div className="erp-loading">Keşif–gerçekleşen karşılaştırması hazırlanıyor...</div>
      </ErpShell>
    );
  }

  if (!data) {
    return (
      <ErpShell design="redwood" title="Metraj Takip" description="">
        <div className="erp-alert error">{error || "Proje bulunamadı."}</div>
      </ErpShell>
    );
  }

  const profit = data.profitEstimate;

  return (
    <ErpShell
      design="redwood"
      title={`Metraj Takip — ${data.projectCode}`}
      description={`${data.projectName} · ${data.contractTypeName}`}
    >
      <div className="erp-toolbar">
        <div>
          <strong>Keşif vs Gerçekleşen</strong>
          <small>Sözleşme metrajı kaynağı: {data.baselineSource}</small>
        </div>

        {/* Metraj ve ek iş kayıtları sahadan giriliyor; bu ekran
            onların özeti ve tazelenmeden eskiyordu. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <Link href={`/projeler/${data.projectId}`}>Proje Kartına Dön</Link>
      </div>

      {data.warnings.map((warning) => (
        <div
          key={warning}
          className={`erp-alert ${data.erosionAlarm ? "error" : ""}`}
        >
          {warning}
        </div>
      ))}

      {/* --- ÖZET --- */}
      <div className="erp-form-grid" style={{ marginTop: 18 }}>
        <Stat
          label="Sözleşme Tutarı (metraj)"
          value={amount(data.totals.contractAmount)}
        />
        <Stat
          label="Gerçekleşen"
          value={amount(data.totals.realizedAmount)}
          hint={`Fiziksel gerçekleşme ${percent(
            data.totals.physicalCompletionRate,
            2
          )}`}
        />
        <Stat
          label="Keşif Üstü"
          value={amount(data.totals.overrunAmount)}
          tone={data.totals.overrunAmount > 0 ? "warn" : undefined}
        />
        <Stat
          label="Keşif Altı"
          value={amount(data.totals.underrunAmount)}
        />
        <Stat
          label="Net Sapma"
          value={amount(data.totals.netDeviationAmount)}
          hint={`${data.totals.warningItemCount} kalem eşiği aştı`}
        />
        {data.contractType === ProjectContractType.LumpSum && (
          <Stat
            label="Kâr Erozyonu"
            value={amount(data.netErosionAmount)}
            tone={data.erosionAlarm ? "bad" : undefined}
            hint="Onaylı ek iş düşülmüş hali"
          />
        )}
      </div>

      {/* --- KÂR TAHMİNİ --- */}
      <div className="erp-form-card" style={{ marginTop: 18, padding: 22 }}>
        <h2 style={{ marginBottom: 10 }}>Güncel Tahmini Kâr</h2>

        {profit.isReliable ? (
          <div style={{ maxWidth: 520 }}>
            <Row label="Sözleşme bedeli" value={amount(profit.contractAmount)} />
            <Row label="Fiili maliyet" value={amount(profit.actualCost)} />
            <Row
              label={`Fiziksel gerçekleşme`}
              value={percent(profit.physicalCompletionRate, 2)}
            />
            <Row
              label="Tahmini toplam maliyet"
              value={amount(profit.estimatedTotalCost)}
            />
            <div className="rw-totals-rule">
              <Row
                label="TAHMİNİ KÂR"
                value={`${amount(profit.estimatedProfit)}  (${percent(
                  profit.estimatedProfitRate,
                  2
                )})`}
                bold
              />
            </div>
            <p className="rw-value-muted" style={{ marginTop: 10, fontSize: 12 }}>
              Tahmin, fiili maliyetin gerçekleşme oranına bölünmesiyle
              bulunur; gerçekleşme arttıkça isabet artar.
            </p>
          </div>
        ) : (
          <p className="rw-value-muted" style={{ fontSize: 13 }}>
            {profit.unreliableReason}
          </p>
        )}
      </div>

      {/* --- İLAVE İŞ GİRİŞİ --- */}
      <div className="erp-form-card" style={{ marginTop: 18, padding: 22 }}>
        <h2 style={{ marginBottom: 6 }}>İlave İş Ekle</h2>
        <p className="rw-value-muted" style={{ marginBottom: 14, fontSize: 13 }}>
          {data.contractType === ProjectContractType.LumpSum
            ? "Anahtar teslim: kayıt onay bekleyerek açılır ve işveren onay belgesi iliştirilmeden tahsil edilebilir sayılmaz."
            : data.contractType === ProjectContractType.UnitPrice
              ? "Birim fiyatlı: sözleşmedeki birim fiyat geçerli olduğu için kayıt doğrudan onaylı açılır ve hakedişe eklenebilir."
              : "Sözleşme tipi belirlenmeden ilave iş kaydedilemez — ilave işin anlamı tipe bağlıdır."}
        </p>

        <form onSubmit={submitExtraWork}>
          <div className="erp-form-grid">
            <label>
              <span>Bölüm</span>
              <select
                value={form.sectionId}
                onChange={(event) =>
                  setForm({ ...form, sectionId: event.target.value })
                }
              >
                <option value="">Bölümsüz</option>
                {sections.map((section) => (
                  <option key={section.id} value={section.id}>
                    {section.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Poz Kodu *</span>
              <input
                required
                value={form.positionCode}
                onChange={(event) =>
                  setForm({ ...form, positionCode: event.target.value })
                }
                placeholder="Örn. EK-01"
              />
            </label>

            <label className="span-2">
              <span>Açıklama *</span>
              <input
                required
                value={form.description}
                onChange={(event) =>
                  setForm({ ...form, description: event.target.value })
                }
              />
            </label>

            <label>
              <span>Birim *</span>
              <input
                required
                value={form.unit}
                onChange={(event) => setForm({ ...form, unit: event.target.value })}
                placeholder="ad / m / m²"
              />
            </label>

            <label>
              <span>Miktar *</span>
              <input
                required
                type="number"
                step="0.01"
                min={0}
                value={form.quantity}
                onChange={(event) =>
                  setForm({ ...form, quantity: event.target.value })
                }
              />
            </label>

            <label>
              <span>Birim Fiyat *</span>
              <input
                required
                type="number"
                step="0.01"
                min={0}
                value={form.unitPrice}
                onChange={(event) =>
                  setForm({ ...form, unitPrice: event.target.value })
                }
              />
            </label>

            <label>
              <span>İş Tarihi</span>
              <input
                type="date"
                value={form.workDate}
                onChange={(event) =>
                  setForm({ ...form, workDate: event.target.value })
                }
              />
            </label>

            <label className="span-2">
              <span>Not</span>
              <input
                value={form.notes}
                onChange={(event) => setForm({ ...form, notes: event.target.value })}
              />
            </label>
          </div>

          <div style={{ marginTop: 14, display: "flex", alignItems: "center", gap: 12 }}>
            <button
              type="submit"
              disabled={
                busy || data.contractType === ProjectContractType.Undetermined
              }
            >
              {busy ? "Kaydediliyor..." : "İlave İşi Kaydet"}
            </button>

            {Number(form.quantity) > 0 && Number(form.unitPrice) > 0 && (
              <span className="rw-value-muted" style={{ fontSize: 13 }}>
                Tutar:{" "}
                <strong>
                  {amount(Number(form.quantity) * Number(form.unitPrice))}
                </strong>
              </span>
            )}
          </div>
        </form>

        {notice && (
          <div className="erp-alert success" style={{ marginTop: 12 }}>
            {notice}
          </div>
        )}
      </div>

      {/* --- İLAVE İŞ LİSTESİ --- */}
      {(extraWorks.length > 0 || data.pendingExtraWorkAmount > 0) && (
        <div className="erp-table-card" style={{ marginTop: 18 }}>
          <div className="erp-table-header">
            <h2>İlave İşler</h2>
            <p>
              {data.contractType === ProjectContractType.LumpSum
                ? "Anahtar teslimde yalnızca işveren onaylı ek iş tahsil edilebilir ve kâr erozyonundan düşülür."
                : "Birim fiyatlı projede ilave işler hakedişe eklenebilir."}
            </p>
          </div>

          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Açıklama</th>
                  <th className="tabular">Miktar</th>
                  <th className="tabular">Birim Fiyat</th>
                  <th className="tabular">Tutar</th>
                  <th>Onay</th>
                  <th>Belge</th>
                  <th>Hakediş</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {extraWorks.map((work) => (
                  <ExtraWorkRow
                    key={work.id}
                    work={work}
                    documents={documents}
                    requiresDocument={
                      data.contractType === ProjectContractType.LumpSum
                    }
                    busy={busy}
                    canTransfer={transferable.some((x) => x.id === work.id)}
                    hasPayments={payments.length > 0}
                    onApprove={approve}
                    onReject={reject}
                    onTransfer={openTransfer}
                  />
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={4}>
                    <strong>Tahsil edilebilir (onaylı)</strong>
                  </td>
                  <td className="tabular">
                    <strong>{amount(data.collectibleExtraWorkAmount)}</strong>
                  </td>
                  <td colSpan={4}></td>
                </tr>
                {data.pendingExtraWorkAmount > 0 && (
                  <tr>
                    <td colSpan={4}>Onay bekleyen (erozyondan düşülmez)</td>
                    <td className="tabular">
                      {amount(data.pendingExtraWorkAmount)}
                    </td>
                    <td colSpan={4}></td>
                  </tr>
                )}
              </tfoot>
            </table>
          </div>
        </div>
      )}

      {/* --- KALEM TABLOSU --- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>Kalem Bazında Karşılaştırma</h2>
          <p>{data.totals.itemCount} kalem</p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Bölüm</th>
                <th>Poz</th>
                <th>Açıklama</th>
                <th>Br.</th>
                <th className="tabular">Keşif</th>
                <th className="tabular">Gerçekleşen</th>
                <th className="tabular">Kalan</th>
                <th className="tabular">Fark</th>
                <th className="tabular">Fark %</th>
                <th className="tabular">Tutar Etkisi</th>
                <th className="tabular">Stok Sarfı</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && (
                <tr>
                  <td colSpan={12}>Karşılaştırılacak kalem yok.</td>
                </tr>
              )}

              {data.items.map((item) => (
                <ItemRow key={item.positionCode} item={item} />
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <Modal
        open={transferTarget !== null}
        title="İlave işi hakedişe aktar"
        description="Aktarılan ilave iş o hakedişe bağlanır ve bir daha aktarılamaz."
        busy={busy}
        onClose={() => {
          setTransferTarget(null);
          setTransferPaymentId("");
        }}
        footer={
          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                setTransferTarget(null);
                setTransferPaymentId("");
              }}
            >
              Vazgeç
            </button>
            <button
              type="button"
              disabled={busy || !transferPaymentId}
              onClick={() => void confirmTransfer()}
            >
              {busy ? "Aktarılıyor..." : "Aktar"}
            </button>
          </div>
        }
      >
        {transferTarget && (
          <div style={{ display: "grid", gap: 14 }}>
            <div>
              <strong>{transferTarget.positionCode}</strong> —{" "}
              {transferTarget.description}
              <div className="rw-value-muted" style={{ marginTop: 4 }}>
                {quantity(transferTarget.quantity)} {transferTarget.unit}{" "}
                × {unitPrice(transferTarget.unitPrice)} ={" "}
                <strong>{amount(transferTarget.amount)}</strong>
              </div>
            </div>

            <Select
              label="Hedef hakediş"
              value={transferPaymentId}
              onChange={(event) => setTransferPaymentId(event.target.value)}
              placeholder="Hakediş seçin"
              options={payments.map((payment) => ({
                label:
                  `${payment.progressPaymentNumber} · ` +
                  `${payment.periodNumber}. dönem · ` +
                  new Date(payment.progressPaymentDate).toLocaleDateString(
                    "tr-TR"
                  ),
                value: payment.id,
              }))}
            />

            {/* Geri alınamaz bir işlem; kullanıcı onaydan önce bilmeli. */}
            <p className="rw-value-warning" style={{ margin: 0 }}>
              Bu işlem geri alınamaz. Yanlış hakedişe aktarılan ilave iş
              yalnızca kaynağından düzeltilebilir.
            </p>
          </div>
        )}
      </Modal>
    </ErpShell>
  );
}

/**
 * İlave iş satırı. Onay bekleyen kayıtta belge seçimi ve onay/ret
 * düğmeleri satırın içinde durur — ayrı ekrana gitmeye gerek yok.
 *
 * Anahtar teslimde belge seçilmeden onay düğmesi açılmaz; sunucu da
 * belgesiz onayı reddediyor, buradaki kilit yalnızca kullanıcıyı erken
 * uyarmak için.
 */
function ExtraWorkRow({
  work,
  documents,
  requiresDocument,
  busy,
  canTransfer,
  hasPayments,
  onApprove,
  onReject,
  onTransfer,
}: {
  work: ProjectExtraWork;
  documents: ProjectDocumentListItem[];
  requiresDocument: boolean;
  busy: boolean;
  /** Uç bu işi aktarılabilir saydı mı — kural burada tekrarlanmıyor. */
  canTransfer: boolean;
  hasPayments: boolean;
  onApprove: (work: ProjectExtraWork, documentId: string) => void;
  onReject: (work: ProjectExtraWork) => void;
  onTransfer: (work: ProjectExtraWork) => void;
}) {
  const [documentId, setDocumentId] = useState("");

  const isPending = work.approvalStatus === ExtraWorkApprovalStatus.Pending;

  const statusClass =
    work.approvalStatus === ExtraWorkApprovalStatus.Approved
      ? "green"
      : work.approvalStatus === ExtraWorkApprovalStatus.Rejected
        ? "red"
        : "yellow";

  const statusLabel =
    work.approvalStatus === ExtraWorkApprovalStatus.Approved
      ? "Onaylı"
      : work.approvalStatus === ExtraWorkApprovalStatus.Rejected
        ? "Reddedildi"
        : "Onay bekliyor";

  return (
    <tr>
      <td>{work.positionCode}</td>
      <td>
        {work.description}
        {work.sectionName && <small>{work.sectionName}</small>}
      </td>
      <td className="tabular">{quantity(work.quantity)}</td>
      <td className="tabular">{unitPrice(work.unitPrice)}</td>
      <td className="tabular">
        <strong>{amount(work.amount)}</strong>
      </td>
      <td>
        <span className={`erp-status ${statusClass}`}>{statusLabel}</span>
      </td>
      <td>
        {work.approvalDocumentName ? (
          work.approvalDocumentName
        ) : isPending && requiresDocument ? (
          <select
            value={documentId}
            onChange={(event) => setDocumentId(event.target.value)}
          >
            <option value="">Onay belgesi seçin</option>
            {documents.map((document) => (
              <option key={document.id} value={document.id}>
                {document.fileName}
              </option>
            ))}
          </select>
        ) : (
          "-"
        )}
      </td>
      <td>{work.progressPaymentNumber ?? "-"}</td>
      <td>
        {isPending && (
          <div style={{ display: "flex", gap: 6 }}>
            <button
              type="button"
              disabled={busy || (requiresDocument && !documentId)}
              title={
                requiresDocument && !documentId
                  ? "Anahtar teslimde işveren onay belgesi zorunlu"
                  : undefined
              }
              onClick={() => onApprove(work, documentId)}
            >
              Onayla
            </button>

            <button type="button" disabled={busy} onClick={() => onReject(work)}>
              Reddet
            </button>
          </div>
        )}

        {canTransfer && (
          <button
            type="button"
            disabled={busy || !hasPayments}
            title={
              hasPayments
                ? undefined
                : "Bu projede henüz hakediş yok; önce hakediş oluşturun."
            }
            onClick={() => onTransfer(work)}
          >
            Hakedişe aktar
          </button>
        )}
      </td>
    </tr>
  );
}

function ItemRow({ item }: { item: TrackingItem }) {
  const style = impactStyle(item.impact);

  return (
    <tr>
      <td>{item.sectionName ?? "-"}</td>
      <td>{item.positionCode}</td>
      <td>
        {item.description}
        {item.exceedsWarningThreshold && (
          <small className="rw-value-danger">
            Keşfin %110&apos;unu aştı
          </small>
        )}
      </td>
      <td>{item.unit}</td>
      <td className="tabular">{quantity(item.contractQuantity)}</td>
      <td className="tabular">
        <strong>{quantity(item.realizedQuantity)}</strong>
      </td>
      <td className="tabular">{quantity(item.remainingQuantity)}</td>
      <td className="tabular">
        {item.deviationQuantity > 0 ? "+" : ""}
        {quantity(item.deviationQuantity)}
      </td>
      <td className="tabular">
        {item.contractQuantity > 0
          ? `${item.deviationRate > 0 ? "+" : ""}${decimal(item.deviationRate, 2)}`
          : "-"}
      </td>
      <td className="tabular">
        <strong>
          {item.deviationAmount > 0 ? "+" : ""}
          {amount(item.deviationAmount)}
        </strong>
      </td>
      <td className="tabular">
        {item.issuedStockQuantity !== null && item.issuedStockQuantity !== undefined
          ? quantity(item.issuedStockQuantity)
          : "-"}
      </td>
      <td>
        <span className={`erp-status ${style.className}`}>{style.label}</span>
        {style.hint && <small>{style.hint}</small>}
      </td>
    </tr>
  );
}

function Stat({
  label,
  value,
  hint,
  tone,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: "warn" | "bad";
}) {
  // Renk tokendan: ham hex marka rengi değiştiğinde geride kalıyordu.
  const toneClass =
    tone === "bad"
      ? "rw-value-danger"
      : tone === "warn"
        ? "rw-value-warning"
        : undefined;

  return (
    <div>
      <span>{label}</span>
      <div
        className={toneClass}
        style={{ marginTop: 6, fontSize: 20, fontWeight: 700 }}
      >
        {value}
      </div>
      {hint && (
        <div className="rw-value-muted" style={{ marginTop: 2, fontSize: 12 }}>
          {hint}
        </div>
      )}
    </div>
  );
}

function Row({
  label,
  value,
  bold,
}: {
  label: string;
  value: string;
  bold?: boolean;
}) {
  return (
    <div
      style={{
        display: "flex",
        justifyContent: "space-between",
        padding: "4px 0",
        fontWeight: bold ? 700 : 400,
      }}
    >
      <span>{label}</span>
      <span className="tabular">{value}</span>
    </div>
  );
}
