import { apiClient } from "@/lib/api/api-client";

export type OfferStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6;

/**
 * Fırsat hunisi durumları.
 *
 * 3 (Reddedildi) kullanımdan kalktı: kayıp artık nedeniyle birlikte
 * Kaybedildi altında tutuluyor. Eski kayıtlarda görünebilir diye
 * etiketi duruyor ama seçilemez.
 */
export const OFFER_STATUS = {
  Draft: 0,
  Submitted: 1,
  Pending: 2,
  Rejected: 3,
  Won: 4,
  Lost: 5,
  Cancelled: 6,
} as const;

export const OFFER_STATUS_LABELS: Record<number, string> = {
  0: "Hazırlanıyor",
  1: "Verildi",
  2: "Beklemede",
  3: "Reddedildi",
  4: "Kazanıldı",
  5: "Kaybedildi",
  6: "İptal",
};

/** Kullanıcının seçebileceği hedef durumlar (geçiş haritasıyla aynı). */
export const OFFER_NEXT_STATUSES: Record<number, number[]> = {
  0: [1, 6],
  1: [2, 4, 5, 6],
  2: [4, 5, 6],
  3: [],
  4: [],
  5: [],
  6: [],
};

export const OFFER_COUNTERPARTY_ROLES: [number, string][] = [
  [1, "İşveren"],
  [2, "Ana yüklenici"],
];

export const OFFER_KINDS: [number, string][] = [
  [1, "Birim fiyatlı (keşif/poz)"],
  [2, "Anahtar teslim götürü"],
];

export const OFFER_LOST_REASONS: [number, string][] = [
  [1, "Fiyat yüksek"],
  [2, "Referans yetersiz"],
  [3, "Başka firmaya verildi"],
  [4, "İş iptal edildi"],
  [5, "Diğer"],
];

export const PROGRESS_PAYMENT_PERIODS: [number, string][] = [
  [0, "Belirlenmedi"],
  [1, "Aylık"],
  [2, "İki haftalık"],
  [3, "Üç aylık"],
  [4, "İş bitiminde"],
  [5, "Diğer"],
];

export const PROJECT_CONTRACT_TYPES: [number, string][] = [
  [0, "Belirlenmedi"],
  [1, "Anahtar teslim (götürü)"],
  [2, "Birim fiyatlı"],
  [3, "Karma"],
];

/** Teklifin takip künyesi — liste ve detayda ortak. */
export type OfferTrackingFields = {
  counterpartyCurrentAccountId?: string | null;
  counterpartyName?: string | null;
  counterpartyRole: number;
  kind: number;
  lostReason: number;
  lostReasonNote?: string | null;
  statusChangedAtUtc?: string | null;
  statusNote?: string | null;
};

export type OfferWinRate = {
  totalCount: number;
  wonCount: number;
  lostCount: number;
  openCount: number;
  cancelledCount: number;
  wonAmount: number;
  lostAmount: number;
  openAmount: number;
  /** Adet bazlı kazanma oranı (%). */
  countWinRate: number;
  /** Tutar bazlı kazanma oranı (%). */
  amountWinRate: number;
  lostReasons: {
    reason: number;
    reasonName: string;
    count: number;
    amount: number;
  }[];
};

export type OfferChain = {
  offer: {
    id: string;
    offerNumber: string;
    title: string;
    offerDate: string;
    currency: string;
    grandTotal: number;
    status: number;
    statusName: string;
    kind: number;
    kindName: string;
    lostReason: number;
    lostReasonName: string;
    counterpartyName?: string | null;
    counterpartyRoleName: string;
  };
  project?: {
    id: string;
    code: string;
    name: string;
    contractNumber?: string | null;
    contractDate?: string | null;
    contractAmount?: number | null;
    currencyCode: string;
    contractType: number;
    progressPaymentPeriod: number;
    paymentTerms?: string | null;
    status: number;
    isArchived: boolean;
    /** Proje bu tekliften mi doğdu (ek işte false). */
    bornFromThisOffer: boolean;
  } | null;
  boqs: {
    id: string;
    boqNumber: string;
    name: string;
    status: number;
    totalAmount: number;
    isCurrentRevision: boolean;
    sourceOfferId?: string | null;
    /** İcmal bu teklifin kalemlerinden mi üretildi. */
    fromThisOffer: boolean;
    itemCount: number;
  }[];
  progressPayments: {
    id: string;
    progressPaymentNumber: string;
    periodNumber: number;
    progressPaymentDate: string;
    status: number;
    currentAmount: number;
    cumulativeAmount: number;
    currencyCode: string;
  }[];
};

