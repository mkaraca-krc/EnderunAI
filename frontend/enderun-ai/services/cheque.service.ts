import { apiClient } from "@/lib/api/api-client";

export const ChequeDirection = {
  Received: 0,
  Issued: 1,
} as const;

export const ChequeStatus = {
  Portfolio: 0,
  AtBank: 1,
  AtFactoring: 2,
  Collected: 3,
  Bounced: 4,
  Issued: 10,
  Paid: 11,
  Returned: 12,
  /** Ertelendi: yerine yeni vadeli çek verildi. */
  Replaced: 20,
  /** İptal edildi: mali etkileri ters kayıtla geri alındı, kayıt durur. */
  Voided: 90,
} as const;

export const CHEQUE_STATUS_LABELS: Record<number, string> = {
  0: "Portföyde",
  1: "Bankada (tahsilde)",
  2: "Faktoringde",
  3: "Tahsil edildi",
  4: "Karşılıksız",
  10: "Verildi",
  11: "Ödendi",
  12: "İade alındı",
  20: "Ertelendi (değiştirildi)",
  90: "İptal edildi",
};

/** erp-status.{renk} sınıfıyla eşleşir. */
export const CHEQUE_STATUS_COLORS: Record<number, string> = {
  0: "blue",
  1: "yellow",
  2: "yellow",
  3: "green",
  4: "red",
  10: "blue",
  11: "green",
  12: "gray",
  20: "yellow",
  90: "gray",
};

/** Bu geçişler için kasa/banka hesabı seçimi zorunlu. */
export const CHEQUE_TRANSITIONS_REQUIRING_CASH_ACCOUNT: Record<string, boolean> = {
  "0-1": true,
  "0-3": true,
  "1-3": true,
  "2-4": true,
  "10-11": true,
};

export function requiresCashAccount(from: number, to: number) {
  return CHEQUE_TRANSITIONS_REQUIRING_CASH_ACCOUNT[`${from}-${to}`] === true;
}

export type ChequeMovement = {
  id: string;
  movementDate: string;
  fromStatus?: number | null;
  fromStatusName?: string | null;
  toStatus: number;
  toStatusName: string;
  description: string;
  cashAccountId?: string | null;
  cashAccountName?: string | null;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
};

export type ChequeAllocation = {
  id: string;
  amount: number;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  costCenterCode?: string | null;
  supplierInvoiceId?: string | null;
  supplierInvoiceNumber?: string | null;
  salesInvoiceId?: string | null;
  salesInvoiceNumber?: string | null;
  description?: string | null;
};

export type ChequeAllocationPayload = {
  amount: number;
  projectId?: string | null;
  costCenterCode?: string | null;
  supplierInvoiceId?: string | null;
  salesInvoiceId?: string | null;
  description?: string | null;
};

export type ChequeListItem = {
  id: string;
  companyId: string;
  direction: number;
  directionName: string;
  status: number;
  statusName: string;
  internalNumber: string;
  chequeNumber: string;
  bankName: string;
  drawer?: string | null;
  currentAccountId?: string | null;
  currentAccountTitle?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  costCenterCode?: string | null;
  amount: number;
  currencyCode: string;
  /** Keşide kuru; TL çekte 1. */
  exchangeRate: number;
  /** Keşide tarihindeki TL karşılığı — defter değeri. */
  amountTry: number;
  issueDate: string;
  dueDate: string;
  daysToDue: number;
  isOverdue: boolean;
  /**
   * TOPLAMA GİRER Mİ — kararı SUNUCU veriyor.
   *
   * Ekran eskiden kendi kuralını yazıyordu (`status !== Voided`) ve
   * liste ucundaki süzgeçten AYRI karar veriyordu. ÇEK/1'deki hata
   * tam olarak bu ayrışmaydı: ödenen çek sunucudan geliyordu, ekran
   * da onu topluyordu. Artık ekranda karar yok.
   */
  countsTowardTotals: boolean;
};

export type ChequeDetail = ChequeListItem & {
  bankBranch?: string | null;
  projectName?: string | null;
  progressPaymentId?: string | null;
  progressPaymentNumber?: string | null;
  supplierInvoiceId?: string | null;
  supplierInvoiceNumber?: string | null;
  cashAccountId?: string | null;
  cashAccountName?: string | null;
  description?: string | null;
  allowedNextStatuses: number[];
  movements: ChequeMovement[];
  allocations: ChequeAllocation[];
  replacedByChequeId?: string | null;
  replacedByChequeNumber?: string | null;
  replacesChequeId?: string | null;
  replacesChequeNumber?: string | null;
  /** Zincirde kaç kez ertelendiği — risk sinyali. */
  renewalCount: number;

  /** Eşzamanlı değişiklik damgası; düzenleme ve iptal isteğinde geri gider. */
  rowVersion: string;

  /** Düzenle düğmesi açık mı — karar SUNUCUDAN gelir. */
  canEdit: boolean;

  /**
   * Kapalıysa nedeni. Ekran bu cümleyi AYNEN gösteriyor; kendi metnini
   * uydursaydı API ile ekran zamanla ayrışırdı.
   */
  editBlockedReason?: string | null;

  /** Kapanmış bir durumdan mı iptal edildi (rozet). */
  voidedFromClosedState: boolean;

  voidReasonKind?: number | null;
  voidReasonName?: string | null;

  changeLog: ChequeChangeLogEntry[];

  /**
   * BU ÇEK İPTAL EDİLİRSE AÇILACAK ORİJİNAL ÇEK — yoksa null.
   * Erteleme zincirinde, yerine geçen çek iptal edilince orijinal
   * önceki durumuna dönüyor. Kullanıcı bunu iptalden ÖNCE görmeli.
   */
  voidRestoresChequeNumber?: string | null;
  voidRestoresStatusName?: string | null;
};

