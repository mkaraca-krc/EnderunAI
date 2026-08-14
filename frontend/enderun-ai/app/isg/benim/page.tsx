"use client";

import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ApiError } from "@/lib/api/api-client";
import { isgService, type IsgPersonnelCard } from "@/services/isg.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

/**
 * Personelin kendi İSG belgeleri.
 *
 * İzin aranmaz: uç, personel kimliğini istekten değil oturumdan alır ve
 * yalnızca çağıranın kendi kaydını döndürür. Başkasının kaydına buradan
 * ulaşmanın bir yolu yok.
 */
export default function MyIsgRecordsPage() {
  const [card, setCard] = useState<IsgPersonnelCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [notLinked, setNotLinked] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;

    isgService
      .getOwnCard()
      .then((result) => {
        if (active) setCard(result);
      })
      .catch((err: unknown) => {
        if (!active) return;

        // 404: kullanıcı hesabı bir personel kartıyla eşleştirilmemiş.
        // Bu bir hata değil, eksik bir kurulum — ayrı anlatılıyor.
        if (err instanceof ApiError && err.status === 404) {
          setNotLinked(true);
          return;
        }

        setError(err instanceof Error ? err.message : "Kayıtlar alınamadı.");
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  return (
    <ErpShell
      design="redwood"
      title="İSG Belgelerim"
      description="Sağlık raporu, eğitim ve yetki belgelerinizin geçerlilik durumu"
    >
      {error && <div className="erp-alert error">{error}</div>}

      {loading && <div className="erp-loading">Yükleniyor...</div>}

      {!loading && notLinked && (
        <div className="erp-empty-state">
          <p>
            Kullanıcı hesabınız bir personel kartıyla eşleştirilmemiş; bu yüzden
            gösterilecek kayıt bulunamadı. İnsan Kaynakları veya sistem
            yöneticisi eşleştirmeyi yaptıktan sonra belgeleriniz burada
            görünecek.
          </p>
        </div>
      )}

      {!loading && card && (
        <>
          <div className="erp-page-toolbar">
            <div>
              <strong>{card.personnelName}</strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                {card.jobTitle ?? "—"}
                {card.employeeNumber ? ` · ${card.employeeNumber}` : ""}
              </small>
            </div>

            <div>
              {card.expiredCount > 0 && (
                <span className="erp-status red">
                  {card.expiredCount} belgenin süresi doldu
                </span>
              )}
              {card.expiringSoonCount > 0 && (
                <span className="erp-status yellow" style={{ marginLeft: "6px" }}>
                  {card.expiringSoonCount} belge 30 gün içinde doluyor
                </span>
              )}
              {card.expiredCount === 0 && card.expiringSoonCount === 0 && (
                <span className="erp-status green">Belgeleriniz güncel</span>
              )}
            </div>
          </div>

          <div className="erp-table-card">
            <div className="erp-table-header">
              <h2>Sağlık Raporu</h2>
            </div>

            {card.healthReports.length === 0 ? (
              <div className="erp-empty-state">
                <p>Kayıtlı sağlık raporunuz yok.</p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tür</th>
                      <th>Muayene Tarihi</th>
                      <th>Geçerlilik Bitişi</th>
                      <th>Sonuç</th>
                      <th>Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.healthReports.map((report) => (
                      <tr key={report.id}>
                        <td>{report.reportTypeName}</td>
                        <td>{formatDate(report.examDate)}</td>
                        <td>{formatDate(report.validUntil)}</td>
                        <td>{report.resultName}</td>
                        <td>
                          <span className={`erp-status ${report.validityColor}`}>
                            {report.validityStatusName}
                          </span>
                          {typeof report.daysRemaining === "number" &&
                            report.daysRemaining >= 0 && (
                              <small>{report.daysRemaining} gün kaldı</small>
                            )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="erp-table-card erp-mt">
            <div className="erp-table-header">
              <h2>İSG Eğitimleri</h2>
            </div>

            {card.trainings.length === 0 ? (
              <div className="erp-empty-state">
                <p>Kayıtlı eğitiminiz yok.</p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tür</th>
                      <th>Konu</th>
                      <th>Tarih</th>
                      <th>Süre</th>
                      <th>Geçerlilik</th>
                      <th>Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.trainings.map((training) => (
                      <tr key={training.id}>
                        <td>{training.trainingTypeName}</td>
                        <td>
                          <strong>{training.topic}</strong>
                        </td>
                        <td>{formatDate(training.trainingDate)}</td>
                        <td>{training.durationHours} saat</td>
                        <td>{formatDate(training.validUntil)}</td>
                        <td>
                          <span className={`erp-status ${training.validityColor}`}>
                            {training.validityStatusName}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="erp-table-card erp-mt">
            <div className="erp-table-header">
              <h2>Yetki Belgelerim</h2>
            </div>

            {card.certificates.length === 0 ? (
              <div className="erp-empty-state">
                <p>Kayıtlı yetki belgeniz yok.</p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Belge</th>
                      <th>Belge No</th>
                      <th>Veren Kurum</th>
                      <th>Tarih</th>
                      <th>Geçerlilik</th>
                      <th>Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.certificates.map((certificate) => (
                      <tr key={certificate.id}>
                        <td>
                          <strong>{certificate.certificateTypeName}</strong>
                        </td>
                        <td>{certificate.certificateNumber ?? "—"}</td>
                        <td>{certificate.issuedBy ?? "—"}</td>
                        <td>{formatDate(certificate.issueDate)}</td>
                        <td>{formatDate(certificate.expiryDate)}</td>
                        <td>
                          <span className={`erp-status ${certificate.validityColor}`}>
                            {certificate.validityStatusName}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </ErpShell>
  );
}
