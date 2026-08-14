"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  priceDifferenceService,
  PriceDifferenceCalculationType,
} from "@/services/price-difference.service";

export default function NewPriceProfilePage() {
  const router = useRouter();

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [saving, setSaving] =
    useState(false);

  const [error, setError] =
    useState("");

  const [form, setForm] = useState({
    companyId: "",
    projectId: "",
    profileName: "2026 Kamu Fiyat Farkı",
    baseYear: 2025,
    baseMonth: 7,
    currencyCode: "TRY",
    isDefault: true,
    isVatIncluded: false,
    formulaName:
      "Kamu Fiyat Farkı Formülü",
    notes: "",

    a: 0.167721,
    b1: 0.000554,
    b2: 0.002470,
    b3: 0.003519,
    b4: 0.000061,
    b5: 0,
    c: 0.820894,
  });

  useEffect(() => {
    async function load() {
      try {
        const companyRows =
          await companyService.getAll();

        setCompanies(companyRows);

        if (companyRows.length === 1) {
          setForm((current) => ({
            ...current,
            companyId:
              companyRows[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şirketler yüklenemedi."
        );
      }
    }

    void load();
  }, []);

  useEffect(() => {
    async function loadProjects() {
      if (!form.companyId) {
        setProjects([]);
        return;
      }

      const rows =
        await projectService.getAll(
          form.companyId
        );

      setProjects(rows);

      if (rows.length === 1) {
        setForm((current) => ({
          ...current,
          projectId:
            rows[0].id,
        }));
      }
    }

    void loadProjects();
  }, [form.companyId]);

  const coefficientTotal = useMemo(
    () =>
      form.a +
      form.b1 +
      form.b2 +
      form.b3 +
      form.b4 +
      form.b5 +
      form.c,
    [form]
  );

  function update(
    key: keyof typeof form,
    value: string | number | boolean
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }));
  }

  async function save(
    event: React.FormEvent
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");

    try {
      await priceDifferenceService.createProfile({
        companyId:
          form.companyId,

        projectId:
          form.projectId,

        profileName:
          form.profileName,

        calculationType:
          PriceDifferenceCalculationType
            .PublicContractFormula,

        baseYear:
          form.baseYear,

        baseMonth:
          form.baseMonth,

        currencyCode:
          form.currencyCode,

        isDefault:
          form.isDefault,

        isVatIncluded:
          form.isVatIncluded,

        formulaName:
          form.formulaName,

        notes:
          form.notes,

        a:
          form.a,

        b1:
          form.b1,

        b2:
          form.b2,

        b3:
          form.b3,

        b4:
          form.b4,

        b5:
          form.b5,

        c:
          form.c,
      });

      router.push(
        "/fiyat-farki"
      );

    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Profil oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Fiyat Farkı Profili"
      description="Proje bazlı fiyat farkı katsayı tanımı"
    >
      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <form
        className="erp-form-card"
        onSubmit={save}
      >
        <div className="erp-form-grid">

          <label>
            <span>Şirket *</span>

            <select
              required
              value={form.companyId}
              onChange={(e) =>
                update(
                  "companyId",
                  e.target.value
                )
              }
            >
              <option value="">
                Seçin
              </option>

              {companies.map(
                (company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.code} —
                    {" "}
                    {company.name}
                  </option>
                )
              )}
            </select>
          </label>


          <label>
            <span>Proje *</span>

            <select
              required
              value={form.projectId}
              onChange={(e) =>
                update(
                  "projectId",
                  e.target.value
                )
              }
            >
              <option value="">
                Seçin
              </option>

              {projects.map(
                (project) => (
                  <option
                    key={project.id}
                    value={project.id}
                  >
                    {project.code} —
                    {" "}
                    {project.name}
                  </option>
                )
              )}
            </select>
          </label>


          <label>
            <span>Profil Adı *</span>

            <input
              className="erp-input"
              value={form.profileName}
              onChange={(e) =>
                update(
                  "profileName",
                  e.target.value
                )
              }
            />
          </label>


          <label>
            <span>Baz Yıl</span>

            <input
              className="erp-input"
              type="number"
              value={form.baseYear}
              onChange={(e) =>
                update(
                  "baseYear",
                  Number(e.target.value)
                )
              }
            />
          </label>


          <label>
            <span>Baz Ay</span>

            <input
              className="erp-input"
              type="number"
              value={form.baseMonth}
              onChange={(e) =>
                update(
                  "baseMonth",
                  Number(e.target.value)
                )
              }
            />
          </label>


          {(
            [
              ["a","A"],
              ["b1","B1 İşçilik"],
              ["b2","B2 Akaryakıt"],
              ["b3","B3 Malzeme"],
              ["b4","B4 Makine"],
              ["b5","B5"],
              ["c","C Diğer"],
            ] as const
          ).map(
            ([key,label]) => (
              <label key={key}>
                <span>{label}</span>

                <input
                  className="erp-input"
                  type="number"
                  step="0.000001"
                  value={form[key]}
                  onChange={(e) =>
                    update(
                      key,
                      Number(
                        e.target.value
                      )
                    )
                  }
                />
              </label>
            )
          )}

          <div>
            <span>
              Katsayı Toplamı
            </span>

            <strong>
              {coefficientTotal.toFixed(6)}
            </strong>
          </div>

        </div>


        <label className="erp-check">
          <input
            type="checkbox"
            checked={form.isDefault}
            onChange={(e) =>
              update(
                "isDefault",
                e.target.checked
              )
            }
          />

          Varsayılan profil
        </label>


        <div className="erp-actions">

          <button
            type="submit"
            disabled={
              saving ||
              Math.abs(
                coefficientTotal - 1
              ) > 0.0001
            }
          >
            {saving
              ? "Kaydediliyor..."
              : "Profili Kaydet"}
          </button>

        </div>

      </form>
    </ErpShell>
  );
}