/** Alan bazlı düzeltme kaydı — "Değişiklik geçmişi" sekmesi. */
export type ChequeChangeLogEntry = {
  id: string;
  fieldName: string;
  fieldLabel: string;
  oldValue?: string | null;
  newValue?: string | null;
  /** Muhasebeyi etkileyen alan mı (tutar, vade, cari) — süzgeç için. */
  affectsAccounting: boolean;
  changedAtUtc: string;
  changedByUserId?: string | null;
  changedByUserName?: string | null;
  reason?: string | null;
};

/** İptal nedenleri — sunucudaki ChequeVoidReason ile birebir. */
export const CHEQUE_VOID_REASONS = [
  { value: 0, label: "Yanlış giriş", onlyOpen: true },
  { value: 1, label: "Karşılıksız", onlyOpen: false },
  { value: 2, label: "Müşteriye iade", onlyOpen: false },
  { value: 90, label: "Diğer", onlyOpen: false },
] as const;

export type ChequeSummary = {
  receivedPortfolioAmount: number;
  receivedAtBankAmount: number;
  receivedAtFactoringAmount: number;
  receivedCollectedAmount: number;
  receivedBouncedAmount: number;
  issuedOpenAmount: number;
  issuedPaidAmount: number;
  receivedOpenCount: number;
  issuedOpenCount: number;
};

export type CreateChequePayload = {
  companyId: string;
  direction: number;
  chequeNumber: string;
  bankName: string;
  bankBranch?: string | null;
  drawer?: string | null;
  currentAccountId?: string | null;
  projectId?: string | null;
  amount: number;
  currencyCode: string;
  /**
   * Keşide tarihindeki kur. Boş bırakılırsa TCMB arşivinden çözülür;
   * arşivde de yoksa dövizli çek kaydedilmez.
   */
  exchangeRate?: number | null;
  issueDate: string;
  dueDate: string;
  progressPaymentId?: string | null;
  supplierInvoiceId?: string | null;
  description?: string | null;
  costCenterCode?: string | null;
  allocations?: ChequeAllocationPayload[] | null;
};

/**
 * Erteleme talebi. Tutar GÖNDERİLMEZ: yeni çek eskisiyle aynı tutarda
 * olmak zorunda, vade farkı ayrı belgeyle kaydedilir.
 */
export type ReplaceChequePayload = {
  chequeNumber: string;
  dueDate: string;
  movementDate: string;
  bankName?: string | null;
  bankBranch?: string | null;
  drawer?: string | null;
  issueDate?: string | null;
  description?: string | null;
  /**
   * EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
   *
   * Çekin durumunu değiştiren HER uç bunu istiyor. Bir uçta eksik
   * olması korumanın hiç olmaması demek: iki kullanıcı aynı çeke aynı
   * anda işlem yaparsa biri diğerininkini görmeden üzerine yazar ve
   * çekte bu, aynı parayı iki kez işlemek anlamına gelir.
   */
  rowVersion: string;
};

export type ChequeStatusChangePayload = {
  toStatus: number;
  movementDate: string;
  cashAccountId?: string | null;
  description?: string | null;
  /**
   * EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
   *
   * Çekin durumunu değiştiren HER uç bunu istiyor. Bir uçta eksik
   * olması korumanın hiç olmaması demek: iki kullanıcı aynı çeke aynı
   * anda işlem yaparsa biri diğerininkini görmeden üzerine yazar ve
   * çekte bu, aynı parayı iki kez işlemek anlamına gelir.
   */
  rowVersion: string;
};

export type UpdateChequePayload = {
  chequeNumber: string;
  bankName: string;
  bankBranch?: string | null;
  drawer?: string | null;
  currentAccountId?: string | null;
  projectId?: string | null;
  amount: number;
  issueDate: string;
  dueDate: string;
  progressPaymentId?: string | null;
  supplierInvoiceId?: string | null;
  description?: string | null;
  costCenterCode?: string | null;
  /**
   * EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
   *
   * Detay yanıtından alınıp aynen geri gönderiliyor. Arada başkası
   * kaydettiyse sunucu reddediyor; sessizce üzerine yazmıyor.
   */
  rowVersion: string;
  /** Düzeltme gerekçesi — denetim kaydına yazılır. */
  editReason?: string | null;
  /** Para birimi; değişirse kur yeniden çözülür ve fiş yeniden kesilir. */
  currencyCode?: string | null;
};

