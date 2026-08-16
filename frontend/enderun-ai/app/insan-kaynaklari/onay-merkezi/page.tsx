"use client";

import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { currencyMoney } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";

import {
  CompanyListItem,
  companyService,
} from "@/services/company.service";

import {
  PersonnelListItem,
  personnelService,
} from "@/services/personnel.service";

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
  PayrollRecord,
  PayrollStatus,
  hrPayrollService,
} from "@/services/hr-payroll.service";

type ApprovalTab =
  | "leave"
  | "overtime"
  | "advance"
  | "payroll";

type LoadError = {
  key: string;
  message: string;
};

type ReasonDialogState = {
  mode: "single" | "selected";
  id?: string;
} | null;

const CURRENT_DATE =
  new Date();

const CURRENT_YEAR =
  CURRENT_DATE.getFullYear();

const CURRENT_MONTH =
  CURRENT_DATE.getMonth() + 1;

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

function formatDate(
  value?: string | null
) {
  if (!value) {
    return "-";
  }

  return new Intl.DateTimeFormat(
    "tr-TR"
  ).format(new Date(value));
}

function money(
  value: number,
  currencyCode = "TRY"
) {
  return currencyMoney(value ?? 0, currencyCode || "TRY");
}

function errorMessage(
  error: unknown
) {
  return error instanceof Error
    ? error.message
    : "Veri yüklenemedi.";
}

function KpiCard({
  title,
  value,
  detail,
  active,
  onClick,
}: {
  title: string;
  value: number;
  detail: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        ...panelStyle,
        padding: "18px",
        textAlign: "left",
        cursor: "pointer",
        borderColor: active
          ? "var(--erp-primary)"
          : "var(--erp-border)",
        background: active
          ? "var(--color-brand-primary-tint)"
          : "var(--erp-panel)",
      }}
    >
      <div
        style={{
          color: "var(--erp-muted)",
          fontSize: "13px",
          fontWeight: 700,
        }}
      >
        {title}
      </div>

      <div
        style={{
          marginTop: "10px",
          color: active
            ? "var(--erp-primary)"
            : "var(--erp-text)",
          fontSize: "30px",
          fontWeight: 900,
        }}
      >
        {value}
      </div>

      <div
        style={{
          marginTop: "5px",
          color: "var(--erp-muted)",
          fontSize: "12px",
        }}
      >
        {detail}
      </div>
    </button>
  );
}

function EmptyState({
  text,
}: {
  text: string;
}) {
  return (
    <div
      style={{
        padding: "36px 20px",
        textAlign: "center",
        color: "var(--erp-muted)",
      }}
    >
      {text}
    </div>
  );
}

function StatusBadge({
  text,
}: {
  text: string;
}) {
  return (
    <span
      style={{
        display: "inline-flex",
        padding: "5px 9px",
        borderRadius: "999px",
        background: "var(--color-semantic-warning-tint)",
        color: "var(--color-semantic-warning)",
        fontSize: "12px",
        fontWeight: 800,
      }}
    >
      {text || "Onay Bekliyor"}
    </span>
  );
}

