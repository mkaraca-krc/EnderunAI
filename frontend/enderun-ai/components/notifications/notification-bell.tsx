"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";

import {
  notificationService,
  type NotificationItem,
} from "@/services/notification.service";
import { companyService } from "@/services/company.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Kritik / Uyarı / Bilgi — motordaki şiddet kademeleriyle aynı. */
const SEVERITY_TONE: Record<number, string> = {
  2: "border-rose-300 bg-rose-50 text-rose-800",
  1: "border-amber-300 bg-amber-50 text-amber-800",
  0: "border-slate-300 bg-slate-50 text-slate-700",
};

/**
 * Bildirim çanı.
 *
 * Üst çubuktaki düğme bugüne kadar ÖLÜYDÜ (hiçbir işleyicisi yoktu).
 * Buradaki liste motordan geliyor; içerik kullanıcının yetkisine göre
 * uçta süzülüyor, ekran ayrıca bir filtre uygulamıyor — iki yerde
 * süzmek, birinin gevşemesi durumunda sessiz bir sızıntı demek.
 */
export default function NotificationBell() {
  const router = useRouter();

  const [companyId, setCompanyId] = useState("");
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    void (async () => {
      try {
        const companies = await companyService.getAll();
        if (companies.length > 0) setCompanyId(companies[0].id);
      } catch {
        setCompanyId("");
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) return;

    try {
      const data = await notificationService.list(companyId);
      setItems(data.items);
      setUnread(data.unreadCount);
    } catch {
      // Çan sessizce boş kalır: bildirim listesi alınamadı diye
      // kullanıcının önüne hata basmak, asıl işini bölmek olurdu.
      setItems([]);
      setUnread(0);
    }
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  // Panel açıkken dışarı tıklama kapatır.
  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      if (!panelRef.current?.contains(event.target as Node)) setOpen(false);
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  async function act(
    id: string,
    action: (id: string) => Promise<unknown>,
  ) {
    setBusyId(id);

    try {
      await action(id);
      await load();
    } catch {
      // Yut: listeyi yeniden yükleyince gerçek durum görünür.
    } finally {
      setBusyId(null);
    }
  }

  function goTo(item: NotificationItem) {
    if (!item.targetPath) return;

    setOpen(false);

    // Gidilen bildirim okunmuş sayılır: kullanıcı zaten baktı.
    void act(item.id, notificationService.markRead);

    router.push(item.targetPath);
  }

  return (
    <div className="relative" ref={panelRef}>
      <button
        type="button"
        title="Bildirimler"
        aria-label={
          unread > 0 ? `Bildirimler (${unread} okunmamış)` : "Bildirimler"
        }
        onClick={() => setOpen(!open)}
      >
        ♢
        {unread > 0 ? (
          <span className="absolute -right-1 -top-1 min-w-4 rounded-full bg-rose-600 px-1 text-[10px] font-bold leading-4 text-white">
            {unread > 9 ? "9+" : unread}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="absolute right-0 z-50 mt-2 max-h-[70vh] w-96 overflow-y-auto rounded-xl border border-slate-200 bg-white shadow-xl">
          <header className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <h2 className="text-sm font-bold text-slate-800">Bildirimler</h2>
            <span className="text-xs text-slate-500">
              {unread} okunmamış
            </span>
          </header>

          {items.length === 0 ? (
            <p className="px-4 py-6 text-sm text-slate-500">
              Bekleyen bildirim yok.
            </p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {items.map((item) => (
                <li key={item.id} className="px-4 py-3">
                  <div className="flex items-start gap-2">
                    <span
                      className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${
                        SEVERITY_TONE[item.severity] ?? SEVERITY_TONE[0]
                      }`}
                    >
                      {item.severityName}
                    </span>

                    <div className="min-w-0 flex-1">
                      <p
                        className={
                          item.status === "Open"
                            ? "text-sm font-semibold text-slate-900"
                            : "text-sm text-slate-700"
                        }
                      >
                        {item.title}
                      </p>

                      {item.detail ? (
                        <p className="mt-0.5 text-xs text-slate-500">
                          {item.detail}
                        </p>
                      ) : null}

                      {item.dueDate ? (
                        <p className="mt-0.5 text-[11px] text-slate-400">
                          Vade {dateFormat.format(new Date(item.dueDate))}
                        </p>
                      ) : null}

                      <div className="mt-2 flex flex-wrap gap-3 text-[11px]">
                        {item.targetPath ? (
                          <button
                            type="button"
                            onClick={() => goTo(item)}
                            className="text-brand-700 hover:underline"
                          >
                            Git
                          </button>
                        ) : null}

                        {item.status === "Open" ? (
                          <button
                            type="button"
                            disabled={busyId === item.id}
                            onClick={() =>
                              void act(item.id, notificationService.markRead)
                            }
                            className="text-slate-600 hover:underline disabled:opacity-50"
                          >
                            Okundu
                          </button>
                        ) : null}

                        <button
                          type="button"
                          disabled={busyId === item.id}
                          onClick={() =>
                            void act(item.id, (id) =>
                              notificationService.snooze(
                                id,
                                new Date(
                                  Date.now() + 7 * 86_400_000,
                                ).toISOString(),
                              ),
                            )
                          }
                          className="text-slate-600 hover:underline disabled:opacity-50"
                        >
                          1 hafta ertele
                        </button>

                        <button
                          type="button"
                          disabled={busyId === item.id}
                          onClick={() =>
                            void act(item.id, notificationService.dismiss)
                          }
                          className="text-slate-600 hover:underline disabled:opacity-50"
                        >
                          Kapat
                        </button>
                      </div>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </div>
  );
}
