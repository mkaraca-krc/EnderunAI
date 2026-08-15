"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  ISG_CERTIFICATE_TYPES,
  ISG_HEALTH_REPORT_TYPES,
  ISG_HEALTH_RESULTS,
  ISG_TRAINING_TYPES,
  isgService,
  type IsgPersonnelCard,
  type IsgPersonnelSummary,
} from "@/services/isg.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

type Tab = "saglik" | "egitim" | "sertifika";

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
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Personel</th>
                  <th>Görev</th>
                  <th>Sağlık Raporu</th>
                  <th>Temel Eğitim</th>
                  <th>Yetki Belgesi</th>
                  <th>Durum</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {people.map((person) => (
                  <tr key={person.personnelId}>
                    <td>
                      <strong>{person.personnelName}</strong>
                      <small>{person.employeeNumber ?? "—"}</small>
                    </td>
                    <td>{person.jobTitle ?? "—"}</td>
                    <td>
                      {person.hasValidHealthReport ? (
                        <span className="erp-status green">
                          {formatDate(person.healthReportValidUntil)}
                        </span>
                      ) : (
                        <span className="erp-status red">Yok / süresi doldu</span>
                      )}
                    </td>
                    <td>
                      <span
                        className={`erp-status ${
                          person.hasValidBasicTraining ? "green" : "red"
                        }`}
                      >
                        {person.hasValidBasicTraining ? "Geçerli" : "Yok"}
                      </span>
                    </td>
                    <td>{person.certificateCount}</td>
                    <td>
                      {person.expiredCount > 0 && (
                        <span className="erp-status red">
                          {person.expiredCount} süresi doldu
                        </span>
                      )}
                      {person.expiringSoonCount > 0 && (
                        <span
                          className="erp-status yellow"
                          style={{ marginLeft: "6px" }}
                        >
                          {person.expiringSoonCount} yakında
                        </span>
                      )}
                      {person.expiredCount === 0 &&
                        person.expiringSoonCount === 0 &&
                        !person.hasMissingRecords && (
                          <span className="erp-status green">Tamam</span>
                        )}
                    </td>
                    <td>
                      <button
                        type="button"
                        className="erp-secondary-button"
                        onClick={() => void openCard(person.personnelId)}
                      >
                        Kartı Aç
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Tür</th>
                    <th>Muayene</th>
                    <th>Geçerlilik</th>
                    <th>Sonuç</th>
                    <th>Hekim</th>
                    {canSeeHealthDetail && <th>Kısıtlama</th>}
                    <th>Durum</th>
                    {canDelete && <th></th>}
                  </tr>
                </thead>
                <tbody>
                  {card.healthReports.length === 0 && (
                    <tr>
                      <td colSpan={8}>Sağlık raporu kaydı yok.</td>
                    </tr>
                  )}

                  {card.healthReports.map((report) => (
                    <tr key={report.id}>
                      <td>{report.reportTypeName}</td>
                      <td>{formatDate(report.examDate)}</td>
                      <td>{formatDate(report.validUntil)}</td>
                      <td>{report.resultName}</td>
                      <td>{report.doctorName ?? "—"}</td>
                      {canSeeHealthDetail && (
                        <td>
                          {report.restrictions ?? "—"}
                          {report.doctorNotes && (
                            <small>{report.doctorNotes}</small>
                          )}
                        </td>
                      )}
                      <td>
                        <span className={`erp-status ${report.validityColor}`}>
                          {report.validityStatusName}
                        </span>
                        {typeof report.daysRemaining === "number" && (
                          <small>{report.daysRemaining} gün</small>
                        )}
                        {report.healthDetailHidden && (
                          <small>Tıbbi detay gizli</small>
                        )}
                      </td>
                      {canDelete && (
                        <td>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => setPendingDelete({ kind: "saglik", id: report.id })}
                          >
                            Sil
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {tab === "egitim" && (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Tür</th>
                    <th>Konu</th>
                    <th>Tarih</th>
                    <th>Süre</th>
                    <th>Geçerlilik</th>
                    <th>Eğitmen</th>
                    <th>Durum</th>
                    {canDelete && <th></th>}
                  </tr>
                </thead>
                <tbody>
                  {card.trainings.length === 0 && (
                    <tr>
                      <td colSpan={8}>Eğitim kaydı yok.</td>
                    </tr>
                  )}

                  {card.trainings.map((training) => (
                    <tr key={training.id}>
                      <td>{training.trainingTypeName}</td>
                      <td>
                        <strong>{training.topic}</strong>
                      </td>
                      <td>{formatDate(training.trainingDate)}</td>
                      <td>{training.durationHours} saat</td>
                      <td>{formatDate(training.validUntil)}</td>
                      <td>{training.trainerName ?? "—"}</td>
                      <td>
                        <span className={`erp-status ${training.validityColor}`}>
                          {training.validityStatusName}
                        </span>
                      </td>
                      {canDelete && (
                        <td>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() =>
                              setPendingDelete({ kind: "egitim", id: training.id })
                            }
                          >
                            Sil
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {tab === "sertifika" && (
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
                    {canDelete && <th></th>}
                  </tr>
                </thead>
                <tbody>
                  {card.certificates.length === 0 && (
                    <tr>
                      <td colSpan={7}>Yetki belgesi kaydı yok.</td>
                    </tr>
                  )}

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
                      {canDelete && (
                        <td>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() =>
                              setPendingDelete({ kind: "sertifika", id: certificate.id })
                            }
                          >
                            Sil
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
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
