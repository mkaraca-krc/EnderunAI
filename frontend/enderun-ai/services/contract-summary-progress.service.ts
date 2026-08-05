import { apiClient } from "@/lib/api/api-client";

export interface ContractSummaryProgressItem {
  boqItemId: string;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  unitPrice: number;
  contractAmount: number;
  /** Onaylı saha raporlarından biriken — İÇ gerçekleşme. */
  fieldQuantity: number;
  /** Hakedişlerde işverenin kabul ettiği kümülatif — RESMÎ gerçekleşme. */
  employerQuantity: number;
  remainingQuantity: number;
  /** Saha − işveren: devreden iş. */
  pendingQuantity: number;
  fieldAmount: number;
  employerAmount: number;
  fieldRate: number;
  employerRate: number;
}

export interface ContractSummaryProgressSection {
  sectionId?: string | null;
  name: string;
  order: number;
  contractAmount: number;
  fieldRate: number;
  employerRate: number;
  fieldAmount: number;
  employerAmount: number;
  items: ContractSummaryProgressItem[];
}

export type ContractSummaryProgress =
  | { hasContractSummary: false; message: string }
  | {
      hasContractSummary: true;
      boqId: string;
      boqNumber: string;
      contractAmount: number;
      fieldRate: number;
      employerRate: number;
      fieldAmount: number;
      employerAmount: number;
      sections: ContractSummaryProgressSection[];
    };

export interface FieldEmployerDifferenceItem {
  sectionName: string;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  fieldQuantity: number;
  employerQuantity: number;
  pendingQuantity: number;
  pendingAmount: number;
  fieldRate: number;
  employerRate: number;
}

export type FieldEmployerDifference =
  | { hasContractSummary: false; message: string }
  | {
      hasContractSummary: true;
      projectFieldRate: number;
      projectEmployerRate: number;
      totalPendingAmount: number;
      differingItemCount: number;
      items: FieldEmployerDifferenceItem[];
    };

export interface HakedisSummaryDraftItem {
  projectBoqItemId: string;
  sectionId?: string | null;
  sectionName: string;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  unitPrice: number;
  previousQuantity: number;
  /** Sahaya göre bu dönem — ÖNERİ, işverenle mutabakata göre değişir. */
  suggestedCurrentQuantity: number;
  cumulativeFieldQuantity: number;
  cumulativeEmployerQuantity: number;
}

export type HakedisSummaryDraft =
  | { hasContractSummary: false; message: string; items: [] }
  | {
      hasContractSummary: true;
      boqId: string;
      boqNumber: string;
      items: HakedisSummaryDraftItem[];
    };

export const contractSummaryProgressService = {
  getProgress(projectId: string) {
    return apiClient<ContractSummaryProgress>(
      `projects/${projectId}/icmal-ilerleme`
    );
  },

  getDifference(projectId: string) {
    return apiClient<FieldEmployerDifference>(
      `progress-payments/saha-isveren-farki?projectId=${encodeURIComponent(projectId)}`
    );
  },

  getHakedisDraft(projectId: string, periodNumber: number) {
    return apiClient<HakedisSummaryDraft>(
      `progress-payments/icmal-taslagi` +
        `?projectId=${encodeURIComponent(projectId)}` +
        `&periodNumber=${periodNumber}`
    );
  },
};
