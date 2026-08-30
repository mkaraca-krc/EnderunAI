import { apiClient } from "@/lib/api/api-client";

export type AccountingAccountNature = 0 | 1 | 2;

export type AccountingAccountListItem = {
  id: string;
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  nature: AccountingAccountNature;
  level: number;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
  isActive: boolean;
  childCount: number;
};

export type AccountingAccountDetail = {
  id: string;
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  nature: AccountingAccountNature;
  level: number;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;

  /**
   * KAYIT SÜRÜMÜ — güncelleme isteğinde AYNEN geri verilir.
   *
   * Sunucudaki `UpdatedAtUtc` damgasının tel üzerindeki hâli (ISO
   * dize); ikinci bir belirteç değil. Taşınmasaydı yalnız istek içi
   * pencere korunurdu — kullanıcının 10 dakika önce açtığı formdaki
   * kayıp güncelleme yakalanamazdı.
   *
   * DEĞER ÜZERİNDE İŞLEM YAPILMAZ: olduğu gibi alınır, olduğu gibi
   * geri verilir. Tarihe çevirip yeniden biçimlendirmek milisaniye
   * kaybına yol açar ve sunucu her isteği "başkası değiştirdi" diye
   * reddederdi.
   */
  surum: string;
};

export type CreateAccountingAccountRequest = {
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
};

/**
 * HESAP GÜNCELLEME — KOD VE AKTİFLİK BURADA YOK (HP/1 · K1, K3).
 *
 * KOD YOK: hesap kodu oluşturulduktan sonra değişmez. Yanlışsa
 * hesap pasife alınır, doğrusu yeni hesap olarak açılır.
 *
 * AKTİFLİK YOK: pasife alma ve geri açma kendi uçlarında
 * (`deactivate` / `activate`). Burada da bulunsaydı iki kapı olur
 * ve biri diğerini sessizce ezerdi.
 *
 * SÜRÜM ZORUNLU: sunucunun okuma yanıtında gelen `surum` aynen geri
 * verilir. Gönderilmezse istek reddedilir — atlanmaz.
 */
export type UpdateAccountingAccountRequest = {
  parentAccountId?: string | null;
  name: string;
  surum: string;
  description?: string | null;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
};

function buildQuery(filters?: {
  companyId?: string;
  parentAccountId?: string;
  isActive?: boolean;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (filters?.companyId) {
    query.set("companyId", filters.companyId);
  }

  if (filters?.parentAccountId) {
    query.set("parentAccountId", filters.parentAccountId);
  }

  if (filters?.isActive !== undefined) {
    query.set("isActive", String(filters.isActive));
  }

  if (filters?.search?.trim()) {
    query.set("search", filters.search.trim());
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const accountingAccountService = {
  getAll(filters?: {
    companyId?: string;
    parentAccountId?: string;
    isActive?: boolean;
    search?: string;
  }) {
    return apiClient<AccountingAccountListItem[]>(
      `accounting-accounts${buildQuery(filters)}`
    );
  },

  /**
   * ARANABİLİR SEÇİCİNİN UCU — sınırlı satır + TOPLAM eşleşme sayısı.
   *
   * Hesap planı canlıda 1.114 satır (~168 KB). Tamamını her ekran
   * açılışında indirmek yerine yazdıkça buradan aranıyor. `signal`
   * zorunlu değil ama seçici her zaman veriyor: hızlı yazarken geç
   * dönen eski isteğin yeni sonucu ezmesi, kullanıcıya YANLIŞ hesabı
   * seçtirir.
   */
  search(
    params: { companyId?: string; isActive?: boolean; search: string; limit?: number },
    signal?: AbortSignal
  ) {
    const query = new URLSearchParams();

    if (params.companyId) query.set("companyId", params.companyId);
    if (params.isActive !== undefined)
      query.set("isActive", String(params.isActive));

    query.set("search", params.search);
    query.set("limit", String(params.limit ?? 50));

    // PagedResult<T> — kod tabanının kırpılmış liste sözleşmesi:
    // dönen kayıtlar, TOPLAM eşleşme ve "daha var mı". Ekran toplamı
    // buradan okuyor; kendi saymıyor.
    return apiClient<{
      items: AccountingAccountListItem[];
      total: number;
      take: number;
      hasMore: boolean;
    }>(`accounting-accounts/arama?${query.toString()}`, { signal });
  },

  getById(id: string) {
    return apiClient<AccountingAccountDetail>(
      `accounting-accounts/${id}`
    );
  },

  create(request: CreateAccountingAccountRequest) {
    return apiClient<AccountingAccountDetail>(
      "accounting-accounts",
      {
        method: "POST",
        body: request,
      }
    );
  },

  update(
    id: string,
    request: UpdateAccountingAccountRequest
  ) {
    return apiClient<AccountingAccountDetail>(
      `accounting-accounts/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  /** K3 — pasife almanın geri alınması. */
  activate(id: string) {
    return apiClient<{ message: string }>(
      `accounting-accounts/${id}/activate`,
      {
        method: "POST",
      }
    );
  },

  deactivate(id: string) {
    return apiClient<{ message: string }>(
      `accounting-accounts/${id}/deactivate`,
      {
        method: "POST",
      }
    );
  },

  seedStandardPlan(companyId: string) {
    return apiClient<{
      createdCount: number;
      existingCount: number;
      totalCount: number;
      message: string;
    }>(
      `accounting-account-seed/${companyId}`,
      {
        method: "POST",
      }
    );
  },
};
