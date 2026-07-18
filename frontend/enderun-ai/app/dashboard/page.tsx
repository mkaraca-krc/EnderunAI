"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { companyService } from "@/services/company.service";
import { branchService } from "@/services/branch.service";
import { currentAccountService } from "@/services/current-account.service";
import { projectService, ProjectListItem } from "@/services/project.service";

type DashboardState = {
  companies: number;
  branches: number;
  accounts: number;
  projects: ProjectListItem[];
};

function formatMoney(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    maximumFractionDigits: 0,
  }).format(value);
}

export default function DashboardPage() {
  const [data, setData] = useState<DashboardState>({
    companies: 0,
    branches: 0,
    accounts: 0,
    projects: [],
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    async function load() {
      try {
        const [companies, branches, accounts, projects] = await Promise.all([
          companyService.getAll(),
          branchService.getAll(),
          currentAccountService.getAll(),
          projectService.getAll(),
        ]);

        setData({
          companies: companies.length,
          branches: branches.length,
          accounts: accounts.length,
          projects,
        });
      } catch (err) {
        setError(err instanceof Error ? err.message : "Dashboard yüklenemedi.");
      } finally {
        setLoading(false);
      }
    }

    load();
  }, []);

  const totalContract = useMemo(
    () =>
      data.projects.reduce(
        (sum, project) => sum + (project.contractAmount ?? 0),
        0
      ),
    [data.projects]
  );

  const stats = [
    {
      label: "Aktif Proje",
      value: loading ? "…" : String(data.projects.length),
      note: "Proje merkezlerini görüntüle",
      href: "/projeler",
      icon: "◈",
    },
    {
      label: "Şirket",
      value: loading ? "…" : String(data.companies),
      note: "Organizasyon yapısı",
      href: "/sirketler",
      icon: "▦",
    },
    {
      label: "Şube",
      value: loading ? "…" : String(data.branches),
      note: "Aktif operasyon noktaları",
      href: "/subeler",
      icon: "▤",
    },
    {
      label: "Cari Kart",
      value: loading ? "…" : String(data.accounts),
      note: "Müşteri ve tedarikçiler",
      href: "/cariler",
      icon: "◎",
    },
    {
      label: "Toplam Sözleşme",
      value: loading ? "…" : formatMoney(totalContract),
      note: "Girilen proje sözleşme toplamı",
      href: "/projeler",
      icon: "₺",
    },
  ];

  return (
    <ErpShell
      title="Yönetim Paneli"
      description="Enderun Enerji operasyonlarının anlık görünümü"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">ENDERUN AI ERP</span>
          <h2>Şirketinizin bugünkü görünümü</h2>
          <p>
            Projeler, organizasyon, cari yapı ve yönetim aksiyonları tek ekranda.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <Link href="/projeler" className="erp-primary-button">
            Projeleri Aç
          </Link>
          <Link href="/ai-asistan" className="erp-secondary-button">
            AI Merkezine Git
          </Link>
        </div>
      </section>

      <div className="enderun-dashboard-stats">
        {stats.map((stat) => (
          <Link href={stat.href} className="enderun-dashboard-stat" key={stat.label}>
            <div className="enderun-dashboard-stat-icon">{stat.icon}</div>
            <div>
              <span>{stat.label}</span>
              <strong>{stat.value}</strong>
              <small>{stat.note}</small>
            </div>
          </Link>
        ))}
      </div>

      <div className="enderun-dashboard-layout">
        <section className="erp-panel enderun-dashboard-projects">
          <div className="erp-panel-header">
            <div>
              <h2>Aktif Projeler</h2>
              <p>Son oluşturulan proje kartları</p>
            </div>
            <Link className="erp-row-link" href="/projeler">
              Tümünü Gör →
            </Link>
          </div>

          {loading ? (
            <div className="erp-loading">Projeler yükleniyor...</div>
          ) : data.projects.length === 0 ? (
            <div className="erp-empty-state">
              <div className="enderun-empty-symbol">◈</div>
              <strong>Henüz proje bulunmuyor</strong>
              <p>İlk projeyi Projeler ekranından oluşturun.</p>
              <Link href="/projeler" className="erp-primary-button">
                Yeni Proje Aç
              </Link>
            </div>
          ) : (
            <div className="enderun-project-cards">
              {data.projects.slice(0, 6).map((project) => (
                <Link
                  className="enderun-project-card"
                  href={`/projeler/${project.id}`}
                  key={project.id}
                >
                  <div className="enderun-project-card-top">
                    <span className="erp-status green">Aktif</span>
                    <span>{project.code}</span>
                  </div>
                  <h3>{project.name}</h3>
                  <p>{project.employerName}</p>
                  <div className="enderun-project-card-meta">
                    <span>{project.branchName}</span>
                    <span>{project.warehouseCount} depo</span>
                  </div>
                </Link>
              ))}
            </div>
          )}
        </section>

        <aside className="erp-panel enderun-dashboard-side">
          <div className="erp-panel-header">
            <div>
              <h2>Yönetim Özeti</h2>
              <p>Bugünkü hızlı aksiyonlar</p>
            </div>
          </div>

          <div className="enderun-action-list">
            <Link href="/satin-alma">
              <div className="enderun-action-icon">⌑</div>
              <div>
                <strong>Satın Alma</strong>
                <span>Talep ve onay süreçleri</span>
              </div>
              <b>→</b>
            </Link>

            <Link href="/hakedis">
              <div className="enderun-action-icon">▧</div>
              <div>
                <strong>Hakedişler</strong>
                <span>Hakediş kayıtları</span>
              </div>
              <b>→</b>
            </Link>

            <Link href="/personel">
              <div className="enderun-action-icon">♙</div>
              <div>
                <strong>Personel</strong>
                <span>Proje personel takibi</span>
              </div>
              <b>→</b>
            </Link>

            <Link href="/ai-asistan">
              <div className="enderun-action-icon">⌘</div>
              <div>
                <strong>AI Merkezi</strong>
                <span>Analiz ve yönetim uyarıları</span>
              </div>
              <b>→</b>
            </Link>
          </div>

          <div className="enderun-ai-summary">
            <span>AI Yönetim Özeti</span>
            <strong>Henüz kritik uyarı bulunmuyor.</strong>
            <p>
              İlerleyen sürümlerde evrak, satın alma, hakediş ve proje riskleri
              burada gösterilecek.
            </p>
          </div>
        </aside>
      </div>
    </ErpShell>
  );
}
