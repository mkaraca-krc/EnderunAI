import type {
  EstimatedMaterialCost,
} from "@/services/offer-costing.service";

export type AISuggestionType =
  | "saving"
  | "warning"
  | "risk"
  | "supplier";

export type AISuggestionSeverity =
  | "low"
  | "medium"
  | "high";

export interface AISuggestion {
  id: string;
  type: AISuggestionType;
  title: string;
  description: string;
  savingAmount?: number;
  severity: AISuggestionSeverity;
}

export interface OfferLineCosting {
  recipeId: string;
  recipeVersion: number;

  materialCost: number;
  laborCost: number;
  machineCost: number;
  unitCost: number;

  laborHours: number;
  machineHours: number;

  pricedMaterialCount: number;
  unpricedMaterialCount: number;

  materials: EstimatedMaterialCost[];
  warnings: string[];
  suggestions: AISuggestion[];
}

export interface OfferLine {
  id: string;

  engineeringPositionId: string;
  positionNumber: string;
  description: string;
  unit: string;

  quantity: string;
  listPrice: string;
  discountRate: string;
  freightRate: string;
  wasteRate: string;
  financeRate: string;
  generalExpenseRate: string;
  profitRate: string;

  manufacturerName: string;
  notes: string;

  costing?: OfferLineCosting;
}

export interface OfferHeader {
  companyId: string;
  customerId: string;
  projectId: string;
  offerNumber: string;
  offerDate: string;
  validityDate: string;
  currency: string;
  exchangeRate: string;
  title: string;
  description: string;
}

export interface OfferSummary {
  subtotal: number;
  discountTotal: number;
  freightTotal: number;
  wasteTotal: number;
  financeTotal: number;
  generalExpenseTotal: number;
  profitTotal: number;
  grandTotal: number;
}

export function createEmptyOfferLine(): OfferLine {
  return {
    id: crypto.randomUUID(),

    engineeringPositionId: "",
    positionNumber: "",
    description: "",
    unit: "Adet",

    quantity: "1",
    listPrice: "0",
    discountRate: "0",
    freightRate: "0",
    wasteRate: "0",
    financeRate: "0",
    generalExpenseRate: "0",
    profitRate: "0",

    manufacturerName: "",
    notes: "",

    costing: undefined,
  };
}
