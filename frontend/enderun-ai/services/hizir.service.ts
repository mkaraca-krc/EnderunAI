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
};