export type OfferListItem = {
  id: string;
  companyId: string;
  companyName: string;
  projectId?: string | null;
  projectName?: string | null;
  customerId?: string | null;
  offerNumber: string;
  title: string;
  offerDate: string;
  validUntil?: string | null;
  currency: string;
  exchangeRate: number;
  status: OfferStatus;
  subtotal: number;
  discountTotal: number;
  costTotal: number;
  profitTotal: number;
  grandTotal: number;
  itemCount: number;
} & OfferTrackingFields;

export type OfferItem = {
  id: string;
  lineNumber: number;
  positionNumber?: string | null;
  engineeringPositionId?: string | null;
  engineeringRecipeId?: string | null;
  recipeVersion?: number | null;
  description: string;
  manufacturerPriceListItemId?: string | null;
  manufacturerName?: string | null;
  productCode?: string | null;
  brand?: string | null;
  model?: string | null;
  quantity: number;
  unit: string;
  listPrice: number;
  discountRate: number;
  netPurchasePrice: number;
  freightRate: number;
  wasteRate: number;
  financeRate: number;
  generalExpenseRate: number;
  profitRate: number;
  unitCost: number;
  unitSalesPrice: number;
  costTotal: number;
  salesTotal: number;
  notes?: string | null;
};

export type OfferDetail = Omit<OfferListItem, "itemCount"> & {
  description?: string | null;
  notes?: string | null;
  items: OfferItem[];
};

export type OfferItemPayload = {
  positionNumber?: string | null;
  engineeringPositionId?: string | null;
  engineeringRecipeId?: string | null;
  recipeVersion?: number | null;
  description: string;
  manufacturerPriceListItemId?: string | null;
  manufacturerName?: string | null;
  productCode?: string | null;
  brand?: string | null;
  model?: string | null;
  quantity: number;
  unit: string;
  listPrice: number;
  discountRate: number;
  freightRate: number;
  wasteRate: number;
  financeRate: number;
  generalExpenseRate: number;
  profitRate: number;
  notes?: string | null;
};

export type CreateOfferPayload = {
  companyId: string;
  projectId?: string | null;
  customerId?: string | null;
  title: string;
  offerDate: string;
  validUntil?: string | null;
  currency: string;
  exchangeRate: number;
  description?: string | null;
  notes?: string | null;
  items: OfferItemPayload[];
};

export type OfferItemCalculation = {
  netPurchasePrice: number;
  unitCost: number;
  unitSalesPrice: number;
  costTotal: number;
  salesTotal: number;
  profitTotal: number;
};

