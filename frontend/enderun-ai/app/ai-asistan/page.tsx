"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import HizirChat from "@/components/hizir/hizir-chat";
import {
  hizirService,
  type HizirConversationSummary,
} from "@/services/hizir.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});

export default function AiAssistantPage() {
  const [conversations, setConversations] = useState<HizirConversationSummary[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    try {
      setConversations(await hizirService.getConversations());
    } catch {
      setConversations([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <ErpShell
      title="Hızır"
      description="Şirket verilerinize göre cevap veren, yetkilerinize saygı gösteren asistan"
    >
      <div className="grid gap-6 xl:grid-cols-[1fr_320px]">
        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <HizirChat pagePath="/ai-asistan" variant="page" />
        </div>

        <aside className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h3 className="font-bold text-slate-900">Geçmiş Sohbetler</h3>

          {loading ? (
            <p className="mt-4 text-sm text-slate-500">Yükleniyor...</p>
          ) : conversations.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">
              Henüz sohbet yok. Soldaki alandan ilk sorunuzu sorabilirsiniz.
            </p>
          ) : (
            <ul className="mt-4 space-y-3 text-sm">
              {conversations.map((conversation) => (
                <li
                  key={conversation.id}
                  className="rounded-xl border border-slate-200 bg-slate-50 p-3"
                >
                  <p className="font-medium text-slate-800">{conversation.title}</p>
                  <p className="mt-1 text-xs text-slate-500">
                    {dateFormat.format(new Date(conversation.lastMessageAtUtc))} ·{" "}
                    {conversation.messageCount} mesaj
                  </p>
                </li>
              ))}
            </ul>
          )}

          <div className="mt-6 rounded-xl border border-slate-200 bg-slate-50 p-3">
            <h4 className="text-sm font-semibold text-slate-800">Nasıl çalışır?</h4>
            <p className="mt-2 text-xs leading-5 text-slate-600">
              Hızır yalnızca sizin yetkiniz dahilindeki verileri görebilir.
              Yetkiniz olmayan bir bilgiyi sorduğunuzda veriyi tahmin etmez,
              göremeyeceğinizi söyler. Verinin bulunmadığı durumlarda da
              rakam uydurmaz.
            </p>
          </div>
        </aside>
      </div>
    </ErpShell>
  );
}