export default function HrApprovalCenterPage() {
  /*
   * Aksiyon izinleri UÇLARDAN türetildi:
   *   onay (approve)  -> attendance-payroll.approve
   *   red / iptal     -> attendance-payroll.edit
   *
   * Red ve iptal EDIT'e bağlı, APPROVE'a değil: uçlar öyle. Onaylamaya
   * yetkili olmayan ama düzeltme yetkisi olan biri reddedebiliyor.
   */
  const actions = useModuleActions("attendance-payroll");

  const [
    companies,
    setCompanies,
  ] = useState<
    CompanyListItem[]
  >([]);

  const [
    companyId,
    setCompanyId,
  ] = useState("");

  const [
    personnel,
    setPersonnel,
  ] = useState<
    PersonnelListItem[]
  >([]);

  const [
    activeTab,
    setActiveTab,
  ] = useState<ApprovalTab>(
    "leave"
  );

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
    payrolls,
    setPayrolls,
  ] = useState<
    PayrollRecord[]
  >([]);

  const [
    loadErrors,
    setLoadErrors,
  ] = useState<
    LoadError[]
  >([]);

  const [
    loading,
    setLoading,
  ] = useState(false);

  const [
    selectedIds,
    setSelectedIds,
  ] = useState<Set<string>>(
    new Set()
  );

  /** Toplu onay için açılan doğrulama. */
  const [bulkOpen, setBulkOpen] = useState(false);

  const [
    processingIds,
    setProcessingIds,
  ] = useState<Set<string>>(
    new Set()
  );

  const [
    actionMessage,
    setActionMessage,
  ] = useState("");

  const [
    actionError,
    setActionError,
  ] = useState("");

  const [
    reasonDialog,
    setReasonDialog,
  ] = useState<ReasonDialogState>(null);

  const [
    reasonText,
    setReasonText,
  ] = useState("");

  const [
    reasonSubmitting,
    setReasonSubmitting,
  ] = useState(false);

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

  const loadCompanies =
    useCallback(async () => {
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
            selected.id
          );
        }
      } catch (error) {
        setLoadErrors([
          {
            key: "companies",
            message:
              errorMessage(error),
          },
        ]);
      }
    }, []);

  const loadData =
    useCallback(async () => {
      if (!companyId) {
        return;
      }

      setLoading(true);
      setLoadErrors([]);

      const results =
        await Promise.allSettled([
          personnelService.getAll({
            companyId,
          }),

          hrLeaveService.getAll({
            companyId,
            status: 1,
          }),

          hrOvertimeService.getAll({
            companyId,
            status: 1,
          }),

          hrAdvanceService.getAll({
            companyId,
            status: 1,
          }),

          hrPayrollService.getAll({
            companyId,
            year: CURRENT_YEAR,
            month: CURRENT_MONTH,
            status:
              PayrollStatus.Calculated,
          }),
        ]);

      const errors: LoadError[] =
        [];

      const [
        personnelResult,
        leaveResult,
        overtimeResult,
        advanceResult,
        payrollResult,
      ] = results;

      if (
        personnelResult.status ===
        "fulfilled"
      ) {
        setPersonnel(
          personnelResult.value
        );
      } else {
        setPersonnel([]);
        errors.push({
          key: "personnel",
          message:
            `Personel verisi: ${errorMessage(
              personnelResult.reason
            )}`,
        });
      }

      if (
        leaveResult.status ===
        "fulfilled"
      ) {
        setLeaves(
          leaveResult.value
        );
      } else {
        setLeaves([]);
        errors.push({
          key: "leave",
          message:
            `İzin verisi: ${errorMessage(
              leaveResult.reason
            )}`,
        });
      }

      if (
        overtimeResult.status ===
        "fulfilled"
      ) {
        setOvertimes(
          overtimeResult.value
        );
      } else {
        setOvertimes([]);
        errors.push({
          key: "overtime",
          message:
            `Fazla mesai verisi: ${errorMessage(
              overtimeResult.reason
            )}`,
        });
      }

      if (
        advanceResult.status ===
        "fulfilled"
      ) {
        setAdvances(
          advanceResult.value
        );
      } else {
        setAdvances([]);
        errors.push({
          key: "advance",
          message:
            `Avans verisi: ${errorMessage(
              advanceResult.reason
            )}`,
        });
      }

      if (
        payrollResult.status ===
        "fulfilled"
      ) {
        setPayrolls(
          payrollResult.value
        );
      } else {
        setPayrolls([]);
        errors.push({
          key: "payroll",
          message:
            `Bordro verisi: ${errorMessage(
              payrollResult.reason
            )}`,
        });
      }

      setLoadErrors(errors);
      setLoading(false);
    }, [companyId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const totalPending =
    leaves.length +
    overtimes.length +
    advances.length +
    payrolls.length;

  const activeIds =
    useMemo(() => {
      if (activeTab === "leave") {
        return leaves.map(
          (item) => item.id
        );
      }

      if (activeTab === "overtime") {
        return overtimes.map(
          (item) => item.id
        );
      }

      if (activeTab === "advance") {
        return advances.map(
          (item) => item.id
        );
      }

      return payrolls.map(
        (item) => item.id
      );
    }, [
      activeTab,
      leaves,
      overtimes,
      advances,
      payrolls,
    ]);

  const selectedVisibleCount =
    activeIds.filter(
      (id) => selectedIds.has(id)
    ).length;

  const allVisibleSelected =
    activeIds.length > 0 &&
    selectedVisibleCount ===
      activeIds.length;

  useEffect(() => {
    setSelectedIds(new Set());
    setActionMessage("");
    setActionError("");
    setReasonDialog(null);
    setReasonText("");
  }, [
    activeTab,
    companyId,
  ]);

  function toggleSelection(
    id: string
  ) {
    setSelectedIds(
      (current) => {
        const next =
          new Set(current);

        if (next.has(id)) {
          next.delete(id);
        } else {
          next.add(id);
        }

        return next;
      }
    );
  }

  function toggleAllVisible() {
    setSelectedIds(
      (current) => {
        const next =
          new Set(current);

        if (allVisibleSelected) {
          activeIds.forEach(
            (id) =>
              next.delete(id)
          );
        } else {
          activeIds.forEach(
            (id) =>
              next.add(id)
          );
        }

        return next;
      }
    );
  }

  async function approveByType(
    id: string
  ) {
    if (activeTab === "leave") {
      await hrLeaveService.approve(id);
      return;
    }

    if (activeTab === "overtime") {
      await hrOvertimeService.approve(id);
      return;
    }

    if (activeTab === "advance") {
      await hrAdvanceService.approve(id);
      return;
    }

    await hrPayrollService.approve(id);
  }

  function actionNoun() {
    if (activeTab === "leave") {
      return "izin talebi";
    }

    if (activeTab === "overtime") {
      return "fazla mesai kaydı";
    }

    if (activeTab === "advance") {
      return "avans talebi";
    }

    return "bordro";
  }

  function negativeActionLabel() {
    return activeTab === "payroll"
      ? "İptal Et"
      : "Reddet";
  }

  async function rejectByType(
    id: string,
    reason: string
  ) {
    if (activeTab === "leave") {
      await hrLeaveService.reject(
        id,
        reason
      );
      return;
    }

    if (activeTab === "overtime") {
      await hrOvertimeService.reject(
        id,
        reason
      );
      return;
    }

    if (activeTab === "advance") {
      await hrAdvanceService.reject(
        id,
        reason
      );
      return;
    }

    await hrPayrollService.cancel(
      id,
      reason
    );
  }

  function openSingleReasonDialog(
    id: string
  ) {
    setActionMessage("");
    setActionError("");
    setReasonText("");
    setReasonDialog({
      mode: "single",
      id,
    });
  }

  function openSelectedReasonDialog() {
    if (selectedVisibleCount === 0) {
      setActionError(
        activeTab === "payroll"
          ? "İptal etmek için en az bir bordro seçin."
          : "Reddetmek için en az bir kayıt seçin."
      );
      return;
    }

    setActionMessage("");
    setActionError("");
    setReasonText("");
    setReasonDialog({
      mode: "selected",
    });
  }

  function closeReasonDialog() {
    if (reasonSubmitting) {
      return;
    }

    setReasonDialog(null);
    setReasonText("");
  }

  async function submitReasonDialog() {
    const reason =
      reasonText.trim();

    if (!reason) {
      setActionError(
        activeTab === "payroll"
          ? "Bordro iptal gerekçesi zorunludur."
          : "Ret gerekçesi zorunludur."
      );
      return;
    }

    if (!reasonDialog) {
      return;
    }

    const ids =
      reasonDialog.mode === "single"
        ? reasonDialog.id
          ? [reasonDialog.id]
          : []
        : activeIds.filter(
            (id) =>
              selectedIds.has(id)
          );

    if (ids.length === 0) {
      setActionError(
        "İşlem yapılacak kayıt bulunamadı."
      );
      setReasonDialog(null);
      return;
    }

    setReasonSubmitting(true);
    setActionMessage("");
    setActionError("");
    setProcessingIds(
      new Set(ids)
    );

    try {
      const results =
        await Promise.allSettled(
          ids.map(
            (id) =>
              rejectByType(
                id,
                reason
              )
          )
        );

      const successCount =
        results.filter(
          (result) =>
            result.status ===
            "fulfilled"
        ).length;

      const failureResults =
        results.filter(
          (result) =>
            result.status ===
            "rejected"
        );

      if (successCount > 0) {
        setActionMessage(
          activeTab === "payroll"
            ? `${successCount} bordro iptal edildi.`
            : `${successCount} kayıt reddedildi.`
        );
      }

      if (
        failureResults.length > 0
      ) {
        const firstFailure =
          failureResults[0];

        setActionError(
          firstFailure.status ===
            "rejected"
            ? errorMessage(
                firstFailure.reason
              )
            : `${failureResults.length} kayıt işlenemedi.`
        );
      }

      setSelectedIds(
        new Set()
      );

      setReasonDialog(null);
      setReasonText("");

      await loadData();
    } finally {
      setProcessingIds(
        new Set()
      );
      setReasonSubmitting(false);
    }
  }

  async function approveSingle(
    id: string
  ) {
    setActionMessage("");
    setActionError("");

    setProcessingIds(
      (current) => {
        const next =
          new Set(current);

        next.add(id);

        return next;
      }
    );

    try {
      await approveByType(id);

      setActionMessage(
        "Kayıt başarıyla onaylandı."
      );

      setSelectedIds(
        (current) => {
          const next =
            new Set(current);

          next.delete(id);

          return next;
        }
      );

      await loadData();
    } catch (error) {
      setActionError(
        errorMessage(error)
      );
    } finally {
      setProcessingIds(
        (current) => {
          const next =
            new Set(current);

          next.delete(id);

          return next;
        }
      );
    }
  }

  async function approveSelected() {
    const ids =
      activeIds.filter(
        (id) =>
          selectedIds.has(id)
      );

    if (ids.length === 0) {
      setActionError(
        "Onaylamak için en az bir kayıt seçin."
      );
      return;
    }

    setBulkOpen(false);
    setActionMessage("");
    setActionError("");
    setProcessingIds(
      new Set(ids)
    );

    const results =
      await Promise.allSettled(
        ids.map(
          (id) =>
            approveByType(id)
        )
      );

    const successCount =
      results.filter(
        (result) =>
          result.status ===
          "fulfilled"
      ).length;

    const failureCount =
      results.length -
      successCount;

    if (successCount > 0) {
      setActionMessage(
        `${successCount} kayıt başarıyla onaylandı.`
      );
    }

    if (failureCount > 0) {
      setActionError(
        `${failureCount} kayıt onaylanamadı.`
      );
    }

    setProcessingIds(
      new Set()
    );

    setSelectedIds(
      new Set()
    );

    await loadData();
  }

  return (
    <ErpShell
      design="redwood"
      title="İK Onay Merkezi"
      description="İzin, fazla mesai, avans ve bordro onaylarını tek merkezden yönetin."
    >
      <div
        style={{
          display: "grid",
          gap: "18px",
        }}
      >
        {actionMessage && (
          <section
            style={{
              ...panelStyle,
              padding: "14px 16px",
              borderColor:
                "var(--color-semantic-success-border)",
              background:
                "var(--color-semantic-success-tint)",
              color: "var(--color-semantic-success)",
              fontWeight: 800,
            }}
          >
            {actionMessage}
          </section>
        )}

        {actionError && (
          <section
            style={{
              ...panelStyle,
              padding: "14px 16px",
              borderColor:
                "var(--color-semantic-danger-border)",
              background:
                "var(--color-semantic-danger-tint)",
              color: "var(--color-semantic-danger)",
              fontWeight: 800,
            }}
          >
            {actionError}
          </section>
        )}

        {loadErrors.length >
          0 && (
          <section
            style={{
              ...panelStyle,
              padding: "14px 16px",
              borderColor:
                "var(--color-semantic-warning-border)",
              background:
                "var(--color-semantic-warning-tint)",
              color: "var(--color-semantic-warning)",
            }}
          >
            <strong>
              Bazı veriler yüklenemedi:
            </strong>

            <div
              style={{
                display: "grid",
                gap: "5px",
                marginTop: "8px",
                fontSize: "13px",
              }}
            >
              {loadErrors.map(
                (item) => (
                  <div
                    key={item.key}
                  >
                    {item.message}
                  </div>
                )
              )}
            </div>
          </section>
        )}

        <section
          style={{
            ...panelStyle,
            padding: "18px",
            display: "grid",
            gridTemplateColumns:
              "minmax(260px, 1fr) auto auto",
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
              value={companyId}
              onChange={(event) =>
                setCompanyId(
                  event.target.value
                )
              }
              style={inputStyle}
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
          </label>

          <div
            style={{
              minWidth: "150px",
              padding: "9px 14px",
              borderRadius: "10px",
              background: "var(--erp-bg)",
              border:
                "1px solid var(--erp-border)",
            }}
          >
            <div
              style={{
                color: "var(--erp-muted)",
                fontSize: "12px",
              }}
            >
              Toplam bekleyen
            </div>

            <strong
              style={{
                display: "block",
                marginTop: "3px",
                fontSize: "20px",
              }}
            >
              {totalPending}
            </strong>
          </div>

          <button
            type="button"
            onClick={() =>
              void loadData()
            }
            disabled={
              loading ||
              !companyId
            }
            style={{
              minHeight: "42px",
              border: "none",
              borderRadius: "10px",
              padding: "0 18px",
              background: "var(--erp-primary)",
              color: "var(--color-on-brand)",
              fontWeight: 800,
              cursor: "pointer",
            }}
          >
            {loading
              ? "Yükleniyor..."
              : "Yenile"}
          </button>
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
            title="Bekleyen İzin"
            value={leaves.length}
            detail="Onay bekleyen izin talebi"
            active={
              activeTab === "leave"
            }
            onClick={() =>
              setActiveTab("leave")
            }
          />

          <KpiCard
            title="Bekleyen Fazla Mesai"
            value={overtimes.length}
            detail="Onay bekleyen mesai kaydı"
            active={
              activeTab ===
              "overtime"
            }
            onClick={() =>
              setActiveTab(
                "overtime"
              )
            }
          />

          <KpiCard
            title="Bekleyen Avans"
            value={advances.length}
            detail="Onay bekleyen avans talebi"
            active={
              activeTab ===
              "advance"
            }
            onClick={() =>
              setActiveTab(
                "advance"
              )
            }
          />

          <KpiCard
            title="Bekleyen Bordro"
            value={payrolls.length}
            detail={`${CURRENT_MONTH}/${CURRENT_YEAR} hesaplanan bordro`}
            active={
              activeTab ===
              "payroll"
            }
            onClick={() =>
              setActiveTab(
                "payroll"
              )
            }
          />
        </section>

        <section
          style={{
            ...panelStyle,
            padding: "14px 16px",
            display: "flex",
            justifyContent:
              "space-between",
            alignItems: "center",
            gap: "14px",
          }}
        >
          <div>
            <strong
              style={{
                display: "block",
                color: "var(--erp-text)",
              }}
            >
              Toplu İşlemler
            </strong>

            <span
              style={{
                display: "block",
                marginTop: "4px",
                color: "var(--erp-muted)",
                fontSize: "13px",
              }}
            >
              {selectedVisibleCount} kayıt seçildi
            </span>
          </div>

          <div
            style={{
              display: "flex",
              gap: "10px",
              alignItems: "center",
            }}
          >
            <button
              type="button"
              onClick={toggleAllVisible}
              disabled={
                activeIds.length === 0 ||
                processingIds.size > 0
              }
              style={{
                minHeight: "40px",
                border:
                  "1px solid var(--erp-border)",
                borderRadius: "10px",
                padding: "0 15px",
                background: "var(--erp-panel)",
                color: "var(--erp-muted)",
                fontWeight: 800,
                cursor: "pointer",
              }}
            >
              {allVisibleSelected
                ? "Seçimi Kaldır"
                : "Tümünü Seç"}
            </button>

            {actions.can("approve") && (
              <button
                type="button"
                onClick={() =>
                  setBulkOpen(true)
                }
                disabled={
                  selectedVisibleCount ===
                    0 ||
                  processingIds.size > 0
                }
                style={{
                  minHeight: "40px",
                  border: "none",
                  borderRadius: "10px",
                  padding: "0 17px",
                  background:
                    selectedVisibleCount > 0
                      ? "var(--color-semantic-success)"
                      : "var(--erp-muted)",
                  color: "var(--color-on-brand)",
                  fontWeight: 800,
                  cursor:
                    selectedVisibleCount > 0
                      ? "pointer"
                      : "not-allowed",
                }}
              >
                {processingIds.size > 0
                  ? "İşleniyor..."
                  : "Seçilenleri Onayla"}
              </button>
            )}

            {actions.can("edit") && (
              <button
                type="button"
                onClick={
                  openSelectedReasonDialog
                }
                disabled={
                  selectedVisibleCount ===
                    0 ||
                  processingIds.size > 0
                }
                style={{
                  minHeight: "40px",
                  border: "none",
                  borderRadius: "10px",
                  padding: "0 17px",
                  background:
                    selectedVisibleCount > 0
                      ? "var(--color-semantic-danger)"
                      : "var(--erp-muted)",
                  color: "var(--color-on-brand)",
                  fontWeight: 800,
                  cursor:
                    selectedVisibleCount > 0
                      ? "pointer"
                      : "not-allowed",
                }}
              >
                {activeTab === "payroll"
                  ? "Seçilenleri İptal Et"
                  : "Seçilenleri Reddet"}
              </button>
            )}
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
              display: "flex",
              gap: "6px",
              padding: "12px",
              borderBottom:
                "1px solid var(--erp-border)",
              background: "var(--erp-bg)",
            }}
          >
            {[
              [
                "leave",
                `İzinler (${leaves.length})`,
              ],
              [
                "overtime",
                `Fazla Mesai (${overtimes.length})`,
              ],
              [
                "advance",
                `Avanslar (${advances.length})`,
              ],
              [
                "payroll",
                `Bordrolar (${payrolls.length})`,
              ],
            ].map(
              ([key, label]) => (
                <button
                  key={key}
                  type="button"
                  onClick={() =>
                    setActiveTab(
                      key as ApprovalTab
                    )
                  }
                  style={{
                    minHeight: "38px",
                    border:
                      activeTab === key
                        ? "1px solid var(--erp-primary)"
                        : "1px solid transparent",
                    borderRadius:
                      "9px",
                    padding:
                      "0 14px",
                    background:
                      activeTab === key
                        ? "var(--color-brand-primary-tint)"
                        : "transparent",
                    color:
                      activeTab === key
                        ? "var(--erp-primary)"
                        : "var(--erp-muted)",
                    fontWeight: 800,
                    cursor: "pointer",
                  }}
                >
                  {label}
                </button>
              )
            )}
          </div>

          {activeTab ===
            "leave" && (
            <table
              style={{
                width: "100%",
                borderCollapse:
                  "collapse",
                minWidth: "980px",
              }}
            >
              <thead>
                <tr
                  style={{
                    background:
                      "var(--erp-panel)",
                  }}
                >
                  {[
                    "Seç",
                    "Personel",
                    "İzin Türü",
                    "Başlangıç",
                    "Bitiş",
                    "Gün",
                    "Sebep",
                    "Durum",
                    "İşlem",
                  ].map((header) => (
                    <th
                      key={header}
                      style={{
                        padding:
                          "13px",
                        textAlign:
                          "left",
                        borderBottom:
                          "1px solid var(--erp-border)",
                      }}
                    >
                      {header}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody>
                {leaves.map(
                  (item) => (
                    <tr key={item.id}>
                      <td
                        style={{
                          width: "58px",
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          textAlign:
                            "center",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={
                            selectedIds.has(
                              item.id
                            )
                          }
                          disabled={
                            processingIds.has(
                              item.id
                            )
                          }
                          onChange={() =>
                            toggleSelection(
                              item.id
                            )
                          }
                          style={{
                            width: "17px",
                            height: "17px",
                            cursor: "pointer",
                          }}
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            700,
                        }}
                      >
                        {personnelMap.get(
                          item.personnelId
                        )?.fullName ??
                          "Personel"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {
                          item.leaveTypeName
                        }
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {formatDate(
                          item.startDate
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {formatDate(
                          item.endDate
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            800,
                        }}
                      >
                        {item.totalDays}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.reason ||
                          "-"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <StatusBadge
                          text={
                            item.statusName
                          }
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            gap: "7px",
                            alignItems:
                              "center",
                            flexWrap:
                              "wrap",
                          }}
                        >
                          {actions.can("approve") && actions.can("approve") && actions.can("approve") && actions.can("approve") && (
                            <button
                              type="button"
                              onClick={() =>
                                void approveSingle(
                                  item.id
                                )
                              }
                              disabled={
                                processingIds.has(
                                  item.id
                                )
                              }
                              style={{
                                minHeight:
                                  "34px",
                                border:
                                  "none",
                                borderRadius:
                                  "8px",
                                padding:
                                  "0 13px",
                                background:
                                  "var(--color-semantic-success)",
                                color:
                                  "var(--erp-panel)",
                                fontSize:
                                  "12px",
                                fontWeight:
                                  800,
                                cursor:
                                  processingIds.has(
                                    item.id
                                  )
                                    ? "not-allowed"
                                    : "pointer",
                                opacity:
                                  processingIds.has(
                                    item.id
                                  )
                                    ? 0.65
                                    : 1,
                              }}
                            >
                              {processingIds.has(
                                item.id
                              )
                                ? "İşleniyor..."
                                : "Onayla"}
                            </button>
                          )}

                          {actions.can("edit") && actions.can("edit") && actions.can("edit") && actions.can("edit") && actions.can("edit") && (
                            <button
                              type="button"
                              onClick={() =>
                                openSingleReasonDialog(
                                  item.id
                                )
                              }
                              disabled={
                                processingIds.has(
                                  item.id
                                )
                              }
                              style={{
                                minHeight:
                                  "34px",
                                border:
                                  "none",
                                borderRadius:
                                  "8px",
                                padding:
                                  "0 13px",
                                background:
                                  "var(--color-semantic-danger)",
                                color:
                                  "var(--erp-panel)",
                                fontSize:
                                  "12px",
                                fontWeight:
                                  800,
                                cursor:
                                  processingIds.has(
                                    item.id
                                  )
                                    ? "not-allowed"
                                    : "pointer",
                                opacity:
                                  processingIds.has(
                                    item.id
                                  )
                                    ? 0.65
                                    : 1,
                              }}
                            >
                              {negativeActionLabel()}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                )}

                {leaves.length ===
                  0 && (
                  <tr>
                    <td colSpan={11}>
                      <EmptyState text="Onay bekleyen izin talebi bulunmuyor." />
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}

          {activeTab ===
            "overtime" && (
            <table
              style={{
                width: "100%",
                borderCollapse:
                  "collapse",
                minWidth: "1050px",
              }}
            >
              <thead>
                <tr>
                  {[
                    "Seç",
                    "Personel",
                    "Tarih",
                    "Talep",
                    "Onay",
                    "Pazar",
                    "Resmî Tatil",
                    "Sebep",
                    "Durum",
                    "İşlem",
                  ].map((header) => (
                    <th
                      key={header}
                      style={{
                        padding:
                          "13px",
                        textAlign:
                          "left",
                        borderBottom:
                          "1px solid var(--erp-border)",
                      }}
                    >
                      {header}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody>
                {overtimes.map(
                  (item) => (
                    <tr key={item.id}>
                      <td
                        style={{
                          width: "58px",
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          textAlign:
                            "center",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={
                            selectedIds.has(
                              item.id
                            )
                          }
                          disabled={
                            processingIds.has(
                              item.id
                            )
                          }
                          onChange={() =>
                            toggleSelection(
                              item.id
                            )
                          }
                          style={{
                            width: "17px",
                            height: "17px",
                            cursor: "pointer",
                          }}
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            700,
                        }}
                      >
                        {personnelMap.get(
                          item.personnelId
                        )?.fullName ??
                          "Personel"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {formatDate(
                          item.workDate
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.requestedHours} saat
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.approvedHours} saat
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.isSundayWork
                          ? "Evet"
                          : "Hayır"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item
                          .isPublicHolidayWork
                          ? "Evet"
                          : "Hayır"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.reason ||
                          "-"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <StatusBadge
                          text={
                            item.statusName
                          }
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            gap: "7px",
                            alignItems:
                              "center",
                            flexWrap:
                              "wrap",
                          }}
                        >
                          <button
                            type="button"
                            onClick={() =>
                              void approveSingle(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-success)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {processingIds.has(
                              item.id
                            )
                              ? "İşleniyor..."
                              : "Onayla"}
                          </button>

                          <button
                            type="button"
                            onClick={() =>
                              openSingleReasonDialog(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-danger)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {negativeActionLabel()}
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                )}

                {overtimes.length ===
                  0 && (
                  <tr>
                    <td colSpan={9}>
                      <EmptyState text="Onay bekleyen fazla mesai kaydı bulunmuyor." />
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}

          {activeTab ===
            "advance" && (
            <table
              style={{
                width: "100%",
                borderCollapse:
                  "collapse",
                minWidth: "1000px",
              }}
            >
              <thead>
                <tr>
                  {[
                    "Seç",
                    "Personel",
                    "Talep Tarihi",
                    "Talep Tutarı",
                    "Onay Tutarı",
                    "Taksit",
                    "Sebep",
                    "Durum",
                    "İşlem",
                  ].map((header) => (
                    <th
                      key={header}
                      style={{
                        padding:
                          "13px",
                        textAlign:
                          "left",
                        borderBottom:
                          "1px solid var(--erp-border)",
                      }}
                    >
                      {header}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody>
                {advances.map(
                  (item) => (
                    <tr key={item.id}>
                      <td
                        style={{
                          width: "58px",
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          textAlign:
                            "center",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={
                            selectedIds.has(
                              item.id
                            )
                          }
                          disabled={
                            processingIds.has(
                              item.id
                            )
                          }
                          onChange={() =>
                            toggleSelection(
                              item.id
                            )
                          }
                          style={{
                            width: "17px",
                            height: "17px",
                            cursor: "pointer",
                          }}
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            700,
                        }}
                      >
                        {personnelMap.get(
                          item.personnelId
                        )?.fullName ??
                          "Personel"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {formatDate(
                          item.requestDate
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            800,
                        }}
                      >
                        {money(
                          item.requestedAmount,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {money(
                          item.approvedAmount,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {
                          item.deductionInstallmentCount
                        }
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.reason ||
                          "-"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <StatusBadge
                          text={
                            item.statusName
                          }
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            gap: "7px",
                            alignItems:
                              "center",
                            flexWrap:
                              "wrap",
                          }}
                        >
                          <button
                            type="button"
                            onClick={() =>
                              void approveSingle(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-success)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {processingIds.has(
                              item.id
                            )
                              ? "İşleniyor..."
                              : "Onayla"}
                          </button>

                          <button
                            type="button"
                            onClick={() =>
                              openSingleReasonDialog(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-danger)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {negativeActionLabel()}
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                )}

                {advances.length ===
                  0 && (
                  <tr>
                    <td colSpan={9}>
                      <EmptyState text="Onay bekleyen avans talebi bulunmuyor." />
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}

          {activeTab ===
            "payroll" && (
            <table
              style={{
                width: "100%",
                borderCollapse:
                  "collapse",
                minWidth: "1050px",
              }}
            >
              <thead>
                <tr>
                  {[
                    "Seç",
                    "Personel",
                    "Dönem",
                    "Brüt",
                    "Toplam Kazanç",
                    "Toplam Kesinti",
                    "Net Ödenecek",
                    "Durum",
                    "İşlem",
                  ].map((header) => (
                    <th
                      key={header}
                      style={{
                        padding:
                          "13px",
                        textAlign:
                          "left",
                        borderBottom:
                          "1px solid var(--erp-border)",
                      }}
                    >
                      {header}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody>
                {payrolls.map(
                  (item) => (
                    <tr key={item.id}>
                      <td
                        style={{
                          width: "58px",
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          textAlign:
                            "center",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={
                            selectedIds.has(
                              item.id
                            )
                          }
                          disabled={
                            processingIds.has(
                              item.id
                            )
                          }
                          onChange={() =>
                            toggleSelection(
                              item.id
                            )
                          }
                          style={{
                            width: "17px",
                            height: "17px",
                            cursor: "pointer",
                          }}
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            700,
                        }}
                      >
                        {personnelMap.get(
                          item.personnelId
                        )?.fullName ??
                          "Personel"}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {item.month}/
                        {item.year}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {money(
                          item.grossSalary,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {money(
                          item.totalEarnings,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        {money(
                          item.totalDeductions,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                          fontWeight:
                            900,
                        }}
                      >
                        {money(
                          item.actualPayableAmount ||
                            item.netPayableAmount,
                          item.currencyCode
                        )}
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <StatusBadge
                          text={
                            item.statusName
                          }
                        />
                      </td>

                      <td
                        style={{
                          padding:
                            "12px 13px",
                          borderBottom:
                            "1px solid var(--erp-border)",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            gap: "7px",
                            alignItems:
                              "center",
                            flexWrap:
                              "wrap",
                          }}
                        >
                          <button
                            type="button"
                            onClick={() =>
                              void approveSingle(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-success)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {processingIds.has(
                              item.id
                            )
                              ? "İşleniyor..."
                              : "Onayla"}
                          </button>

                          <button
                            type="button"
                            onClick={() =>
                              openSingleReasonDialog(
                                item.id
                              )
                            }
                            disabled={
                              processingIds.has(
                                item.id
                              )
                            }
                            style={{
                              minHeight:
                                "34px",
                              border:
                                "none",
                              borderRadius:
                                "8px",
                              padding:
                                "0 13px",
                              background:
                                "var(--color-semantic-danger)",
                              color:
                                "var(--erp-panel)",
                              fontSize:
                                "12px",
                              fontWeight:
                                800,
                              cursor:
                                processingIds.has(
                                  item.id
                                )
                                  ? "not-allowed"
                                  : "pointer",
                              opacity:
                                processingIds.has(
                                  item.id
                                )
                                  ? 0.65
                                  : 1,
                            }}
                          >
                            {negativeActionLabel()}
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                )}

                {payrolls.length ===
                  0 && (
                  <tr>
                    <td colSpan={9}>
                      <EmptyState text="Onay bekleyen bordro bulunmuyor." />
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}
        </section>
      </div>
      {reasonDialog && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="hr-reason-dialog-title"
          onMouseDown={(event) => {
            if (
              event.target ===
              event.currentTarget
            ) {
              closeReasonDialog();
            }
          }}
          style={{
            position: "fixed",
            inset: 0,
            zIndex: 1000,
            display: "grid",
            placeItems: "center",
            padding: "20px",
            background:
              "rgba(15, 23, 42, 0.55)",
          }}
        >
          <section
            style={{
              width: "min(560px, 100%)",
              borderRadius: "18px",
              border:
                "1px solid var(--erp-border)",
              background: "var(--erp-panel)",
              boxShadow:
                "0 24px 70px rgba(15, 23, 42, 0.28)",
              overflow: "hidden",
            }}
          >
            <header
              style={{
                padding:
                  "20px 22px 16px",
                borderBottom:
                  "1px solid var(--erp-border)",
              }}
            >
              <h2
                id="hr-reason-dialog-title"
                style={{
                  margin: 0,
                  color: "var(--erp-text)",
                  fontSize: "20px",
                }}
              >
                {activeTab === "payroll"
                  ? "Bordro İptal Gerekçesi"
                  : "Ret Gerekçesi"}
              </h2>

              <p
                style={{
                  margin:
                    "7px 0 0",
                  color: "var(--erp-muted)",
                  fontSize: "13px",
                  lineHeight: 1.5,
                }}
              >
                {reasonDialog.mode ===
                "selected"
                  ? `${selectedVisibleCount} ${actionNoun()} için aynı gerekçe uygulanacaktır.`
                  : `Seçilen ${actionNoun()} için gerekçe yazın.`}
              </p>
            </header>

            <div
              style={{
                padding: "20px 22px",
              }}
            >
              <label>
                <span
                  style={{
                    display: "block",
                    marginBottom:
                      "8px",
                    color: "var(--erp-muted)",
                    fontWeight:
                      800,
                  }}
                >
                  Gerekçe
                </span>

                <textarea
                  autoFocus
                  value={reasonText}
                  onChange={(event) =>
                    setReasonText(
                      event.target.value
                    )
                  }
                  disabled={
                    reasonSubmitting
                  }
                  rows={6}
                  maxLength={2000}
                  placeholder={
                    activeTab ===
                    "payroll"
                      ? "Bordronun neden iptal edildiğini açıklayın..."
                      : "Talebin neden reddedildiğini açıklayın..."
                  }
                  style={{
                    width: "100%",
                    resize: "vertical",
                    minHeight:
                      "130px",
                    border:
                      "1px solid var(--erp-border)",
                    borderRadius:
                      "12px",
                    padding:
                      "12px 13px",
                    color: "var(--erp-text)",
                    background:
                      "var(--erp-panel)",
                    fontFamily:
                      "inherit",
                    fontSize:
                      "14px",
                    lineHeight: 1.5,
                    boxSizing:
                      "border-box",
                  }}
                />

                <div
                  style={{
                    marginTop: "6px",
                    color: "var(--erp-muted)",
                    fontSize: "12px",
                    textAlign:
                      "right",
                  }}
                >
                  {reasonText.length}/2000
                </div>
              </label>
            </div>

            <footer
              style={{
                display: "flex",
                justifyContent:
                  "flex-end",
                gap: "10px",
                padding:
                  "16px 22px 20px",
                borderTop:
                  "1px solid var(--erp-border)",
              }}
            >
              <button
                type="button"
                onClick={
                  closeReasonDialog
                }
                disabled={
                  reasonSubmitting
                }
                style={{
                  minHeight:
                    "40px",
                  border:
                    "1px solid var(--erp-border)",
                  borderRadius:
                    "10px",
                  padding:
                    "0 17px",
                  background:
                    "var(--erp-panel)",
                  color: "var(--erp-muted)",
                  fontWeight:
                    800,
                  cursor:
                    reasonSubmitting
                      ? "not-allowed"
                      : "pointer",
                }}
              >
                Vazgeç
              </button>

              <button
                type="button"
                onClick={() =>
                  void submitReasonDialog()
                }
                disabled={
                  reasonSubmitting ||
                  !reasonText.trim()
                }
                style={{
                  minHeight:
                    "40px",
                  border: "none",
                  borderRadius:
                    "10px",
                  padding:
                    "0 18px",
                  background:
                    reasonText.trim()
                      ? "var(--color-semantic-danger)"
                      : "var(--erp-muted)",
                  color: "var(--color-on-brand)",
                  fontWeight:
                    800,
                  cursor:
                    reasonSubmitting ||
                    !reasonText.trim()
                      ? "not-allowed"
                      : "pointer",
                }}
              >
                {reasonSubmitting
                  ? "İşleniyor..."
                  : negativeActionLabel()}
              </button>
            </footer>
          </section>
        </div>
      )}
      <ConfirmDialog
        open={bulkOpen}
        title="Seçili Kayıtları Onayla"
        description={`${selectedVisibleCount} kayıt onaylanacak. Onaylanan kayıtlar ilgili modüllerde işleme girer ve geri alınması tek tek yapılır.`}
        confirmLabel={`${selectedVisibleCount} Kaydı Onayla`}
        busy={processingIds.size > 0}
        error={actionError}
        onCancel={() => setBulkOpen(false)}
        onConfirm={() => void approveSelected()}
      />
    </ErpShell>
  );
}
