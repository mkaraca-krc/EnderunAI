"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { currencyMoney } from "@/lib/format/turkish";

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
  NetToGrossResult,
  SalaryDefinition,
  UpdateSalaryDefinitionRequest,
  hrSalaryService,
} from "@/services/hr-salary.service";

type SalaryForm = {
  companyId: string;
  personnelId: string;
  effectiveStartDate: string;
  effectiveEndDate: string;
  /** "0" brüt esaslı, "1" net esaslı. */
  salaryBasis: string;
  targetNetSalary: string;
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
  salaryBasis: "0",
  targetNetSalary: "",
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
  background: "var(--erp-panel)",
  border: "1px solid var(--erp-border)",
  borderRadius: "16px",
  boxShadow:
    "0 8px 24px rgba(15, 23, 42, 0.05)",
};

const inputStyle = {
  width: "100%",
  minHeight: "42px",
  border: "1px solid var(--erp-border)",
  borderRadius: "10px",
  padding: "8px 11px",
  background: "var(--erp-panel)",
  color: "var(--erp-text)",
};

function money(
  value: number,
  currencyCode = "TRY"
) {
  return currencyMoney(value ?? 0, currencyCode || "TRY");
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
  const cleaned = value
    .trim()
    .replace(/\s/g, "")
    .replace(/[^\d,.-]/g, "");

  if (!cleaned) {
    return 0;
  }

  const commaIndex =
    cleaned.lastIndexOf(",");
  const dotIndex =
    cleaned.lastIndexOf(".");
  const decimalIndex =
    Math.max(
      commaIndex,
      dotIndex
    );

  const normalized =
    decimalIndex >= 0
      ? `${cleaned
          .slice(0, decimalIndex)
          .replace(/[.,]/g, "")}.${cleaned
          .slice(decimalIndex + 1)
          .replace(/[.,]/g, "")}`
      : cleaned;

  const result =
    Number(normalized);

  return Number.isFinite(result)
    ? result
    : 0;
}

