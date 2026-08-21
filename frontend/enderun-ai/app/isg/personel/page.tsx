"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { Button, ConfirmDialog } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  ISG_CERTIFICATE_TYPES,
  ISG_HEALTH_REPORT_TYPES,
  ISG_HEALTH_RESULTS,
  ISG_TRAINING_TYPES,
  isgService,
  type IsgCertificate,
  type IsgHealthReport,
  type IsgPersonnelCard,
  type IsgPersonnelSummary,
  type IsgTraining,
} from "@/services/isg.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

type Tab = "saglik" | "egitim" | "sertifika";

/*
 * SÜTUN TANIMLARI FONKSİYON (F4m): yetkiye göre sütun EKLENİYOR ya da
 * çıkarılıyor ve silme işleyicisi parametre geçiyor. Sabit dizi olsaydı
 * işleyici kapanışa alınır, bayat kapanış silme düğmesini yanlış kayıt
 * üzerinde çalıştırabilirdi (F4b desen kararı).
 *
 * TIBBİ DETAY SÜTUNU YETKİYE BAĞLI: kısıtlama ve hekim notu yalnız
 * `canSeeHealthDetail` ile geliyor. Sütunu her zaman basıp içini
 * boşaltmak yetmezdi — dışa aktarmada başlık yine görünür, "veri yok"
 * sanılırdı.
 */
function reportColumns(
  canSeeHealthDetail: boolean,
  canDelete: boolean,
  onDelete: (target: { kind: "saglik"; id: string }) => void
): DataTableColumn<IsgHealthReport>[] {
  const columns: DataTableColumn<IsgHealthReport>[] = [
    { key: "tur", header: "Tür", value: (row) => row.reportTypeName },
    { key: "muayene", header: "Muayene", value: (row) => formatDate(row.examDate) },
    { key: "gecerlilik", header: "Geçerlilik", value: (row) => formatDate(row.validUntil) },
    { key: "sonuc", header: "Sonuç", value: (row) => row.resultName },
    { key: "hekim", header: "Hekim", value: (row) => row.doctorName ?? "—" },
  ];

  if (canSeeHealthDetail) {
    columns.push({
      key: "kisitlama",
      header: "Kısıtlama",
      value: (row) =>
        [row.restrictions ?? "—", row.doctorNotes].filter(Boolean).join(" · "),
      render: (row) => (
        <>
          {row.restrictions ?? "—"}
          {row.doctorNotes && <small>{row.doctorNotes}</small>}
        </>
      ),
    });
  }

  columns.push({
    key: "durum",
    header: "Durum",
    value: (row) =>
      [
        row.validityStatusName,
        typeof row.daysRemaining === "number" ? `${row.daysRemaining} gün` : "",
        row.healthDetailHidden ? "Tıbbi detay gizli" : "",
      ]
        .filter(Boolean)
        .join(" · "),
    render: (row) => (
      <>
        <span className={`erp-status ${row.validityColor}`}>
          {row.validityStatusName}
        </span>
        {typeof row.daysRemaining === "number" && (
          <small>{row.daysRemaining} gün</small>
        )}
        {row.healthDetailHidden && <small>Tıbbi detay gizli</small>}
      </>
    ),
  });

  if (canDelete) {
    columns.push({
      key: "sil",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          className="erp-secondary-button"
          onClick={() => onDelete({ kind: "saglik", id: row.id })}
        >
          Sil
        </button>
      ),
    });
  }

  return columns;
}

function trainingColumns(
  canDelete: boolean,
  onDelete: (target: { kind: "egitim"; id: string }) => void
): DataTableColumn<IsgTraining>[] {
  const columns: DataTableColumn<IsgTraining>[] = [
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
      footer: (rows) => `${rows.reduce((sum, row) => sum + row.durationHours, 0)} saat`,
    },
    { key: "gecerlilik", header: "Geçerlilik", value: (row) => formatDate(row.validUntil) },
    { key: "egitmen", header: "Eğitmen", value: (row) => row.trainerName ?? "—" },
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

  if (canDelete) {
    columns.push({
      key: "sil",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          className="erp-secondary-button"
          onClick={() => onDelete({ kind: "egitim", id: row.id })}
        >
          Sil
        </button>
      ),
    });
  }

  return columns;
}