function buildQuery(params?: {
  companyId?: string;
  projectId?: string;
  status?: number;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) query.set("companyId", params.companyId);
  if (params?.projectId) query.set("projectId", params.projectId);
  if (params?.status !== undefined) query.set("status", String(params.status));
  if (params?.search) query.set("search", params.search);

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const offerService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    status?: number;
    search?: string;
  }) {
    return apiClient<OfferListItem[]>(`offers${buildQuery(params)}`);
  },

  getById(id: string) {
    return apiClient<OfferDetail>(`offers/${id}`);
  },

  calculateItem(payload: {
    quantity: number;
    listPrice: number;
    discountRate: number;
    freightRate: number;
    wasteRate: number;
    financeRate: number;
    generalExpenseRate: number;
    profitRate: number;
  }) {
    return apiClient<OfferItemCalculation>("offers/calculate-item", {
      method: "POST",
      body: payload,
    });
  },

  create(payload: CreateOfferPayload) {
    return apiClient<{
      message: string;
      id: string;
      offerNumber: string;
      grandTotal: number;
      status: OfferStatus;
    }>("offers", {
      method: "POST",
      body: payload,
    });
  },

  /**
   * Pozdan kalem ekler. Fiyat ya kurumun resmî yıl fiyatından ya da
   * pozun reçete analizinden gelir; ikisi de bulunamazsa uç hata
   * döner ve kalem eklenmez — sıfır fiyatlı bir keşif satırı toplamı
   * sessizce düşürürdü.
   */
  addItemFromPosition(
    offerId: string,
    payload: {
      engineeringPositionId: string;
      quantity: number;
      source: OfferPositionPriceSourceValue;
      year?: number | null;
      institution?: number | null;
      profitRate: number;
      laborHourRate: number;
      machineHourRate: number;
    }
  ) {
    return apiClient<{
      message: string;
      id: string;
      lineNumber: number;
      unitSalesPrice: number;
      materialUnitPrice: number;
      laborUnitPrice: number;
      overheadUnitPrice: number;
      salesTotal: number;
      sourceNote: string;
    }>(`offers/${offerId}/items/from-position`, {
      method: "POST",
      body: payload,
    });
  },

  /** Teklifi projenin keşif icmaline aktarır (tek yönlü). */
  transferToBoq(offerId: string, payload?: { projectId?: string; name?: string }) {
    return apiClient<OfferBoqTransferResult>(`offers/${offerId}/icmale-aktar`, {
      method: "POST",
      body: payload ?? {},
    });
  },

  /** Teklifin takip künyesi: kime verildi, hangi tipte. */
  updateTracking(
    offerId: string,
    payload: {
      counterpartyCurrentAccountId?: string | null;
      counterpartyRole: number;
      kind: number;
    }
  ) {
    return apiClient<{ message: string }>(`offers/${offerId}/takip`, {
      method: "PUT",
      body: payload,
    });
  },

  /** Huni durumunu değiştirir; geçerli geçişleri arka uç zorlar. */
  changeStatus(
    offerId: string,
    payload: {
      status: number;
      lostReason?: number;
      lostReasonNote?: string | null;
      note?: string | null;
    }
  ) {
    return apiClient<{
      message: string;
      status: number;
      statusName: string;
      requiresContract: boolean;
    }>(`offers/${offerId}/durum`, { method: "POST", body: payload });
  },

  /** Kazanılan teklifin sözleşmesini açar (yeni proje veya ek iş). */
  createContract(
    offerId: string,
    payload: Record<string, unknown>
  ) {
    return apiClient<{
      message: string;
      projectId: string;
      projectCode: string;
      projectCreated: boolean;
      warehouseId?: string | null;
      projectBoqId?: string | null;
      boqNumber?: string | null;
      boqItemCount: number;
      boqTotalAmount: number;
      warnings: string[];
    }>(`offers/${offerId}/sozlesme`, { method: "POST", body: payload });
  },

  /** Adet ve tutar bazlı kazanma oranı + kayıp nedeni dağılımı. */
  getWinRate(params: {
    companyId?: string;
    counterpartyId?: string;
    kind?: number;
    fromDate?: string;
    toDate?: string;
  }) {
    const query = new URLSearchParams();
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.counterpartyId) query.set("counterpartyId", params.counterpartyId);
    if (params.kind !== undefined) query.set("kind", String(params.kind));
    if (params.fromDate) query.set("fromDate", params.fromDate);
    if (params.toDate) query.set("toDate", params.toDate);

    const suffix = query.toString() ? `?${query.toString()}` : "";
    return apiClient<OfferWinRate>(`offers/kazanma-orani${suffix}`);
  },

  /** Teklif → proje → icmal → hakediş zinciri. */
  getChain(offerId: string) {
    return apiClient<OfferChain>(`offers/${offerId}/zincir`);
  },

  /** Antetli çıktı için teklif + şirket bilgisi tek istekte. */
  getPrintData(offerId: string) {
    return apiClient<OfferPrintData>(`offers/${offerId}/print`);
  },
};

export const OfferPositionPriceSource = {
  /** Kurumun yayımladığı yıl birim fiyatı. */
  OfficialYearPrice: 0,
  /** Pozun reçetesinden malzeme + işçilik analizi. */
  RecipeAnalysis: 1,
} as const;

export type OfferPositionPriceSourceValue =
  (typeof OfferPositionPriceSource)[keyof typeof OfferPositionPriceSource];

export type OfferBoqTransferResult = {
  projectBoqId: string;
  boqNumber: string;
  itemCount: number;
  totalAmount: number;
  /** Aktarımda varsayıma düşülen yerler; boş olabilir. */
  warnings: string[];
};

export type OfferPrintData = {
  id: string;
  offerNumber: string;
  title: string;
  offerDate: string;
  validUntil?: string | null;
  currency: string;
  status: number;
  description?: string | null;
  notes?: string | null;
  subtotal: number;
  discountTotal: number;
  grandTotal: number;
  company: {
    name: string;
    taxOffice?: string | null;
    taxNumber?: string | null;
    address?: string | null;
    phone?: string | null;
    email?: string | null;
  };
  projectCode?: string | null;
  projectName?: string | null;
  items: {
    lineNumber: number;
    positionNumber?: string | null;
    description: string;
    unit: string;
    quantity: number;
    unitSalesPrice: number;
    materialUnitPrice: number;
    laborUnitPrice: number;
    overheadUnitPrice: number;
    salesTotal: number;
  }[];
};
