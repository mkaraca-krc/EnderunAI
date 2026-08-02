import { apiClient } from "@/lib/api/api-client";


export interface CurrentAccountListItem {
  id: string;
  companyId: string;
  companyName: string;

  code: string;
  title: string;
  shortName?: string | null;

  roles: number;
  status: number;

  taxOffice?: string | null;
  taxNumber?: string | null;

  authorizedPerson?: string | null;
  phone?: string | null;
  email?: string | null;

  paymentTerm?: string | null;
  creditLimit?: number | null;

  isActive: boolean;
}



export interface CurrentAccountSummary {
  totalReceivable: number;
  totalPayable: number;
  netBalance: number;
  accountCount: number;
  // Cari hareket/bakiye defteri (fatura + tahsilat) henüz uygulamaya
  // bağlı değil - false ise yukarıdaki tutar alanları gerçek değil,
  // arayüz bunları "veri yok" olarak göstermeli. accountCount her
  // zaman gerçektir (kayıtlı cari kart sayısı).
  balancesAvailable: boolean;
  message?: string | null;
}



export const currentAccountService = {


  getAll(companyId?: string) {

    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";


    return apiClient<CurrentAccountListItem[]>(
      `current-accounts${query}`
    );

  },


  getSummary() {

    return apiClient<CurrentAccountSummary>(
      "finance/cari-summary"
    );

  }

};
