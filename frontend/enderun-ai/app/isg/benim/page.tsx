"use client";

import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { ApiError } from "@/lib/api/api-client";
import {
  isgService,
  type IsgCertificate,
  type IsgHealthReport,
  type IsgPersonnelCard,
  type IsgTraining,
} from "@/services/isg.service";
import { Button } from "@/components/ui";

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
  const [reloadKey, setReloadKey] = useState(0);

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
  }, [reloadKey]);

  /* SÜTUNLAR VERİ OLARAK (F4l). Rozet basan sütunda `value` ayrı. */
  const reportColumns: DataTableColumn<
    IsgHealthReport
  >[] = [
    { key: "tur", header: "Tür", value: (row) => row.reportTypeName },
    { key: "muayene", header: "Muayene Tarihi", value: (row) => formatDate(row.examDate) },
    { key: "gecerlilik", header: "Geçerlilik Bitişi", value: (row) => formatDate(row.validUntil) },
    { key: "sonuc", header: "Sonuç", value: (row) => row.resultName },
    {
      key: "durum",
      header: "Durum",
      value: (row) =>
        typeof row.daysRemaining === "number" && row.daysRemaining >= 0
          ? `${row.validityStatusName} · ${row.daysRemaining} gün kaldı`
          : row.validityStatusName,
      render: (row) => (
        <>
          <span className={`erp-status ${row.validityColor}`}>
            {row.validityStatusName}
          </span>
          {typeof row.daysRemaining === "number" && row.daysRemaining >= 0 && (
            <small>{row.daysRemaining} gün kaldı</small>
          )}
        </>
      ),
    },
  ];

  const trainingColumns: DataTableColumn<
    IsgTraining
  >[] = [
    { key: "tur", header: "Tür", value: (row) => row.trainingTypeName },
    {
      key: "konu",
      header: "Konu",
      value: (row) => row.topic,
      render: (row) => <strong>{row.topic}</strong>,
    },
    { key: "tarih", header: "Tarih", value: (row) => formatDate(row.trainingDate) },
    {
      key: "sure",
      header: "Süre",
      numeric: true,
      value: (row) => `${row.durationHours} saat`,
      footer: (rows) =>
        `${rows.reduce((sum, row) => sum + row.durationHours, 0)} saat`,
    },
    { key: "gecerlilik", header: "Geçerlilik", value: (row) => formatDate(row.validUntil) },
    {
      key: "durum",
      header: "Durum",
      value: (row) => row.validityStatusName,
      render: (row) => (
        <span className={`erp-status ${row.validityColor}`}>
          {row.validityStatusName}
        </span>
      ),
    },
  ];

  const certificateColumns: DataTableColumn<
    IsgCertificate
  >[] = [
    {
      key: "belge",
      header: "Belge",
      value: (row) => row.certificateTypeName,
      render: (row) => <strong>{row.certificateTypeName}</strong>,
    },
    { key: "no", header: "Belge No", value: (row) => row.certificateNumber ?? "—" },
    { key: "kurum", header: "Veren Kurum", value: (row) => row.issuedBy ?? "—" },
    { key: "tarih", header: "Tarih", value: (row) => formatDate(row.issueDate) },
    { key: "gecerlilik", header: "Geçerlilik", value: (row) => formatDate(row.expiryDate) },
    {
      key: "durum",
      header: "Durum",
      value: (row) => row.validityStatusName,
      render: (row) => (
        <span className={`erp-status ${row.validityColor}`}>
          {row.validityStatusName}
        </span>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="İSG Belgelerim"
      description="Sağlık raporu, eğitim ve yetki belgelerinizin geçerlilik durumu"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => setReloadKey((key) => key + 1)}>Yenile</Button>
      </div>

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
              <DataTable
                rows={card.healthReports}
                columns={reportColumns}
                rowKey={(row) => row.id}
                title="Sağlık Raporlarım"
              />
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
              <DataTable
                rows={card.trainings}
                columns={trainingColumns}
                rowKey={(row) => row.id}
                title="İSG Eğitimlerim"
              />
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
              <DataTable
                rows={card.certificates}
                columns={certificateColumns}
                rowKey={(row) => row.id}
                title="Yetki Belgelerim"
              />
            )}
          </div>
        </>
      )}
    </ErpShell>
  );
}
