"use client";

import Link from "next/link";

import {
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
  HrDashboardPayroll,
  HrDashboardPersonnel,
  hrDashboardService,
} from "@/services/hr-dashboard.service";

import {
  HrLeaveListItem,
  hrLeaveService,
} from "@/services/hr-leave.service";

import {
  HrOvertimeItem,
  hrOvertimeService,
} from "@/services/hr-overtime.service";

import {
  HrAdvanceItem,
  hrAdvanceService,
} from "@/services/hr-advance.service";

import {
  BranchListItem,
  branchService,
} from "@/services/branch.service";

const TODAY =
  new Date();

const CURRENT_YEAR =
  TODAY.getFullYear();

const CURRENT_MONTH =
  TODAY.getMonth() + 1;

const TODAY_VALUE =
  TODAY.toISOString().slice(0, 10);

const MONTHS = [
  "Ocak",
  "Şubat",
  "Mart",
  "Nisan",
  "Mayıs",
  "Haziran",
  "Temmuz",
  "Ağustos",
  "Eylül",
  "Ekim",
  "Kasım",
  "Aralık",
];

const panelStyle = {
  background: "#ffffff",
  border: "1px solid #e2e8f0",
  borderRadius: "16px",
  boxShadow:
    "0 8px 24px rgba(15, 23, 42, 0.05)",
};

function money(
  value: number,
  currencyCode = "TRY"
) {
  return new Intl.NumberFormat(
    "tr-TR",
    {
      style: "currency",
      currency:
        currencyCode === "MIXED"
          ? "TRY"
          : currencyCode || "TRY",
      maximumFractionDigits: 2,
    }
  ).format(value ?? 0);
}

function number(
  value: number
) {
  return new Intl.NumberFormat(
    "tr-TR"
  ).format(value ?? 0);
}

function date(
  value?: string | null
) {
  if (!value) {
    return "-";
  }

  return new Intl.DateTimeFormat(
    "tr-TR"
  ).format(new Date(value));
}

function errorMessage(
  error: unknown
) {
  return error instanceof Error
    ? error.message
    : "Dashboard verileri alınamadı.";
}

function personnelName(
  item: HrDashboardPersonnel
) {
  const value =
    item.fullName?.trim() ||
    `${item.firstName ?? ""} ${
      item.lastName ?? ""
    }`.trim();

  return value || "Personel";
}

function personnelStatus(
  item: HrDashboardPersonnel
) {
  if (item.statusName) {
    return item.statusName;
  }

  switch (item.status) {
    case 0:
      return "Aday";
    case 1:
      return "Aktif";
    case 2:
      return "İzinli";
    case 3:
      return "Askıda";
    case 4:
      return "Ayrıldı";
    default:
      return item.isActive === false
        ? "Pasif"
        : "Aktif";
  }
}

function payrollStatus(
  item: HrDashboardPayroll
) {
  if (item.statusName) {
    switch (
      item.statusName.toLowerCase()
    ) {
      case "draft":
        return "Taslak";
      case "calculated":
        return "Hesaplandı";
      case "approved":
        return "Onaylandı";
      case "paid":
        return "Ödendi";
      default:
        return item.statusName;
    }
  }

  switch (item.status) {
    case 1:
      return "Hesaplandı";
    case 2:
      return "Onaylandı";
    case 3:
      return "Ödendi";
    default:
      return "Taslak";
  }
}

function percentage(
  part: number,
  total: number
) {
  if (total <= 0) {
    return 0;
  }

  return Math.min(
    100,
    Math.max(
      0,
      Math.round(
        (part / total) * 100
      )
    )
  );
}

type KpiCardProps = {
  title: string;
  value: string;
  detail: string;
  icon: string;
};

function KpiCard({
  title,
  value,
  detail,
  icon,
}: KpiCardProps) {
  return (
    <article
      style={{
        ...panelStyle,
        padding: "18px",
        minHeight: "142px",
        display: "flex",
        flexDirection: "column",
        justifyContent:
          "space-between",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent:
            "space-between",
          alignItems: "flex-start",
          gap: "12px",
        }}
      >
        <div
          style={{
            color: "#64748b",
            fontSize: "13px",
            fontWeight: 800,
          }}
        >
          {title}
        </div>

        <span
          style={{
            width: "38px",
            height: "38px",
            display: "grid",
            placeItems: "center",
            borderRadius: "11px",
            background: "#f1f5f9",
            color: "#0f766e",
            fontSize: "18px",
            fontWeight: 900,
          }}
        >
          {icon}
        </span>
      </div>

      <div>
        <strong
          style={{
            display: "block",
            color: "#0f172a",
            fontSize: "25px",
            lineHeight: 1.2,
          }}
        >
          {value}
        </strong>

        <div
          style={{
            marginTop: "7px",
            color: "#64748b",
            fontSize: "12px",
          }}
        >
          {detail}
        </div>
      </div>
    </article>
  );
}

