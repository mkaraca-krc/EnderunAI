"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";

import BoqImportMappingPanel, {
  guessMapping,
} from "@/components/engineering/boq-import-mapping";
import BoqImportMatchTable, {
  toDecisions,
} from "@/components/engineering/boq-import-match-table";
import ErpShell from "@/components/erp/erp-shell";
import { usePermissions } from "@/lib/use-permissions";
import {
  projectBoqService,
  ProjectBoqItemType,
  ProjectBoqStatus,
  type BoqImportMapping,
  type BoqImportPreview,
  type BoqSpreadsheetInspection,
  type ProjectBoqDetail,
} from "@/services/project-boq.service";

const statusLabels: Record<ProjectBoqStatus, string> = {
  [ProjectBoqStatus.Draft]: "Taslak",
  [ProjectBoqStatus.Approved]: "Onaylandı",
  [ProjectBoqStatus.Superseded]: "Eski Revizyon",
  [ProjectBoqStatus.Archived]: "Arşivlendi",
};

const statusColors: Record<ProjectBoqStatus, string> = {
  [ProjectBoqStatus.Draft]: "yellow",
  [ProjectBoqStatus.Approved]: "green",
  [ProjectBoqStatus.Superseded]: "gray",
  [ProjectBoqStatus.Archived]: "gray",
};

const itemTypeLabels: Record<ProjectBoqItemType, string> = {
  [ProjectBoqItemType.Mixed]: "Karma",
  [ProjectBoqItemType.Material]: "Malzeme",
  [ProjectBoqItemType.Labor]: "İşçilik",
};

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency })
    .format(value);
}

