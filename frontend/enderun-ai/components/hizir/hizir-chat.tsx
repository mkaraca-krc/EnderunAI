"use client";

import { FormEvent, useCallback, useEffect, useRef, useState } from "react";

import { ApiError } from "@/lib/api/api-client";
import {
  hizirService,
  HizirMessageRole,
  type HizirMessage,
  type HizirPendingAction,
} from "@/services/hizir.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "Hızır şu anda cevap veremedi.";
}

const QUICK_QUESTIONS = [
  "Bekleyen onaylarım neler?",
  "Yaklaşan çek vadeleri neler?",
  "Günlük saha raporunu nereden giriyorum?",
  "Projelerin durumu nedir?",
];

type Props = {
  /** Kullanıcının o an baktığı sayfa — Hızır'a bağlam olarak gider. */
  pagePath: string;
  /** Panel yüksekliği; baloncukta dar, tam sayfada geniş. */
  variant?: "panel" | "page";
};

export default function HizirChat({ pagePath, variant = "panel" }: Props) {
  const [messages, setMessages] = useState<HizirMessage[]>([]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [pendingActions, setPendingActions] = useState<HizirPendingAction[]>([]);
  const [resolvingActionId, setResolvingActionId] = useState<string | null>(null);

  const scrollRef = useRef<HTMLDivElement>(null);

  const refreshPendingActions = useCallback(async () => {
    try {
      setPendingActions(await hizirService.getPendingActions());
    } catch {
      // Bekleyen eylem listesi okunamazsa sohbet çalışmaya devam etsin.
    }
  }, []);

  useEffect(() => {
    let cancelled = false;

    hizirService
      .getStatus()
      .then((status) => {
        if (!cancelled && !status.isConfigured && status.message) {
          setNotice(status.message);
        }
      })
      .catch(() => {
        // Durum okunamazsa sohbet yine denenebilir; hata gönderimde çıkar.
      });

    // Önceki oturumdan kalan onay bekleyen eylem varsa hemen görünsün.
    void refreshPendingActions();

    return () => {
      cancelled = true;
    };
  }, [refreshPendingActions]);

  useEffect(() => {
    scrollRef.current?.scrollTo({
      top: scrollRef.current.scrollHeight,
      behavior: "smooth",
    });
  }, [messages, sending]);

  const send = useCallback(
    async (text: string) => {
      const message = text.trim();
      if (!message || sending) return;

      setError("");
      setSending(true);

      // Kullanıcı mesajı hemen görünsün; cevap gelene kadar beklemesin.
      const optimistic: HizirMessage = {
        id: `local-${Date.now()}`,
        role: HizirMessageRole.User,
        content: message,
        pagePath,
        createdAtUtc: new Date().toISOString(),
      };

      setMessages((current) => [...current, optimistic]);
      setInput("");

      try {
        const result = await hizirService.ask({
          conversationId,
          message,
          pagePath,
        });

        setConversationId(result.conversationId);

        setMessages((current) => [
          ...current,
          {
            id: `answer-${Date.now()}`,
            role: HizirMessageRole.Assistant,
            content: result.answer,
            pagePath,
            createdAtUtc: new Date().toISOString(),
          },
        ]);

        // Cevapta onay gerektiren bir eylem hazırlanmış olabilir.
        await refreshPendingActions();
      } catch (requestError) {
        setError(getErrorMessage(requestError));
        // Cevap alınamadıysa iyimser mesaj listede kalmasın.
        setMessages((current) => current.filter((x) => x.id !== optimistic.id));
        setInput(message);
      } finally {
        setSending(false);
      }
    },
    [conversationId, pagePath, refreshPendingActions, sending]
  );

  /**
   * Onay ve vazgeçme. Eylem yalnızca burada, kullanıcının kendi
   * oturumuyla yürütülür; sonuç sohbete yazılır.
   */
  const resolveAction = useCallback(
    async (action: HizirPendingAction, approve: boolean) => {
      if (resolvingActionId) return;

      setError("");
      setResolvingActionId(action.id);

      try {
        const result = approve
          ? await hizirService.confirmAction(action.id)
          : await hizirService.cancelAction(action.id);

        setMessages((current) => [
          ...current,
          {
            id: `action-${action.id}`,
            role: HizirMessageRole.Assistant,
            content: result.resultMessage ?? (approve ? "Eylem tamamlandı." : "Vazgeçildi."),
            pagePath,
            createdAtUtc: new Date().toISOString(),
          },
        ]);
      } catch (actionError) {
        setError(getErrorMessage(actionError));
      } finally {
        setResolvingActionId(null);
        await refreshPendingActions();
      }
    },
    [pagePath, refreshPendingActions, resolvingActionId]
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    void send(input);
  }

  const isPage = variant === "page";

  return (
    <div className="flex h-full flex-col">
      {notice && (
        <div className="mb-3 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
          {notice}
        </div>
      )}

      <div
        ref={scrollRef}
        className={`flex-1 overflow-y-auto rounded-xl bg-slate-50 p-4 ${
          isPage ? "min-h-[420px]" : "min-h-[260px]"
        }`}
      >
        {messages.length === 0 ? (
          <div className="space-y-3">
            <p className="text-sm leading-6 text-slate-600">
              Merhaba, ben Hızır. Şirket verilerinize göre soru
              cevaplayabilir, bir işlemi nereden yapacağınızı adım adım
              anlatabilirim. Yalnızca sizin yetkiniz dahilindeki verileri
              görebilirim.
            </p>

            <div className="space-y-2">
              {QUICK_QUESTIONS.map((question) => (
                <button
                  key={question}
                  type="button"
                  onClick={() => void send(question)}
                  className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-left text-sm text-slate-600 hover:border-cyan-300 hover:bg-cyan-50"
                >
                  {question}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="space-y-3">
            {messages.map((message) => {
              const isUser = message.role === HizirMessageRole.User;

              return (
                <div
                  key={message.id}
                  className={`flex ${isUser ? "justify-end" : "justify-start"}`}
                >
                  <div
                    className={`max-w-[85%] whitespace-pre-wrap rounded-2xl px-4 py-2.5 text-sm leading-6 ${
                      isUser
                        ? "bg-cyan-700 text-white"
                        : "border border-slate-200 bg-white text-slate-700"
                    }`}
                  >
                    {message.content}
                  </div>
                </div>
              );
            })}

            {sending && (
              <div className="flex justify-start">
                <div className="rounded-2xl border border-slate-200 bg-white px-4 py-2.5 text-sm text-slate-500">
                  <span className="inline-flex items-center gap-1">
                    Hızır yazıyor
                    <span className="animate-pulse">...</span>
                  </span>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {pendingActions.length > 0 && (
        <div className="mt-3 space-y-2">
          {pendingActions.map((action) => (
            <div
              key={action.id}
              className="rounded-xl border border-amber-300 bg-amber-50 px-3 py-2.5"
            >
              <p className="text-xs font-bold uppercase tracking-wide text-amber-700">
                Onayınızı bekliyor
              </p>

              <p className="mt-1 whitespace-pre-wrap text-sm leading-6 text-slate-700">
                {action.summary}
              </p>

              <p className="mt-1 text-xs text-slate-500">
                Onaylamazsanız bu işlem yapılmaz.
              </p>

              <div className="mt-2 flex gap-2">
                <button
                  type="button"
                  disabled={resolvingActionId !== null}
                  onClick={() => void resolveAction(action, true)}
                  className="rounded-lg bg-emerald-700 px-3 py-1.5 text-xs font-bold text-white hover:bg-emerald-800 disabled:opacity-60"
                >
                  {resolvingActionId === action.id ? "Yapılıyor..." : "Onayla"}
                </button>
                <button
                  type="button"
                  disabled={resolvingActionId !== null}
                  onClick={() => void resolveAction(action, false)}
                  className="rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-bold text-slate-600 hover:bg-slate-50 disabled:opacity-60"
                >
                  Vazgeç
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {error && (
        <div className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
          {error}
        </div>
      )}

      <form onSubmit={submit} className="mt-3 flex gap-2">
        <input
          value={input}
          onChange={(event) => setInput(event.target.value)}
          disabled={sending}
          placeholder="Hızır'a sorun..."
          className="min-w-0 flex-1 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm text-slate-900 outline-none focus:border-cyan-500 disabled:bg-slate-50"
        />
        <button
          type="submit"
          disabled={sending || !input.trim()}
          className="rounded-xl bg-cyan-700 px-4 py-2.5 text-sm font-bold text-white hover:bg-cyan-800 disabled:opacity-60"
        >
          {sending ? "..." : "Gönder"}
        </button>
      </form>
    </div>
  );
}
