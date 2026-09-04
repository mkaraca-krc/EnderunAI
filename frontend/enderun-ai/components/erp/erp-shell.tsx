"use client";

import HizirBubble from "@/components/hizir/hizir-bubble";
import Link from "next/link";
import { ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { apiClient } from "@/lib/api/api-client";
import { LogoutButton } from "@/components/logout-button";
import WorkHourSessionWatcher from "@/components/work-hour-session-watcher";
import NotificationBell from "@/components/notifications/notification-bell";

// YOL → İZİN HARİTASI TEK KAYNAKTAN: aynı harita middleware'de de
// kullanılıyor. Menüde gizleyip sayfayı açık bırakmak (ya da tersi)
// artık mümkün değil.
import { canAccessRoute } from "@/lib/auth/route-permissions";

// MENÜ AĞACI TEK KAYNAKTAN: aynı ağaç komut paletini ve kırıntı yolunu
// da besliyor. Kabuk kendi listesini tutsaydı, palete eklenmeyen bir
// sayfa aramada bulunamazdı.
import {
  findMenuEntry,
  pathOnly,
  visibleMenuGroups,
} from "@/lib/navigation/menu";
import CommandPalette from "@/components/erp/command-palette";

type ErpShellProps = {
  title: string;
  description?: string;
  children: ReactNode;

  /**
   * Sayfanın tasarım dili.
   *
   * "klasik" (varsayılan) bugünkü görünümdür — hiçbir sayfa istemeden
   * değişmez. "redwood" A1'de tanımlanan semantik tokenları devreye
   * sokar; ekranlar tek tek geçirilir.
   *
   * NEDEN OPT-IN: tokenlar tek hamlede tüm `erp-*` sınıflarına
   * bağlansaydı 175 sayfanın görünümü aynı anda değişir, hiçbiri tek
   * tek gözden geçirilmemiş olurdu. Böyle referans ekranlar önce
   * onaylanır, yayma sonra gelir.
   */
  design?: "klasik" | "redwood";
};


type CurrentSession = {
  id?: string | null;
  username?: string | null;
  fullName?: string | null;
  roles: string[];
  permissions: string[];
  /** Katalogdaki her izne sahip mi — backend'den gelir, rol adı DEĞİL. */
  hasAllPermissions?: boolean;
};



import { HataSiniri } from "./hata-siniri";
import { istemciHatasiBildir } from "@/services/istemci-hatasi.service";

/**
 * KABUK — İKİ KATMANLI HATA SINIRI.
 *
 * DIŞ KATMAN kabuğun KENDİ kodunu sarıyor. Kabuğun bir satırı
 * patladığında React ağacı kökünden söküyor ve geriye boş bir
 * `<div>` kalıyordu; kullanıcı beyaz ekran görüyordu. Kabuk her
 * ekranı sardığı için çöktüğünde açık kalan tek bir sayfa bile
 * olmuyordu.
 *
 * İÇ KATMAN yalnız sayfa içeriğini sarıyor. Bir ekran çökse bile yan
 * menü, arama ve kimlik AYAKTA kalır; kullanıcı başka bir ekrana
 * geçebilir. Tek katman olsaydı bir raporun hatası bütün gezinmeyi
 * de götürürdü.
 *
 * SIRA ÖNEMLİ: iç sınır önce yakalar. Dıştaki yalnız kabuğun kendi
 * çöküşünde devreye girer.
 *
 * GÖVDE "ErpShell" ÖNEKİYLE ADLANDIRILMADI. Redwood sözleşmesi
 * kabuk açılışlarını etiketin ÖNEKİNE bakarak sayıyor; "ErpShell"
 * ile başlayan her etiket bir ekran açılışı sayılıyor. Gövde o
 * önekle adlandırılınca kabuk, kendi içinde bayraksız bir ekran
 * açıyormuş gibi göründü ve sözleşme düştü.
 *
 * Sözleşme GEVŞETİLMEDİ — ad düzeltildi. Zaten doğrusu da bu:
 * bu bileşen bir kabuk değil, kabuğun gövdesi. (Bu yorumun
 * kendisi de o öneki yazmıyor; sayaç yorum ile kodu ayırmıyor.)
 */
export default function ErpShell(props: ErpShellProps) {
  return (
    <HataSiniri nerede="kabuk" bicim="tam" onHata={istemciHatasiBildir}>
      <KabukGovdesi {...props} />
    </HataSiniri>
  );
}

function KabukGovdesi({
  title,
  description,
  children,
  design = "klasik",
}: ErpShellProps) {
  const pathname = usePathname();

  useEffect(() => {
    document.title = `Enderun ERP - ${title}`;
  }, [title]);

  const [collapsed, setCollapsed] = useState(false);
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({});
  const navRef = useRef<HTMLElement | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentSession | null>(null);
  const [favoritePaths, setFavoritePaths] = useState<string[]>([]);
  const [paletteOpen, setPaletteOpen] = useState(false);

  // Tercihler gelmeden yazma yapılmaz: ilk render'daki varsayılan
  // (daraltılmış değil, favori yok) kullanıcının gerçek tercihinin
  // üzerine yazılırdı.
  const preferencesLoaded = useRef(false);

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

  useEffect(() => {
    let active = true;

    void apiClient<{ sidebarCollapsed: boolean; favoritePaths: string[] }>(
      "user-preferences"
    )
      .then((preference) => {
        if (!active) return;

        setCollapsed(preference.sidebarCollapsed);
        setFavoritePaths(preference.favoritePaths ?? []);
        preferencesLoaded.current = true;
      })
      .catch(() => {
        // TERCİH OKUNAMAZSA ARAYÜZ ÇALIŞMAYA DEVAM EDER: menü tercihi
        // uygulamayı durdurmaya değmez. Yalnızca yazma kapalı kalır ki
        // varsayılanlar kullanıcının kaydını ezmesin.
        if (active) preferencesLoaded.current = false;
      });

    return () => {
      active = false;
    };
  }, []);

  function persistPreferences(next: {
    sidebarCollapsed: boolean;
    favoritePaths: string[];
  }) {
    if (!preferencesLoaded.current) return;

    void apiClient("user-preferences", {
      method: "PUT",
      body: next,
    }).catch(() => {
      // Sessiz geçilir: kaydedilemeyen bir menü tercihi için
      // kullanıcının önüne hata çıkarmak, yaptığı işi böler.
    });
  }

  const visibleGroups = useMemo(() => {
    // Oturum henüz gelmediyse menü GÖSTERİLMEZ: dolu menüyü gösterip
    // sonra öğeleri kaybetmek, kullanıcıya olmayan yetkiyi bir an için
    // göstermek demek.
    if (!currentUser) return [];

    return visibleMenuGroups(
      new Set(currentUser.permissions),
      currentUser.hasAllPermissions === true
    );
  }, [currentUser]);

  const sessionInitials = (currentUser?.fullName || currentUser?.username || "K")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    // GÖSTERİM: üst çubuktaki kullanıcı avatarı.
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

  /**
   * FAVORİLER GÖRÜNÜRLÜK SÜZGECİNDEN GEÇER. Kullanıcı bir sayfayı
   * favoriye alıp sonra o yetkisini kaybederse kısayol da düşer;
   * favori listesi bir yan kapı olamaz. Menüde karşılığı kalmayan
   * (yeniden adlandırılmış) yol da sessizce elenir.
   */
  const favorites = useMemo(() => {
    if (!currentUser) return [];

    const permissions = new Set(currentUser.permissions);
    const all = currentUser.hasAllPermissions === true;

    return favoritePaths
      .filter((path) => canAccessRoute(path, permissions, all))
      .map((path) => ({ path, entry: findMenuEntry(path, visibleGroups) }))
      .filter(
        (favorite): favorite is { path: string; entry: NonNullable<ReturnType<typeof findMenuEntry>> } =>
          favorite.entry !== null
      );
  }, [favoritePaths, currentUser, visibleGroups]);

  const currentPath = pathOnly(pathname);
  const currentEntry = useMemo(
    () => findMenuEntry(currentPath, visibleGroups),
    [currentPath, visibleGroups]
  );

  // Kırıntı yolu MENÜDEN türer, URL parçalarından değil: "/muhasebe/
  // hesap-plani/aktar" parçalanınca kullanıcıya "hesap-plani" diye
  // teknik bir metin gösterilirdi. Menüde karşılığı yoksa yalnızca
  // sayfa başlığı kalır — uydurma bir üst seviye gösterilmez.
  const breadcrumb = useMemo(() => {
    const trail: { label: string; href?: string }[] = [
      { label: "Ana Sayfa", href: "/dashboard" },
    ];

    if (currentEntry) {
      trail.push({ label: currentEntry.group.label });

      if (currentEntry.item.label !== title) {
        trail.push({ label: currentEntry.item.label, href: currentEntry.item.href });
      }
    }

    trail.push({ label: title });

    return trail;
  }, [currentEntry, title]);

  const canFavoriteCurrent = currentEntry !== null;
  const currentIsFavorite = favoritePaths.includes(currentPath);

  // YAN ETKİ setState GÜNCELLEYİCİSİNİN İÇİNDE DEĞİL: React
  // güncelleyiciyi saf sayar ve geliştirme kipinde iki kez çağırabilir;
  // sunucuya yazma oraya konsaydı her tıklama iki istek atardı.
  function toggleFavorite(path: string) {
    const next = favoritePaths.includes(path)
      ? favoritePaths.filter((value) => value !== path)
      : [...favoritePaths, path];

    setFavoritePaths(next);
    persistPreferences({ sidebarCollapsed: collapsed, favoritePaths: next });
  }

  function toggleCollapsed() {
    const next = !collapsed;

    setCollapsed(next);
    persistPreferences({ sidebarCollapsed: next, favoritePaths });
  }

  // CTRL+K HER SAYFADA: palet yalnızca fare ile açılabilseydi, klavyede
  // çalışan kullanıcı için hiçbir hız kazancı olmazdı. ⌘K macOS için.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen(true);
      }
    }

    window.addEventListener("keydown", onKeyDown);

    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

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

          {/*
            ONAY MERKEZİ BAĞLANTISI KALDIRILDI (İŞEMRİ/1-A).
            
            Burada `MENU_GROUPS`'tan bağımsız, SABİT KODLANMIŞ bir
            `<Link href="/onay-merkezi">` duruyordu. İŞEMRİ/1'de menü
            tanımından aynı girişi kaldırdım ve "uygulandı" dedim;
            muhafız da yeşil verdi — çünkü muhafız `MENU_GROUPS`'u
            okuyor, kabuğun gövdesine yazılmış bir bağlantıyı değil.
            İhlali tarayıcı gördü, kod taraması değil (Kural 70/71).
            
            /onay-merkezi ROTASI DURUYOR: /yapilacaklar'a yönlendiriyor,
            yer imleri kırılmıyor. Giden yalnızca ikinci menü girişi.
          */}

          {/*
            KISAYOLLAR EN ÜSTTE: favori, "her gün açtığım sayfa" demek;
            listenin ortasında olsaydı yine aramak gerekirdi.
          */}
          {favorites.length > 0 && (
            <section className="erp-nav-group erp-nav-favorites">
              {!collapsed && (
                <div className="erp-nav-group-title">KISAYOLLARIM</div>
              )}

              <div className="erp-nav-group-items">
                {favorites.map((favorite) => {
                  const active =
                    currentPath === favorite.path ||
                    currentPath.startsWith(`${favorite.path}/`);

                  return (
                    <Link
                      key={favorite.path}
                      className={`erp-nav-link ${active ? "active" : ""}`}
                      href={favorite.entry.item.href}
                      title={favorite.entry.item.label}
                    >
                      <span className="erp-nav-icon">★</span>
                      {!collapsed && <span>{favorite.entry.item.label}</span>}
                    </Link>
                  );
                })}
              </div>
            </section>
          )}

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
              <span>{currentUser?.roles?.[0] || "Kullanıcı"}</span>
            </div>
          )}
          {/*
              PAROLA DEĞİŞTİRME — KULLANICI BİLGİSİNİN YANINDA.

              Yazılmış ama bulunamayan bir ekran, yazılmamış ekrandan
              farksızdır. Buraya konuyor çünkü kullanıcının kendine
              ait tek yer burası; ayrı bir menü başlığı açmak, tek
              maddelik bir menü üretirdi.
          */}
          <Link
            href="/parola"
            className="erp-user-action"
            title="Parola Değiştir"
            aria-label="Parola Değiştir"
          >
            🔑
          </Link>
          <LogoutButton variant="erp" />
          <button
            type="button"
            className="erp-collapse-button"
            onClick={toggleCollapsed}
            title={collapsed ? "Menüyü aç" : "Menüyü daralt"}
          >
            {collapsed ? "›" : "‹"}
          </button>
        </div>
      </aside>

      <main className={`erp-main ${design === "redwood" ? "rw" : ""}`}>
        <header className="erp-topbar">
          <button
            type="button"
            className="erp-mobile-menu-button"
            onClick={toggleCollapsed}
            aria-label="Menüyü aç/kapat"
          >
            ☰
          </button>

          {/*
            KIRINTI YOLU üst çubukta: kullanıcı derin bir sayfada
            "buraya nereden geldim" sorusuna cevap bulabilmeli.
          */}
          <nav className="erp-breadcrumb" aria-label="Sayfa yolu">
            {breadcrumb.map((crumb, index) => {
              const last = index === breadcrumb.length - 1;

              return (
                <span key={`${crumb.label}-${index}`}>
                  {index > 0 && (
                    <span className="erp-breadcrumb-separator" aria-hidden="true">
                      ›
                    </span>
                  )}

                  {crumb.href && !last ? (
                    <Link href={crumb.href}>{crumb.label}</Link>
                  ) : (
                    <span aria-current={last ? "page" : undefined}>
                      {crumb.label}
                    </span>
                  )}
                </span>
              );
            })}
          </nav>

          <div className="erp-topbar-actions">
            {/*
              ARAMA DÜĞMESİ ARTIK ÇALIŞIYOR: bugüne kadar üst çubukta
              duran ⌕ hiçbir şey yapmıyordu.
            */}
            <button
              type="button"
              title="Sayfa ara (Ctrl+K)"
              onClick={() => setPaletteOpen(true)}
              aria-label="Sayfa ara"
            >
              ⌕
            </button>

            {canFavoriteCurrent && (
              <button
                type="button"
                className={`erp-favorite-toggle ${currentIsFavorite ? "on" : ""}`}
                onClick={() => toggleFavorite(currentPath)}
                aria-pressed={currentIsFavorite}
                title={
                  currentIsFavorite
                    ? "Kısayollardan çıkar"
                    : "Kısayollara ekle"
                }
                aria-label={
                  currentIsFavorite
                    ? "Bu sayfayı kısayollardan çıkar"
                    : "Bu sayfayı kısayollara ekle"
                }
              >
                {currentIsFavorite ? "★" : "☆"}
              </button>
            )}
            <NotificationBell />
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

        <div className="erp-content">
          <HataSiniri
            nerede="içerik"
            bicim="govde"
            onHata={istemciHatasiBildir}
          >
            {children}
          </HataSiniri>
        </div>
      </main>

      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        groups={visibleGroups}
        favoritePaths={favoritePaths}
        onToggleFavorite={toggleFavorite}
      />

      {/* Kullanıcı hangi sayfada olursa olsun Hızır'a ulaşabilsin. */}
      <HizirBubble />
    </div>
  );
}
