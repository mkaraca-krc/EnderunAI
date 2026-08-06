"use client";

import Link from "next/link";
import { useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  POSITION_PRICE_INSTITUTION_LABELS,
  PositionPriceInstitution,
} from "@/services/engineering-position.service";
import {
  bookImportService,
  PositionImportAction,
  positionImportService,
  type BookImportProfile,
  type BookImportSummary,
  type PositionImportPreview,
  type SpreadsheetInspection,
} from "@/services/position-import.service";
import { useEffect } from "react";

const DISCIPLINES: Record<number, string> = {
  0: "Elektrik",
  1: "Zayıf Akım",
  2: "Fiber",
  3: "Mekanik",
  4: "İnşaat",
};

const money = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});

/** Sütun seçici — 0 "eşlenmedi" demek. */
function ColumnSelect({
  label,
  value,
  headers,
  optional,
  onChange,
}: {
  label: string;
  value: number;
  headers: string[];
  optional?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <label>
      <span>
        {label}
        {optional ? " (ops.)" : " *"}
      </span>
      <select
        className="erp-input"
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      >
        <option value={0}>— eşlenmedi —</option>
        {headers.map((header, index) => (
          <option key={index} value={index + 1}>
            {index + 1}. {header || "(başlıksız)"}
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * Poz kitabı toplu içe aktarma.
 *
 * Sütun düzeni varsayılmıyor: ÇŞB ve TEDAŞ kitapları farklı düzende ve
 * düzen yıldan yıla değişiyor. Kullanıcı hangi sütunun ne olduğunu
 * burada söyler, önizlemede ne olacağını görür, sonra aktarır.
 */
export default function PositionImportPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [file, setFile] = useState<File | null>(null);
  const [inspection, setInspection] = useState<SpreadsheetInspection | null>(null);
  const [preview, setPreview] = useState<PositionImportPreview | null>(null);

  const [year, setYear] = useState(new Date().getFullYear());
  const [institution, setInstitution] = useState<number>(
    PositionPriceInstitution.Csb
  );
  const [discipline, setDiscipline] = useState(0);
  const [sourceNote, setSourceNote] = useState("");

  const [headerRow, setHeaderRow] = useState(1);
  const [codeColumn, setCodeColumn] = useState(0);
  const [nameColumn, setNameColumn] = useState(0);
  const [unitColumn, setUnitColumn] = useState(0);
  const [priceColumn, setPriceColumn] = useState(0);
  const [categoryColumn, setCategoryColumn] = useState(0);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  // Hazır profil kipi: ÇŞB/TEDAŞ kitaplarının düzeni bilindiği için
  // kullanıcı sütun seçmez.
  const [useProfile, setUseProfile] = useState(true);
  const [profiles, setProfiles] = useState<BookImportProfile[]>([]);
  const [profileKey, setProfileKey] = useState("");
  const [codePrefix, setCodePrefix] = useState("");
  const [profileFile, setProfileFile] = useState<File | null>(null);
  const [profileSummary, setProfileSummary] = useState<BookImportSummary | null>(
    null
  );
  const [profileWritten, setProfileWritten] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await companyService.getAll();
        if (cancelled) return;

        setCompanies(data);
        setCompanyId((current) => current || data[0]?.id || "");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
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
        const data = await bookImportService.getProfiles();
        if (cancelled) return;

        setProfiles(data);
        setProfileKey((current) => current || data[0]?.key || "");
      } catch {
        if (cancelled) return;

        // Profiller alınamazsa elle eşleme akışı çalışmaya devam eder.
        setProfiles([]);
        setUseProfile(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const selectedProfile = profiles.find((x) => x.key === profileKey) ?? null;

  async function runProfile(write: boolean) {
    if (!profileFile || !profileKey || !companyId) {
      setError("Profil, şirket ve dosya seçilmelidir.");
      return;
    }

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const fields = {
        profileKey,
        companyId,
        year,
        sourceNote: sourceNote.trim() || null,
        codePrefix: codePrefix.trim() || null,
      };

      const summary = write
        ? await bookImportService.commit(profileFile, fields)
        : await bookImportService.preview(profileFile, fields);

      setProfileSummary(summary);
      setProfileWritten(write);

      if (write) setNotice(summary.message);
    } catch (err) {
      setProfileSummary(null);
      setError(err instanceof Error ? err.message : "Kitap okunamadı.");
    } finally {
      setBusy(false);
    }
  }

  const mapping = {
    sheetName: inspection?.sheetName ?? null,
    headerRowIndex: headerRow,
    codeColumn,
    nameColumn,
    unitColumn,
    priceColumn,
    categoryColumn: categoryColumn || null,
    descriptionColumn: null,
  };

  const options = {
    companyId,
    year,
    institution,
    discipline,
    sourceNote: sourceNote.trim() || null,
  };

  const mappingComplete =
    codeColumn > 0 && nameColumn > 0 && unitColumn > 0 && priceColumn > 0;

  async function handleInspect(selected: File, sheet?: string, row?: number) {
    setBusy(true);
    setError("");
    setNotice("");
    setPreview(null);

    try {
      const result = await positionImportService.inspect(selected, sheet, row);

      setInspection(result);
      setHeaderRow(result.headerRowIndex);

      // Başlık adlarından makul bir ilk eşleme önerilir; kullanıcı
      // görüp değiştirebilir. Sessizce varsayılmaz.
      const guess = (patterns: string[]) => {
        const index = result.headers.findIndex((header) =>
          patterns.some((pattern) =>
            header.toLocaleLowerCase("tr-TR").includes(pattern)
          )
        );

        return index >= 0 ? index + 1 : 0;
      };

      setCodeColumn(guess(["poz", "kod"]));
      setNameColumn(guess(["tanım", "tanim", "açıklama", "aciklama", "ad"]));
      setUnitColumn(guess(["birim"]) === guess(["birim fiyat"]) ? 0 : guess(["birim"]));
      setPriceColumn(guess(["fiyat", "tutar"]));
      setCategoryColumn(guess(["grup", "kategori"]));
    } catch (err) {
      setInspection(null);
      setError(err instanceof Error ? err.message : "Dosya okunamadı.");
    } finally {
      setBusy(false);
    }
  }

  async function handlePreview() {
    if (!file) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      setPreview(await positionImportService.preview(file, mapping, options));
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "Önizleme alınamadı.");
    } finally {
      setBusy(false);
    }
  }

  async function handleCommit() {
    if (!file) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await positionImportService.commit(file, mapping, options);

      setNotice(result.message);
      setPreview(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Aktarım başarısız.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <ErpShell
      title="Poz Kitabı İçe Aktarma"
      description="Excel'den toplu poz ve yıllık birim fiyat aktarımı"
    >
      <div className="erp-project-breadcrumb">
        <Link href="/muhendislik/pozlar">Pozlar</Link>
        <span>›</span>
        <strong>İçe Aktar</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {profiles.length > 0 && (
        <section className="erp-panel">
          <div className="erp-panel-header">
            <div>
              <h3>Aktarım yöntemi</h3>
              <p>
                Düzeni bilinen kitaplarda (ÇŞB, TEDAŞ) hazır profil kullanın;
                sütun seçmeniz gerekmez. Başka bir dosya için elle sütun
                eşleyin.
              </p>
            </div>
          </div>

          <div style={{ display: "flex", gap: 16, padding: "0 16px 16px" }}>
            <label>
              <input
                type="radio"
                checked={useProfile}
                onChange={() => setUseProfile(true)}
              />{" "}
              Hazır profil
            </label>
            <label>
              <input
                type="radio"
                checked={!useProfile}
                onChange={() => setUseProfile(false)}
              />{" "}
              Elle sütun eşleme
            </label>
          </div>
        </section>
      )}

      {useProfile && profiles.length > 0 ? (
        <section className="erp-panel">
          <div className="erp-panel-header">
            <div>
              <h3>Hazır profille aktarım</h3>
              <p>
                {selectedProfile?.description ??
                  "Bir profil seçin."}
              </p>
            </div>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(5, minmax(0, 1fr))",
              gap: 12,
            }}
          >
            <label>
              <span>Profil *</span>
              <select
                className="erp-input"
                value={profileKey}
                onChange={(event) => {
                  setProfileKey(event.target.value);
                  setProfileSummary(null);
                }}
              >
                {profiles.map((profile) => (
                  <option key={profile.key} value={profile.key}>
                    {profile.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şirket *</span>
              <select
                className="erp-input"
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
              >
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Yıl *</span>
              <input
                className="erp-input"
                type="number"
                min={2000}
                max={2100}
                value={year}
                onChange={(event) => setYear(Number(event.target.value))}
              />
            </label>

            <label>
              <span>Poz ön eki</span>
              <input
                className="erp-input"
                value={codePrefix}
                placeholder="35."
                onChange={(event) => setCodePrefix(event.target.value)}
              />
            </label>

            <label>
              <span>Kaynak notu</span>
              <input
                className="erp-input"
                value={sourceNote}
                placeholder="ÇŞB 2026 Birim Fiyat Kitabı"
                onChange={(event) => setSourceNote(event.target.value)}
              />
            </label>
          </div>

          <div style={{ marginTop: 16 }}>
            <input
              type="file"
              accept={
                selectedProfile?.fileKind === "pdf" ? ".pdf" : ".xlsx,.xlsm"
              }
              onChange={(event) => {
                setProfileFile(event.target.files?.[0] ?? null);
                setProfileSummary(null);
              }}
            />

            <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
              <button
                type="button"
                className="erp-secondary-button"
                disabled={busy || !profileFile}
                onClick={() => void runProfile(false)}
              >
                {busy ? "Okunuyor..." : "Önizle"}
              </button>

              <button
                type="button"
                className="erp-primary-button"
                disabled={busy || !profileFile || !profileSummary}
                onClick={() => void runProfile(true)}
              >
                Aktar
              </button>
            </div>
          </div>

          {profileSummary && (
            <div style={{ padding: 16 }}>
              <div className="erp-detail-grid">
                <div>
                  <span className="erp-stat-label">Okunan satır</span>
                  <strong>{profileSummary.parsedRows}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Grup başlığı</span>
                  <strong>{profileSummary.groupHeaders}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Şüpheli satır</span>
                  <strong>{profileSummary.suspiciousRows}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">
                    {profileWritten ? "Açılan poz" : "Açılacak poz"}
                  </span>
                  <strong>{profileSummary.createdPositions}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">
                    {profileWritten ? "Yazılan fiyat" : "Yazılacak fiyat"}
                  </span>
                  <strong>{profileSummary.upsertedPrices}</strong>
                </div>
              </div>

              {profileSummary.suspiciousRows > 0 && (
                <div className="erp-alert warning erp-mt">
                  <strong>
                    Okunuşundan emin olunamayan {profileSummary.suspiciousRows}{" "}
                    satır var; bunlar aktarılmaz.
                  </strong>
                  <ul>
                    {profileSummary.suspiciousLines.slice(0, 15).map((line, index) => (
                      <li key={index}>
                        <small>{line}</small>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {profileSummary.warnings.length > 0 && (
                <ul className="erp-mt">
                  {profileSummary.warnings.map((warning, index) => (
                    <li key={index}>
                      <small>{warning}</small>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </section>
      ) : (
        <>
      <section className="erp-panel">
        <div className="erp-panel-header">
          <div>
            <h3>1. Kitap bilgisi ve dosya</h3>
            <p>
              Fiyatlar seçtiğiniz yıl ve kuruma yazılır. Aynı kitap ikinci kez
              yüklenirse poz çoğalmaz, o yılın fiyatı güncellenir.
            </p>
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(5, minmax(0, 1fr))",
            gap: 12,
          }}
        >
          <label>
            <span>Şirket *</span>
            <select
              className="erp-input"
              value={companyId}
              onChange={(event) => setCompanyId(event.target.value)}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Yıl *</span>
            <input
              className="erp-input"
              type="number"
              min={2000}
              max={2100}
              value={year}
              onChange={(event) => setYear(Number(event.target.value))}
            />
          </label>

          <label>
            <span>Kurum *</span>
            <select
              className="erp-input"
              value={institution}
              onChange={(event) => setInstitution(Number(event.target.value))}
            >
              {Object.entries(POSITION_PRICE_INSTITUTION_LABELS).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                )
              )}
            </select>
          </label>

          <label>
            <span>Disiplin *</span>
            <select
              className="erp-input"
              value={discipline}
              onChange={(event) => setDiscipline(Number(event.target.value))}
            >
              {Object.entries(DISCIPLINES).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Kaynak notu</span>
            <input
              className="erp-input"
              value={sourceNote}
              placeholder="ÇŞB 2025 Birim Fiyat Kitabı"
              onChange={(event) => setSourceNote(event.target.value)}
            />
          </label>
        </div>

        <div style={{ marginTop: 16 }}>
          <input
            type="file"
            accept=".xlsx,.xlsm"
            onChange={(event) => {
              const selected = event.target.files?.[0] ?? null;
              setFile(selected);
              setPreview(null);

              if (selected) void handleInspect(selected);
            }}
          />
        </div>
      </section>

      {inspection && (
        <section className="erp-panel">
          <div className="erp-panel-header">
            <div>
              <h3>2. Sütun eşleme</h3>
              <p>
                Sayfa: <strong>{inspection.sheetName}</strong> ·{" "}
                {inspection.totalRowCount} veri satırı. Başlıklardan bir öneri
                dolduruldu; yanlışsa değiştirin.
              </p>
            </div>

            {inspection.sheetNames.length > 1 && (
              <select
                className="erp-input"
                value={inspection.sheetName}
                onChange={(event) => {
                  if (file) void handleInspect(file, event.target.value);
                }}
              >
                {inspection.sheetNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            )}
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(6, minmax(0, 1fr))",
              gap: 12,
            }}
          >
            <label>
              <span>Başlık satırı *</span>
              <input
                className="erp-input"
                type="number"
                min={1}
                value={headerRow}
                onChange={(event) => setHeaderRow(Number(event.target.value))}
                onBlur={() => {
                  if (file) void handleInspect(file, inspection.sheetName, headerRow);
                }}
              />
            </label>

            <ColumnSelect
              label="Poz No"
              value={codeColumn}
              headers={inspection.headers}
              onChange={setCodeColumn}
            />
            <ColumnSelect
              label="Tanım"
              value={nameColumn}
              headers={inspection.headers}
              onChange={setNameColumn}
            />
            <ColumnSelect
              label="Birim"
              value={unitColumn}
              headers={inspection.headers}
              onChange={setUnitColumn}
            />
            <ColumnSelect
              label="Birim Fiyat"
              value={priceColumn}
              headers={inspection.headers}
              onChange={setPriceColumn}
            />
            <ColumnSelect
              label="Grup"
              value={categoryColumn}
              headers={inspection.headers}
              optional
              onChange={setCategoryColumn}
            />
          </div>

          {inspection.sampleRows.length > 0 && (
            <div className="erp-table-wrap" style={{ marginTop: 16 }}>
              <table className="erp-table">
                <thead>
                  <tr>
                    {inspection.headers.map((header, index) => (
                      <th key={index}>
                        {index + 1}. {header || "(başlıksız)"}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {inspection.sampleRows.map((row, rowIndex) => (
                    <tr key={rowIndex}>
                      {row.map((cell, cellIndex) => (
                        <td key={cellIndex}>{cell}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div style={{ marginTop: 16 }}>
            <button
              type="button"
              className="erp-primary-button"
              disabled={busy || !mappingComplete || !companyId}
              onClick={() => void handlePreview()}
            >
              {busy ? "İşleniyor..." : "Önizle"}
            </button>

            {!mappingComplete && (
              <span style={{ marginLeft: 12, fontSize: 13 }}>
                Poz no, tanım, birim ve fiyat sütunlarının hepsi eşlenmeli.
              </span>
            )}
          </div>
        </section>
      )}

      {preview && (
        <section className="erp-panel">
          <div className="erp-panel-header">
            <div>
              <h3>3. Önizleme</h3>
              <p>
                {preview.totalRows} satır · {preview.newPositions} yeni poz ·{" "}
                {preview.priceUpdates} fiyat kaydı ·{" "}
                {preview.descriptionChanges} tanım değişikliği ·{" "}
                {preview.invalidRows} hatalı satır
              </p>
            </div>

            <button
              type="button"
              className="erp-primary-button"
              disabled={busy || preview.validRows === 0}
              onClick={() => void handleCommit()}
            >
              {busy ? "Aktarılıyor..." : `${preview.validRows} satırı aktar`}
            </button>
          </div>

          {preview.fileWarnings.map((warning) => (
            <div className="erp-alert warning" key={warning}>
              {warning}
            </div>
          ))}

          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Satır</th>
                  <th>Poz No</th>
                  <th>Tanım</th>
                  <th>Birim</th>
                  <th>Fiyat</th>
                  <th>Yapılacak</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row) => (
                  <tr key={row.rowNumber}>
                    <td>{row.rowNumber}</td>
                    <td>{row.code || "—"}</td>
                    <td>
                      {row.name || "—"}
                      {row.existingName && (
                        <small>önceki: {row.existingName}</small>
                      )}
                    </td>
                    <td>{row.unit || "AD"}</td>
                    <td>
                      {row.unitPrice != null ? money.format(row.unitPrice) : "—"}
                    </td>
                    <td>
                      <span
                        className={`erp-status ${
                          row.action === PositionImportAction.Skip
                            ? "red"
                            : row.action === PositionImportAction.CreatePosition
                              ? "green"
                              : row.action ===
                                  PositionImportAction.UpdatePositionAndPrice
                                ? "yellow"
                                : "blue"
                        }`}
                      >
                        {row.actionName}
                      </span>
                      {row.error && <small>{row.error}</small>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
        </>
      )}
    </ErpShell>
  );
}