function decimalInput(
  value: string
) {
  return value
    .replace(/\s/g, "")
    .replace(/[^\d.,]/g, "");
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

  /** Silinmek üzere onay bekleyen maaş kartı. */
  const [pending, setPending] = useState<SalaryDefinition | null>(null);

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

  const selectedPersonnel =
    useMemo(
      () =>
        personnelMap.get(
          form.personnelId
        ),
      [
        form.personnelId,
        personnelMap,
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
      effectiveStartDate:
        dateValue(
          firstPerson
            ?.employmentStartDate
        ) || today,
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
      salaryBasis: String(
        record.salaryBasis ?? 0
      ),
      targetNetSalary: String(
        record.targetNetSalary ?? 0
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
      salaryBasis: Number(
        form.salaryBasis
      ),
      targetNetSalary:
        parseNumber(
          form.targetNetSalary
        ),
    };

    // Net esaslı kartta brüt sistemce hesaplanır; kullanıcıdan
    // beklenen tek tutar anlaşılan nettir.
    if (
      common.salaryBasis === 1 &&
      common.targetNetSalary <= 0
    ) {
      setError(
        "Net esaslı kartta anlaşılan net ücret girilmelidir."
      );
      return;
    }

    if (
      common.salaryBasis === 0 &&
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
    setPending(null);
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
      design="redwood"
      title="Maaş Kartları"
      description="Resmî net, elden ödeme ve toplam ele geçen tek ekranda; günlük/saatlik ücret ve mesai katsayıları dahil."
    >
      {/* Maaş kartları başka İK kullanıcısınca da düzenleniyor. */}
      <div className="mb-4 flex justify-end">
        <button
          type="button"
          className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          onClick={() => void loadRecords()}
        >
          Yenile
        </button>
      </div>
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
                ? "var(--color-semantic-danger)"
                : "var(--color-semantic-success)",
              background: error
                ? "var(--color-semantic-danger-tint)"
                : "var(--color-semantic-success-tint)",
              borderColor: error
                ? "var(--color-semantic-danger-border)"
                : "var(--color-semantic-success-border)",
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
                    color: "var(--erp-muted)",
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
                    color: "var(--erp-text)",
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
                background: "var(--erp-primary)",
                color: "var(--color-on-brand)",
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
                minWidth: "1160px",
              }}
            >
              <thead>
                <tr
                  style={{
                    background:
                      "var(--erp-bg)",
                  }}
                >
                  {[
                    "Personel",
                    "İşe Giriş",
                    "Dönem",
                    "Brüt",
                    "Resmî Net",
                    "Elden",
                    "Toplam Ele Geçen",
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
                          "var(--erp-muted)",
                        fontSize:
                          "13px",
                        borderBottom:
                          "1px solid var(--erp-border)",
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
                        colSpan={10}
                        style={{
                          padding:
                            "32px",
                          textAlign:
                            "center",
                          color:
                            "var(--erp-muted)",
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
                              "1px solid var(--erp-border)",
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
                                "var(--erp-muted)",
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
                              "1px solid var(--erp-border)",
                          }}
                        >
                          {dateValue(
                            record
                              .employmentStartDate ??
                              employee
                                ?.employmentStartDate
                          ) || "—"}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid var(--erp-border)",
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
                              "1px solid var(--erp-border)",
                          }}
                        >
                          {money(
                            record.grossSalary,
                            record.currencyCode
                          )}

                          {record.salaryBasis === 1 && (
                            <small
                              style={{
                                display: "block",
                                color: "var(--erp-muted)",
                              }}
                            >
                              netten hesaplandı
                            </small>
                          )}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid var(--erp-border)",
                          }}
                        >
                          {money(
                            record.officialNetSalary ??
                              record.netSalary,
                            record.currencyCode
                          )}
                        </td>

                        <td
                          style={{
                            padding: "13px 14px",
                            borderBottom:
                              "1px solid var(--erp-border)",
                          }}
                        >
                          {record.extraPaymentHidden ? (
                            <span style={{ color: "var(--erp-muted)" }}>
                              gizli
                            </span>
                          ) : record.extraPaymentMonthlyAmount ? (
                            money(
                              record.extraPaymentMonthlyAmount,
                              record.currencyCode
                            )
                          ) : (
                            "—"
                          )}
                        </td>

                        <td
                          style={{
                            padding: "13px 14px",
                            borderBottom:
                              "1px solid var(--erp-border)",
                            fontWeight: 700,
                          }}
                        >
                          {record.totalTakeHome != null
                            ? money(
                                record.totalTakeHome,
                                record.currencyCode
                              )
                            : "—"}
                        </td>

                        <td
                          style={{
                            padding:
                              "13px 14px",
                            borderBottom:
                              "1px solid var(--erp-border)",
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
                              "1px solid var(--erp-border)",
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
                              "1px solid var(--erp-border)",
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
                              "1px solid var(--erp-border)",
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
                                  ? "var(--color-semantic-success-tint)"
                                  : "var(--erp-bg)",
                              color:
                                active
                                  ? "var(--color-semantic-success)"
                                  : "var(--erp-muted)",
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
                              "1px solid var(--erp-border)",
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
                                  "1px solid var(--erp-border)",
                                borderRadius:
                                  "8px",
                                background:
                                  "var(--erp-panel)",
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
                                setPending(record)
                              }
                              style={{
                                border:
                                  "1px solid var(--color-semantic-danger-border)",
                                borderRadius:
                                  "8px",
                                background:
                                  "var(--color-semantic-danger-tint)",
                                color:
                                  "var(--color-semantic-danger)",
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
              background: "var(--erp-panel)",
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
                  "1px solid var(--erp-border)",
              }}
            >
              <div>
                <h2
                  style={{
                    margin: 0,
                    color: "var(--erp-text)",
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
                      "var(--erp-muted)",
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
                    "1px solid var(--erp-border)",
                  borderRadius:
                    "10px",
                  background:
                    "var(--erp-panel)",
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
                        effectiveStartDate:
                          today,
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
                  ) => {
                    const personnelId =
                      event.target
                        .value;

                    const employee =
                      formPersonnel.find(
                        (item) =>
                          item.id ===
                          personnelId
                      );

                    setForm(
                      (current) => ({
                        ...current,
                        personnelId,
                        effectiveStartDate:
                          editingId
                            ? current
                                .effectiveStartDate
                            : dateValue(
                                employee
                                  ?.employmentStartDate
                              ) ||
                              current
                                .effectiveStartDate,
                      })
                    )
                  }}
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
                  İşe Giriş Tarihi
                </span>

                <input
                  type="date"
                  readOnly
                  aria-readonly="true"
                  value={
                    dateValue(
                      selectedPersonnel
                        ?.employmentStartDate
                    )
                  }
                  placeholder="Personel kartından alınır"
                  title="Bu tarih personel kartından otomatik alınır."
                  style={{
                    ...inputStyle,
                    background:
                      "var(--erp-bg)",
                    color:
                      "var(--erp-muted)",
                    cursor:
                      "not-allowed",
                  }}
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
                  Maaş Geçerlilik Başlangıcı
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
                  Maaş Geçerlilik Bitişi
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

              <label style={{ gridColumn: "1 / -1" }}>
                <span
                  style={{
                    display: "block",
                    marginBottom: "7px",
                    fontWeight: 700,
                  }}
                >
                  Ücret Esası
                </span>

                <select
                  value={form.salaryBasis}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      salaryBasis:
                        event.target.value,
                    }))
                  }
                  style={inputStyle}
                >
                  <option value="0">
                    Brüt esaslı — brüt girilir, net ondan çıkar
                  </option>
                  <option value="1">
                    Net esaslı — net girilir, brüt hesaplanır
                  </option>
                </select>

                <small
                  style={{
                    display: "block",
                    marginTop: "6px",
                    color: "var(--erp-muted)",
                  }}
                >
                  {form.salaryBasis === "1"
                    ? "Personel her ay tam olarak girilen neti alır; vergi dilimi yükseldikçe brüt otomatik artar, farkı şirket üstlenir."
                    : "Karttaki brüt sabit kalır; yıl içinde vergi dilimi yükseldikçe ele geçen net düşer."}
                </small>
              </label>

              {form.salaryBasis === "1" && (
                <label style={{ gridColumn: "1 / -1" }}>
                  <span
                    style={{
                      display: "block",
                      marginBottom: "7px",
                      fontWeight: 700,
                    }}
                  >
                    Anlaşılan Resmî Net Maaş *
                  </span>

                  <input
                    type="text"
                    inputMode="decimal"
                    pattern="[0-9.,]*"
                    autoComplete="off"
                    value={form.targetNetSalary}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        targetNetSalary:
                          event.target.value,
                      }))
                    }
                    style={inputStyle}
                  />

                  <NetToGrossPreview
                    companyId={form.companyId}
                    effectiveStartDate={
                      form.effectiveStartDate
                    }
                    targetNet={form.targetNetSalary}
                  />
                </label>
              )}

              {[
                ...(form.salaryBasis === "1"
                  ? []
                  : [
                      [
                        "Brüt Maaş",
                        "grossSalary",
                      ],
                      [
                        "Net Maaş",
                        "netSalary",
                      ],
                    ]),
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
                      type="text"
                      inputMode="decimal"
                      pattern="[0-9.,]*"
                      autoComplete="off"
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
                              decimalInput(
                                event
                                  .target
                                  .value
                              ),
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
                  "1px solid var(--erp-border)",
                background:
                  "var(--erp-bg)",
              }}
            >
              <button
                type="button"
                onClick={closeForm}
                disabled={saving}
                style={{
                  minHeight: "43px",
                  border:
                    "1px solid var(--erp-border)",
                  borderRadius:
                    "10px",
                  background:
                    "var(--erp-panel)",
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
                      ? "var(--erp-muted)"
                      : "var(--erp-primary)",
                  color: "var(--color-on-brand)",
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
      {pending && (
        <ConfirmDialog
          open
          title="Maaş Kartını Sil"
          description={`${
            personnelMap.get(pending.personnelId)?.fullName ?? "Personel"
          } maaş kartı kalıcı olarak silinecek. Bu kart bordroya girdiyse geçmiş bordrolar etkilenmez, ama yeni dönemde ücret bilgisi kalmaz.`}
          confirmLabel="Kartı Sil"
          busy={actionId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() => void deleteRecord(pending)}
        />
      )}
    </ErpShell>
  );
}

