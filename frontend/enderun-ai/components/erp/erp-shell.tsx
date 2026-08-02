"use client";

import Link from "next/link";
import { ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { apiClient } from "@/lib/api/api-client";
import { LogoutButton } from "@/components/logout-button";
import WorkHourSessionWatcher from "@/components/work-hour-session-watcher";

type ErpShellProps = {
  title: string;
  description?: string;
  children: ReactNode;
};

type MenuItem = {
  label: string;
  href: string;
  icon?: string;
};

type MenuGroup = {
  key: string;
  label: string;
  items: MenuItem[];
};

type CurrentSession = {
  id?: string | null;
  username?: string | null;
  fullName?: string | null;
  roles: string[];
  permissions: string[];
};

function requiredPermissionForPath(pathname: string): string | string[] | null {
  if (pathname.startsWith("/sistem-yonetimi/sirket-ayarlari")) {
    return ["company-settings.view", "system.users.manage"];
  }
  if (pathname.startsWith("/sistem-yonetimi")) return "system.users.manage";
  if (/^\/insan-kaynaklari\/(bordro|ucret-kartlari|ek-ucretler|avanslar)/.test(pathname)) {
    return "payroll.view";
  }
  if (/^\/insan-kaynaklari\/(puantaj|gunluk-puantaj|izinler|fazla-mesai)/.test(pathname)) {
    return "attendance.view";
  }
  if (pathname.startsWith("/insan-kaynaklari")) return "personnel.view";
  if (pathname.startsWith("/muhasebe")) return "accounting.view";
  if (pathname.startsWith("/finans")) return "finance.view";
  if (
    pathname.startsWith("/hakedis") ||
    pathname.startsWith("/fiyat-farki") ||
    pathname.startsWith("/metrajlar")
  ) return "hakedis.view";
  if (pathname.startsWith("/satin-alma/butce-onay")) {
    return ["purchasing.view", "finance.view"];
  }
  if (pathname.startsWith("/satin-alma")) return "purchasing.view";
  if (pathname.startsWith("/depo")) return "inventory.view";
  if (
    pathname.startsWith("/muhendislik") ||
    pathname.startsWith("/kesifler")
  ) return "engineering.view";
  if (
    pathname.startsWith("/projeler") ||
    pathname.startsWith("/teklifler")
  ) return "projects.view";
  if (
    pathname.startsWith("/sekreterya") ||
    pathname.startsWith("/dokumanlar")
  ) return "secretariat.view";
  if (pathname.startsWith("/gorevler")) return "tasks.view";
  if (pathname.startsWith("/raporlar")) return "reports.view";
  if (pathname.startsWith("/ai-asistan")) return "ai.use";
  if (
    pathname.startsWith("/sirketler") ||
    pathname.startsWith("/subeler") ||
    pathname.startsWith("/cariler")
  ) return "companies.view";
  return null;
}

const groups: MenuGroup[] = [
  {
    key: "organization",
    label: "ORGANİZASYON",
    items: [
      {
        label: "Şirketler",
        href: "/sirketler",
        icon: "▦",
      },
      {
        label: "Şubeler",
        href: "/subeler",
        icon: "▤",
      },
    ],
  },
  {
    key: "accounting",
    label: "MUHASEBE",
    items: [
      {
        label: "Muhasebe Merkezi",
        href: "/muhasebe",
        icon: "▦",
      },
      {
        label: "Hesap Planı",
        href: "/muhasebe/hesap-plani",
        icon: "○",
      },
      {
        label: "Hesap Planı Aktar",
        href: "/muhasebe/hesap-plani/aktar",
        icon: "○",
      },
      {
        label: "Muhasebe Fişleri",
        href: "/muhasebe/fisler",
        icon: "○",
      },
      {
        label: "Yeni Muhasebe Fişi",
        href: "/muhasebe/fisler/yeni",
        icon: "○",
      },
      {
        label: "Yevmiye Defteri",
        href: "/muhasebe/yevmiye",
        icon: "○",
      },
      {
        label: "Büyük Defter",
        href: "/muhasebe/buyuk-defter",
        icon: "○",
      },
      {
        label: "Rapor Merkezi",
        href: "/raporlar",
        icon: "▤",
      },
    ],
  },
  {
    key: "finance",
    label: "FİNANS",
    items: [
      {
        label: "Finans Merkezi",
        href: "/finans",
        icon: "▨",
      },
      {
        label: "Cari Kartlar",
        href: "/cariler",
        icon: "○",
      },
      {
        label: "Hakedişler",
        href: "/hakedis",
        icon: "▧",
      },
      {
        label: "Yeni Hakediş",
        href: "/hakedis/yeni",
        icon: "○",
      },
      {
        label: "Fiyat Farkı",
        href: "/fiyat-farki",
        icon: "∆",
      },
    ],
  },
  {
    key: "purchasing",
    label: "SATIN ALMA",
    items: [
      {
        label: "Satın Alma Talepleri",
        href: "/satin-alma",
        icon: "⌑",
      },
      {
        label: "RFQ Süreçleri",
        href: "/satin-alma/rfq",
        icon: "≋",
      },
      {
        label: "Siparişler",
        href: "/satin-alma/siparis",
        icon: "▤",
      },
      {
        label: "Satın Alma Raporları",
        href: "/satin-alma/raporlar",
        icon: "▦",
      },
      {
        label: "Karar Destek",
        href: "/satin-alma/karar-destek",
        icon: "★",
      },
      {
        label: "Bütçe ve Onay",
        href: "/satin-alma/butce-onay",
        icon: "✓",
      },
      {
        label: "Mal Kabul",
        href: "/depo-stok/mal-kabul",
        icon: "○",
      },
    ],
  },
  {
    key: "inventory",
    label: "DEPO VE STOK",
    items: [
      {
        label: "Depo Merkezi",
        href: "/depo-stok",
        icon: "⌂",
      },
      {
        label: "Yeni Depo",
        href: "/depo-stok/yeni",
        icon: "○",
      },
      {
        label: "Stok Giriş",
        href: "/depo-stok/giris",
        icon: "○",
      },
      {
        label: "Stok Çıkış",
        href: "/depo-stok/cikis",
        icon: "○",
      },
      {
        label: "Stok Hareketleri",
        href: "/depo-stok/hareketler",
        icon: "○",
      },
      {
        label: "Depo Transferi",
        href: "/depo-stok/transfer",
        icon: "○",
      },
      {
        label: "Malzeme Talepleri",
        href: "/depo-stok/malzeme-talepleri",
        icon: "○",
      },
      {
        label: "Rezervasyonlar",
        href: "/depo-stok/rezervasyonlar",
        icon: "○",
      },
    ],
  },
  {
    key: "projects",
    label: "PROJE VE OPERASYON",
    items: [
      {
        label: "Projeler",
        href: "/projeler",
        icon: "▣",
      },
      {
        label: "Keşifler",
        href: "/kesifler",
        icon: "▤",
      },
      {
        label: "Metrajlar",
        href: "/metrajlar",
        icon: "▥",
      },
      {
        label: "Teklifler",
        href: "/teklifler",
        icon: "₺",
      },
    ],
  },
  {
    key: "human-resources",
    label: "İNSAN KAYNAKLARI",
    items: [
      {
        label: "İK Dashboard",
        href: "/insan-kaynaklari",
        icon: "▦",
      },
      {
        label: "Personeller",
        href: "/insan-kaynaklari/personeller",
        icon: "♙",
      },
      {
        label: "Personel 360°",
        href: "/insan-kaynaklari/personel-360",
        icon: "◎",
      },
      {
        label: "Maaş Kartları",
        href: "/insan-kaynaklari/ucret-kartlari",
        icon: "₺",
      },
      {
        label: "Ek Ücretler",
        href: "/insan-kaynaklari/ek-ucretler",
        icon: "+",
      },
      {
        label: "Organizasyon",
        href: "/insan-kaynaklari/organizasyon",
        icon: "▤",
      },
      {
        label: "İşe Alım",
        href: "/insan-kaynaklari/ise-alim",
        icon: "+",
      },
      {
        label: "Puantaj",
        href: "/insan-kaynaklari/puantaj",
        icon: "◷",
      },
      {
        label: "İzin Yönetimi",
        href: "/insan-kaynaklari/izinler",
        icon: "○",
      },
      {
        label: "Fazla Mesai",
        href: "/insan-kaynaklari/fazla-mesai",
        icon: "○",
      },
      {
        label: "Avanslar",
        href: "/insan-kaynaklari/avanslar",
        icon: "₺",
      },
      {
        label: "Bordro",
        href: "/insan-kaynaklari/bordro",
        icon: "▧",
      },
      {
        label: "İK Raporları",
        href: "/insan-kaynaklari/raporlar",
        icon: "▤",
      },
      {
        label: "Onay Merkezi",
        href: "/insan-kaynaklari/onay-merkezi",
        icon: "✓",
      },
      {
        label: "Eğitimler",
        href: "/insan-kaynaklari/egitimler",
        icon: "◇",
      },
      {
        label: "Sertifikalar",
        href: "/insan-kaynaklari/sertifikalar",
        icon: "□",
      },
      {
        label: "Yetkinlikler",
        href: "/insan-kaynaklari/yetkinlikler",
        icon: "★",
      },
      {
        label: "Performans",
        href: "/insan-kaynaklari/performans",
        icon: "↗",
      },
      {
        label: "Disiplin",
        href: "/insan-kaynaklari/disiplin",
        icon: "⚖",
      },
      {
        label: "Zimmetler",
        href: "/insan-kaynaklari/zimmetler",
        icon: "▣",
      },
      {
        label: "Kariyer",
        href: "/insan-kaynaklari/kariyer",
        icon: "↑",
      },
    ],
  },
  {
    key: "engineering",
    label: "MÜHENDİSLİK",
    items: [
      {
        label: "Mühendislik Merkezi",
        href: "/muhendislik",
        icon: "◇",
      },
      {
        label: "Poz Kütüphanesi",
        href: "/muhendislik/pozlar",
        icon: "▦",
      },
      {
        label: "Reçeteler",
        href: "/muhendislik/receteler",
        icon: "⚙",
      },
      {
        label: "Fiyat Listeleri",
        href: "/teklifler/fiyatlar",
        icon: "₺",
      },
    ],
  },
  {
    key: "secretariat",
    label: "SEKRETERYA",
    items: [
      {
        label: "Gelen / Giden Evrak",
        href: "/sekreterya/evrak",
        icon: "✉",
      },
      {
        label: "Kargo Takibi",
        href: "/sekreterya/kargo",
        icon: "□",
      },
      {
        label: "Ziyaretçiler",
        href: "/sekreterya/ziyaretciler",
        icon: "♙",
      },
      {
        label: "Telefon Notları",
        href: "/sekreterya/telefon-notlari",
        icon: "☎",
      },
      {
        label: "Toplantılar",
        href: "/sekreterya/toplantilar",
        icon: "▤",
      },
      {
        label: "Randevular",
        href: "/sekreterya/randevular",
        icon: "◷",
      },
    ],
  },
  {
    key: "management",
    label: "YÖNETİM",
    items: [
      {
        label: "Onay Merkezi",
        href: "/onay-merkezi",
        icon: "✓",
      },
      {
        label: "Görevler",
        href: "/gorevler",
        icon: "☑",
      },
      {
        label: "Dokümanlar",
        href: "/dokumanlar",
        icon: "□",
      },
      {
        label: "Raporlar",
        href: "/raporlar",
        icon: "▤",
      },
    ],
  },
  {
    key: "system",
    label: "SİSTEM YÖNETİMİ",
    items: [
      {
        label: "Kullanıcılar ve Yetkiler",
        href: "/sistem-yonetimi/kullanicilar",
        icon: "⚿",
      },
      {
        label: "Yetki Matrisi",
        href: "/sistem-yonetimi/yetki-matrisi",
        icon: "▦",
      },
      {
        label: "Şirket Ayarları",
        href: "/sistem-yonetimi/sirket-ayarlari",
        icon: "⚙",
      },
      {
        label: "Erişim Talepleri",
        href: "/sistem-yonetimi/erisim-talepleri",
        icon: "⏱",
      },
    ],
  },
  {
    key: "ai",
    label: "ENDERUN AI",
    items: [
      {
        label: "AI Asistan",
        href: "/ai-asistan",
        icon: "⌘",
      },
    ],
  },
];

function pathOnly(href: string) {
  return href.split("?")[0];
}

export default function ErpShell({
  title,
  description,
  children,
}: ErpShellProps) {
  const pathname = usePathname();

  useEffect(() => {
    document.title = `Enderun ERP - ${title}`;
  }, [title]);

  const [collapsed, setCollapsed] = useState(false);
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({});
  const navRef = useRef<HTMLElement | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentSession | null>(null);

  useEffect(() => {
    let active = true;

    void apiClient<CurrentSession>("auth/me")
      .then((session) => {
        if (active) setCurrentUser(session);
      })
      .catch(() => {
        if (active) setCurrentUser(null);
      });

    return () => {
      active = false;
    };
  }, []);

  const visibleGroups = useMemo(() => {
    if (
      !currentUser ||
      currentUser.roles.includes("Admin") ||
      currentUser.roles.includes("Genel Müdür")
    ) {
      return groups;
    }

    const permissions = new Set(currentUser.permissions);
    return groups
      .map((group) => ({
        ...group,
        items: group.items.filter((item) => {
          const required = requiredPermissionForPath(item.href);
          return (
            !required ||
            (Array.isArray(required)
              ? required.some((permission) => permissions.has(permission))
              : permissions.has(required))
          );
        }),
      }))
      .filter((group) => group.items.length > 0);
  }, [currentUser]);

  const sessionInitials = (currentUser?.fullName || currentUser?.username || "K")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase("tr-TR"))
    .join("");

  const activeGroup = useMemo(
    () =>
      visibleGroups.find((group) =>
        group.items.some((item) => {
          const href = pathOnly(item.href);
          return pathname === href || pathname.startsWith(`${href}/`);
        })
      )?.key,
    [pathname, visibleGroups]
  );

  useEffect(() => {
    setOpenGroups((current) => {
      if (Object.keys(current).length > 0) {
        if (
          activeGroup &&
          !current[activeGroup]
        ) {
          return {
            ...current,
            [activeGroup]: true,
          };
        }

        return current;
      }

      const initial: Record<string, boolean> = {};

      for (const group of visibleGroups) {
        initial[group.key] =
          group.key === activeGroup ||
          [
            "accounting",
            "finance",
            "projects",
            "human-resources",
          ].includes(group.key);
      }

      return initial;
    });
  }, [activeGroup]);


  function saveSidebarScroll() {
    const nav = navRef.current;

    if (!nav) {
      return;
    }

    sessionStorage.setItem(
      "enderun-ai-sidebar-scroll",
      String(nav.scrollTop)
    );
  }

  useEffect(() => {
    const nav = navRef.current;

    if (!nav) {
      return;
    }

    const savedPosition = Number(
      sessionStorage.getItem(
        "enderun-ai-sidebar-scroll"
      ) ?? "0"
    );

    if (!Number.isFinite(savedPosition)) {
      return;
    }

    const restore = () => {
      if (navRef.current) {
        navRef.current.scrollTop =
          savedPosition;
      }
    };

    const frame1 =
      window.requestAnimationFrame(
        restore
      );

    const timer1 =
      window.setTimeout(
        restore,
        50
      );

    const timer2 =
      window.setTimeout(
        restore,
        200
      );

    const timer3 =
      window.setTimeout(
        restore,
        500
      );

    return () => {
      window.cancelAnimationFrame(
        frame1
      );

      window.clearTimeout(timer1);
      window.clearTimeout(timer2);
      window.clearTimeout(timer3);
    };
  }, [pathname, openGroups]);

  function toggleGroup(key: string) {
    if (collapsed) {
      setCollapsed(false);
      setOpenGroups((current) => ({ ...current, [key]: true }));
      return;
    }

    setOpenGroups((current) => ({
      ...current,
      [key]: !current[key],
    }));
  }

  return (
    <div className={`erp-layout ${collapsed ? "erp-sidebar-collapsed" : ""}`}>
      <WorkHourSessionWatcher />
      <aside className="erp-sidebar">
        <Link href="/dashboard" className="erp-brand">
          <span className="erp-brand-mark">
            <img src="/logo-star-white.png" alt="Enderun Enerji" />
          </span>
          {!collapsed && (
            <div>
              <strong>ENDERUN ERP</strong>
              <span>Yönetim Platformu</span>
            </div>
          )}
        </Link>

        <nav
          ref={navRef}
          className="erp-nav"
          onScroll={saveSidebarScroll}
          onClickCapture={saveSidebarScroll}
        >
          <Link
            className={`erp-nav-link ${
              pathname === "/dashboard" || pathname === "/" ? "active" : ""
            }`}
            href="/dashboard"
            title="Dashboard"
          >
            <span className="erp-nav-icon">⌂</span>
            {!collapsed && <span>Dashboard</span>}
          </Link>

          <Link
            className={`erp-nav-link ${
              pathname === "/onay-merkezi" ? "active" : ""
            }`}
            href="/onay-merkezi"
            title="Onay Merkezi"
          >
            <span className="erp-nav-icon">✓</span>
            {!collapsed && <span>Onay Merkezi</span>}
          </Link>

          {visibleGroups.map((group) => {
            const isOpen = openGroups[group.key];

            return (
              <section className="erp-nav-group" key={group.key}>
                <button
                  type="button"
                  className="erp-nav-group-button"
                  onClick={() => toggleGroup(group.key)}
                  title={group.label}
                >
                  {!collapsed && <span>{group.label}</span>}
                  {!collapsed && (
                    <span className={`erp-chevron ${isOpen ? "open" : ""}`}>
                      ›
                    </span>
                  )}
                </button>

                {(isOpen || collapsed) && (
                  <div className="erp-nav-group-items">
                    {group.items.map((item) => {
                      const href = pathOnly(item.href);
                      const active =
                        pathname === href || pathname.startsWith(`${href}/`);

                      return (
                        <Link
                          key={`${group.key}-${item.label}`}
                          className={`erp-nav-link ${active ? "active" : ""} ${
                            item.icon === "○" ? "sub-item" : ""
                          }`}
                          href={item.href}
                          title={item.label}
                        >
                          <span className="erp-nav-icon">{item.icon}</span>
                          {!collapsed && <span>{item.label}</span>}
                        </Link>
                      );
                    })}
                  </div>
                )}
              </section>
            );
          })}
        </nav>

        <div className="erp-sidebar-footer">
          <div className="erp-user-avatar">{sessionInitials}</div>
          {!collapsed && (
            <div className="erp-user-info">
              <strong>{currentUser?.fullName || currentUser?.username || "Kullanıcı"}</strong>
              <span>{currentUser?.roles[0] || "Kullanıcı"}</span>
            </div>
          )}
          <LogoutButton variant="erp" />
          <button
            type="button"
            className="erp-collapse-button"
            onClick={() => setCollapsed((value) => !value)}
            title={collapsed ? "Menüyü aç" : "Menüyü daralt"}
          >
            {collapsed ? "›" : "‹"}
          </button>
        </div>
      </aside>

      <main className="erp-main">
        <header className="erp-topbar">
          <button
            type="button"
            className="erp-mobile-menu-button"
            onClick={() => setCollapsed((value) => !value)}
            aria-label="Menüyü aç/kapat"
          >
            ☰
          </button>

          <div className="erp-topbar-actions">
            <button type="button" title="Ara">⌕</button>
            <button type="button" title="Bildirimler">♢</button>
            <button type="button" title="Yardım">?</button>
            <button type="button" className="erp-company-switcher">
              ▦ Enderun Enerji A.Ş.⌄
            </button>
          </div>
        </header>

        <div className="erp-page-header">
          <div>
            <h1>{title}</h1>
            {description && <p>{description}</p>}
          </div>
        </div>

        <div className="erp-content">{children}</div>
      </main>
    </div>
  );
}
