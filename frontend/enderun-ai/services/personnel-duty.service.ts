import { apiClient } from "@/lib/api/api-client";

/**
 * Görevlendirme türü. Hangi MALİYET YOLUNUN çalışacağını bu belirler:
 * yalnız çalışma görevlendirmesinde gün maliyeti hedef projeye kayar;
 * keşif ve ziyarette sadece masraf yansır.
 */
export type DutyType = 0 | 1 | 2;

/** 0 talep · 1 onaylı · 2 reddedildi · 3 tamamlandı · 4 iptal */
export type DutyStatus = 0 | 1 | 2 | 3 | 4;

/** 0 personelden düş · 1 gider kabul et */
export type SettlementDecision = 0 | 1;

export type PersonnelDutyItem = {
  id: string;
  personnelId: string;
  personnelFullName: string;
  dutyType: DutyType;
  dutyTypeName: string;
  shiftsLaborCost: boolean;
  targetProjectId: string;
  targetProjectCode: string;
  targetProjectName: string;
  sourceProjectId?: string | null;
  startDate: string;
  endDate: string;
  dayCount: number;
  isOutOfCity: boolean;
  purpose: string;
  status: DutyStatus;
  statusName: string;
  hasSurveyReport: boolean;
  requestedAtUtc: string;
  approvedAtUtc?: string | null;
  decisionNote?: string | null;

  /**
   * Tutarlar ELDEN izolasyonuna tabi: extra_payment.view yoksa
   * gizlenmez, hiç gelmez (null). Mahsup bekliyor bilgisi de mali bir
   * durumdur ve aynı kapıdan geçer.
   */
  amountsHidden: boolean;
  dailyAllowance?: number | null;
  totalAllowance?: number | null;
  settlementPending?: boolean | null;
};

export type PersonnelDutyDetail = PersonnelDutyItem & {
  companyId: string;
  targetProjectStatus: number;
  targetProjectSurveyOutcome: number;
  targetProjectSiteId?: string | null;
  notes?: string | null;

  travelCost?: number | null;
  accommodationCost?: number | null;
  receiptAmount?: number | null;
  totalExpense?: number | null;
  settlementGap?: number | null;
  settlementDecision?: SettlementDecision | null;
  settlementNote?: string | null;
  settlementAtUtc?: string | null;
  settlementAdvanceId?: string | null;

  allowanceRevisedAtUtc?: string | null;
  allowanceRevisionNote?: string | null;

  /**
   * Tutar YAZMA kapısı: personnel.edit yetmiyor, ek ödeme yetkisi de
   * gerekiyor. Görmediği rakamı yazan kullanıcı yanlışını fark
   * edemezdi.
   */
  canWriteAmounts: boolean;
};

export type CreateDutyRequest = {
  companyId: string;
  personnelId: string;
  dutyType: DutyType;
  targetProjectId: string;
  targetProjectSiteId?: string | null;
  sourceProjectId?: string | null;
  startDate: string;
  endDate: string;
  isOutOfCity: boolean;
  dailyAllowance: number;
  purpose: string;
  notes?: string | null;
};

export type SaveDutyExpenseRequest = {
  travelCost: number;
  accommodationCost: number;
  receiptAmount: number;
};

export type SettleDutyRequest = {
  decision: SettlementDecision;
  note: string;
  installmentCount?: number;
};

export const dutyTypeOptions: { value: DutyType; label: string; hint: string }[] =
  [
    {
      value: 0,
      label: "Çalışma görevlendirmesi",
      hint: "Gittiği gün kadarı hedef projeye işçilik olarak sayılır.",
    },
    {
      value: 1,
      label: "Keşif görevi",
      hint: "Keşif statüsündeki projeye açılır; yalnız masraf yansır.",
    },
    {
      value: 2,
      label: "Ziyaret / denetim / görüşme",
      hint: "İşçilik günü yeniden atanmaz; yalnız masraf yansır.",
    },
  ];

export const personnelDutyService = {
  getAll(filters?: {
    companyId?: string;
    personnelId?: string;
    projectId?: string;
    status?: number;
  }) {
    const query = new URLSearchParams();

    if (filters?.companyId) query.set("companyId", filters.companyId);
    if (filters?.personnelId) query.set("personnelId", filters.personnelId);
    if (filters?.projectId) query.set("projectId", filters.projectId);
    if (filters?.status !== undefined) {
      query.set("status", String(filters.status));
    }

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return apiClient<PersonnelDutyItem[]>(`hr/gorevlendirmeler${suffix}`);
  },

  get(id: string) {
    return apiClient<PersonnelDutyDetail>(`hr/gorevlendirmeler/${id}`);
  },

  create(payload: CreateDutyRequest) {
    return apiClient<{ id: string; message: string }>("hr/gorevlendirmeler", {
      method: "POST",
      body: payload,
    });
  },

  approve(id: string, decisionNote?: string | null) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${id}/onayla`,
      {
        method: "POST",
        body: { decisionNote: decisionNote ?? null },
      }
    );
  },

  reject(id: string, decisionNote: string) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${id}/reddet`,
      { method: "POST", body: { decisionNote } }
    );
  },

  saveExpense(id: string, payload: SaveDutyExpenseRequest) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${id}/masraf`,
      { method: "POST", body: payload }
    );
  },

  /**
   * Harcırah düzeltme. Görev kartındaki tutar sabit tutulduğu için
   * yanlış girilen harcırahın tek çaresi görevi iptal edip yeniden
   * açmaktı; bu uç düzeltmeyi iz bırakarak yapıyor.
   */
  reviseAllowance(id: string, dailyAllowance: number, note: string) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${id}/harcirah`,
      { method: "POST", body: { dailyAllowance, note } }
    );
  },

  settle(id: string, payload: SettleDutyRequest) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${id}/mahsup`,
      { method: "POST", body: payload }
    );
  },
};
