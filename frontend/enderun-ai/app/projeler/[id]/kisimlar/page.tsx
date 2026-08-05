"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { usePermissions } from "@/lib/use-permissions";
import {
  projectBoqService,
  type HakedisSectionTemplate,
} from "@/services/project-boq.service";

type SectionRow = {
  key: string;
  id?: string | null;
  order: number;
  name: string;
  code: string;
  isActive: boolean;
};

function emptyRow(order: number): SectionRow {
  return {
    key: crypto.randomUUID(),
    id: null,
    order,
    name: "",
    code: "",
    isActive: true,
  };
}

export default function ProjectSectionsPage() {
  const params = useParams<{ id: string }>();
  const { has } = usePermissions();

  const [rows, setRows] = useState<SectionRow[]>([]);
  const [templates, setTemplates] = useState<HakedisSectionTemplate[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const canEdit = has("hakedis.edit");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [sections, templateList] = await Promise.all([
        projectBoqService.getSections(params.id),
        projectBoqService.getSectionTemplates().catch(() => []),
      ]);

      setRows(
        sections.map((section) => ({
          key: section.id,
          id: section.id,
          order: section.order,
          name: section.name,
          code: section.code ?? "",
          isActive: section.isActive,
        }))
      );

      setTemplates(templateList);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kısımlar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    if (!params.id) return;

    const timer = window.setTimeout(() => void load(), 100);
    return () => window.clearTimeout(timer);
  }, [params.id, load]);

  function update(key: string, patch: Partial<SectionRow>) {
    setRows((current) =>
      current.map((row) => (row.key === key ? { ...row, ...patch } : row))
    );
  }

  /**
   * Şablon kısımları listeye EKLER, mevcutları silmez. Silseydi
   * kalemleri o kısma bağlı bir icmal sessizce kopardı.
   */
  function applyTemplate() {
    const template = templates.find((x) => x.key === selectedTemplate);
    if (!template) return;

    setNotice("");

    const existingNames = new Set(
      rows.map((row) => row.name.trim().toLocaleLowerCase("tr-TR"))
    );

    const additions = template.sections
      .filter(
        (section) =>
          !existingNames.has(section.name.trim().toLocaleLowerCase("tr-TR"))
      )
      .map((section, index) => ({
        key: crypto.randomUUID(),
        id: null,
        order: rows.length + index + 1,
        name: section.name,
        code: "",
        isActive: true,
      }));

    if (additions.length === 0) {
      setNotice("Şablondaki kısımların hepsi zaten listede.");
      return;
    }

    setRows((current) => [...current, ...additions]);
    setNotice(
      `${additions.length} kısım eklendi. Kaydetmeden önce düzenleyebilirsiniz.`
    );
  }

  const validationErrors: string[] = [];

  rows.forEach((row, index) => {
    if (!row.name.trim()) {
      validationErrors.push(`${index + 1}. satırda kısım adı boş.`);
    }
  });

  const duplicateNames = rows
    .map((row) => row.name.trim().toLocaleLowerCase("tr-TR"))
    .filter((name, index, all) => name && all.indexOf(name) !== index);

  if (duplicateNames.length > 0) {
    validationErrors.push("Aynı isimde birden fazla kısım var.");
  }

  async function save() {
    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await projectBoqService.replaceSections(
        params.id,
        rows.map((row, index) => ({
          id: row.id ?? null,
          order: index + 1,
          name: row.name.trim(),
          code: row.code.trim() || null,
          isActive: row.isActive,
        }))
      );

      setNotice("Kısımlar kaydedildi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kısımlar kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Sözleşme İcmali Kısımları"
      description="Projenin imalat kırılımı — her iş için farklıdır, serbestçe tanımlanır"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{rows.length} kısım</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Kısımlar koda gömülü değil. Şablon yalnızca başlangıç önerisidir;
            boş başlamak da mümkün.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <Link className="erp-secondary-button" href={`/projeler/${params.id}`}>
            ← Proje
          </Link>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {canEdit && templates.length > 0 && (
        <div className="erp-panel">
          <div className="erp-panel-header">
            <h2>Hazır Şablon</h2>
          </div>

          <div className="erp-form-grid">
            <label className="span-2">
              <span>Şablon seçin</span>
              <select
                value={selectedTemplate}
                onChange={(event) => setSelectedTemplate(event.target.value)}
              >
                <option value="">Şablon kullanma</option>
                {templates.map((template) => (
                  <option key={template.key} value={template.key}>
                    {template.name} ({template.sectionCount} kısım)
                  </option>
                ))}
              </select>
              {selectedTemplate && (
                <small>
                  {templates.find((x) => x.key === selectedTemplate)?.description}
                </small>
              )}
            </label>
          </div>

          <div
            className="erp-form-actions"
            style={{ justifyContent: "flex-start" }}
          >
            <button
              type="button"
              className="erp-secondary-button"
              disabled={!selectedTemplate}
              onClick={applyTemplate}
            >
              Şablonu Listeye Ekle
            </button>
            <span style={{ marginLeft: "10px" }}>
              Mevcut kısımlar silinmez; yalnızca eksik olanlar eklenir.
            </span>
          </div>
        </div>
      )}

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Kısımlar</h2>

          {canEdit && (
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() =>
                setRows((current) => [...current, emptyRow(current.length + 1)])
              }
            >
              + Kısım Ekle
            </button>
          )}
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : rows.length === 0 ? (
          <div className="erp-empty-state">
            <p>
              Henüz kısım tanımlanmamış. Şablondan başlayabilir veya elle
              ekleyebilirsiniz.
            </p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th style={{ width: "60px" }}>Sıra</th>
                  <th>Kısım Adı *</th>
                  <th style={{ width: "120px" }}>Kod</th>
                  <th style={{ width: "100px" }}>Durum</th>
                  {canEdit && <th style={{ width: "80px" }}></th>}
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.key}>
                    <td>{index + 1}</td>
                    <td>
                      <input
                        type="text"
                        value={row.name}
                        disabled={!canEdit}
                        onChange={(event) =>
                          update(row.key, { name: event.target.value })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={row.code}
                        disabled={!canEdit}
                        onChange={(event) =>
                          update(row.key, { code: event.target.value })
                        }
                        placeholder="Ops."
                      />
                    </td>
                    <td>
                      <label className="erp-check-label">
                        <input
                          type="checkbox"
                          checked={row.isActive}
                          disabled={!canEdit}
                          onChange={(event) =>
                            update(row.key, { isActive: event.target.checked })
                          }
                        />
                        <span>Aktif</span>
                      </label>
                    </td>
                    {canEdit && (
                      <td>
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() =>
                            setRows((current) =>
                              current.filter((x) => x.key !== row.key)
                            )
                          }
                        >
                          Çıkar
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {canEdit && rows.length > 0 && (
          <div className="erp-form-actions">
            <span style={{ marginRight: "auto" }}>
              Listeden çıkarılan kısım silinmez, pasife çekilir — geçmiş
              hakedişlerin satırları ona bağlı olabilir.
            </span>
            <button
              type="button"
              className="erp-primary-button"
              disabled={saving}
              onClick={() => void save()}
            >
              {saving ? "Kaydediliyor..." : "Kısımları Kaydet"}
            </button>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