export default function HumanResourcesDashboardPage() {
  const [
    companies,
    setCompanies,
  ] = useState<CompanyListItem[]>([]);

  const [
    companyId,
    setCompanyId,
  ] = useState("");

  const [
    personnel,
    setPersonnel,
  ] = useState<
    HrDashboardPersonnel[]
  >([]);

  const [
    payrolls,
    setPayrolls,
  ] = useState<
    HrDashboardPayroll[]
  >([]);

  const [
    salaryPersonnelIds,
    setSalaryPersonnelIds,
  ] = useState<Set<string>>(
    new Set()
  );

  const [
    trendPayrolls,
    setTrendPayrolls,
  ] = useState<
    HrDashboardPayroll[]
  >([]);

  const [
    leaves,
    setLeaves,
  ] = useState<
    HrLeaveListItem[]
  >([]);

  const [
    overtimes,
    setOvertimes,
  ] = useState<
    HrOvertimeItem[]
  >([]);

  const [
    advances,
    setAdvances,
  ] = useState<
    HrAdvanceItem[]
  >([]);

  const [
    branches,
    setBranches,
  ] = useState<
    BranchListItem[]
  >([]);

  const [
    loading,
    setLoading,
  ] = useState(false);

  const [
    error,
    setError,
  ] = useState("");

  const loadCompanies =
    useCallback(async () => {
      setLoading(true);
      setError("");

      try {
        const rows =
          await companyService.getAll();

        setCompanies(rows);

        const selected =
          rows.find(
            (item) =>
              item.isActive !== false
          ) ?? rows[0];

        if (selected) {
          setCompanyId(
            (current) =>
              current || selected.id
          );
        }
      } catch (loadError) {
        setError(
          errorMessage(loadError)
        );
      } finally {
        setLoading(false);
      }
    }, []);

  const loadDashboard =
    useCallback(async () => {
      if (!companyId) {
        return;
      }

      setLoading(true);
      setError("");

      try {
        const monthStart =
          new Date(
            CURRENT_YEAR,
            CURRENT_MONTH - 1,
            1
          )
            .toISOString()
            .slice(0, 10);

        const monthEnd =
          new Date(
            CURRENT_YEAR,
            CURRENT_MONTH,
            0
          )
            .toISOString()
            .slice(0, 10);

        const [
          personnelResult,
          currentPayrollResult,
          salaryResult,
          allPayrollResult,
          leaveResult,
          overtimeResult,
          advanceResult,
          branchResult,
        ] = await Promise.allSettled([
          hrDashboardService
            .getPersonnel(companyId),

          hrDashboardService
            .getPayrolls(
              companyId,
              CURRENT_YEAR,
              CURRENT_MONTH
            ),

          hrDashboardService
            .getSalaryDefinitions(
              companyId,
              TODAY_VALUE
            ),

          hrDashboardService
            .getPayrolls(companyId),

          hrLeaveService.getAll({
            companyId,
            startDate: monthStart,
            endDate: monthEnd,
          }),

          hrOvertimeService.getAll({
            companyId,
            startDate: monthStart,
            endDate: monthEnd,
          }),

          hrAdvanceService.getAll({
            companyId,
            startDate: monthStart,
            endDate: monthEnd,
          }),

          branchService.getAll(
            companyId
          ),
        ]);

        const personnelRows =
          personnelResult.status ===
          "fulfilled"
            ? personnelResult.value
            : [];

        const currentPayrollRows =
          currentPayrollResult.status ===
          "fulfilled"
            ? currentPayrollResult.value
            : [];

        const salaryRows =
          salaryResult.status ===
          "fulfilled"
            ? salaryResult.value
            : [];

        const allPayrollRows =
          allPayrollResult.status ===
          "fulfilled"
            ? allPayrollResult.value
            : [];

        const leaveRows =
          leaveResult.status ===
          "fulfilled"
            ? leaveResult.value
            : [];

        const overtimeRows =
          overtimeResult.status ===
          "fulfilled"
            ? overtimeResult.value
            : [];

        const advanceRows =
          advanceResult.status ===
          "fulfilled"
            ? advanceResult.value
            : [];

        const branchRows =
          branchResult.status ===
          "fulfilled"
            ? branchResult.value
            : [];

        const failedSourceCount = [
          personnelResult,
          currentPayrollResult,
          salaryResult,
          allPayrollResult,
          leaveResult,
          overtimeResult,
          advanceResult,
          branchResult,
        ].filter(
          (result) =>
            result.status === "rejected"
        ).length;

        if (failedSourceCount > 0) {
          setError(
            `${failedSourceCount} veri kaynağı alınamadı; ulaşılabilen İK verileri gösteriliyor.`
          );
        }

        setPersonnel(personnelRows);
        setPayrolls(
          currentPayrollRows
        );

        setSalaryPersonnelIds(
          new Set(
            salaryRows.map(
              (item) =>
                item.personnelId
            )
          )
        );

        setTrendPayrolls(
          allPayrollRows
        );

        setLeaves(leaveRows);
        setOvertimes(overtimeRows);
        setAdvances(advanceRows);
        setBranches(branchRows);
      } catch (loadError) {
        setError(
          errorMessage(loadError)
        );
      } finally {
        setLoading(false);
      }
    }, [companyId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadDashboard();
  }, [loadDashboard]);

  const activePersonnel =
    useMemo(
      () =>
        personnel.filter(
          (item) =>
            item.status === 1 ||
            (
              item.status ===
                undefined &&
              item.isActive !== false
            )
        ),
      [personnel]
    );

  const startedThisMonth =
    useMemo(
      () =>
        personnel.filter(
          (item) => {
            if (
              !item
                .employmentStartDate
            ) {
              return false;
            }

            const value =
              new Date(
                item
                  .employmentStartDate
              );

            return (
              value.getFullYear() ===
                CURRENT_YEAR &&
              value.getMonth() + 1 ===
                CURRENT_MONTH
            );
          }
        ),
      [personnel]
    );

  const paidPayrolls =
    useMemo(
      () =>
        payrolls.filter(
          (item) =>
            item.status === 3
        ),
      [payrolls]
    );

  const approvedPayrolls =
    useMemo(
      () =>
        payrolls.filter(
          (item) =>
            item.status === 2
        ),
      [payrolls]
    );

  const calculatedPayrolls =
    useMemo(
      () =>
        payrolls.filter(
          (item) =>
            item.status === 1
        ),
      [payrolls]
    );

  const payrollTotals =
    useMemo(
      () => ({
        gross:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item.grossSalary ||
                  0
              ),
            0
          ),

        net:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item
                  .actualPayableAmount ||
                  item
                    .netPayableAmount ||
                  0
              ),
            0
          ),

        deductions:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item
                  .totalDeductions ||
                  0
              ),
            0
          ),

        sgk:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item
                  .sgkEmployeeDeduction ||
                  0
              ),
            0
          ),

        incomeTax:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item
                  .incomeTaxDeduction ||
                  0
              ),
            0
          ),

        stampTax:
          payrolls.reduce(
            (sum, item) =>
              sum +
              Number(
                item
                  .stampTaxDeduction ||
                  0
              ),
            0
          ),
      }),
      [payrolls]
    );

  const personnelStatusRows =
    useMemo(() => {
      const values =
        new Map<string, number>();

      personnel.forEach(
        (item) => {
          const label =
            personnelStatus(item);

          values.set(
            label,
            (values.get(label) ??
              0) + 1
          );
        }
      );

      return Array.from(
        values.entries()
      )
        .map(
          ([label, count]) => ({
            label,
            count,
          })
        )
        .sort(
          (left, right) =>
            right.count -
            left.count
        );
    }, [personnel]);

  const payrollStatusRows =
    useMemo(() => {
      const values =
        new Map<string, number>();

      payrolls.forEach(
        (item) => {
          const label =
            payrollStatus(item);

          values.set(
            label,
            (values.get(label) ??
              0) + 1
          );
        }
      );

      return Array.from(
        values.entries()
      )
        .map(
          ([label, count]) => ({
            label,
            count,
          })
        )
        .sort(
          (left, right) =>
            right.count -
            left.count
        );
    }, [payrolls]);

  const payrollTrend =
    useMemo(() => {
      return Array.from(
        { length: 12 },
        (_, index) => {
          const value =
            new Date(
              CURRENT_YEAR,
              CURRENT_MONTH -
                1 -
                (11 - index),
              1
            );

          const year =
            value.getFullYear();

          const month =
            value.getMonth() + 1;

          const rows =
            trendPayrolls.filter(
              (item) =>
                item.year === year &&
                item.month ===
                  month
            );

          return {
            key:
              `${year}-${month}`,
            label:
              `${MONTHS[
                month - 1
              ].slice(0, 3)} ${String(
                year
              ).slice(-2)}`,
            count: rows.length,
            net: rows.reduce(
              (sum, item) =>
                sum +
                Number(
                  item
                    .actualPayableAmount ||
                    item
                      .netPayableAmount ||
                    0
                ),
              0
            ),
          };
        }
      );
    }, [trendPayrolls]);

  const maximumTrend =
    Math.max(
      1,
      ...payrollTrend.map(
        (item) => item.net
      )
    );

  const recentPersonnel =
    useMemo(
      () =>
        [...personnel]
          .sort(
            (left, right) =>
              new Date(
                right
                  .employmentStartDate ||
                  right
                    .createdAtUtc ||
                  0
              ).getTime() -
              new Date(
                left
                  .employmentStartDate ||
                  left
                    .createdAtUtc ||
                  0
              ).getTime()
          )
          .slice(0, 6),
      [personnel]
    );

  const pendingLeaves =
    useMemo(
      () =>
        leaves.filter(
          (item) =>
            item.status === 0 ||
            item.statusName
              ?.toLowerCase()
              .includes("pending") ||
            item.statusName
              ?.toLocaleLowerCase(
                "tr-TR"
              )
              .includes("bekle")
        ),
      [leaves]
    );

  const approvedLeaves =
    useMemo(
      () =>
        leaves.filter(
          (item) =>
            item.status === 1 ||
            item.statusName
              ?.toLowerCase()
              .includes("approved") ||
            item.statusName
              ?.toLocaleLowerCase(
                "tr-TR"
              )
              .includes("onay")
        ),
      [leaves]
    );

  const personnelOnLeave =
    useMemo(() => {
      const today =
        new Date(
          `${TODAY_VALUE}T00:00:00`
        );

      return approvedLeaves.filter(
        (item) => {
          const start =
            new Date(item.startDate);

          const end =
            new Date(item.endDate);

          return (
            start <= today &&
            end >= today
          );
        }
      );
    }, [approvedLeaves]);

  const overtimeSummary =
    useMemo(
      () => ({
        requested:
          overtimes.reduce(
            (sum, item) =>
              sum +
              Number(
                item.requestedHours ||
                  0
              ),
            0
          ),

        approved:
          overtimes.reduce(
            (sum, item) =>
              sum +
              Number(
                item.approvedHours ||
                  0
              ),
            0
          ),

        pending:
          overtimes.filter(
            (item) =>
              item.status === 0
          ).length,
      }),
      [overtimes]
    );

  const advanceSummary =
    useMemo(
      () => ({
        requested:
          advances.reduce(
            (sum, item) =>
              sum +
              Number(
                item.requestedAmount ||
                  0
              ),
            0
          ),

        approved:
          advances.reduce(
            (sum, item) =>
              sum +
              Number(
                item.approvedAmount ||
                  0
              ),
            0
          ),

        pending:
          advances.filter(
            (item) =>
              item.status === 0
          ).length,

        paid:
          advances.filter(
            (item) =>
              Boolean(item.paidAtUtc) ||
              item.status === 2 ||
              item.status === 3
          ).length,
      }),
      [advances]
    );

  const branchDistribution =
    useMemo(() => {
      const values =
        new Map<string, number>();

      personnel.forEach(
        (item) => {
          const branch =
            branches.find(
              (row) =>
                row.id ===
                item.branchId
            );

          const label =
            branch?.name ||
            "Şube Atanmamış";

          values.set(
            label,
            (values.get(label) ??
              0) + 1
          );
        }
      );

      return Array.from(
        values.entries()
      )
        .map(
          ([label, count]) => ({
            label,
            count,
          })
        )
        .sort(
          (left, right) =>
            right.count -
            left.count
        );
    }, [personnel, branches]);

  const attentionItems =
    useMemo(
      () => [
        {
          label:
            "Maaş kartı eksik personel",
          count: Math.max(
            0,
            activePersonnel.length -
              salaryPersonnelIds.size
          ),
          href:
            "/insan-kaynaklari/ucret-kartlari",
        },
        {
          label:
            "Bekleyen izin talepleri",
          count:
            pendingLeaves.length,
          href:
            "/insan-kaynaklari/izinler",
        },
        {
          label:
            "Bekleyen fazla mesailer",
          count:
            overtimeSummary.pending,
          href:
            "/insan-kaynaklari/fazla-mesai",
        },
        {
          label:
            "Bekleyen avans talepleri",
          count:
            advanceSummary.pending,
          href:
            "/insan-kaynaklari/avanslar",
        },
        {
          label:
            "Ödeme bekleyen bordrolar",
          count:
            approvedPayrolls.length,
          href:
            "/insan-kaynaklari/bordro",
        },
      ].filter(
        (item) =>
          item.count > 0
      ),
      [
        activePersonnel.length,
        salaryPersonnelIds.size,
        pendingLeaves.length,
        overtimeSummary.pending,
        advanceSummary.pending,
        approvedPayrolls.length,
      ]
    );

  const currencyCode =
    payrolls
      .map(
        (item) =>
          item.currencyCode
      )
      .filter(Boolean)
      .find(Boolean) ??
    "TRY";

  return (
    <ErpShell
      title="İK Dashboard"
      description="Personel, maaş kartı, bordro ve maliyet göstergelerini tek ekrandan yönetin."
    >
      <div
        style={{
          display: "grid",
          gap: "18px",
        }}
      >
        <section
          style={{
            ...panelStyle,
            padding: "16px 18px",
            display: "flex",
            justifyContent:
              "space-between",
            alignItems: "center",
            gap: "16px",
            flexWrap: "wrap",
          }}
        >
          <div>
            <strong
              style={{
                display: "block",
                color: "#0f172a",
                fontSize: "17px",
              }}
            >
              {
                MONTHS[
                  CURRENT_MONTH - 1
                ]
              }{" "}
              {CURRENT_YEAR}
            </strong>

            <span
              style={{
                display: "block",
                marginTop: "4px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              İnsan Kaynakları genel görünümü
            </span>
          </div>

          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "10px",
            }}
          >
            <select
              value={companyId}
              onChange={(event) =>
                setCompanyId(
                  event.target.value
                )
              }
              style={{
                minWidth: "220px",
                minHeight: "42px",
                border:
                  "1px solid #cbd5e1",
                borderRadius: "10px",
                padding: "7px 10px",
                background: "#ffffff",
                color: "#0f172a",
                fontWeight: 700,
              }}
            >
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

            <button
              type="button"
              onClick={() =>
                void loadDashboard()
              }
              disabled={loading}
              style={{
                minHeight: "42px",
                border: "none",
                borderRadius: "10px",
                padding: "0 16px",
                background: "#0f766e",
                color: "#ffffff",
                fontWeight: 800,
                cursor: loading
                  ? "wait"
                  : "pointer",
              }}
            >
              {loading
                ? "Yükleniyor..."
                : "Yenile"}
            </button>
          </div>
        </section>

        {error && (
          <section
            style={{
              ...panelStyle,
              padding: "14px 16px",
              borderColor: "#fecaca",
              background: "#fef2f2",
              color: "#b91c1c",
              fontWeight: 700,
            }}
          >
            {error}
          </section>
        )}

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(4, minmax(0, 1fr))",
            gap: "14px",
          }}
        >
          <KpiCard
            title="Toplam Personel"
            value={number(
              personnel.length
            )}
            detail={`${activePersonnel.length} aktif personel`}
            icon="♙"
          />

          <KpiCard
            title="Maaş Kartı"
            value={number(
              salaryPersonnelIds.size
            )}
            detail={`${Math.max(
              0,
              activePersonnel.length -
                salaryPersonnelIds.size
            )} personelin kartı eksik`}
            icon="₺"
          />

          <KpiCard
            title="Bu Ay Bordro"
            value={number(
              payrolls.length
            )}
            detail={`${paidPayrolls.length} ödendi · ${approvedPayrolls.length} ödeme bekliyor`}
            icon="▧"
          />

          <KpiCard
            title="Bu Ay İşe Giriş"
            value={number(
              startedThisMonth.length
            )}
            detail={`Aktif personelin %${percentage(
              activePersonnel.length,
              personnel.length
            )}'i`}
            icon="+"
          />

          <KpiCard
            title="Toplam Brüt"
            value={money(
              payrollTotals.gross,
              currencyCode
            )}
            detail="Bu ay hesaplanan bordrolar"
            icon="∑"
          />

          <KpiCard
            title="Net Ödenecek"
            value={money(
              payrollTotals.net,
              currencyCode
            )}
            detail={`${approvedPayrolls.length + calculatedPayrolls.length} açık bordro`}
            icon="₺"
          />

          <KpiCard
            title="Toplam Kesinti"
            value={money(
              payrollTotals.deductions,
              currencyCode
            )}
            detail={`SGK ${money(
              payrollTotals.sgk,
              currencyCode
            )}`}
            icon="−"
          />

          <KpiCard
            title="Vergi Toplamı"
            value={money(
              payrollTotals.incomeTax +
                payrollTotals.stampTax,
              currencyCode
            )}
            detail={`Gelir + damga vergisi`}
            icon="%"
          />
        </section>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "minmax(0, 2fr) minmax(300px, 1fr)",
            gap: "18px",
          }}
        >
          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent:
                  "space-between",
                alignItems: "center",
                gap: "12px",
              }}
            >
              <div>
                <h2
                  style={{
                    margin: 0,
                    color: "#0f172a",
                    fontSize: "18px",
                  }}
                >
                  12 Aylık Net Bordro Trendi
                </h2>

                <p
                  style={{
                    margin: "5px 0 0",
                    color: "#64748b",
                    fontSize: "13px",
                  }}
                >
                  Aylık net ödeme tutarları
                </p>
              </div>
            </div>

            <div
              style={{
                height: "260px",
                display: "flex",
                alignItems: "flex-end",
                gap: "9px",
                marginTop: "24px",
                paddingTop: "20px",
                borderBottom:
                  "1px solid #cbd5e1",
              }}
            >
              {payrollTrend.map(
                (item) => (
                  <div
                    key={item.key}
                    title={`${item.label}: ${money(
                      item.net,
                      currencyCode
                    )}`}
                    style={{
                      flex: 1,
                      height: "100%",
                      display: "flex",
                      flexDirection:
                        "column",
                      justifyContent:
                        "flex-end",
                      alignItems:
                        "center",
                      gap: "8px",
                    }}
                  >
                    <span
                      style={{
                        color: "#64748b",
                        fontSize: "10px",
                        fontWeight: 700,
                      }}
                    >
                      {item.count > 0
                        ? item.count
                        : ""}
                    </span>

                    <div
                      style={{
                        width: "100%",
                        maxWidth: "34px",
                        minHeight:
                          item.net > 0
                            ? "8px"
                            : "2px",
                        height: `${Math.max(
                          item.net > 0
                            ? 8
                            : 2,
                          (item.net /
                            maximumTrend) *
                            180
                        )}px`,
                        borderRadius:
                          "7px 7px 0 0",
                        background:
                          item.net > 0
                            ? "#0f766e"
                            : "#e2e8f0",
                      }}
                    />

                    <span
                      style={{
                        minHeight: "25px",
                        color: "#64748b",
                        fontSize: "10px",
                        fontWeight: 700,
                        writingMode:
                          "vertical-rl",
                        transform:
                          "rotate(180deg)",
                      }}
                    >
                      {item.label}
                    </span>
                  </div>
                )
              )}
            </div>
          </article>

          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <h2
              style={{
                margin: 0,
                color: "#0f172a",
                fontSize: "18px",
              }}
            >
              Bordro Durumları
            </h2>

            <p
              style={{
                margin: "5px 0 18px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              Seçili ayın bordro dağılımı
            </p>

            <div
              style={{
                display: "grid",
                gap: "15px",
              }}
            >
              {payrollStatusRows.length ===
                0 && (
                <div
                  style={{
                    padding: "20px",
                    borderRadius: "12px",
                    background: "#f8fafc",
                    color: "#64748b",
                    textAlign: "center",
                  }}
                >
                  Bu ay bordro bulunamadı.
                </div>
              )}

              {payrollStatusRows.map(
                (item) => (
                  <div
                    key={item.label}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent:
                          "space-between",
                        marginBottom: "7px",
                        color: "#334155",
                        fontSize: "13px",
                        fontWeight: 800,
                      }}
                    >
                      <span>
                        {item.label}
                      </span>
                      <span>
                        {item.count}
                      </span>
                    </div>

                    <div
                      style={{
                        height: "9px",
                        overflow: "hidden",
                        borderRadius: "999px",
                        background: "#e2e8f0",
                      }}
                    >
                      <div
                        style={{
                          width: `${percentage(
                            item.count,
                            payrolls.length
                          )}%`,
                          height: "100%",
                          borderRadius: "999px",
                          background: "#2563eb",
                        }}
                      />
                    </div>
                  </div>
                )
              )}
            </div>
          </article>
        </section>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(4, minmax(0, 1fr))",
            gap: "14px",
          }}
        >
          <KpiCard
            title="İzinli Personel"
            value={number(
              personnelOnLeave.length
            )}
            detail={`${pendingLeaves.length} izin talebi bekliyor`}
            icon="○"
          />

          <KpiCard
            title="Fazla Mesai"
            value={`${number(
              overtimeSummary.approved
            )} saat`}
            detail={`${number(
              overtimeSummary.requested
            )} saat talep edildi`}
            icon="◷"
          />

          <KpiCard
            title="Avans Talepleri"
            value={number(
              advances.length
            )}
            detail={`${advanceSummary.pending} bekliyor · ${advanceSummary.paid} ödendi`}
            icon="₺"
          />

          <KpiCard
            title="Onaylı Avans"
            value={money(
              advanceSummary.approved,
              advances[0]
                ?.currencyCode ||
                "TRY"
            )}
            detail={`${money(
              advanceSummary.requested,
              advances[0]
                ?.currencyCode ||
                "TRY"
            )} talep edildi`}
            icon="↗"
          />
        </section>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(3, minmax(0, 1fr))",
            gap: "18px",
          }}
        >
          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <h2
              style={{
                margin: 0,
                color: "#0f172a",
                fontSize: "18px",
              }}
            >
              Şube Dağılımı
            </h2>

            <p
              style={{
                margin: "5px 0 18px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              Personellerin şubelere göre dağılımı
            </p>

            <div
              style={{
                display: "grid",
                gap: "14px",
              }}
            >
              {branchDistribution.length ===
                0 && (
                <div
                  style={{
                    padding: "18px",
                    borderRadius: "12px",
                    background: "#f8fafc",
                    color: "#64748b",
                    textAlign: "center",
                  }}
                >
                  Şube verisi bulunamadı.
                </div>
              )}

              {branchDistribution.map(
                (item) => (
                  <div
                    key={item.label}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent:
                          "space-between",
                        marginBottom: "7px",
                        color: "#334155",
                        fontSize: "13px",
                        fontWeight: 800,
                      }}
                    >
                      <span>
                        {item.label}
                      </span>

                      <span>
                        {item.count}
                      </span>
                    </div>

                    <div
                      style={{
                        height: "9px",
                        overflow: "hidden",
                        borderRadius: "999px",
                        background: "#e2e8f0",
                      }}
                    >
                      <div
                        style={{
                          width: `${percentage(
                            item.count,
                            personnel.length
                          )}%`,
                          height: "100%",
                          borderRadius: "999px",
                          background: "#7c3aed",
                        }}
                      />
                    </div>
                  </div>
                )
              )}
            </div>
          </article>

          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <h2
              style={{
                margin: 0,
                color: "#0f172a",
                fontSize: "18px",
              }}
            >
              Dikkat Gerektiren İşlemler
            </h2>

            <p
              style={{
                margin: "5px 0 18px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              Onay veya işlem bekleyen kayıtlar
            </p>

            {attentionItems.length ===
              0 ? (
              <div
                style={{
                  padding: "22px",
                  borderRadius: "12px",
                  background: "#f0fdf4",
                  color: "#166534",
                  textAlign: "center",
                  fontWeight: 800,
                }}
              >
                Bekleyen kritik işlem bulunmuyor.
              </div>
            ) : (
              <div
                style={{
                  display: "grid",
                  gap: "10px",
                }}
              >
                {attentionItems.map(
                  (item) => (
                    <Link
                      key={item.label}
                      href={item.href}
                      style={{
                        display: "flex",
                        justifyContent:
                          "space-between",
                        alignItems: "center",
                        gap: "12px",
                        padding: "12px 14px",
                        border:
                          "1px solid #fed7aa",
                        borderRadius: "11px",
                        background: "#fff7ed",
                        color: "#9a3412",
                        textDecoration: "none",
                        fontWeight: 800,
                      }}
                    >
                      <span>
                        {item.label}
                      </span>

                      <span
                        style={{
                          minWidth: "28px",
                          height: "28px",
                          display: "grid",
                          placeItems: "center",
                          borderRadius:
                            "999px",
                          background: "#ffedd5",
                        }}
                      >
                        {item.count}
                      </span>
                    </Link>
                  )
                )}
              </div>
            )}
          </article>

          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <h2
              style={{
                margin: 0,
                color: "#0f172a",
                fontSize: "18px",
              }}
            >
              Hızlı İşlemler
            </h2>

            <p
              style={{
                margin: "5px 0 18px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              Sık kullanılan İK ekranları
            </p>

            <div
              style={{
                display: "grid",
                gridTemplateColumns:
                  "repeat(2, minmax(0, 1fr))",
                gap: "10px",
              }}
            >
              {[
                [
                  "Yeni Personel",
                  "/insan-kaynaklari/personeller",
                ],
                [
                  "Maaş Kartı",
                  "/insan-kaynaklari/ucret-kartlari",
                ],
                [
                  "İzin Talebi",
                  "/insan-kaynaklari/izinler",
                ],
                [
                  "Fazla Mesai",
                  "/insan-kaynaklari/fazla-mesai",
                ],
                [
                  "Avans",
                  "/insan-kaynaklari/avanslar",
                ],
                [
                  "Bordro",
                  "/insan-kaynaklari/bordro",
                ],
              ].map(
                ([label, href]) => (
                  <Link
                    key={href}
                    href={href}
                    style={{
                      minHeight: "58px",
                      display: "grid",
                      placeItems: "center",
                      border:
                        "1px solid #cbd5e1",
                      borderRadius: "11px",
                      background: "#f8fafc",
                      color: "#0f172a",
                      textDecoration: "none",
                      textAlign: "center",
                      fontSize: "13px",
                      fontWeight: 800,
                    }}
                  >
                    {label}
                  </Link>
                )
              )}
            </div>
          </article>
        </section>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(2, minmax(0, 1fr))",
            gap: "18px",
          }}
        >
          <article
            style={{
              ...panelStyle,
              padding: "20px",
            }}
          >
            <h2
              style={{
                margin: 0,
                color: "#0f172a",
                fontSize: "18px",
              }}
            >
              Personel Durumları
            </h2>

            <p
              style={{
                margin: "5px 0 18px",
                color: "#64748b",
                fontSize: "13px",
              }}
            >
              Tüm personelin mevcut durumu
            </p>

            <div
              style={{
                display: "grid",
                gap: "15px",
              }}
            >
              {personnelStatusRows.map(
                (item) => (
                  <div
                    key={item.label}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent:
                          "space-between",
                        marginBottom: "7px",
                        color: "#334155",
                        fontSize: "13px",
                        fontWeight: 800,
                      }}
                    >
                      <span>
                        {item.label}
                      </span>
                      <span>
                        {item.count}
                      </span>
                    </div>

                    <div
                      style={{
                        height: "9px",
                        overflow: "hidden",
                        borderRadius: "999px",
                        background: "#e2e8f0",
                      }}
                    >
                      <div
                        style={{
                          width: `${percentage(
                            item.count,
                            personnel.length
                          )}%`,
                          height: "100%",
                          borderRadius: "999px",
                          background: "#0f766e",
                        }}
                      />
                    </div>
                  </div>
                )
              )}
            </div>
          </article>

          <article
            style={{
              ...panelStyle,
              overflow: "hidden",
            }}
          >
            <div
              style={{
                padding: "20px",
                borderBottom:
                  "1px solid #e2e8f0",
              }}
            >
              <h2
                style={{
                  margin: 0,
                  color: "#0f172a",
                  fontSize: "18px",
                }}
              >
                Son Personeller
              </h2>

              <p
                style={{
                  margin: "5px 0 0",
                  color: "#64748b",
                  fontSize: "13px",
                }}
              >
                En son işe başlayan personeller
              </p>
            </div>

            {recentPersonnel.length ===
              0 && (
              <div
                style={{
                  padding: "30px",
                  color: "#64748b",
                  textAlign: "center",
                }}
              >
                Personel kaydı bulunamadı.
              </div>
            )}

            {recentPersonnel.map(
              (item) => (
                <div
                  key={item.id}
                  style={{
                    display: "flex",
                    justifyContent:
                      "space-between",
                    gap: "16px",
                    padding: "14px 20px",
                    borderBottom:
                      "1px solid #eef2f7",
                  }}
                >
                  <div>
                    <strong
                      style={{
                        display: "block",
                        color: "#0f172a",
                      }}
                    >
                      {personnelName(
                        item
                      )}
                    </strong>

                    <span
                      style={{
                        display: "block",
                        marginTop: "4px",
                        color: "#64748b",
                        fontSize: "12px",
                      }}
                    >
                      {item.employeeNumber ||
                        "Personel no yok"}
                      {" · "}
                      {item.jobTitle ||
                        item.profession ||
                        "Görev tanımlanmamış"}
                    </span>
                  </div>

                  <div
                    style={{
                      textAlign: "right",
                    }}
                  >
                    <span
                      style={{
                        display:
                          "inline-block",
                        borderRadius:
                          "999px",
                        padding: "4px 8px",
                        background: "#dcfce7",
                        color: "#166534",
                        fontSize: "11px",
                        fontWeight: 800,
                      }}
                    >
                      {personnelStatus(
                        item
                      )}
                    </span>

                    <span
                      style={{
                        display: "block",
                        marginTop: "5px",
                        color: "#64748b",
                        fontSize: "11px",
                      }}
                    >
                      {date(
                        item
                          .employmentStartDate
                      )}
                    </span>
                  </div>
                </div>
              )
            )}
          </article>
        </section>
      </div>
    </ErpShell>
  );
}
