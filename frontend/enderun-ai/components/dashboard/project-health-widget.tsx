import Link from "next/link";

import QuickCard from "@/components/dashboard/quick-card";

type ProjectHealthWidgetProps = {
  totalProjects: number;
  activeProjects: number;
  riskyProjects: number;
  activePersonnel: number;
};

export default function ProjectHealthWidget({
  totalProjects,
  activeProjects,
  riskyProjects,
  activePersonnel,
}: ProjectHealthWidgetProps) {
  return (
    <div className="erp-panel dashboard-project-health-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Proje Sağlık Durumu</h2>
          <p>Aktif ve riskli proje görünümü</p>
        </div>

        <Link href="/projeler">Proje Merkezi</Link>
      </div>

      <div className="erp-quick-grid">
        <QuickCard
          label="Toplam Proje"
          value={totalProjects}
          href="/projeler"
        />

        <QuickCard
          label="Aktif Proje"
          value={activeProjects}
          href="/projeler"
        />

        <QuickCard
          label="Riskli Proje"
          value={riskyProjects}
          href="/projeler"
        />

        <QuickCard
          label="Aktif Personel"
          value={activePersonnel}
          href="/personel"
        />
      </div>
    </div>
  );
}