function certificateColumns(
  canDelete: boolean,
  onDelete: (target: { kind: "sertifika"; id: string }) => void
): DataTableColumn<IsgCertificate>[] {
  const columns: DataTableColumn<IsgCertificate>[] = [
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

  if (canDelete) {
    columns.push({
      key: "sil",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          className="erp-secondary-button"
          onClick={() => onDelete({ kind: "sertifika", id: row.id })}
        >
          Sil
        </button>
      ),
    });
  }

  return columns;
}

export default function IsgPersonnelPage() {
  const { has } = usePermissions();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [search, setSearch] = useState("");

  const [people, setPeople] = useState<IsgPersonnelSummary[]>([]);
  const [card, setCard] = useState<IsgPersonnelCard | null>(null);
  const [tab, setTab] = useState<Tab>("saglik");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  /**
   * Silinecek kayıt: türü ve kimliği birlikte.
   *
   * Tür de tutuluyor çünkü üç ayrı defter (sağlık raporu,
   * eğitim, sertifika) aynı düğmeyi paylaşıyor ve onay
   * metninin hangisinden bahsettiğini söylemesi gerekiyor —
   * "Kayıt silinsin mi?" üçünde de aynıydı.
   */
  const [pendingDelete, setPendingDelete] = useState<{
    kind: Tab;
    id: string;
  } | null>(null);
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const [formOpen, setFormOpen] = useState(false);

  // Sağlık raporu formu
  const [reportType, setReportType] = useState("1");
  const [examDate, setExamDate] = useState(today());
  const [healthValidUntil, setHealthValidUntil] = useState("");
  const [healthResult, setHealthResult] = useState("0");
  const [doctorName, setDoctorName] = useState("");
  const [restrictions, setRestrictions] = useState("");
  const [doctorNotes, setDoctorNotes] = useState("");

  // Eğitim formu
  const [trainingType, setTrainingType] = useState("0");
  const [topic, setTopic] = useState("");
  const [trainingDate, setTrainingDate] = useState(today());
  const [durationHours, setDurationHours] = useState("");
  const [trainingValidUntil, setTrainingValidUntil] = useState("");
  const [trainerName, setTrainerName] = useState("");

  // Sertifika formu
  const [certificateType, setCertificateType] = useState("0");
  const [customTypeName, setCustomTypeName] = useState("");
  const [certificateNumber, setCertificateNumber] = useState("");
  const [issuedBy, setIssuedBy] = useState("");
  const [issueDate, setIssueDate] = useState(today());
  const [expiryDate, setExpiryDate] = useState("");

  const canCreate = has("isg.create");
  const canDelete = has("isg.delete");
  const canSeeHealthDetail = has("isg.health.view");

  useEffect(() => {
    void (async () => {
      try {
        const result = await companyService.getAll();
        setCompanies(result);
        setCompanyId(result[0]?.id ?? "");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
      }
    })();
  }, []);

  const loadPeople = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setPeople(
        await isgService.getPersonnelSummary(companyId, search.trim() || undefined)
      );
    } catch (err) {
      setPeople([]);
      setError(err instanceof Error ? err.message : "Personel listesi alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadPeople(), 300);
    return () => window.clearTimeout(timer);
  }, [loadPeople]);

  const openCard = useCallback(async (personnelId: string) => {
    setError("");

    try {
      setCard(await isgService.getPersonnelCard(personnelId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel kartı açılamadı.");
    }
  }, []);

  function resetForms() {
    setReportType("1");
    setExamDate(today());
    setHealthValidUntil("");
    setHealthResult("0");
    setDoctorName("");
    setRestrictions("");
    setDoctorNotes("");

    setTrainingType("0");
    setTopic("");
    setTrainingDate(today());
    setDurationHours("");
    setTrainingValidUntil("");
    setTrainerName("");

    setCertificateType("0");
    setCustomTypeName("");
    setCertificateNumber("");
    setIssuedBy("");
    setIssueDate(today());
    setExpiryDate("");
  }

  function validate(): string[] {
    const messages: string[] = [];

    if (tab === "saglik") {
      if (!examDate) messages.push("Muayene tarihi girin.");
      if (healthResult === "1" && !restrictions.trim()) {
        // "Şartlı çalışabilir" kısıtlama yazılmadan anlamsız: sahada
        // kimse neye dikkat edeceğini bilemez.
        messages.push("Şartlı çalışabilir sonucunda kısıtlama yazın.");
      }
    }

    if (tab === "egitim") {
      if (!topic.trim()) messages.push("Eğitim konusu girin.");
      if (!trainingDate) messages.push("Eğitim tarihi girin.");
      if (!(Number(durationHours) > 0)) {
        messages.push("Eğitim süresi sıfırdan büyük olmalı.");
      }
    }

    if (tab === "sertifika") {
      if (!issueDate) messages.push("Belge tarihi girin.");
      if (certificateType === "99" && !customTypeName.trim()) {
        messages.push("Diğer türünde belge adını yazın.");
      }
    }

    return messages;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!card) return;

    const problems = validate();
    if (problems.length > 0) {
      setError(problems.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    try {
      if (tab === "saglik") {
        await isgService.createHealthReport({
          companyId,
          personnelId: card.personnelId,
          reportType: Number(reportType),
          examDate,
          validUntil: healthValidUntil || null,
          result: Number(healthResult),
          doctorName: doctorName.trim() || null,
          restrictions: restrictions.trim() || null,
          doctorNotes: doctorNotes.trim() || null,
        });
        setNotice("Sağlık raporu kaydedildi.");
      } else if (tab === "egitim") {
        await isgService.createTraining({
          companyId,
          personnelId: card.personnelId,
          trainingType: Number(trainingType),
          topic: topic.trim(),
          trainingDate,
          durationHours: Number(durationHours),
          validUntil: trainingValidUntil || null,
          trainerName: trainerName.trim() || null,
        });
        setNotice("Eğitim kaydedildi.");
      } else {
        await isgService.createCertificate({
          companyId,
          personnelId: card.personnelId,
          certificateType: Number(certificateType),
          customTypeName: customTypeName.trim() || null,
          certificateNumber: certificateNumber.trim() || null,
          issuedBy: issuedBy.trim() || null,
          issueDate,
          expiryDate: expiryDate || null,
        });
        setNotice("Yetki belgesi kaydedildi.");
      }

      resetForms();
      setFormOpen(false);
      await openCard(card.personnelId);
      await loadPeople();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt eklenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function removeRecord(kind: Tab, id: string) {
    if (!card) return;
    setPendingDelete(null);

    setError("");

    try {
      if (kind === "saglik") await isgService.deleteHealthReport(id);
      else if (kind === "egitim") await isgService.deleteTraining(id);
      else await isgService.deleteCertificate(id);

      setNotice("Kayıt silindi.");
      await openCard(card.personnelId);
      await loadPeople();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt silinemedi.");
    }
  }

  const peopleColumns: DataTableColumn<(typeof people)[number]>[] = [
    {
      key: "personel",
      header: "Personel",
      value: (row) => `${row.personnelName} (${row.employeeNumber ?? "—"})`,
      render: (row) => (
        <>
          <strong>{row.personnelName}</strong>
          <small>{row.employeeNumber ?? "—"}</small>
        </>
      ),
    },
    { key: "gorev", header: "Görev", value: (row) => row.jobTitle ?? "—" },
    {
      key: "saglik",
      header: "Sağlık Raporu",
      value: (row) =>
        row.hasValidHealthReport
          ? formatDate(row.healthReportValidUntil)
          : "Yok / süresi doldu",
      render: (row) =>
        row.hasValidHealthReport ? (
          <span className="erp-status green">
            {formatDate(row.healthReportValidUntil)}
          </span>
        ) : (
          <span className="erp-status red">Yok / süresi doldu</span>
        ),
    },
    {
      key: "egitim",
      header: "Temel Eğitim",
      value: (row) => (row.hasValidBasicTraining ? "Geçerli" : "Yok"),
      render: (row) => (
        <span
          className={`erp-status ${row.hasValidBasicTraining ? "green" : "red"}`}
        >
          {row.hasValidBasicTraining ? "Geçerli" : "Yok"}
        </span>
      ),
    },
    {
      key: "belge",
      header: "Yetki Belgesi",
      numeric: true,
      value: (row) => row.certificateCount,
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => {
        if (row.expiredCount === 0 && row.expiringSoonCount === 0 && !row.hasMissingRecords)
          return "Tamam";

        return [
          row.expiredCount > 0 ? `${row.expiredCount} süresi doldu` : "",
          row.expiringSoonCount > 0 ? `${row.expiringSoonCount} yakında` : "",
        ]
          .filter(Boolean)
          .join(" · ");
      },
      render: (row) => (
        <>
          {row.expiredCount > 0 && (
            <span className="erp-status red">{row.expiredCount} süresi doldu</span>
          )}
          {row.expiringSoonCount > 0 && (
            <span className="erp-status yellow" style={{ marginLeft: "6px" }}>
              {row.expiringSoonCount} yakında
            </span>
          )}
          {row.expiredCount === 0 &&
            row.expiringSoonCount === 0 &&
            !row.hasMissingRecords && <span className="erp-status green">Tamam</span>}
        </>
      ),
    },
    {
      key: "ac",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          className="erp-secondary-button"
          onClick={() => void openCard(row.personnelId)}
        >
          Kartı Aç
        </button>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Personel İSG Kayıtları"
      description="Sağlık raporu, İSG eğitimi ve yetki belgesi geçerlilik takibi"
    >
      <div className="erp-page-toolbar">
        {/* Sağlık raporu ve eğitim kayıtları başka kullanıcılarca
            giriliyor; geçerlilik rozetleri tazelenmeden eskiyordu. */}
        <Button variant="secondary" onClick={() => void loadPeople()}>Yenile</Button>

        <div>
          <strong>{people.length} personel</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Kırmızı rozet süresi dolmuş, sarı 30 gün içinde dolacak kaydı
            gösterir.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => {
              setCompanyId(event.target.value);
              setCard(null);
            }}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <input
            type="search"
            placeholder="Ad veya sicil no"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Personel</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : people.length === 0 ? (
          <div className="erp-empty-state">
            <p>Kayıt bulunamadı.</p>
          </div>
        ) : (
            <DataTable
              rows={people}
              columns={peopleColumns}
              rowKey={(row) => row.personnelId}
              title="İSG Personel Durumu"
              resetKey={`${companyId}|${search}`}
            />
        )}
      </div>

      {card && (
        <div className="erp-panel erp-mt">
          <div className="erp-panel-header">
            <h2>
              {card.personnelName}
              {card.employeeNumber ? ` — ${card.employeeNumber}` : ""}
            </h2>

            <div className="erp-row-actions">
              {canCreate && (
                <button
                  type="button"
                  className="erp-primary-button"
                  onClick={() => {
                    resetForms();
                    setFormOpen((open) => !open);
                  }}
                >
                  {formOpen ? "Formu Kapat" : "+ Kayıt Ekle"}
                </button>
              )}

              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => {
                  setCard(null);
                  setFormOpen(false);
                }}
              >
                Kapat
              </button>
            </div>
          </div>

          <div style={{ marginBottom: "12px" }}>
            {card.expiredCount > 0 && (
              <span className="erp-status red">
                {card.expiredCount} kaydın süresi doldu
              </span>
            )}
            {card.expiringSoonCount > 0 && (
              <span className="erp-status yellow" style={{ marginLeft: "6px" }}>
                {card.expiringSoonCount} kayıt 30 gün içinde doluyor
              </span>
            )}
          </div>

          <div className="erp-project-tabs">
            <a
              className={tab === "saglik" ? "active" : ""}
              onClick={() => {
                setTab("saglik");
                setFormOpen(false);
              }}
            >
              Sağlık Raporu ({card.healthReports.length})
            </a>
            <a
              className={tab === "egitim" ? "active" : ""}
              onClick={() => {
                setTab("egitim");
                setFormOpen(false);
              }}
            >
              Eğitim ({card.trainings.length})
            </a>
            <a
              className={tab === "sertifika" ? "active" : ""}
              onClick={() => {
                setTab("sertifika");
                setFormOpen(false);
              }}
            >
              Yetki Belgesi ({card.certificates.length})
            </a>
          </div>

          {formOpen && canCreate && (
            <form className="erp-form-card" onSubmit={submit}>
              {tab === "saglik" && (
                <>
                  <div className="erp-form-header">
                    <h2>Yeni Sağlık Raporu</h2>
                    <p>
                      Kısıtlama ve hekim notu tıbbi detaydır; yalnızca sağlık
                      detayı yetkisi olanlara gösterilir.
                    </p>
                  </div>

                  <div className="erp-form-grid">
                    <label>
                      <span>Muayene Türü *</span>
                      <select
                        value={reportType}
                        onChange={(event) => setReportType(event.target.value)}
                      >
                        {ISG_HEALTH_REPORT_TYPES.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </label>

                    <label>
                      <span>Muayene Tarihi *</span>
                      <input
                        type="date"
                        value={examDate}
                        onChange={(event) => setExamDate(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Geçerlilik Bitişi</span>
                      <input
                        type="date"
                        value={healthValidUntil}
                        onChange={(event) =>
                          setHealthValidUntil(event.target.value)
                        }
                      />
                      <small>Boş bırakılırsa süresiz sayılır.</small>
                    </label>

                    <label>
                      <span>Sonuç *</span>
                      <select
                        value={healthResult}
                        onChange={(event) => setHealthResult(event.target.value)}
                      >
                        {ISG_HEALTH_RESULTS.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </label>

                    <label>
                      <span>Hekim</span>
                      <input
                        type="text"
                        value={doctorName}
                        onChange={(event) => setDoctorName(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Kısıtlama</span>
                      <input
                        type="text"
                        value={restrictions}
                        onChange={(event) => setRestrictions(event.target.value)}
                        placeholder="Örn. yüksekte çalışamaz"
                      />
                    </label>

                    <label className="span-2">
                      <span>Hekim Notu</span>
                      <input
                        type="text"
                        value={doctorNotes}
                        onChange={(event) => setDoctorNotes(event.target.value)}
                      />
                    </label>
                  </div>
                </>
              )}

              {tab === "egitim" && (
                <>
                  <div className="erp-form-header">
                    <h2>Yeni Eğitim Kaydı</h2>
                  </div>

                  <div className="erp-form-grid">
                    <label>
                      <span>Eğitim Türü *</span>
                      <select
                        value={trainingType}
                        onChange={(event) => setTrainingType(event.target.value)}
                      >
                        {ISG_TRAINING_TYPES.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </label>

                    <label>
                      <span>Konu *</span>
                      <input
                        type="text"
                        value={topic}
                        onChange={(event) => setTopic(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Tarih *</span>
                      <input
                        type="date"
                        value={trainingDate}
                        onChange={(event) => setTrainingDate(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Süre (saat) *</span>
                      <input
                        type="number"
                        step="0.5"
                        min="0"
                        value={durationHours}
                        onChange={(event) => setDurationHours(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Geçerlilik Bitişi</span>
                      <input
                        type="date"
                        value={trainingValidUntil}
                        onChange={(event) =>
                          setTrainingValidUntil(event.target.value)
                        }
                      />
                    </label>

                    <label>
                      <span>Eğitmen</span>
                      <input
                        type="text"
                        value={trainerName}
                        onChange={(event) => setTrainerName(event.target.value)}
                      />
                    </label>
                  </div>
                </>
              )}

              {tab === "sertifika" && (
                <>
                  <div className="erp-form-header">
                    <h2>Yeni Yetki Belgesi</h2>
                  </div>

                  <div className="erp-form-grid">
                    <label>
                      <span>Belge Türü *</span>
                      <select
                        value={certificateType}
                        onChange={(event) =>
                          setCertificateType(event.target.value)
                        }
                      >
                        {ISG_CERTIFICATE_TYPES.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </label>

                    {certificateType === "99" && (
                      <label>
                        <span>Belge Adı *</span>
                        <input
                          type="text"
                          value={customTypeName}
                          onChange={(event) =>
                            setCustomTypeName(event.target.value)
                          }
                        />
                      </label>
                    )}

                    <label>
                      <span>Belge No</span>
                      <input
                        type="text"
                        value={certificateNumber}
                        onChange={(event) =>
                          setCertificateNumber(event.target.value)
                        }
                      />
                    </label>

                    <label>
                      <span>Veren Kurum</span>
                      <input
                        type="text"
                        value={issuedBy}
                        onChange={(event) => setIssuedBy(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Belge Tarihi *</span>
                      <input
                        type="date"
                        value={issueDate}
                        onChange={(event) => setIssueDate(event.target.value)}
                      />
                    </label>

                    <label>
                      <span>Geçerlilik Bitişi</span>
                      <input
                        type="date"
                        value={expiryDate}
                        onChange={(event) => setExpiryDate(event.target.value)}
                      />
                    </label>
                  </div>
                </>
              )}

              <div className="erp-form-actions">
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => setFormOpen(false)}
                >
                  Vazgeç
                </button>

                <button
                  type="submit"
                  className="erp-primary-button"
                  disabled={saving}
                >
                  {saving ? "Kaydediliyor..." : "Kaydet"}
                </button>
              </div>
            </form>
          )}

          {tab === "saglik" && (
            <DataTable
              rows={card.healthReports}
              columns={reportColumns(canSeeHealthDetail, canDelete, setPendingDelete)}
              rowKey={(row) => row.id}
              title="Sağlık Raporları"
              emptyText="Sağlık raporu kaydı yok."
              resetKey={card.personnelId}
            />
          )}

          {tab === "egitim" && (
            <DataTable
              rows={card.trainings}
              columns={trainingColumns(canDelete, setPendingDelete)}
              rowKey={(row) => row.id}
              title="İSG Eğitimleri"
              emptyText="Eğitim kaydı yok."
              resetKey={card.personnelId}
            />
          )}

          {tab === "sertifika" && (
            <DataTable
              rows={card.certificates}
              columns={certificateColumns(canDelete, setPendingDelete)}
              rowKey={(row) => row.id}
              title="Yetki Belgeleri"
              emptyText="Yetki belgesi kaydı yok."
              resetKey={card.personnelId}
            />
          )}
        </div>
      )}
      <ConfirmDialog
        open={pendingDelete !== null}
        title={
          pendingDelete?.kind === "saglik"
            ? "Sağlık Raporunu Sil"
            : pendingDelete?.kind === "egitim"
              ? "Eğitim Kaydını Sil"
              : "Sertifikayı Sil"
        }
        description={
          "Kayıt kalıcı olarak silinecek. Bu işlem geri alınamaz; " +
          "İSG kayıtları denetimde istenebiliyor."
        }
        confirmLabel="Kaydı Sil"
        error={error}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() =>
          void removeRecord(pendingDelete!.kind, pendingDelete!.id)
        }
      />
    </ErpShell>
  );
}
