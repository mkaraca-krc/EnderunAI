"use client";

import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";

const modules = [
  {
    title: "Hesap Planı",
    description:
      "Tek düzen hesap planını ve alt hesapları yönetin.",
    href: "/muhasebe/hesap-plani",
    status: "Aktif",
  },
  {
    title: "Muhasebe Fişleri",
    description:
      "Mahsup, tahsil ve tediye fişlerini oluşturun.",
    href: "/muhasebe/fisler",
    status: "Aktif",
  },
  {
    title: "Yevmiye Defteri",
    description:
      "Kesinleşmiş muhasebe kayıtlarını tarih sırasıyla izleyin.",
    href: "/muhasebe/yevmiye",
    status: "Aktif",
  },
  {
    title: "Mizan",
    description:
      "Hesapların borç, alacak ve bakiye toplamlarını inceleyin.",
    href: "/muhasebe/mizan",
    status: "Planlandı",
  },
  {
    title: "Büyük Defter",
    description:
      "Hesap bazında ayrıntılı hareketleri görüntüleyin.",
    href: "/muhasebe/buyuk-defter",
    status: "Aktif",
  },
  {
    title: "Mali Tablolar",
    description:
      "Bilanço ve gelir tablosu raporlarını hazırlayın.",
    href: "/muhasebe/mali-tablolar",
    status: "Planlandı",
  },
];

export default function AccountingPage() {
  return (
    <ErpShell
      title="Muhasebe Merkezi"
      description="Enderun AI finansal kayıt ve raporlama altyapısı"
    >
      <section className="erp-form-card">
        <div className="erp-toolbar">
          <div>
            <strong>Muhasebe Çekirdeği</strong>
            <small>
              Hesap planından mali tablolara uzanan entegre yapı
            </small>
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(auto-fit, minmax(260px, 1fr))",
            gap: 14,
          }}
        >
          {modules.map((module) => {
            const active = module.status === "Aktif";

            return (
              <article
                key={module.title}
                style={{
                  border: "1px solid var(--erp-border)",
                  borderRadius: 12,
                  padding: 18,
                  background: "#fff",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    gap: 12,
                  }}
                >
                  <strong>{module.title}</strong>

                  <span
                    className={`erp-status ${
                      active ? "green" : "gray"
                    }`}
                  >
                    {module.status}
                  </span>
                </div>

                <p
                  style={{
                    color: "var(--erp-muted)",
                    fontSize: 13,
                    minHeight: 42,
                  }}
                >
                  {module.description}
                </p>

                {active ? (
                  <Link
                    href={module.href}
                    className="erp-primary-button"
                    style={{
                      display: "inline-block",
                      textDecoration: "none",
                    }}
                  >
                    Modülü Aç
                  </Link>
                ) : (
                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled
                  >
                    Yakında
                  </button>
                )}
              </article>
            );
          })}
        </div>
      </section>
    </ErpShell>
  );
}
