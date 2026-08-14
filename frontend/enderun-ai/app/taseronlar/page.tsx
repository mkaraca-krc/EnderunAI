"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";
import {
  projectBoqService,
  type ProjectHakedisSection,
} from "@/services/project-boq.service";
import {
  subcontractorService,
  SubcontractorContractType,
  SubcontractorResponsibility,
  type SubcontractorContractListItem,
} from "@/services/subcontractor.service";

/** CurrentAccountRoles.Subcontractor bit değeri. */
const SUBCONTRACTOR_ROLE_FLAG = 4;

type ScopeKey =
  | "mealResponsibility"
  | "accommodationResponsibility"
  | "socialSecurityResponsibility"
  | "materialResponsibility"
  | "ohsResponsibility";

/**
 * Kapsam tikleri. Etiketler sözleşme dilinde yazıldı: kullanıcı
 * "yükümlülük kimde" diye düşünmüyor, "yemeği kim veriyor" diye
 * düşünüyor.
 */
const scopeFields: Array<{
  key: ScopeKey;
  label: string;
  usLabel: string;
  subcontractorLabel: string;
  hint: string;
}> = [
  {
    key: "mealResponsibility",
    label: "Yemek",
    usLabel: "Bize ait",
    subcontractorLabel: "Taşerona ait",
    hint: "Bize aitse puantaj adetleri hakedişten kesilir ve yansıtılır.",
  },
  {
    key: "accommodationResponsibility",
    label: "Konaklama",
    usLabel: "Bize ait",
    subcontractorLabel: "Taşerona ait",
    hint: "Bize aitse konaklama adetleri hakedişten kesilir ve yansıtılır.",
  },
  {
    key: "socialSecurityResponsibility",
    label: "Sigorta / SGK",
    usLabel: "Bizde",
    subcontractorLabel: "Taşeronda",
    hint: "Bizdeyse işçiler bizim bordromuzda; bordro maliyeti hakedişten kesilir.",
  },
  {
    key: "materialResponsibility",
    label: "Malzeme",
    usLabel: "Bizden",
    subcontractorLabel: "Taşerondan",
    hint: "Bizdense verdiğimiz malzemenin bedeli hakedişten kesilir.",
  },
  {
    key: "ohsResponsibility",
    label: "İSG",
    usLabel: "Bize ait (yansıtmalı)",
    subcontractorLabel: "Taşerona ait",
    hint: "Bize aitse işveren hakedişimizden kesilen İSG payı işçi oranıyla yansıtılır.",
  },
];

type SectionRow = {
  projectHakedisSectionId: string;
  sectionAmount: string;
};

type FormState = {
  companyId: string;
  currentAccountId: string;
  projectId: string;
  projectSiteId: string;
  contractNumber: string;
  workDescription: string;
  contractType: string;
  contractAmount: string;
  startDate: string;
  endDate: string;
  retentionRate: string;
  withholdingNumerator: string;
  withholdingDenominator: string;
  notes: string;
} & Record<ScopeKey, string>;

const emptyForm: FormState = {
  companyId: "",
  currentAccountId: "",
  projectId: "",
  projectSiteId: "",
  contractNumber: "",
  workDescription: "",
  contractType: String(SubcontractorContractType.UnitPrice),
  contractAmount: "",
  startDate: new Date().toISOString().slice(0, 10),
  endDate: "",
  retentionRate: "5",
  withholdingNumerator: "4",
  withholdingDenominator: "10",
  notes: "",
  mealResponsibility: String(SubcontractorResponsibility.Subcontractor),
  accommodationResponsibility: String(SubcontractorResponsibility.Subcontractor),
  socialSecurityResponsibility: String(SubcontractorResponsibility.Subcontractor),
  materialResponsibility: String(SubcontractorResponsibility.Subcontractor),
  ohsResponsibility: String(SubcontractorResponsibility.Subcontractor),
};

