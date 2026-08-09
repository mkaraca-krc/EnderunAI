import { apiClient } from "@/lib/api/api-client";

export type RehireDecision = "blocked" | "warning" | "clear" | "no-match";

/**
 * İşe alım öncesi TC kontrolünün sonucu.
 *
 * Kırmızı eşleşmede engel körlemesine değil: kim, ne zaman ayrıldı,
 * hangi kod ve GEREKÇE birlikte gelir.
 */
export type RehireCheckResult = {
  identityNumber: string;
  decision: RehireDecision;
  matched: boolean;
  message: string;

  personnelId?: string | null;
  personnelFullName?: string | null;
  employeeNumber?: string | null;
  personnelStatus?: number | null;
  recordDeleted?: boolean | null;
  employmentStartDate?: string | null;
  employmentEndDate?: string | null;

  hasTermination?: boolean;
  terminationId?: string | null;
  terminationDate?: string | null;
  terminationReason?: number | null;

  rehireCode?: number | null;
  rehireCodeName?: string | null;
  rehireNote?: string | null;
  rehireMarkedAtUtc?: string | null;
  rehireMarkedByName?: string | null;
};

export const rehireCheckService = {
  /**
   * TC doğrulanır doğrulanmaz, form dolmadan çağrılır. Kayıt
   * oluşturmaz.
   */
  check(identityNumber: string) {
    return apiClient<RehireCheckResult>(
      `hr/ise-alim/tc-kontrol?identityNumber=${encodeURIComponent(identityNumber)}`
    );
  },
};
