import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";

/**
 * Merkezi doküman arşivi henüz yok — bu sayfa eski AppShell'i kullanan
 * "modül hazırlanıyor" taslağıydı ve menüden erişilebiliyordu.
 *
 * Sayfayı kaldırmak yerine dürüst hale getirdik: bugün dokümanların
 * gerçekte nerede tutulduğunu söylüyor. Bu modül için ayrı bir uç yok;
 * yalnızca proje dokümanları (ProjectDocumentsController) ve İSG saha
 * belgeleri (IsgSiteDocumentsController) mevcut.
 */
export default function Page() {
  return (
    <ErpShell
      title="Dokümanlar"
      description="Merkezi doküman arşivi henüz devrede değil"
    >
      <div className="erp-alert warning">
        Şirket geneli doküman arşivi modülü henüz yapılmadı. Bu sayfa yalnızca
        bugün dosyaların nerede tutulduğunu gösteriyor.
      </div>

      <div className="erp-panel">
        <div className="erp-panel-header">
          <h2>Dosyalar bugün nerede?</h2>
        </div>

        <div className="erp-project-list">
          <div className="erp-project-list-item">
            <div>
              <strong>Proje dokümanları</strong>
              <span>
                Sözleşme, şartname, teknik çizim ve proje evrakı — projenin
                kendi sayfasındaki Dokümanlar sekmesinde.
              </span>
            </div>
            <Link className="erp-row-link" href="/projeler">
              Projeler
            </Link>
          </div>

          <div className="erp-project-list-item">
            <div>
              <strong>İSG saha belgeleri</strong>
              <span>
                Risk değerlendirmesi, acil durum planı, kurul tutanağı, denetim
                ve KKD zimmet formları — geçerlilik takipli.
              </span>
            </div>
            <Link className="erp-row-link" href="/isg/belgeler">
              Saha Belgeleri
            </Link>
          </div>

          <div className="erp-project-list-item">
            <div>
              <strong>Gelen / giden evrak</strong>
              <span>
                Kurumsal yazışma kayıtları ve ekleri sekreterya defterinde.
              </span>
            </div>
            <Link className="erp-row-link" href="/sekreterya/evrak">
              Evrak Defteri
            </Link>
          </div>

          <div className="erp-project-list-item">
            <div>
              <strong>E-fatura arşivi</strong>
              <span>
                İçe aktarılan faturaların orijinal XML dosyaları sunucuda
                saklanır ve fatura kaydından indirilir.
              </span>
            </div>
            <Link className="erp-row-link" href="/muhasebe/faturalar">
              Tedarikçi Faturaları
            </Link>
          </div>
        </div>
      </div>
    </ErpShell>
  );
}
