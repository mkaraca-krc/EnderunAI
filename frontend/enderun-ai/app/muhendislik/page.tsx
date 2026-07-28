import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";

const modules = [
  {
    title: "Poz Kütüphanesi",
    description:
      "Elektrik, zayıf akım, OG ve veri merkezi pozlarını yönetin.",
    href: "/muhendislik/pozlar",
    icon: "▦",
    status: "Aktif",
  },
  {
    title: "Mühendislik Reçeteleri",
    description:
      "Pozlara bağlı malzeme, işçilik, makine ve adam/saat analizleri.",
    href: "/muhendislik/receteler",
    icon: "⚙",
    status: "Aktif",
  },
  {
    title: "Üretici Fiyat Listeleri",
    description:
      "Marka ve üretici fiyat listelerini teklif maliyetlerinde kullanın.",
    href: "/teklifler/fiyatlar",
    icon: "₺",
    status: "Aktif",
  },
  {
    title: "Marka Kütüphanesi",
    description:
      "Şartnameye uygun marka, alternatif ürün ve üretici yönetimi.",
    href: "/muhendislik/markalar",
    icon: "◇",
    status: "Hazırlanıyor",
  },
  {
    title: "AI Mühendislik Analizi",
    description:
      "Poz, keşif, şartname ve reçeteleri yapay zekâ ile analiz edin.",
    href: "/ai-asistan",
    icon: "⌘",
    status: "Aktif",
  },
  {
    title: "Teknik Dokümanlar",
    description:
      "Şartname, katalog, proje ve teknik belgeleri tek merkezde yönetin.",
    href: "/dokumanlar",
    icon: "▧",
    status: "Aktif",
  },
];

const workflow = [
  {
    number: "01",
    title: "Pozu Tanımla",
    description: "Poz numarası, açıklama, birim ve kategori bilgilerini girin.",
  },
  {
    number: "02",
    title: "Reçeteyi Oluştur",
    description: "Malzeme, işçilik, makine ve fire oranlarını tanımlayın.",
  },
  {
    number: "03",
    title: "Maliyeti Hesapla",
    description: "Güncel fiyat listeleri ve adam/saat verileriyle analiz yapın.",
  },
  {
    number: "04",
    title: "Teklife Aktar",
    description: "Hazırlanan mühendislik analizini teklif kalemine dönüştürün.",
  },
];

export default function EngineeringCenterPage() {
  return (
    <ErpShell
      title="Mühendislik Merkezi"
      description="Poz, reçete, teknik analiz ve maliyet yönetim merkezi"
    >
      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            ENDERUN AI ENGINEERING
          </span>

          <h2>Elektrik taahhüt mühendisliği tek merkezde</h2>

          <p>
            Poz kütüphanesi, mühendislik reçeteleri, adam/saat analizleri,
            üretici fiyatları ve teknik dokümanları ortak bir veri yapısında
            yönetin.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <Link href="/muhendislik/pozlar" className="erp-primary-button">
            Poz Kütüphanesini Aç
          </Link>

          <Link
            href="/muhendislik/receteler"
            className="erp-secondary-button"
          >
            Reçeteleri Gör
          </Link>
        </div>
      </section>

      <div className="enderun-dashboard-stats">
        <Link
          href="/muhendislik/pozlar"
          className="enderun-dashboard-stat"
        >
          <div className="enderun-dashboard-stat-icon">▦</div>
          <div>
            <span>Poz Kütüphanesi</span>
            <strong>Merkezi</strong>
            <small>Tüm mühendislik pozları</small>
          </div>
        </Link>

        <Link
          href="/muhendislik/receteler"
          className="enderun-dashboard-stat"
        >
          <div className="enderun-dashboard-stat-icon">⚙</div>
          <div>
            <span>Reçete Motoru</span>
            <strong>Aktif</strong>
            <small>Malzeme ve işçilik analizi</small>
          </div>
        </Link>

        <Link
          href="/teklifler/fiyatlar"
          className="enderun-dashboard-stat"
        >
          <div className="enderun-dashboard-stat-icon">₺</div>
          <div>
            <span>Fiyat Listeleri</span>
            <strong>Bağlı</strong>
            <small>Üretici ve marka fiyatları</small>
          </div>
        </Link>

        <Link href="/ai-asistan" className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">⌘</div>
          <div>
            <span>AI Analizi</span>
            <strong>Hazır</strong>
            <small>Teknik ve maliyet analizi</small>
          </div>
        </Link>
      </div>

      <section className="erp-panel">
        <div className="erp-panel-header">
          <div>
            <h2>Mühendislik Modülleri</h2>
            <p>Teklif ve proje süreçlerinin teknik veri merkezi</p>
          </div>
        </div>

        <div className="enderun-project-cards">
          {modules.map((module) => (
            <Link
              href={module.href}
              className="enderun-project-card"
              key={module.title}
            >
              <div className="enderun-project-card-top">
                <span
                  className={
                    module.status === "Aktif"
                      ? "erp-status green"
                      : "erp-status"
                  }
                >
                  {module.status}
                </span>

                <span>{module.icon}</span>
              </div>

              <h3>{module.title}</h3>
              <p>{module.description}</p>

              <div className="enderun-project-card-meta">
                <span>Modülü Aç</span>
                <span>→</span>
              </div>
            </Link>
          ))}
        </div>
      </section>

      <div className="enderun-dashboard-layout">
        <section className="erp-panel">
          <div className="erp-panel-header">
            <div>
              <h2>Mühendislik İş Akışı</h2>
              <p>Pozdan teklife standart ve izlenebilir süreç</p>
            </div>
          </div>

          <div className="enderun-action-list">
            {workflow.map((item) => (
              <div key={item.number}>
                <div className="enderun-action-icon">{item.number}</div>

                <div>
                  <strong>{item.title}</strong>
                  <span>{item.description}</span>
                </div>
              </div>
            ))}
          </div>
        </section>

        <aside className="erp-panel enderun-dashboard-side">
          <div className="erp-panel-header">
            <div>
              <h2>AI Mühendislik Özeti</h2>
              <p>Teknik veri kalitesi ve analiz durumu</p>
            </div>
          </div>

          <div className="enderun-ai-summary">
            <span>Recipe Engine</span>
            <strong>Backend ve veritabanı hazır.</strong>
            <p>
              Sonraki aşamada poz kütüphanesi ve reçete düzenleme ekranları
              gerçek API verileriyle bağlanacak.
            </p>
          </div>

          <div className="enderun-action-list">
            <Link href="/muhendislik/pozlar">
              <div className="enderun-action-icon">▦</div>
              <div>
                <strong>Pozları Yönet</strong>
                <span>Poz kütüphanesine git</span>
              </div>
              <b>→</b>
            </Link>

            <Link href="/muhendislik/receteler">
              <div className="enderun-action-icon">⚙</div>
              <div>
                <strong>Reçeteleri Yönet</strong>
                <span>Malzeme ve işçilik analizi</span>
              </div>
              <b>→</b>
            </Link>

            <Link href="/teklifler">
              <div className="enderun-action-icon">₺</div>
              <div>
                <strong>Tekliflere Git</strong>
                <span>Mühendislik verisini teklifte kullan</span>
              </div>
              <b>→</b>
            </Link>
          </div>
        </aside>
      </div>
    </ErpShell>
  );
}