const number = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 4 });

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function ContractSummaryDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const { has } = usePermissions();

  const fileInputRef = useRef<HTMLInputElement>(null);

  const [item, setItem] = useState<ProjectBoqDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  // Excel aktarma: önce önizleme, sonra kullanıcının onayıyla yazma.
  const [importFile, setImportFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<BoqImportPreview | null>(null);
  const [importing, setImporting] = useState(false);

  // Satır bazında poz kararı: seçilen poz ya da null (bilerek atla).
  // Dokunulmayan satırda uç yalnızca kesin eşleşmeyi uygular.
  const [matchDecisions, setMatchDecisions] = useState<
    Record<number, string | null>
  >({});

  // Sütun eşleme: dosya düzeni varsayılmıyor.
  const [inspection, setInspection] = useState<BoqSpreadsheetInspection | null>(
    null
  );
  const [mapping, setMapping] = useState<BoqImportMapping | null>(null);

  // Revizyon formu
  const [revisionOpen, setRevisionOpen] = useState(false);
  const [amendmentNumber, setAmendmentNumber] = useState("");
  const [amendmentDate, setAmendmentDate] = useState("");
  const [revisionReason, setRevisionReason] = useState("");

  const canEdit = has("hakedis.edit");
  const canApprove = has("hakedis.approve");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setItem(await projectBoqService.getById(params.id));
    } catch (err) {
      setItem(null);
      setError(err instanceof Error ? err.message : "İcmal yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    if (!params.id) return;

    const timer = window.setTimeout(() => void load(), 100);
    return () => window.clearTimeout(timer);
  }, [params.id, load]);

  function clearImport() {
    setImportFile(null);
    setPreview(null);
    setMatchDecisions({});
    setInspection(null);
    setMapping(null);

    // input.value sıfırlanmazsa aynı dosya ikinci kez seçilemez.
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function runAction(
    action: () => Promise<unknown>,
    failureMessage: string,
    successMessage?: string
  ) {
    setBusy(true);
    setActionError("");
    setNotice("");

    try {
      await action();
      if (successMessage) setNotice(successMessage);
      await load();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : failureMessage);
    } finally {
      setBusy(false);
    }
  }

  /**
   * Dosya seçilince önce YAPISI okunur: sayfa adları ve başlıklar.
   * Ayrıştırma bundan sonra, kullanıcının onayladığı eşlemeyle yapılır.
   */
  async function runInspect(file: File, sheetName?: string, headerRow?: number) {
    setImporting(true);
    setActionError("");
    setNotice("");
    setPreview(null);

    try {
      const result = await projectBoqService.importInspect(
        params.id,
        file,
        sheetName,
        headerRow
      );

      setInspection(result);
      setMapping(guessMapping(result));
    } catch (err) {
      setInspection(null);
      setMapping(null);
      setActionError(err instanceof Error ? err.message : "Dosya okunamadı.");
    } finally {
      setImporting(false);
    }
  }

  async function runPreview() {
    if (!importFile) {
      setActionError("Önce bir Excel dosyası seçin.");
      return;
    }

    setImporting(true);
    setActionError("");
    setNotice("");

    try {
      setPreview(
        await projectBoqService.importPreview(
          params.id,
          importFile,
          mapping ?? undefined
        )
      );
      setMatchDecisions({});
    } catch (err) {
      setPreview(null);
      setActionError(err instanceof Error ? err.message : "Dosya okunamadı.");
    } finally {
      setImporting(false);
    }
  }

  async function commitImport() {
    if (!importFile) return;

    setImporting(true);
    setActionError("");

    try {
      const result = await projectBoqService.importCommit(
        params.id,
        importFile,
        toDecisions(matchDecisions),
        mapping ?? undefined
      );
      setNotice(result.message);
      clearImport();
      await load();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Aktarım yapılamadı.");
    } finally {
      setImporting(false);
    }
  }

  async function createRevision() {
    setBusy(true);
    setActionError("");

    try {
      const result = await projectBoqService.createRevision(params.id, {
        amendmentNumber: amendmentNumber.trim() || null,
        amendmentDate: amendmentDate || null,
        reason: revisionReason.trim() || null,
      });

      setRevisionOpen(false);
      router.push(`/kesifler/${result.id}`);
    } catch (err) {
      setActionError(
        err instanceof Error ? err.message : "Revizyon oluşturulamadı."
      );
      setBusy(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Sözleşme İcmali" description="Yükleniyor">
        <div className="erp-loading">İcmal yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell title="Sözleşme İcmali" description="Kayıt bulunamadı">
        <div className="erp-alert error">{error || "İcmal bulunamadı."}</div>
        <Link className="erp-secondary-button" href="/kesifler">
          ← İcmal listesi
        </Link>
      </ErpShell>
    );
  }

  const sectionsWithItems = item.sections.filter((x) => x.itemCount > 0);

  return (
    <ErpShell
      title={`${item.boqNumber} · ${item.name}`}
      description={`${item.projectCode} — ${item.projectName}`}
    >
      <div className="erp-page-toolbar">
        <div>
          <span className={`erp-status ${statusColors[item.status]}`}>
            {statusLabels[item.status]}
          </span>
          {item.isCurrentRevision && (
            <span className="erp-status blue" style={{ marginLeft: "6px" }}>
              {item.revisionCode} · Güncel
            </span>
          )}
          {item.isContractBaseline && (
            <span className="erp-status green" style={{ marginLeft: "6px" }}>
              Sözleşme tabanı
            </span>
          )}
          <small style={{ display: "block", marginTop: "6px" }}>
            {item.itemCount} poz · {sectionsWithItems.length} kısım · Genel
            toplam {money(item.totalAmount, item.currencyCode)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <Link className="erp-secondary-button" href="/kesifler">
            ← Liste
          </Link>

          {item.status === ProjectBoqStatus.Draft && canApprove && (
            <button
              type="button"
              className="erp-primary-button"
              disabled={busy}
              onClick={() =>
                void runAction(
                  () => projectBoqService.approve(params.id),
                  "Onaylanamadı.",
                  "İcmal onaylandı ve kilitlendi."
                )
              }
            >
              Onayla ve Kilitle
            </button>
          )}

          {item.status === ProjectBoqStatus.Approved && canEdit && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => {
                setRevisionOpen((open) => !open);
                setActionError("");
              }}
            >
              {revisionOpen ? "Vazgeç" : "Revizyon Oluştur"}
            </button>
          )}
        </div>
      </div>

      {actionError && <div className="erp-alert error">{actionError}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {item.isLocked && (
        <div className="erp-alert warning">
          Bu icmal onaylandığı için kilitli. Kalemler değiştirilemez —
          değişiklik revizyon (zeyilname) ile yapılır. Geçmiş hakedişler bu
          revizyonun sözleşme miktarlarına dayanıyor.
        </div>
      )}

      {revisionOpen && (
        <div className="erp-form-card">
          <div className="erp-form-header">
            <h2>Yeni Revizyon</h2>
            <p>
              Kalemler yeni revizyona kopyalanır, bu kayıt eski revizyon
              olarak donar. Silinmez: geçmiş hakedişler ona dayanıyor.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Zeyilname No</span>
              <input
                type="text"
                value={amendmentNumber}
                onChange={(event) => setAmendmentNumber(event.target.value)}
                placeholder="Örn. ZEY-01"
              />
            </label>

            <label>
              <span>Zeyilname Tarihi</span>
              <input
                type="date"
                value={amendmentDate}
                onChange={(event) => setAmendmentDate(event.target.value)}
              />
            </label>

            <label className="span-2">
              <span>Gerekçe</span>
              <input
                type="text"
                value={revisionReason}
                onChange={(event) => setRevisionReason(event.target.value)}
                placeholder="Örn. ilave kat imalatı"
              />
            </label>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setRevisionOpen(false)}
            >
              Vazgeç
            </button>
            <button
              type="button"
              className="erp-primary-button"
              disabled={busy}
              onClick={() => void createRevision()}
            >
              {busy ? "Oluşturuluyor..." : "Revizyonu Oluştur"}
            </button>
          </div>
        </div>
      )}

      {!item.isLocked && canEdit && (
        <div className="erp-panel erp-mt">
          <div className="erp-panel-header">
            <h2>Excel ile Toplu Aktarma</h2>
            <a
              className="erp-row-link"
              href={projectBoqService.templateDownloadUrl()}
            >
              Şablonu İndir
            </a>
          </div>

          <p>
            Kendi icmal dosyanızı yükleyebilirsiniz: sütun düzeni
            varsayılmıyor, dosya seçilince hangi sütunun ne olduğunu
            söylersiniz. Şablonu kullanmak isterseniz indirip
            doldurabilirsiniz. Dosya önce okunup önizleme gösterilir; hiçbir
            şey siz onaylamadan yazılmaz. Aktarım mevcut kalemlerin ÜZERİNE
            yazar.
          </p>

          <div
            className="erp-form-actions"
            style={{ justifyContent: "flex-start" }}
          >
            <input
              ref={fileInputRef}
              type="file"
              accept=".xlsx"
              style={{ display: "none" }}
              onChange={(event) => {
                const selected = event.target.files?.[0] ?? null;

                setImportFile(selected);
                setPreview(null);
                setInspection(null);
                setMapping(null);

                if (selected) void runInspect(selected);
              }}
            />

            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => fileInputRef.current?.click()}
            >
              Dosya Seç
            </button>

            <span style={{ margin: "0 10px" }}>
              {importFile ? importFile.name : "Dosya seçilmedi"}
            </span>

            <button
              type="button"
              className="erp-primary-button"
              disabled={importing}
              onClick={() => void runPreview()}
            >
              {importing ? "Okunuyor..." : "Önizle"}
            </button>

            {importFile && (
              <button
                type="button"
                className="erp-secondary-button"
                onClick={clearImport}
              >
                Temizle
              </button>
            )}
          </div>

          {inspection && mapping && (
            <BoqImportMappingPanel
              inspection={inspection}
              mapping={mapping}
              disabled={importing}
              onChange={setMapping}
              onReinspect={(sheetName, headerRow) => {
                if (importFile) void runInspect(importFile, sheetName, headerRow);
              }}
            />
          )}

          {preview && (
            <div className="erp-mt">
              <div className="erp-detail-grid">
                <div>
                  <span className="erp-stat-label">Okunan kısım</span>
                  <strong>{preview.sectionCount}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Okunan poz</span>
                  <strong>{preview.itemCount}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Toplam tutar</span>
                  <strong>
                    {money(preview.totalAmount, item.currencyCode)}
                  </strong>
                </div>
                <div>
                  <span className="erp-stat-label">Atlanan satır</span>
                  <strong>{preview.errors.length}</strong>
                </div>
              </div>

              {preview.errors.length > 0 && (
                <div className="erp-alert warning erp-mt">
                  <strong>Okunamayan satırlar:</strong>
                  <ul>
                    {preview.errors.slice(0, 20).map((problem) => (
                      <li key={problem.rowNumber}>
                        Satır {problem.rowNumber}: {problem.message}
                      </li>
                    ))}
                  </ul>
                  {preview.errors.length > 20 && (
                    <span>
                      ve {preview.errors.length - 20} satır daha. Bu satırlar
                      aktarılmayacak; kalanlar aktarılacak.
                    </span>
                  )}
                </div>
              )}

              {preview.sections.length > 0 && (
                <div className="erp-table-wrap erp-mt">
                  <table className="erp-table">
                    <thead>
                      <tr>
                        <th>Kısım</th>
                        <th>Durum</th>
                        <th style={{ textAlign: "right" }}>Poz</th>
                        <th style={{ textAlign: "right" }}>Tutar</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.sections.map((section) => (
                        <tr key={section.rowNumber}>
                          <td>
                            <strong>{section.name}</strong>
                          </td>
                          <td>
                            <span
                              className={`erp-status ${
                                section.isNew ? "yellow" : "green"
                              }`}
                            >
                              {section.isNew ? "Yeni açılacak" : "Mevcut"}
                            </span>
                          </td>
                          <td style={{ textAlign: "right" }}>
                            {section.itemCount}
                          </td>
                          <td style={{ textAlign: "right" }}>
                            {money(section.totalAmount, item.currencyCode)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {/* Aktarım şeridi eşleştirme tablosunun ÜSTÜNDE de duruyor:
                  350 satırlık bir icmalde tek kopya tablonun altında
                  kalıyor ve kullanıcı önizlemeyi görüp aktarıma hiç
                  ulaşamıyor. */}
              <div className="erp-form-actions">
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={clearImport}
                >
                  Vazgeç
                </button>
                <button
                  type="button"
                  className="erp-primary-button"
                  disabled={importing || preview.itemCount === 0}
                  onClick={() => void commitImport()}
                >
                  {importing
                    ? "Aktarılıyor..."
                    : `${preview.itemCount} pozu aktar`}
                </button>
              </div>

              {preview.items.length > 0 && (
                <div className="erp-mt">
                  <h3>Poz Eşleştirme</h3>
                  <p>
                    Kesin eşleşen satırlar aktarımda kütüphaneye bağlanır.
                    Belirsiz satırda seçim sizde: adaydan seçin, özel poz açın
                    ya da atlayın. Atlanan satır aktarılır, yalnızca poza
                    bağlanmaz.
                  </p>

                  {preview.itemCount > preview.items.length && (
                    <div className="erp-alert warning">
                      Ekranda ilk {preview.items.length} satır gösteriliyor.
                      Kalan {preview.itemCount - preview.items.length} satırda
                      yalnızca kesin eşleşme uygulanacak; belirsiz olanlar poza
                      bağlanmadan aktarılacak.
                    </div>
                  )}

                  <BoqImportMatchTable
                    companyId={item.companyId}
                    items={preview.items}
                    decisions={matchDecisions}
                    disabled={importing}
                    onChange={(rowNumber, positionId) =>
                      setMatchDecisions((current) => ({
                        ...current,
                        [rowNumber]: positionId,
                      }))
                    }
                  />
                </div>
              )}

              <div className="erp-form-actions">
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={clearImport}
                >
                  Vazgeç
                </button>
                <button
                  type="button"
                  className="erp-primary-button"
                  disabled={importing || preview.itemCount === 0}
                  onClick={() => void commitImport()}
                >
                  {importing
                    ? "Aktarılıyor..."
                    : `${preview.itemCount} pozu aktar`}
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      <div className="erp-panel erp-mt">
        <div className="erp-panel-header">
          <h2>Kısım İcmali</h2>
        </div>

        {sectionsWithItems.length === 0 && item.unsectionedItemCount === 0 ? (
          <div className="erp-empty-state">
            <p>Henüz kalem yok.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kısım</th>
                  <th style={{ textAlign: "right" }}>Poz</th>
                  <th style={{ textAlign: "right" }}>Malzeme</th>
                  <th style={{ textAlign: "right" }}>Montaj</th>
                  <th style={{ textAlign: "right" }}>GG&amp;K</th>
                  <th style={{ textAlign: "right" }}>Ara Toplam</th>
                </tr>
              </thead>
              <tbody>
                {sectionsWithItems.map((section) => (
                  <tr key={section.id}>
                    <td>
                      <strong>{section.name}</strong>
                      {section.code && <small>{section.code}</small>}
                    </td>
                    <td style={{ textAlign: "right" }}>{section.itemCount}</td>
                    <td style={{ textAlign: "right" }}>
                      {money(section.materialAmount, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(section.laborAmount, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(section.overheadAmount, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>
                        {money(section.totalAmount, item.currencyCode)}
                      </strong>
                    </td>
                  </tr>
                ))}

                {item.unsectionedItemCount > 0 && (
                  <tr>
                    <td>
                      <strong>Kısımsız</strong>
                      <small>Kısma bağlanmamış kalemler</small>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {item.unsectionedItemCount}
                    </td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>
                      <strong>
                        {money(item.unsectionedAmount, item.currencyCode)}
                      </strong>
                    </td>
                  </tr>
                )}

                <tr>
                  <td colSpan={5}>
                    <strong>GENEL TOPLAM</strong>
                  </td>
                  <td style={{ textAlign: "right" }}>
                    <strong>{money(item.totalAmount, item.currencyCode)}</strong>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Poz Satırları ({item.items.length})</h2>
        </div>

        {item.items.length === 0 ? (
          <div className="erp-empty-state">
            <p>Kalem bulunmuyor. Excel ile toplu aktarabilirsiniz.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Tanım</th>
                  <th>Kısım</th>
                  <th>Birim</th>
                  <th style={{ textAlign: "right" }}>Miktar</th>
                  <th style={{ textAlign: "right" }}>Malzeme</th>
                  <th style={{ textAlign: "right" }}>Montaj</th>
                  <th style={{ textAlign: "right" }}>GG&amp;K</th>
                  <th style={{ textAlign: "right" }}>Birim Fiyat</th>
                  <th style={{ textAlign: "right" }}>Tutar</th>
                  <th>Tip</th>
                </tr>
              </thead>
              <tbody>
                {item.items.map((line) => (
                  <tr key={line.id}>
                    <td>
                      <strong>{line.positionCode}</strong>
                    </td>
                    <td>{line.description}</td>
                    <td>
                      {item.sections.find(
                        (x) => x.id === line.projectHakedisSectionId
                      )?.name ?? "—"}
                    </td>
                    <td>{line.unit}</td>
                    <td style={{ textAlign: "right" }}>
                      {number.format(line.contractQuantity)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(line.materialUnitPrice, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(line.laborUnitPrice, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(line.overheadUnitPrice, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {money(line.unitPrice, item.currencyCode)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>
                        {money(line.totalAmount, item.currencyCode)}
                      </strong>
                    </td>
                    <td>{itemTypeLabels[line.itemType]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="erp-panel erp-mt">
        <div className="erp-panel-header">
          <h2>Kayıt Bilgileri</h2>
        </div>

        <div className="erp-detail-grid">
          <div>
            <span className="erp-stat-label">Onay tarihi</span>
            <strong>{formatDate(item.approvedAtUtc)}</strong>
          </div>
          <div>
            <span className="erp-stat-label">Oluşturulma</span>
            <strong>{formatDate(item.createdAtUtc)}</strong>
          </div>
          <div className="span-2">
            <span className="erp-stat-label">Açıklama</span>
            <strong>{item.description || "—"}</strong>
          </div>
          <div className="span-2">
            <span className="erp-stat-label">Notlar</span>
            <strong>{item.notes || "—"}</strong>
          </div>
        </div>
      </div>
    </ErpShell>
  );
}
