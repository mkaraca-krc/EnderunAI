"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  CompanyListItem,
  companyService,
} from "@/services/company.service";

import {
  PersonnelListItem,
  personnelService,
} from "@/services/personnel.service";

import {
  CreateSalaryDefinitionRequest,
  SalaryDefinition,
  UpdateSalaryDefinitionRequest,
  hrSalaryService,
} from "@/services/hr-salary.service";

type SalaryForm = {
  companyId: string;
  personnelId: string;
  effectiveStartDate: string;
  effectiveEndDate: string;
  grossSalary: string;
  netSalary: string;
  dailyRate: string;
  hourlyRate: string;
  overtimeMultiplier: string;
  sundayMultiplier: string;
  publicHolidayMultiplier: string;
  currencyCode: string;
  description: string;
};

const today =
  new Date()
    .toISOString()
    .slice(0, 10);

const emptyForm: SalaryForm = {
  companyId: "",
  personnelId: "",
  effectiveStartDate: today,
  effectiveEndDate: "",
  grossSalary: "",
  netSalary: "",
  dailyRate: "",
  hourlyRate: "",
  overtimeMultiplier: "1.50",
  sundayMultiplier: "2.00",
  publicHolidayMultiplier: "2.00",
  currencyCode: "TRY",
  description: "",
};

const panelStyle = {
  background: "#ffffff",
  border: "1px solid #e2e8f0",
  borderRadius: "16px",
  boxShadow:
    "0 8px 24px rgba(15, 23, 42, 0.05)",
};

const inputStyle = {
  width: "100%",
  minHeight: "42px",
  border: "1px solid #cbd5e1",
  borderRadius: "10px",
  padding: "8px 11px",
  background: "#ffffff",
  color: "#0f172a",
};

function money(
  value: number,
  currencyCode = "TRY"
) {
  return new Intl.NumberFormat(
    "tr-TR",
    {
      style: "currency",
      currency: currencyCode || "TRY",
      maximumFractionDigits: 2,
    }
  ).format(value ?? 0);
}

function dateValue(
  value?: string | null
) {
  return value
    ? value.slice(0, 10)
    : "";
}

function parseNumber(
  value: string
) {
  if (!value.trim()) {
    return 0;
  }

  const normalized =
    value
      .replace(/\./g, "")
      .replace(",", ".");

  const result =
    Number(normalized);

  return Number.isFinite(result)
    ? result
    : 0;
}

function errorMessage(
  error: unknown
) {
  return error instanceof Error
    ? error.message
    : "İşlem sırasında beklenmeyen bir hata oluştu.";
}

