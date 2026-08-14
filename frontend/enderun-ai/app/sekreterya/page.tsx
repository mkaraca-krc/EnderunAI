"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  secretariatPlannerService,
  type SecretariatDashboard,
} from "@/services/secretariat-planner.service";

const modules = [
  { href: "/sekreterya/evrak", title: "Gelen / Giden Evrak", detail: "Kayıt, yönlendirme, arşiv ve ek dosyalar", icon: "✉" },
  { href: "/sekreterya/kargo", title: "Kargo Takibi", detail: "Gelen ve giden gönderiler", icon: "□" },
  { href: "/sekreterya/ziyaretciler", title: "Ziyaretçiler", detail: "Beklenen ve içerideki ziyaretçiler", icon: "♙" },
  { href: "/sekreterya/telefon-notlari", title: "Telefon Notları", detail: "Aramalar, iletim ve geri dönüş takibi", icon: "☎" },
  { href: "/sekreterya/toplantilar", title: "Toplantılar", detail: "Katılımcı, zaman ve sonuç takibi", icon: "▤" },
  { href: "/sekreterya/randevular", title: "Randevular", detail: "Yönetim ve ekip randevu planı", icon: "◷" },
];

export default function SecretariatDashboardPage() {
  const [dashboard, setDashboard] = useState<SecretariatDashboard | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    secretariatPlannerService
      .dashboard()
      .then((value) => {
        if (active) setDashboard(value);
      })
      .catch((cause) => {
        if (active) {
          setError(cause instanceof Error ? cause.message : "Sekreterya özeti yüklenemedi.");
        }
      });
    return () => {
      active = false;
    };
  }, []);

  const stats = dashboard
    ? [
        ["Bugün Gelen Evrak", dashboard.todayIncoming],
        ["Bugün Giden Evrak", dashboard.todayOutgoing],
        ["Bekleyen Evrak", dashboard.pendingDocuments],
        ["Geciken Evrak", dashboard.overdueDocuments],
        ["Yoldaki Kargo", dashboard.cargoInTransit],
        ["İçerideki Ziyaretçi", dashboard.visitorsInside],
        ["Açık Telefon Notu", dashboard.openPhoneNotes],
        ["Bugünkü Toplantı", dashboard.todayMeetings],
        ["Bugünkü Randevu", dashboard.todayAppointments],
      ]
    : [];

  return (
    <ErpShell
      design="redwood"
      title="Sekreterya Merkezi"
      description="Evrak, kargo, ziyaretçi ve yönetim takvimini tek merkezden yönetin."
    >
      <div className="space-y-6">
        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            {error}
          </div>
        )}

        <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {modules.map((module) => (
            <Link
              key={module.href}
              href={module.href}
              className="rounded-2xl border bg-white p-5 shadow-sm transition hover:border-slate-400 hover:shadow-md"
            >
              <div className="flex items-start gap-4">
                <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-700 text-lg text-white">
                  {module.icon}
                </span>
                <div>
                  <h2 className="font-semibold text-slate-900">{module.title}</h2>
                  <p className="mt-1 text-sm leading-6 text-slate-500">{module.detail}</p>
                </div>
              </div>
            </Link>
          ))}
        </section>

        <section className="rounded-2xl border bg-white p-5 shadow-sm">
          <h2 className="text-lg font-semibold">Anlık Durum</h2>
          {!dashboard ? (
            <p className="mt-4 text-sm text-slate-500">Sekreterya verileri hazırlanıyor...</p>
          ) : (
            <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {stats.map(([label, value]) => (
                <div key={String(label)} className="rounded-xl bg-slate-50 p-4">
                  <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
                  <p className="mt-2 text-2xl font-semibold text-slate-900">{value}</p>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="rounded-2xl border bg-white p-5 shadow-sm">
          <h2 className="text-lg font-semibold">Son Hareketler</h2>
          {!dashboard || dashboard.recentActivities.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">Henüz hareket kaydı bulunmuyor.</p>
          ) : (
            <div className="mt-4 divide-y">
              {dashboard.recentActivities.map((activity) => (
                <div key={`${activity.module}-${activity.recordId}-${activity.actionAtUtc}`} className="flex flex-wrap items-center justify-between gap-3 py-3">
                  <div>
                    <p className="font-medium text-slate-900">{activity.title}</p>
                    <p className="text-xs text-slate-500">
                      {activity.module} · {activity.action}
                      {activity.userName ? ` · ${activity.userName}` : ""}
                    </p>
                  </div>
                  <time className="text-xs text-slate-500">
                    {new Intl.DateTimeFormat("tr-TR", {
                      dateStyle: "short",
                      timeStyle: "short",
                    }).format(new Date(activity.actionAtUtc))}
                  </time>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </ErpShell>
  );
}
