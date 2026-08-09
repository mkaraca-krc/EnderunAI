import { apiClient } from "@/lib/api/api-client";

/** Ayrılış türleri — backend TerminationReason enum'ıyla aynı sıra. */
export const TerminationReason = {
  EmployerTermination: 0,
  Resignation: 1,
  ResignationWithJustCause: 2,
  Retirement: 3,
  MilitaryService: 4,
  Marriage: 5,
  EmployerTerminationWithJustCause: 6,
  FixedTermContractEnd: 7,
  Death: 8,
} as const;

export const TerminationStatus = {
  Draft: 0,
  Finalized: 1,
} as const;

export type TerminationReasonOption = {
  reason: number;
  name: string;
  hasSeverance: boolean;
  hasNotice: boolean;
  hasUnusedLeave: boolean;
};

export type TerminationComponent = {
  gross: number;
  sgkAmount: number;
  incomeTax: number;
  stampTax: number;
  net: number;
};

/**
 * Tazminat hesabı. actualNetTotal ve extraPaymentDifference elden ödeme
 * bilgisi taşır; extra_payment.view izni olmayan kullanıcıya sunucu
 * bunları null döner (arayüzde gizlenmez, hiç gelmez).
 */
export type TerminationCalculation = {
  personnelId: string;
  personnelFullName: string;
  employmentStartDate: string;
  terminationDate: string;
  reason: number;
  reasonName: string;
  hasSeveranceRight: boolean;
  hasNoticeRight: boolean;
  serviceDays: number;
  fullServiceYears: number;
  noticeWeeks: number;
  unusedLeaveDays: number;
  officialMonthlyGross: number;
  severanceCeilingApplied: boolean;
  officialSeverance: TerminationComponent;
  officialNotice: TerminationComponent;
  officialLeave: TerminationComponent;
  officialNetTotal: number;
  extraMonthlyAmount: number | null;
  actualNetTotal: number | null;
  extraPaymentDifference: number | null;
  warnings: string[];
};

export type TerminationListItem = {
  id: string;
  personnelId: string;
  personnelFullName: string;
  terminationDate: string;
  reason: number;
  status: number;
  serviceDays: number;
  unusedLeaveDays: number;
  officialNetTotal: number;
  severanceCeilingApplied: boolean;
  finalizedAtUtc: string | null;
};

/**
 * Ayrılış değerlendirmesi — tekrar işe alım kodu ve gerekçesi.
 *
 * Yasal çıkış nedeninden AYRI katman. Kod boşsa "değerlendirilmedi":
 * nötr, ne engel ne uyarı üretir.
 *
 * GİZLİLİK: yalnız İK/GM okuyup atayabilir; saha personeli göremez.
 */
export type RehireAssessment = {
  id: string;
  personnelId: string;
  personnelFullName: string;
  terminationDate: string;
  reason: number;
  rehireCode: number | null;
  rehireCodeName: string;
  rehireNote: string | null;
  rehireMarkedAtUtc: string | null;
  rehireMarkedByUserId: string | null;
};

export type ExtraPayment = {
  id: string;
  personnelId: string;
  personnelFullName: string;
  monthlyAmount: number;
  effectiveStartDate: string;
  effectiveEndDate: string | null;
  note: string | null;
};

export const terminationService = {
  getReasons() {
    return apiClient<TerminationReasonOption[]>("personnel-terminations/reasons");
  },

  /** Kayıt oluşturmadan hesaplar. */
  simulate(params: {
    personnelId: string;
    reason: number;
    terminationDate?: string;
    unusedLeaveDays?: number;
  }) {
    const query = new URLSearchParams({
      personnelId: params.personnelId,
      reason: String(params.reason),
    });

    if (params.terminationDate) query.set("terminationDate", params.terminationDate);
    if (params.unusedLeaveDays !== undefined) {
      query.set("unusedLeaveDays", String(params.unusedLeaveDays));
    }

    return apiClient<TerminationCalculation>(
      `personnel-terminations/simulation?${query.toString()}`
    );
  },

  list() {
    return apiClient<TerminationListItem[]>("personnel-terminations");
  },

  getRehireAssessment(terminationId: string) {
    return apiClient<RehireAssessment>(
      `personnel-terminations/${terminationId}/rehire-degerlendirmesi`
    );
  },

  /** Kırmızı ve sarıda gerekçe zorunludur. */
  setRehireAssessment(
    terminationId: string,
    rehireCode: number | null,
    rehireNote: string | null
  ) {
    return apiClient<{ message: string }>(
      `personnel-terminations/${terminationId}/rehire-degerlendirmesi`,
      { method: "POST", body: { rehireCode, rehireNote } }
    );
  },

  create(payload: {
    personnelId: string;
    reason: number;
    terminationDate: string;
    unusedLeaveDays?: number | null;
    note?: string | null;
  }) {
    return apiClient<{ id: string; message: string }>("personnel-terminations", {
      method: "POST",
      body: payload,
    });
  },

  finalize(id: string) {
    return apiClient<{ message: string }>(
      `personnel-terminations/${id}/finalize`,
      { method: "POST" }
    );
  },
};

export const extraPaymentService = {
  list(personnelId?: string) {
    const query = personnelId ? `?personnelId=${personnelId}` : "";
    return apiClient<ExtraPayment[]>(`personnel-extra-payments${query}`);
  },

  create(payload: {
    personnelId: string;
    monthlyAmount: number;
    effectiveStartDate: string;
    effectiveEndDate?: string | null;
    note?: string | null;
  }) {
    return apiClient<{ id: string; message: string }>("personnel-extra-payments", {
      method: "POST",
      body: payload,
    });
  },

  update(
    id: string,
    payload: {
      personnelId: string;
      monthlyAmount: number;
      effectiveStartDate: string;
      effectiveEndDate?: string | null;
      note?: string | null;
    }
  ) {
    return apiClient<{ message: string }>(`personnel-extra-payments/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
};
