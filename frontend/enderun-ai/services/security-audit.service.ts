import type { Paged } from "@/lib/api/paged";
import { apiClient } from "@/lib/api/api-client";

const root = "security-audit/events";

/**
 * Güvenlik denetim kayıtları — salt okunur.
 *
 * Uç `Admin` / `Genel Müdür` rolüne VE `audit-log.view` iznine
 * bağlı; ikisi birden gerekiyor.
 *
 * BİLİNEN EKSİK: kayıtlardaki `ipAddress` şu an gerçek istemci
 * IP'sini taşımıyor — genel proxy `X-Forwarded-For`'u iletmediği
 * için login dışındaki her işlem 127.0.0.1 olarak yazılıyor.
 * Ekran alanı olduğu gibi gösteriyor ve bunu bir not olarak
 * söylüyor; sessizce doğruymuş gibi sunmak denetimi yanıltırdı.
 * (TEMIZLIK-TARAMASI'nda ayrı kalem.)
 */

export interface SecurityAuditEvent {
  id: string;
  actorUserId: string | null;
  actorUsername: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  /** Serbest biçimli JSON; ekran ham gösterir. */
  detailsJson: string | null;
  ipAddress: string | null;
  occurredAtUtc: string;
}

export const securityAuditService = {
  /** `take` uçta 1-200 arasına sıkıştırılıyor; varsayılan 50. */
  getEvents(params?: {
    entityType?: string;
    entityId?: string;
    take?: number;
  }) {
    const query = new URLSearchParams();
    if (params?.entityType) query.set("entityType", params.entityType);
    if (params?.entityId) query.set("entityId", params.entityId);
    if (params?.take) query.set("take", String(params.take));

    const suffix = query.toString();
    return apiClient<Paged<SecurityAuditEvent>>(
      suffix ? `${root}?${suffix}` : root
    );
  },
};