export const chequeService = {
  getAll(
    params: {
      companyId?: string;
      direction?: number;
      status?: number;
      currentAccountId?: string;
      projectId?: string;
      /** Merkez (ya da proje dışı masraf merkezi) süzgeci. */
      costCenterCode?: string;
      search?: string;
      /**
       * İptaller VARSAYILAN OLARAK GİZLİ. Denetim izi için kayıt
       * silinmiyor ama günlük listede iptal edilmiş çek gürültü;
       * kullanıcı açıkça isterse geliyor.
       */
      includeVoided?: boolean;
      /**
       * KAPANMIŞ ÇEKLER VARSAYILAN OLARAK GİZLİ (ÇEK/1).
       *
       * Ödenen/tahsil edilen/karşılıksız çek listede kalmaya devam
       * ediyor ve o ayın toplamına giriyordu. Silinmiyor, gizlenmiyor
       * — varsayılandan çıkıyor; durum süzgeciyle ya da bu bayrakla
       * her zaman görülebiliyor.
       */
      includeClosed?: boolean;
    } = {}
  ) {
    const query = new URLSearchParams();
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.direction !== undefined) query.set("direction", String(params.direction));
    if (params.status !== undefined) query.set("status", String(params.status));
    if (params.currentAccountId) query.set("currentAccountId", params.currentAccountId);
    if (params.projectId) query.set("projectId", params.projectId);
    if (params.costCenterCode)
      query.set("costCenterCode", params.costCenterCode);
    if (params.search) query.set("search", params.search);
    if (params.includeVoided) query.set("includeVoided", "true");
    if (params.includeClosed) query.set("includeClosed", "true");

    const suffix = query.toString();
    return apiClient<ChequeListItem[]>(`cheques${suffix ? `?${suffix}` : ""}`);
  },

  getSummary(companyId?: string) {
    const suffix = companyId ? `?companyId=${companyId}` : "";
    return apiClient<ChequeSummary>(`cheques/summary${suffix}`);
  },

  getById(id: string) {
    return apiClient<ChequeDetail>(`cheques/${id}`);
  },

  /**
   * Çek düzeltme. Tutar ve cari değişirse giriş fişi ters kayıtla
   * kapanıp yenisi kesiliyor; işlem görmüş çekte uç reddediyor —
   * önce durumu geri almak gerekiyor.
   */
  update(id: string, payload: UpdateChequePayload) {
    return apiClient<ChequeDetail>(`cheques/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  create(payload: CreateChequePayload) {
    return apiClient<ChequeDetail>("cheques", { method: "POST", body: payload });
  },

  replaceAllocations(
    id: string,
    allocations: ChequeAllocationPayload[],
    rowVersion: string
  ) {
    return apiClient<ChequeDetail>(`cheques/${id}/allocations`, {
      method: "PUT",
      body: { allocations, rowVersion },
    });
  },

  replace(id: string, payload: ReplaceChequePayload) {
    return apiClient<ChequeDetail>(`cheques/${id}/replace`, {
      method: "POST",
      body: payload,
    });
  },

  changeStatus(id: string, payload: ChequeStatusChangePayload) {
    return apiClient<ChequeDetail>(`cheques/${id}/status`, {
      method: "POST",
      body: payload,
    });
  },

  /**
   * Son durum değişikliğini geri alır (yanlış "Ödendi" gibi).
   * Silmez: fişi ters kayıtla kapatır, banka hareketini karşıt bir
   * hareketle dengeler ve iz bırakır.
   */
  reverseStatus(id: string, reason: string, rowVersion: string) {
    return apiClient<ChequeDetail>(`cheques/${id}/durum-geri-al`, {
      method: "POST",
      body: { reason, rowVersion },
    });
  },

  /**
   * Çeki iptale çeker ve ürettiği bütün mali etkileri aynı işlemde
   * geri alır. Mali kayıt olduğu için silme yok — geçmiş defterde
   * kalıyor.
   */
  /**
   * İPTAL — NEDEN LİSTEDEN, DAMGA ZORUNLU.
   *
   * `reasonKind` sayılabilir neden (0 yanlış giriş, 1 karşılıksız,
   * 2 müşteriye iade, 90 diğer). Serbest metin nedenin yerine geçmiyor:
   * "kaç çek karşılıksız çıktı" ancak sayılabilir nedenle cevaplanır.
   *
   * `rowVersion` eşzamanlı değişiklik damgası; sunucu zorunlu tutuyor.
   * Opsiyonel olsaydı korumayı atlatmak için alanı göndermemek yeterdi.
   */
  void(
    id: string,
    input: { reason?: string | null; reasonKind: number; rowVersion: string }
  ) {
    return apiClient<ChequeDetail>(`cheques/${id}/iptal`, {
      method: "POST",
      body: input,
    });
  },
};
