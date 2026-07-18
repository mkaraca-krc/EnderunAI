"use client";

import Link from "next/link";
import { ReactNode, useEffect, useMemo, useState } from "react";
import { usePathname } from "next/navigation";

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

const groups: MenuGroup[] = [
  {
    key: "organization",
    label: "ORGANİZASYON",
    items: [
      { label: "Şirketler", href: "/sirketler", icon: "▦" },
      { label: "Şubeler", href: "/subeler", icon: "▤" },
    ],
  },
  {
    key: "crm",
    label: "CRM",
    items: [
      { label: "Müşteriler", href: "/cariler?rol=musteri", icon: "○" },
      { label: "Tedarikçiler", href: "/cariler?rol=tedarikci", icon: "○" },
      { label: "İletişim Kişileri", href: "/cariler?rol=iletisim", icon: "○" },
    ],
  },
  {
    key: "operations",
    label: "OPERASYON",
    items: [
      { label: "Projeler", href: "/projeler", icon: "▣" },
      { label: "Personel", href: "/personel", icon: "♙" },
      { label: "Depolar", href: "/depo", icon: "⌂" },
      { label: "Araçlar", href: "/araclar", icon: "▱" },
      { label: "Satın Alma", href: "/satin-alma", icon: "⌑" },
      { label: "İş Programı", href: "/is-programi", icon: "▥" },
    ],
  },
  {
    key: "finance",
    label: "FİNANS",
    items: [
      { label: "Hakedişler", href: "/hakedis", icon: "▧" },
      { label: "Finans", href: "/finans", icon: "▨" },
      { label: "Muhasebe", href: "/muhasebe", icon: "▦" },
      { label: "Hesap Planı", href: "/muhasebe/hesap-plani", icon: "○" },
      { label: "Faturalar", href: "/muhasebe/faturalar", icon: "○" },
      { label: "Cari Kartlar", href: "/cariler", icon: "○" },
      { label: "Cari Hareketler", href: "/muhasebe/cari-hareketler", icon: "○" },
      { label: "Ödemeler", href: "/muhasebe/odemeler", icon: "○" },
      { label: "Banka İşlemleri", href: "/muhasebe/banka-islemleri", icon: "○" },
      { label: "Raporlar", href: "/muhasebe/raporlar", icon: "○" },
    ],
  },
  {
    key: "management",
    label: "YÖNETİM",
    items: [
      { label: "Dokümanlar", href: "/dokumanlar", icon: "□" },
      { label: "Onay Merkezi", href: "/onay-merkezi", icon: "✓" },
      { label: "Ayarlar", href: "/ayarlar", icon: "⚙" },
    ],
  },
  {
    key: "ai",
    label: "AI",
    items: [
      { label: "AI Merkezi", href: "/ai-asistan", icon: "⌘" },
      { label: "Analizler", href: "/ai-analizler", icon: "⌁" },
      { label: "Raporlar", href: "/ai-raporlar", icon: "⌑" },
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
  const [collapsed, setCollapsed] = useState(false);
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({});

  const activeGroup = useMemo(
    () =>
      groups.find((group) =>
        group.items.some((item) => {
          const href = pathOnly(item.href);
          return pathname === href || pathname.startsWith(`${href}/`);
        })
      )?.key,
    [pathname]
  );

  useEffect(() => {
    const initial: Record<string, boolean> = {};
    for (const group of groups) {
      initial[group.key] =
        group.key === activeGroup ||
        ["organization", "operations", "finance"].includes(group.key);
    }
    setOpenGroups(initial);
  }, [activeGroup]);

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
      <aside className="erp-sidebar">
        <div className="erp-brand">
          <div className="erp-brand-mark">E</div>
          {!collapsed && (
            <div>
              <strong>ENDERUN AI</strong>
              <span>Yönetim Sistemi</span>
            </div>
          )}
        </div>

        <nav className="erp-nav">
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

          {groups.map((group) => {
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
          <div className="erp-user-avatar">MK</div>
          {!collapsed && (
            <div className="erp-user-info">
              <strong>Mehmet Karacabey</strong>
              <span>Yönetici</span>
            </div>
          )}
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