function money(value: number, currency = "TRY") {
  return currencyMoney(value, currency);
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

export default function SubcontractorsPage() {
  const [items, setItems] = useState<SubcontractorContractListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);
  const [sections, setSections] = useState<ProjectHakedisSection[]>([]);
  const [sectionRows, setSectionRows] = useState<SectionRow[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [denied, setDenied] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);

  const [projectFilter, setProjectFilter] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);


  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const [companyResult, accountResult, projectResult] = await Promise.all([
          companyService.getAll(),
          currentAccountService.getAll(),
          projectService.getAll(),
        ]);

        if (cancelled) return;

        setCompanies(companyResult.filter((x) => x.isActive !== false));
        // Yalnızca "taşeron" işaretli cariler seçilebilir; sunucu da
        // aynı kuralı uyguluyor, buradaki filtre kullanıcıyı reddedilecek
        // bir seçime hiç götürmemek için.
        setAccounts(
          accountResult.filter(
            (x) => (x.roles & SUBCONTRACTOR_ROLE_FLAG) === SUBCONTRACTOR_ROLE_FLAG
          )
        );
        setProjects(projectResult);
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  // Liste; refreshKey kaydetmeden sonra yeniden çekmek için.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await subcontractorService.list(
          projectFilter ? { projectId: projectFilter } : undefined
        );

        if (cancelled) return;

        setItems(result);
        setDenied(false);
      } catch (loadError) {
        if (cancelled) return;

        if (loadError instanceof ApiError && loadError.status === 403) {
          setDenied(true);
          return;
        }

        setError(errorMessage(loadError));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [projectFilter, refreshKey]);

  // Proje seçilince o projenin şantiyeleri ve icmal kısımları gelir.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      if (!form.projectId) {
        if (!cancelled) {
          setSites([]);
          setSections([]);
        }
        return;
      }

      try {
        const [siteResult, sectionResult] = await Promise.all([
          projectSiteService.getAll(form.projectId),
          projectBoqService.getSections(form.projectId),
        ]);

        if (cancelled) return;

        setSites(siteResult);
        setSections(sectionResult);
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [form.projectId]);

  const projectAccounts = useMemo(
    () =>
      accounts.filter((x) => !form.companyId || x.companyId === form.companyId),
    [accounts, form.companyId]
  );

  const formProjects = useMemo(
    () =>
      projects.filter((x) => !form.companyId || x.companyId === form.companyId),
    [projects, form.companyId]
  );

  const isLumpSum =
    Number(form.contractType) === SubcontractorContractType.LumpSum;

  const sectionTotal = sectionRows.reduce(
    (sum, row) => sum + (Number(row.sectionAmount.replace(",", ".")) || 0),
    0
  );

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function openCreate() {
    const defaultCompanyId = companies.length === 1 ? companies[0].id : "";
    setEditingId(null);
    setForm({ ...emptyForm, companyId: defaultCompanyId });
    setSectionRows([]);
    setFormOpen(true);
    setError("");
    setNotice("");
  }

  async function openEdit(id: string) {
    setError("");
    setNotice("");

    try {
      const detail = await subcontractorService.getById(id);

      setEditingId(id);
      setForm({
        companyId: detail.companyId,
        currentAccountId: detail.currentAccountId,
        projectId: detail.projectId,
        projectSiteId: detail.projectSiteId ?? "",
        contractNumber: detail.contractNumber,
        workDescription: detail.workDescription,
        contractType: String(detail.contractType),
        contractAmount: String(detail.contractAmount),
        startDate: detail.startDate.slice(0, 10),
        endDate: detail.endDate ? detail.endDate.slice(0, 10) : "",
        retentionRate: String(detail.retentionRate),
        withholdingNumerator: String(detail.withholdingNumerator),
        withholdingDenominator: String(detail.withholdingDenominator),
        notes: detail.notes ?? "",
        mealResponsibility: String(detail.mealResponsibility),
        accommodationResponsibility: String(detail.accommodationResponsibility),
        socialSecurityResponsibility: String(detail.socialSecurityResponsibility),
        materialResponsibility: String(detail.materialResponsibility),
        ohsResponsibility: String(detail.ohsResponsibility),
      });
      setSectionRows(
        detail.sections.map((x) => ({
          projectHakedisSectionId: x.projectHakedisSectionId,
          sectionAmount: String(x.sectionAmount),
        }))
      );
      setFormOpen(true);
    } catch (loadError) {
      setError(errorMessage(loadError));
    }
  }

  function toggleSection(sectionId: string) {
    setSectionRows((current) =>
      current.some((x) => x.projectHakedisSectionId === sectionId)
        ? current.filter((x) => x.projectHakedisSectionId !== sectionId)
        : [...current, { projectHakedisSectionId: sectionId, sectionAmount: "0" }]
    );
  }

  function setSectionAmount(sectionId: string, value: string) {
    setSectionRows((current) =>
      current.map((row) =>
        row.projectHakedisSectionId === sectionId
          ? { ...row, sectionAmount: value }
          : row
      )
    );
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setNotice("");

    const payload = {
      companyId: form.companyId,
      currentAccountId: form.currentAccountId,
      projectId: form.projectId,
      projectSiteId: form.projectSiteId || null,
      contractNumber: form.contractNumber.trim(),
      workDescription: form.workDescription.trim(),
      contractType: Number(form.contractType),
      contractAmount: Number(form.contractAmount.replace(",", ".")) || 0,
      currencyCode: "TRY",
      startDate: form.startDate,
      endDate: form.endDate || null,
      retentionRate: Number(form.retentionRate.replace(",", ".")) || 0,
      withholdingNumerator: Number(form.withholdingNumerator) || 0,
      withholdingDenominator: Number(form.withholdingDenominator) || 0,
      mealResponsibility: Number(form.mealResponsibility),
      accommodationResponsibility: Number(form.accommodationResponsibility),
      socialSecurityResponsibility: Number(form.socialSecurityResponsibility),
      materialResponsibility: Number(form.materialResponsibility),
      ohsResponsibility: Number(form.ohsResponsibility),
      notes: form.notes.trim() || null,
      sections: sectionRows.map((row, index) => ({
        projectHakedisSectionId: row.projectHakedisSectionId,
        sectionAmount: Number(row.sectionAmount.replace(",", ".")) || 0,
        order: index + 1,
      })),
    };

    try {
      if (editingId) {
        await subcontractorService.update(editingId, payload);
        setNotice("Taşeron sözleşmesi güncellendi.");
      } else {
        await subcontractorService.create(payload);
        setNotice("Taşeron sözleşmesi oluşturuldu.");
      }

      setFormOpen(false);
      setRefreshKey((current) => current + 1);
    } catch (saveError) {
      setError(errorMessage(saveError));
    } finally {
      setSaving(false);
    }
  }

  if (denied) {
    return (
      <ErpShell design="redwood" title="Taşeronlar">
        <main style={{ padding: 24 }}>
          <div style={box}>
            Taşeron sözleşmelerini görme yetkiniz yok.
          </div>
        </main>
      </ErpShell>
    );
  }

  return (
    <ErpShell design="redwood" title="Taşeronlar">
      <main style={{ padding: 24, display: "grid", gap: 18 }}>
        <section style={topBar}>
          <div>
            <h1 style={{ margin: 0, fontSize: 28 }}>Taşeron Sözleşmeleri</h1>
            <p style={{ margin: "6px 0 0", color: "var(--erp-muted)" }}>
              Kapsam tikleri hakedişin kesinti kalemlerini belirler; hakediş
              bunları sözleşmeden okur.
            </p>
          </div>

          <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
            <select
              value={projectFilter}
              onChange={(event) => setProjectFilter(event.target.value)}
              style={input}
            >
              <option value="">Tüm projeler</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.name}
                </option>
              ))}
            </select>

            {/* Sözleşme kapsamı ve hakediş durumu başka kullanıcılarca
                değiştiriliyor; refreshKey vardı ama düğmesi yoktu. */}
            <button
              type="button"
              onClick={() => setRefreshKey((value) => value + 1)}
              style={smallButton}
              disabled={loading}
            >
              Yenile
            </button>

            <button type="button" onClick={openCreate} style={primaryButton}>
              Yeni Sözleşme
            </button>
          </div>
        </section>

        {error && <div style={{ ...box, color: "var(--color-semantic-danger)" }}>{error}</div>}
        {notice && <div style={{ ...box, color: "var(--color-semantic-success)" }}>{notice}</div>}

        <section style={{ ...card, padding: 0, overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 980 }}>
            <thead>
              <tr style={{ background: "var(--erp-bg)" }}>
                {[
                  "Sözleşme No",
                  "Taşeron",
                  "Proje / Şantiye",
                  "İş Tanımı",
                  "Tip",
                  "Bedel",
                  "Kapsam",
                  "Durum",
                  "",
                ].map((title) => (
                  <th key={title} style={th}>
                    {title}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {!loading && items.length === 0 && (
                <tr>
                  <td colSpan={9} style={{ ...td, textAlign: "center", color: "var(--erp-muted)" }}>
                    Taşeron sözleşmesi bulunamadı.
                  </td>
                </tr>
              )}

              {items.map((item) => {
                const ours = scopeFields
                  .filter(
                    (field) =>
                      item[field.key] === SubcontractorResponsibility.Us
                  )
                  .map((field) => field.label);

                return (
                  <tr key={item.id}>
                    <td style={td}>
                      <strong>{item.contractNumber}</strong>
                    </td>
                    <td style={td}>{item.subcontractorTitle}</td>
                    <td style={td}>
                      {item.projectName}
                      {item.projectSiteName && (
                        <div style={{ fontSize: 12, color: "var(--erp-muted)" }}>
                          {item.projectSiteName}
                        </div>
                      )}
                    </td>
                    <td style={td}>{item.workDescription}</td>
                    <td style={td}>{item.contractTypeName}</td>
                    <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                      {money(item.contractAmount, item.currencyCode)}
                    </td>
                    <td style={td}>
                      {ours.length === 0 ? (
                        <span style={{ color: "var(--erp-muted)" }}>tamamı taşeronda</span>
                      ) : (
                        <span>bizde: {ours.join(", ")}</span>
                      )}
                    </td>
                    <td style={td}>{item.statusName}</td>
                    <td style={td}>
                      <div style={{ display: "flex", gap: 6 }}>
                        <Link href={`/taseronlar/${item.id}`} style={linkButton}>
                          Aç
                        </Link>
                        <button
                          type="button"
                          onClick={() => void openEdit(item.id)}
                          style={smallButton}
                        >
                          Düzenle
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>

        {formOpen && (
          <form onSubmit={handleSubmit} style={{ ...card, display: "grid", gap: 16 }}>
            <h2 style={{ margin: 0, fontSize: 20 }}>
              {editingId ? "Sözleşmeyi Düzenle" : "Yeni Taşeron Sözleşmesi"}
            </h2>

            <div style={grid3}>
              <label style={fieldLabel}>
                Şirket
                <select
                  value={form.companyId}
                  onChange={(event) => update("companyId", event.target.value)}
                  style={input}
                  disabled={Boolean(editingId)}
                  required
                >
                  <option value="">Seçiniz</option>
                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>
                      {company.name}
                    </option>
                  ))}
                </select>
              </label>

              <label style={fieldLabel}>
                Taşeron Carisi
                <select
                  value={form.currentAccountId}
                  onChange={(event) =>
                    update("currentAccountId", event.target.value)
                  }
                  style={input}
                  disabled={Boolean(editingId)}
                  required
                >
                  <option value="">Seçiniz</option>
                  {projectAccounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.title}
                    </option>
                  ))}
                </select>
                {projectAccounts.length === 0 && form.companyId && (
                  <small style={{ color: "var(--color-semantic-warning)" }}>
                    Bu şirkette &quot;taşeron&quot; işaretli cari yok. Cari kartında
                    Taşeron rolünü işaretleyin.
                  </small>
                )}
              </label>

              <label style={fieldLabel}>
                Proje
                <select
                  value={form.projectId}
                  onChange={(event) => update("projectId", event.target.value)}
                  style={input}
                  disabled={Boolean(editingId)}
                  required
                >
                  <option value="">Seçiniz</option>
                  {formProjects.map((project) => (
                    <option key={project.id} value={project.id}>
                      {project.name}
                    </option>
                  ))}
                </select>
              </label>

              <label style={fieldLabel}>
                Şantiye (isteğe bağlı)
                <select
                  value={form.projectSiteId}
                  onChange={(event) =>
                    update("projectSiteId", event.target.value)
                  }
                  style={input}
                >
                  <option value="">Proje geneli</option>
                  {sites.map((site) => (
                    <option key={site.id} value={site.id}>
                      {site.name}
                    </option>
                  ))}
                </select>
              </label>

              <label style={fieldLabel}>
                Sözleşme No
                <input
                  value={form.contractNumber}
                  onChange={(event) =>
                    update("contractNumber", event.target.value)
                  }
                  style={input}
                  required
                />
              </label>

              <label style={fieldLabel}>
                Sözleşme Tipi
                <select
                  value={form.contractType}
                  onChange={(event) => update("contractType", event.target.value)}
                  style={input}
                >
                  <option value={SubcontractorContractType.UnitPrice}>
                    Birim fiyatlı
                  </option>
                  <option value={SubcontractorContractType.LumpSum}>
                    Götürü
                  </option>
                </select>
              </label>
            </div>

            <label style={fieldLabel}>
              İş Tanımı
              <input
                value={form.workDescription}
                onChange={(event) =>
                  update("workDescription", event.target.value)
                }
                placeholder="Kaba elektrik tesisatı"
                style={input}
                required
              />
            </label>

            <div style={grid3}>
              <label style={fieldLabel}>
                Sözleşme Bedeli
                <input
                  value={form.contractAmount}
                  onChange={(event) =>
                    update("contractAmount", event.target.value)
                  }
                  inputMode="decimal"
                  style={input}
                  required
                />
              </label>

              <label style={fieldLabel}>
                Başlangıç
                <input
                  type="date"
                  value={form.startDate}
                  onChange={(event) => update("startDate", event.target.value)}
                  style={input}
                  required
                />
              </label>

              <label style={fieldLabel}>
                Bitiş
                <input
                  type="date"
                  value={form.endDate}
                  onChange={(event) => update("endDate", event.target.value)}
                  style={input}
                />
              </label>

              <label style={fieldLabel}>
                Teminat Oranı (%)
                <input
                  value={form.retentionRate}
                  onChange={(event) =>
                    update("retentionRate", event.target.value)
                  }
                  inputMode="decimal"
                  style={input}
                />
              </label>

              <label style={fieldLabel}>
                Tevkifat Payı
                <input
                  value={form.withholdingNumerator}
                  onChange={(event) =>
                    update("withholdingNumerator", event.target.value)
                  }
                  inputMode="numeric"
                  style={input}
                />
                <small style={{ color: "var(--erp-muted)" }}>
                  Yapım işleri KDV tevkifatı; 0/0 girilirse tevkifat yok.
                </small>
              </label>

              <label style={fieldLabel}>
                Tevkifat Paydası
                <input
                  value={form.withholdingDenominator}
                  onChange={(event) =>
                    update("withholdingDenominator", event.target.value)
                  }
                  inputMode="numeric"
                  style={input}
                />
              </label>
            </div>

            <div>
              <h3 style={{ margin: "0 0 4px", fontSize: 16 }}>Sözleşme Kapsamı</h3>
              <p style={{ margin: "0 0 12px", color: "var(--erp-muted)", fontSize: 13 }}>
                Bir kalem bizdeyse masrafı biz yaptığımız için taşeron
                hakedişinden kesilir; taşerondaysa hakedişte hiç görünmez.
              </p>

              <div style={grid3}>
                {scopeFields.map((field) => (
                  <label key={field.key} style={fieldLabel}>
                    {field.label}
                    <select
                      value={form[field.key]}
                      onChange={(event) =>
                        update(field.key, event.target.value)
                      }
                      style={input}
                    >
                      <option value={SubcontractorResponsibility.Us}>
                        {field.usLabel}
                      </option>
                      <option value={SubcontractorResponsibility.Subcontractor}>
                        {field.subcontractorLabel}
                      </option>
                    </select>
                    <small style={{ color: "var(--erp-muted)", fontSize: 12 }}>
                      {field.hint}
                    </small>
                  </label>
                ))}
              </div>
            </div>

            <div>
              <h3 style={{ margin: "0 0 4px", fontSize: 16 }}>İcmal Kısımları</h3>
              <p style={{ margin: "0 0 12px", color: "var(--erp-muted)", fontSize: 13 }}>
                {isLumpSum
                  ? "Götürü sözleşmede ilerleme kısım bazında girilir; en az bir kısım seçilmelidir."
                  : "Maliyet ve kâr analizi bu kısımlar üzerinden yürür."}
              </p>

              {sections.length === 0 ? (
                <div style={{ color: "var(--erp-muted)" }}>
                  {form.projectId
                    ? "Bu projede tanımlı icmal kısmı yok."
                    : "Önce proje seçin."}
                </div>
              ) : (
                <div style={{ display: "grid", gap: 8 }}>
                  {sections.map((section) => {
                    const row = sectionRows.find(
                      (x) => x.projectHakedisSectionId === section.id
                    );

                    return (
                      <div
                        key={section.id}
                        style={{
                          display: "grid",
                          gridTemplateColumns: "24px minmax(0,1fr) 180px",
                          gap: 10,
                          alignItems: "center",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={Boolean(row)}
                          onChange={() => toggleSection(section.id)}
                        />
                        <span>{section.name}</span>
                        <input
                          value={row?.sectionAmount ?? ""}
                          onChange={(event) =>
                            setSectionAmount(section.id, event.target.value)
                          }
                          placeholder="Kısım bedeli"
                          inputMode="decimal"
                          style={input}
                          disabled={!row}
                        />
                      </div>
                    );
                  })}

                  <div style={{ color: "var(--erp-muted)", fontSize: 13 }}>
                    Kısım bedelleri toplamı: <strong>{money(sectionTotal)}</strong>
                    {sectionTotal >
                      (Number(form.contractAmount.replace(",", ".")) || 0) && (
                      <span style={{ color: "var(--color-semantic-danger)" }}>
                        {" "}
                        — sözleşme bedelini aşıyor
                      </span>
                    )}
                  </div>
                </div>
              )}
            </div>

            <label style={fieldLabel}>
              Not
              <input
                value={form.notes}
                onChange={(event) => update("notes", event.target.value)}
                style={input}
              />
            </label>

            <div style={{ display: "flex", gap: 10 }}>
              <button type="submit" style={primaryButton} disabled={saving}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button
                type="button"
                onClick={() => setFormOpen(false)}
                style={smallButton}
              >
                Vazgeç
              </button>
            </div>
          </form>
        )}
      </main>
    </ErpShell>
  );
}

const card = { background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 16, padding: 18, boxShadow: "0 8px 24px rgba(15,23,42,.05)" } as const;
const topBar = { display: "flex", justifyContent: "space-between", alignItems: "center", gap: 18, flexWrap: "wrap", background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 16, padding: 18 } as const;
const box = { background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 12, padding: 14 } as const;
const input = { minHeight: 42, border: "1px solid var(--erp-border)", borderRadius: 10, padding: "8px 11px", background: "var(--erp-panel)", color: "var(--erp-text)" } as const;
const fieldLabel = { display: "grid", gap: 6, fontSize: 13, color: "var(--erp-muted)" } as const;
const grid3 = { display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(240px,1fr))", gap: 12 } as const;
const th = { padding: "13px 14px", textAlign: "left", color: "var(--erp-muted)", fontSize: 13, borderBottom: "1px solid var(--erp-border)" } as const;
const td = { padding: "13px 14px", borderBottom: "1px solid var(--erp-border)" } as const;
const primaryButton = { height: 42, padding: "0 18px", borderRadius: 10, border: "none", background: "var(--erp-primary)", color: "var(--color-on-brand)", fontWeight: 600, cursor: "pointer" } as const;
const linkButton = { display: "inline-flex", alignItems: "center", height: 38, padding: "0 14px", borderRadius: 10, border: "1px solid var(--erp-border)", background: "var(--erp-panel)", color: "var(--erp-text)", fontWeight: 600, textDecoration: "none" } as const;
const smallButton = { height: 38, padding: "0 14px", borderRadius: 10, border: "1px solid var(--erp-border)", background: "var(--erp-panel)", color: "var(--erp-text)", fontWeight: 600, cursor: "pointer" } as const;
