import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";

type HrQuickLink = {
  label: string;
  href: string;
  description: string;
  icon: string;
};

type HrModulePageProps = {
  title: string;
  description: string;
  icon: string;
  apiEndpoint?: string;
  features?: string[];
};

const quickLinks: HrQuickLink[] = [
  {
    label: "İK Dashboard",
    href: "/insan-kaynaklari",
    description: "İK özetleri, riskler ve yönetici göstergeleri",
    icon: "▦",
  },
  {
    label: "Personeller",
    href: "/insan-kaynaklari/personeller",
    description: "Personel kartları ve çalışan kayıtları",
    icon: "♙",
  },
  {
    label: "Puantaj",
    href: "/insan-kaynaklari/puantaj",
    description: "Günlük çalışma ve adam/saat kayıtları",
    icon: "◷",
  },
  {
    label: "Bordro",
    href: "/insan-kaynaklari/bordro",
    description: "Ücret, kesinti ve ödeme süreçleri",
    icon: "₺",
  },
  {
    label: "Performans",
    href: "/insan-kaynaklari/performans",
    description: "Değerlendirmeler ve gelişim sonuçları",
    icon: "★",
  },
  {
    label: "Zimmetler",
    href: "/insan-kaynaklari/zimmetler",
    description: "Personel ve proje ekipman takibi",
    icon: "▣",
  },
];

export default function HrModulePage({
  title,
  description,
  icon,
  apiEndpoint,
  features = [],
}: HrModulePageProps) {
  return (
    <ErpShell title={title} description={description}>
      <section className="hr-module-hero">
        <div className="hr-module-hero-icon">{icon}</div>

        <div>
          <span className="hr-module-kicker">
            ENDERUN AI · İNSAN KAYNAKLARI
          </span>

          <h2>{title}</h2>
          <p>{description}</p>
        </div>
      </section>

      <section className="hr-module-grid">
        <article className="erp-panel hr-module-status-card">
          <div className="hr-module-status-heading">
            <div>
              <span className="hr-module-label">MODÜL DURUMU</span>
              <h3>Frontend altyapısı hazır</h3>
            </div>

            <span className="hr-module-ready-badge">
              Hazır
            </span>
          </div>

          <p>
            Bu ekran Enderun AI İK backend servislerine bağlanmak üzere
            oluşturuldu. Sonraki aşamada listeleme, filtreleme, form ve
            onay işlemleri bu sayfa üzerinde aktif hale getirilecek.
          </p>

          {apiEndpoint && (
            <div className="hr-module-api">
              <span>Backend API</span>
              <code>{apiEndpoint}</code>
            </div>
          )}
        </article>

        <article className="erp-panel">
          <span className="hr-module-label">MODÜL KAPSAMI</span>

          <div className="hr-feature-list">
            {(features.length > 0
              ? features
              : [
                  "Listeleme ve filtreleme",
                  "Yeni kayıt oluşturma",
                  "Kayıt güncelleme",
                  "Detay görüntüleme",
                  "Raporlama ve analiz",
                ]
            ).map((feature) => (
              <div className="hr-feature-item" key={feature}>
                <span>✓</span>
                <p>{feature}</p>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="erp-panel">
        <div className="hr-section-heading">
          <div>
            <span className="hr-module-label">HIZLI ERİŞİM</span>
            <h3>İnsan Kaynakları modülleri</h3>
          </div>

          <Link
            href="/insan-kaynaklari"
            className="erp-secondary-button"
          >
            İK merkezine dön
          </Link>
        </div>

        <div className="hr-quick-link-grid">
          {quickLinks.map((item) => (
            <Link
              className="hr-quick-link"
              href={item.href}
              key={item.href}
            >
              <span className="hr-quick-link-icon">{item.icon}</span>

              <div>
                <strong>{item.label}</strong>
                <p>{item.description}</p>
              </div>

              <span className="hr-quick-link-arrow">›</span>
            </Link>
          ))}
        </div>
      </section>
    </ErpShell>
  );
}