export default function SalaryCardsPage() {
  const [
    companies,
    setCompanies,
  ] = useState<CompanyListItem[]>([]);

  const [
    personnel,
    setPersonnel,
  ] = useState<PersonnelListItem[]>([]);

  const [
    records,
    setRecords,
  ] = useState<SalaryDefinition[]>([]);

  const [
    companyFilter,
    setCompanyFilter,
  ] = useState("");

  const [
    personnelFilter,
    setPersonnelFilter,
  ] = useState("");

  const [
    effectiveDate,
    setEffectiveDate,
  ] = useState("");

  const [
    form,
    setForm,
  ] = useState<SalaryForm>(
    emptyForm
  );

  const [
    editingId,
    setEditingId,
  ] = useState<string | null>(
    null
  );

  const [
    showForm,
    setShowForm,
  ] = useState(false);

  const [
    loading,
    setLoading,
  ] = useState(false);

  const [
    saving,
    setSaving,
  ] = useState(false);

  const [
    actionId,
    setActionId,
  ] = useState<string | null>(
    null
  );

  const [
    error,
    setError,
  ] = useState("");

  const [
    success,
    setSuccess,
  ] = useState("");

  const personnelMap =
    useMemo(
      () =>
        new Map(
          personnel.map(
            (item) => [
              item.id,
              item,
            ]
          )
        ),
      [personnel]
    );

  const formPersonnel =
    useMemo(
      () =>
        personnel.filter(
          (item) =>
            !form.companyId ||
            item.companyId ===
              form.companyId
        ),
      [
        form.companyId,
        personnel,
      ]
    );

  const filterPersonnel =
    useMemo(
      () =>
        personnel.filter(
          (item) =>
            !companyFilter ||
            item.companyId ===
              companyFilter
        ),
      [
        companyFilter,
        personnel,
      ]
    );

  const activeRecords =
    useMemo(() => {
      const now = new Date(
        `${today}T00:00:00`
      );

      return records.filter(
        (record) => {
          const start =
            new Date(
              record
                .effectiveStartDate
            );

          const end =
            record.effectiveEndDate
              ? new Date(
                  record
                    .effectiveEndDate
                )
              : null;

          return (
            start <= now &&
            (!end || end >= now)
          );
        }
      );
    }, [records]);

  const totals =
    useMemo(
      () => ({
        count: records.length,
        activeCount:
          activeRecords.length,
        totalGross:
          activeRecords.reduce(
            (sum, item) =>
              sum +
              Number(
                item.grossSalary
              ),
            0
          ),
        totalNet:
          activeRecords.reduce(
            (sum, item) =>
              sum +
              Number(
                item.netSalary
              ),
            0
          ),
      }),
      [
        records,
        activeRecords,
      ]
    );

  const loadInitial =
    useCallback(async () => {
      setLoading(true);
      setError("");

      try {
        const [
          companyRows,
          personnelRows,
        ] = await Promise.all([
          companyService.getAll(),
          personnelService.getAll(),
        ]);

        setCompanies(
          companyRows
        );

        setPersonnel(
          personnelRows
        );

        const firstCompany =
          companyRows.find(
            (item) =>
              item.isActive !==
              false
          ) ??
          companyRows[0];

        if (firstCompany) {
          setCompanyFilter(
            (current) =>
              current ||
              firstCompany.id
          );

          setForm(
            (current) => ({
              ...current,
              companyId:
                current.companyId ||
                firstCompany.id,
            })
          );
        }
      } catch (loadError) {
        setError(
          errorMessage(
            loadError
          )
        );
      } finally {
        setLoading(false);
      }
    }, []);

  const loadRecords =
    useCallback(async () => {
      if (!companyFilter) {
        setRecords([]);
        return;
      }

      setLoading(true);
      setError("");

      try {
        const rows =
          await hrSalaryService.getAll(
            {
              companyId:
                companyFilter,
              personnelId:
                personnelFilter ||
                undefined,
              effectiveDate:
                effectiveDate ||
                undefined,
            }
          );

        setRecords(rows);
      } catch (loadError) {
        setError(
          errorMessage(
            loadError
          )
        );
      } finally {
        setLoading(false);
      }
    }, [
      companyFilter,
      personnelFilter,
      effectiveDate,
    ]);

  useEffect(() => {
    void loadInitial();
  }, [loadInitial]);

  useEffect(() => {
    void loadRecords();
  }, [loadRecords]);

  function newRecord() {
    const firstPerson =
      personnel.find(
        (item) =>
          item.companyId ===
          companyFilter
      );

    setEditingId(null);

    setForm({
      ...emptyForm,
      companyId:
        companyFilter,
      personnelId:
        firstPerson?.id ?? "",
    });

    setShowForm(true);
    setError("");
    setSuccess("");
  }

  function editRecord(
    record: SalaryDefinition
  ) {
    setEditingId(record.id);

    setForm({
      companyId:
        record.companyId,
      personnelId:
        record.personnelId,
      effectiveStartDate:
        dateValue(
          record
            .effectiveStartDate
        ),
      effectiveEndDate:
        dateValue(
          record
            .effectiveEndDate
        ),
      grossSalary:
        String(
          record.grossSalary
        ),
      netSalary:
        String(
          record.netSalary
        ),
      dailyRate:
        String(
          record.dailyRate
        ),
      hourlyRate:
        String(
          record.hourlyRate
        ),
      overtimeMultiplier:
        String(
          record
            .overtimeMultiplier
        ),
      sundayMultiplier:
        String(
          record
            .sundayMultiplier
        ),
      publicHolidayMultiplier:
        String(
          record
            .publicHolidayMultiplier
        ),
      currencyCode:
        record.currencyCode,
      description:
        record.description ?? "",
    });

    setShowForm(true);
    setError("");
    setSuccess("");
  }

  function closeForm() {
    if (saving) {
      return;
    }

    setShowForm(false);
    setEditingId(null);
    setForm(emptyForm);
  }

  async function submit(
    event: FormEvent
  ) {
    event.preventDefault();

    if (!form.companyId) {
      setError(
        "Şirket seçilmelidir."
      );
      return;
    }

    if (!form.personnelId) {
      setError(
        "Personel seçilmelidir."
      );
      return;
    }

    if (
      !form.effectiveStartDate
    ) {
      setError(
        "Geçerlilik başlangıç tarihi zorunludur."
      );
      return;
    }

    const common = {
      effectiveStartDate:
        form
          .effectiveStartDate,
      effectiveEndDate:
        form.effectiveEndDate ||
        null,
      grossSalary:
        parseNumber(
          form.grossSalary
        ),
      netSalary:
        parseNumber(
          form.netSalary
        ),
      dailyRate:
        parseNumber(
          form.dailyRate
        ),
      hourlyRate:
        parseNumber(
          form.hourlyRate
        ),
      overtimeMultiplier:
        parseNumber(
          form
            .overtimeMultiplier
        ),
      sundayMultiplier:
        parseNumber(
          form
            .sundayMultiplier
        ),
      publicHolidayMultiplier:
        parseNumber(
          form
            .publicHolidayMultiplier
        ),
      currencyCode:
        form.currencyCode
          .trim()
          .toUpperCase(),
      description:
        form.description
          .trim() || null,
    };

    if (
      common.grossSalary <= 0 &&
      common.netSalary <= 0 &&
      common.dailyRate <= 0 &&
      common.hourlyRate <= 0
    ) {
      setError(
        "En az bir maaş veya ücret alanı sıfırdan büyük olmalıdır."
      );
      return;
    }

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (editingId) {
        const payload:
          UpdateSalaryDefinitionRequest =
          common;

        await hrSalaryService.update(
          editingId,
          payload
        );

        setSuccess(
          "Maaş kartı güncellendi."
        );
      } else {
        const payload:
          CreateSalaryDefinitionRequest =
          {
            companyId:
              form.companyId,
            personnelId:
              form.personnelId,
            ...common,
          };

        await hrSalaryService.create(
          payload
        );

        setSuccess(
          "Maaş kartı oluşturuldu."
        );
      }

      setShowForm(false);
      setEditingId(null);
      setForm(emptyForm);

      await loadRecords();
    } catch (saveError) {
      setError(
        errorMessage(
          saveError
        )
      );
    } finally {
      setSaving(false);
    }
  }

  async function deleteRecord(
    record: SalaryDefinition
  ) {
    const employee =
      personnelMap.get(
        record.personnelId
      );

    const confirmed =
      window.confirm(
        `${employee?.fullName ?? "Personel"} maaş kartı silinsin mi?`
      );

    if (!confirmed) {
      return;
    }

    setActionId(record.id);
    setError("");
    setSuccess("");

    try {
      await hrSalaryService.delete(
        record.id
      );

      setSuccess(
        "Maaş kartı silindi."
      );

      await loadRecords();
    } catch (deleteError) {
      setError(
        errorMessage(
          deleteError
        )
      );
    } finally {
      setActionId(null);
    }
  }

  return (
    <ErpShell
      title="Maaş Kartları"
      description="Personel maaş, günlük ücret, saatlik ücret ve mesai katsayılarını yönetin."
    >
      <div
        style={{
          display: "grid",
          gap: "18px",
        }}
      >
        {(error || success) && (
          <div
            style={{
              ...panelStyle,
              padding: "14px 16px",
              color: error
                ? "#b91c1c"
                : "#166534",
              background: error
                ? "#fef2f2"
                : "#f0fdf4",
              borderColor: error
                ? "#fecaca"
                : "#bbf7d0",
              fontWeight: 700,
            }}
          >
            {error || success}
          </div>
        )}

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(4, minmax(0, 1fr))",
            gap: "14px",
          }}
        >
          {[
            [
              "Toplam Kart",
              totals.count,
            ],
            [
              "Aktif Kart",
              totals.activeCount,
            ],
            [
              "Toplam Brüt",
              money(
                totals.totalGross
              ),
            ],
            [
              "Toplam Net",
              money(
                totals.totalNet
              ),
            ],
          ].map(
            ([label, value]) => (
              <article
                key={String(label)}
                style={{
                  ...panelStyle,
                  padding: "18px",
                }}
              >
                <div
                  style={{
                    color: "#64748b",
                    fontSize: "13px",
                    fontWeight: 700,
                  }}
                >
                  {label}
                </div>

                <strong
                  style={{
                    display: "block",
                    marginTop: "8px",
                    color: "#0f172a",
                    fontSize: "23px",
                  }}
                >
                  {value}
                </strong>
              </article>
            )
          )}
        </section>

        <section
          style={{
            ...panelStyle,
            padding: "18px",
          }}
        >
          <div
            style={{
              display: "grid",
              gridTemplateColumns:
                "2fr 2fr 1.5fr auto",
              gap: "12px",
              alignItems: "end",
            }}
          >
            <label>
              <span
                style={{
                  display: "block",
                  marginBottom: "7px",
                  fontWeight: 700,
                }}
              >
                Şirket
              </span>

              <select
                value={
                  companyFilter
                }
                onChange={(
                  event
                ) => {
                  setCompanyFilter(
                    event.target.value
                  );
                  setPersonnelFilter(
                    ""
                  );
                }}
                style={inputStyle}
              >
                <option value="">
                  Şirket seçiniz
                </option>

                {companies.map(
                  (company) => (
                    <option
                      key={company.id}
                      value={company.id}
                    >
                      {company.name}
                    </option>
                  )
                )}
              </select>
            </label>

            <label>
              <span
                style={{
                  display: "block",
                  marginBottom: "7px",
                  fontWeight: 700,
                }}
              >
                Personel
              </span>

              <select
                value={
                  personnelFilter
                }
                onChange={(
                  event
                ) =>
                  setPersonnelFilter(
                    event.target.value
                  )
                }
                style={inputStyle}
              >
                <option value="">
                  Tüm personeller
                </option>

                {filterPersonnel.map(
                  (employee) => (
                    <option
                      key={
                        employee.id
                      }
                      value={
                        employee.id
                      }
                    >
                      {
                        employee
                          .employeeNumber
                      }
                      {" · "}
                      {
                        employee.fullName
                      }
                    </option>
                  )
                )}
              </select>
            </label>

            <label>
              <span
                style={{
                  display: "block",
                  marginBottom: "7px",
                  fontWeight: 700,
                }}
              >
                Geçerlilik Tarihi
              </span>

              <input
                type="date"
                value={
                  effectiveDate
                }
                onChange={(
                  event
                ) =>
                  setEffectiveDate(
                    event.target.value
                  )
                }
                style={inputStyle}
              />
            </label>

            <button
              type="button"
              onClick={newRecord}
              style={{
                minHeight: "43px",
                border: "none",
                borderRadius: "10px",
                padding: "0 18px",
                background: "#0f766e",
                color: "#ffffff",
                fontWeight: 800,
                cursor: "pointer",
              }}
            >
              Yeni Maaş Kartı
            </button>
          </div>
        </section>

        <section
          style={{
            ...panelStyle,
            overflow: "hidden",
          }}
        >
          <div
            style={{
              overflowX: "auto",
            }}
          >
            <table
              style={{
                width: "100%",
                borderCollapse:
                  "collapse",
                minWidth: "1050px",
              }}
            >
              <thead>
                <tr
                  style={{
                    background:
                      "#f8fafc",
                  }}
                >
                  {[
                    "Personel",
                    "Dönem",
                    "Brüt",
                    "Net",
                    "Günlük",
                    "Saatlik",
                    "Katsayılar",
                    "Durum",
                    "İşlem",
                  ].map((title) => (
                    <th
                      key={title}
                      style={{
                        padding:
                          "13px 14px",
                        textAlign:
                          "left",
                        color:
                          "#475569",
                        fontSize:
                          "13px",
                        borderBottom:
                          "1px solid #e2e8f0",
                      }}
                    >
                      {title}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody>
                {!loading &&
                  records.length ===
                    0 && (
                    <tr>
                      <td
                        colSpan={9}
                        style={{
                          padding:
                            "32px",
                          textAlign:
                            "center",
                          color:
                            "#64748b",
                        }}
                      >
                        Maaş kartı
                        bulunamadı.
                      </td>
                    </tr>
                  )}

                {records.map(
                  (record) => {
                    const employee =
                      personnelMap.get(
                        record.personnelId
                      );

                    const now =
                      new Date(
                        `${today}T00:00:00`
                      );

                    const start =
                      new Date(
                        record
                          .effectiveStartDate
                      );

                    const end =
                      record
                        .effectiveEndDate
                        ? new Date(
                            record
                              .effectiveEndDate
                          )
                        : null;

                    const active =
                      start <= now &&
                      (!end ||
                        end >= now);

                    return (
                      <tr
                        key={
                          record.id
                        }
                      >
                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          <strong>
                            {employee
                              ?.fullName ??
                              "Personel"}
                          </strong>

                          <div
                            style={{
                              color:
                                "#64748b",
                              fontSize:
                                "12px",
                              marginTop:
                                "4px",
                            }}
                          >
                            {employee
                              ?.employeeNumber ??
                              record.personnelId}
                          </div>
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          {dateValue(
                            record
                              .effectiveStartDate
                          )}

                          {" → "}

                          {dateValue(
                            record
                              .effectiveEndDate
                          ) ||
                            "Süresiz"}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          {money(
                            record.grossSalary,
                            record.currencyCode
                          )}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          {money(
                            record.netSalary,
                            record.currencyCode
                          )}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          {money(
                            record.dailyRate,
                            record.currencyCode
                          )}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          {money(
                            record.hourlyRate,
                            record.currencyCode
                          )}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                            fontSize:
                              "12px",
                            lineHeight:
                              1.7,
                          }}
                        >
                          Mesai:{" "}
                          {
                            record
                              .overtimeMultiplier
                          }
                          <br />
                          Pazar:{" "}
                          {
                            record
                              .sundayMultiplier
                          }
                          <br />
                          Tatil:{" "}
                          {
                            record
                              .publicHolidayMultiplier
                          }
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          <span
                            style={{
                              display:
                                "inline-block",
                              borderRadius:
                                "999px",
                              padding:
                                "5px 9px",
                              background:
                                active
                                  ? "#dcfce7"
                                  : "#f1f5f9",
                              color:
                                active
                                  ? "#166534"
                                  : "#475569",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                            }}
                          >
                            {active
                              ? "Aktif"
                              : "Pasif"}
                          </span>
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid #eef2f7",
                          }}
                        >
                          <div
                            style={{
                              display:
                                "flex",
                              gap: "8px",
                            }}
                          >
                            <button
                              type="button"
                              onClick={() =>
                                editRecord(
                                  record
                                )
                              }
                              style={{
                                border:
                                  "1px solid #cbd5e1",
                                borderRadius:
                                  "8px",
                                background:
                                  "#ffffff",
                                padding:
                                  "7px 10px",
                                cursor:
                                  "pointer",
                                fontWeight:
                                  700,
                              }}
                            >
                              Düzenle
                            </button>

                            <button
                              type="button"
                              disabled={
                                actionId ===
                                record.id
                              }
                              onClick={() =>
                                void deleteRecord(
                                  record
                                )
                              }
                              style={{
                                border:
                                  "1px solid #fecaca",
                                borderRadius:
                                  "8px",
                                background:
                                  "#fef2f2",
                                color:
                                  "#b91c1c",
                                padding:
                                  "7px 10px",
                                cursor:
                                  "pointer",
                                fontWeight:
                                  700,
                              }}
                            >
                              Sil
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  }
                )}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      {showForm && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            zIndex: 1000,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "20px",
            background:
              "rgba(15, 23, 42, 0.62)",
          }}
        >
          <form
            onSubmit={submit}
            style={{
              width: "100%",
              maxWidth: "820px",
              maxHeight: "92vh",
              overflowY: "auto",
              borderRadius: "18px",
              background: "#ffffff",
              boxShadow:
                "0 24px 80px rgba(15, 23, 42, 0.3)",
            }}
          >
            <header
              style={{
                display: "flex",
                justifyContent:
                  "space-between",
                alignItems: "center",
                gap: "15px",
                padding: "20px 22px",
                borderBottom:
                  "1px solid #e2e8f0",
              }}
            >
              <div>
                <h2
                  style={{
                    margin: 0,
                    color: "#0f172a",
                  }}
                >
                  {editingId
                    ? "Maaş Kartını Düzenle"
                    : "Yeni Maaş Kartı"}
                </h2>

                <p
                  style={{
                    margin:
                      "5px 0 0",
                    color:
                      "#64748b",
                  }}
                >
                  Bordro ve işçilik
                  maliyetlerinde
                  kullanılacak ücretleri
                  tanımlayın.
                </p>
              </div>

              <button
                type="button"
                onClick={closeForm}
                disabled={saving}
                style={{
                  width: "38px",
                  height: "38px",
                  border:
                    "1px solid #e2e8f0",
                  borderRadius:
                    "10px",
                  background:
                    "#ffffff",
                  fontSize: "22px",
                  cursor:
                    "pointer",
                }}
              >
                ×
              </button>
            </header>

            <div
              style={{
                display: "grid",
                gridTemplateColumns:
                  "repeat(2, minmax(0, 1fr))",
                gap: "16px",
                padding: "22px",
              }}
            >
              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Şirket
                </span>

                <select
                  disabled={
                    Boolean(
                      editingId
                    )
                  }
                  required
                  value={
                    form.companyId
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        companyId:
                          event.target
                            .value,
                        personnelId:
                          "",
                      })
                    )
                  }
                  style={inputStyle}
                >
                  <option value="">
                    Şirket seçiniz
                  </option>

                  {companies.map(
                    (company) => (
                      <option
                        key={
                          company.id
                        }
                        value={
                          company.id
                        }
                      >
                        {
                          company.name
                        }
                      </option>
                    )
                  )}
                </select>
              </label>

              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Personel
                </span>

                <select
                  disabled={
                    Boolean(
                      editingId
                    )
                  }
                  required
                  value={
                    form.personnelId
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        personnelId:
                          event.target
                            .value,
                      })
                    )
                  }
                  style={inputStyle}
                >
                  <option value="">
                    Personel seçiniz
                  </option>

                  {formPersonnel.map(
                    (employee) => (
                      <option
                        key={
                          employee.id
                        }
                        value={
                          employee.id
                        }
                      >
                        {
                          employee
                            .employeeNumber
                        }
                        {" · "}
                        {
                          employee
                            .fullName
                        }
                      </option>
                    )
                  )}
                </select>
              </label>

              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Başlangıç Tarihi
                </span>

                <input
                  type="date"
                  required
                  value={
                    form
                      .effectiveStartDate
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        effectiveStartDate:
                          event.target
                            .value,
                      })
                    )
                  }
                  style={inputStyle}
                />
              </label>

              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Bitiş Tarihi
                </span>

                <input
                  type="date"
                  value={
                    form
                      .effectiveEndDate
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        effectiveEndDate:
                          event.target
                            .value,
                      })
                    )
                  }
                  style={inputStyle}
                />
              </label>

              {[
                [
                  "Brüt Maaş",
                  "grossSalary",
                ],
                [
                  "Net Maaş",
                  "netSalary",
                ],
                [
                  "Günlük Ücret",
                  "dailyRate",
                ],
                [
                  "Saatlik Ücret",
                  "hourlyRate",
                ],
                [
                  "Fazla Mesai Katsayısı",
                  "overtimeMultiplier",
                ],
                [
                  "Pazar Katsayısı",
                  "sundayMultiplier",
                ],
                [
                  "Resmî Tatil Katsayısı",
                  "publicHolidayMultiplier",
                ],
              ].map(
                ([label, key]) => (
                  <label key={key}>
                    <span
                      style={{
                        display:
                          "block",
                        marginBottom:
                          "7px",
                        fontWeight:
                          700,
                      }}
                    >
                      {label}
                    </span>

                    <input
                      type="number"
                      step="0.01"
                      min="0"
                      value={
                        form[
                          key as keyof SalaryForm
                        ] as string
                      }
                      onChange={(
                        event
                      ) =>
                        setForm(
                          (current) => ({
                            ...current,
                            [key]:
                              event
                                .target
                                .value,
                          })
                        )
                      }
                      style={inputStyle}
                    />
                  </label>
                )
              )}

              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Para Birimi
                </span>

                <select
                  value={
                    form.currencyCode
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        currencyCode:
                          event.target
                            .value,
                      })
                    )
                  }
                  style={inputStyle}
                >
                  <option value="TRY">
                    TRY
                  </option>
                  <option value="USD">
                    USD
                  </option>
                  <option value="EUR">
                    EUR
                  </option>
                  <option value="GBP">
                    GBP
                  </option>
                </select>
              </label>

              <label
                style={{
                  gridColumn:
                    "1 / -1",
                }}
              >
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "7px",
                    fontWeight: 700,
                  }}
                >
                  Açıklama
                </span>

                <textarea
                  rows={3}
                  value={
                    form.description
                  }
                  onChange={(
                    event
                  ) =>
                    setForm(
                      (current) => ({
                        ...current,
                        description:
                          event.target
                            .value,
                      })
                    )
                  }
                  style={{
                    ...inputStyle,
                    resize:
                      "vertical",
                  }}
                />
              </label>
            </div>

            <footer
              style={{
                display: "flex",
                justifyContent:
                  "flex-end",
                gap: "12px",
                padding: "18px 22px",
                borderTop:
                  "1px solid #e2e8f0",
                background:
                  "#f8fafc",
              }}
            >
              <button
                type="button"
                onClick={closeForm}
                disabled={saving}
                style={{
                  minHeight: "43px",
                  border:
                    "1px solid #cbd5e1",
                  borderRadius:
                    "10px",
                  background:
                    "#ffffff",
                  padding:
                    "0 17px",
                  fontWeight: 800,
                  cursor:
                    "pointer",
                }}
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                style={{
                  minHeight: "43px",
                  border: "none",
                  borderRadius:
                    "10px",
                  background:
                    saving
                      ? "#94a3b8"
                      : "#0f766e",
                  color: "#ffffff",
                  padding:
                    "0 20px",
                  fontWeight: 900,
                  cursor:
                    saving
                      ? "not-allowed"
                      : "pointer",
                }}
              >
                {saving
                  ? "Kaydediliyor..."
                  : editingId
                    ? "Değişiklikleri Kaydet"
                    : "Maaş Kartı Oluştur"}
              </button>
            </footer>
          </form>
        </div>
      )}
    </ErpShell>
  );
}