/**
 * Girilen nete karşılık gelen brütü ve kesinti kırılımını canlı
 * gösterir. Kayıt yazmaz.
 *
 * Sunucudan hesaplanır — brütleştirme kuralı arayüzde tekrar
 * yazılsaydı iki hesap arasında sessiz bir fark doğardı.
 */
function NetToGrossPreview({
  companyId,
  effectiveStartDate,
  targetNet,
}: {
  companyId: string;
  effectiveStartDate: string;
  targetNet: string;
}) {
  const [result, setResult] = useState<NetToGrossResult | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const amount = Number(targetNet.replace(",", "."));
  const year = Number(effectiveStartDate.slice(0, 4));

  const canCalculate =
    Boolean(companyId) && Number.isFinite(amount) && amount > 0 && Boolean(year);

  useEffect(() => {
    let active = true;

    // Kullanıcı yazarken her tuşta istek atmamak için kısa bekleme.
    // Sıfırlama da bu geciktirmenin içinde: efekt gövdesinde doğrudan
    // state yazmak zincirleme render tetikliyor.
    const timer = window.setTimeout(() => {
      if (!active) return;

      if (!canCalculate) {
        setResult(null);
        setError("");
        setLoading(false);
        return;
      }

      setLoading(true);

      void hrSalaryService
        .netToGross({ companyId, year, targetNet: amount, month: 1 })
        .then((value) => {
          if (!active) return;
          setResult(value);
          setError("");
        })
        .catch((err: unknown) => {
          if (!active) return;
          setResult(null);
          setError(
            err instanceof Error ? err.message : "Brüt hesaplanamadı."
          );
        })
        .finally(() => {
          if (active) setLoading(false);
        });
    }, 400);

    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [companyId, amount, year, canCalculate]);

  if (!canCalculate) return null;

  if (loading && !result) {
    return (
      <small style={{ display: "block", marginTop: "8px", color: "var(--erp-muted)" }}>
        Brüt hesaplanıyor...
      </small>
    );
  }

  if (error) {
    return (
      <small style={{ display: "block", marginTop: "8px", color: "var(--color-semantic-danger)" }}>
        {error}
      </small>
    );
  }

  if (!result) return null;

  return (
    <div
      style={{
        marginTop: "10px",
        padding: "12px 14px",
        borderRadius: "10px",
        background: "var(--erp-bg)",
        border: "1px solid var(--erp-border)",
        fontSize: "13px",
      }}
    >
      <strong style={{ display: "block", marginBottom: "6px" }}>
        Hesaplanan brüt: {money(result.grossSalary)}
      </strong>

      <div style={{ color: "var(--erp-muted)", lineHeight: 1.7 }}>
        SGK işçi: {money(result.sgkEmployee)} · İşsizlik:{" "}
        {money(result.unemploymentEmployee)} · Gelir vergisi:{" "}
        {money(result.incomeTax)} · Damga:{" "}
        {money(result.stampTax)}
        <br />
        Toplam kesinti: {money(result.totalDeductions)} · İşverene
        maliyeti: {money(result.totalEmployerCost)}
      </div>

      {!result.isExact && (
        <div style={{ marginTop: "8px", color: "var(--color-semantic-warning)" }}>
          Yuvarlama nedeniyle tam olarak bu net yakalanamıyor; en yakın brütle
          ele geçen {money(result.achievedNet)} (
          {result.difference > 0 ? "+" : ""}
          {money(result.difference)}).
        </div>
      )}

      <small
        style={{ display: "block", marginTop: "8px", color: "var(--erp-muted)" }}
      >
        Ocak esaslı referans. Bordroda her ayın kümülatif vergi matrahıyla
        yeniden hesaplanır; ele geçen net her ay aynı kalır.
      </small>
    </div>
  );
}
