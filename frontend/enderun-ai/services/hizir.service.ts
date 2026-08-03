import { apiClient } from "@/lib/api/api-client";

export type HizirStatus = {
  isConfigured: boolean;
  message: string | null;
};

export type HizirChatResponse = {
  conversationId: string;
  answer: string;
  usedTools: string[];
  deniedTools: string[];
};

export type HizirConversationSummary = {
  id: string;
  title: string;
  startedOnPath: string | null;
  lastMessageAtUtc: string;
  messageCount: number;
};

export const HizirMessageRole = {
  User: 0,
  Assistant: 1,
} as const;

export type HizirMessage = {
  id: string;
  role: number;
  content: string;
  pagePath: string | null;
  createdAtUtc: string;
};

export const HizirPendingActionStatus = {
  Pending: 0,
  Executed: 1,
  Cancelled: 2,
  Expired: 3,
  Failed: 4,
} as const;

/**
 * Hızır'ın hazırladığı ama henüz YAPMADIĞI eylem. Özet sunucuda
 * üretilir; kullanıcı onaylayana kadar hiçbir şey değişmez.
 */
export type HizirPendingAction = {
  id: string;
  actionName: string;
  summary: string;
  status: number;
  expiresAtUtc: string;
  resultMessage: string | null;
};

export const hizirService = {
  getStatus() {
    return apiClient<HizirStatus>("hizir/status");
  },

  ask(payload: {
    conversationId?: string | null;
    message: string;
    pagePath?: string | null;
  }) {
    return apiClient<HizirChatResponse>("hizir/chat", {
      method: "POST",
      body: payload,
    });
  },

  getConversations() {
    return apiClient<HizirConversationSummary[]>("hizir/conversations");
  },

  getMessages(conversationId: string) {
    return apiClient<HizirMessage[]>(`hizir/conversations/${conversationId}`);
  },

  getPendingActions() {
    return apiClient<HizirPendingAction[]>("hizir/actions/pending");
  },

  /** Eylemin gerçekten yürütüldüğü tek uç. */
  confirmAction(id: string) {
    return apiClient<HizirPendingAction>(`hizir/actions/${id}/confirm`, {
      method: "POST",
    });
  },

  cancelAction(id: string) {
    return apiClient<HizirPendingAction>(`hizir/actions/${id}/cancel`, {
      method: "POST",
    });
  },
};
