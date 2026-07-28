"use client";

import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";


const reports = [
  {
    title: "Hakediş Raporları",
    description:
      "Hakediş finans özeti, poz detayları ve net ödeme raporları",
    href: "/hakedis",
    icon: "📄",
  },
  {
    title: "Fiyat Farkı Raporları",
    description:
      "Pn, Delta, endeks ve fiyat farkı hesap raporları",
    href: "/fiyat-farki",
    icon: "📈",
  },
  {
    title: "Kesinti Raporları",
    description:
      "Tevkifat, teminat, damga ve diğer kesinti dökümleri",
    href: "/hakedis",
    icon: "✂",
  },
  {
    title: "Proje Finans Özeti",
    description:
      "Sözleşme, hakediş, tahsilat ve mali durum",
    href: "/dashboard",
    icon: "🏗",
  },
  {
    title: "AI Analiz Raporları",
    description:
      "Risk, sapma ve yönetici analizleri",
    href: "/ai-asistan",
    icon: "🤖",
  },
];


export default function ReportsPage() {

  return (

    <ErpShell
      title="Rapor Merkezi"
      description="Enderun AI raporlama ve analiz merkezi"
    >

      <div className="erp-panel">

        <div className="erp-panel-header">

          <div>

            <h2>
              Yönetim Raporları
            </h2>

            <p>
              Operasyon, finans ve proje raporlarına hızlı erişim
            </p>

          </div>

        </div>


        <div className="enderun-project-module-grid">

          {reports.map((report)=>(

            <Link
              key={report.title}
              href={report.href}
            >

              <div className="enderun-project-module-icon">
                {report.icon}
              </div>

              <strong>
                {report.title}
              </strong>

              <span>
                {report.description}
              </span>

            </Link>

          ))}

        </div>

      </div>


    </ErpShell>

  );
}
