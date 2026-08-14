"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import {
  personnelService,
  type PersonnelDetail,
} from "@/services/personnel.service";

const statusLabels: Record<number, string> = {
  0: "Aday",
  1: "Aktif",
  2: "İzinli",
  3: "Askıda",
  4: "İşten Ayrıldı",
};

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function formatMoney(value?: number | null) {
  return value == null
    ? "—"
    : money(value);
}

export default function PersonnelDetailPage() {
  const params = useParams<{ id: string }>();

  const [person, setPerson] = useState<PersonnelDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // Yükleme effect'in içinde; tazeleme için effect'i sayaçla yeniden
  // tetiklemek yeterli.
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const result = await personnelService.getById(params.id);
        setPerson(result);
      } catch (err) {
        setPerson(null);
        setError(err instanceof Error ? err.message : "Personel yüklenemedi.");
      } finally {
        setLoading(false);
      }
    }

    if (params.id) {
      void load();
    }
  }, [params.id, reloadToken]);

  return (
    <ErpShell
      design="redwood"
      title={person?.fullName ?? "Personel Detayı"}
      description={person ? person.employeeNumber : "Personel bilgileri yükleniyor"}
    >
      <div className="erp-project-breadcrumb rw-breadcrumb-bar">
        <div>
          <Link href="/personel">Personel</Link>
          <span>›</span>
          <strong>{person?.fullName ?? "Detay"}</strong>
        </div>

        {/* İK kartı başka kullanıcı tarafından güncelleniyor. */}
        <button
          type="button"
          className="erp-secondary-button"
          disabled={loading}
          onClick={() => setReloadToken((value) => value + 1)}
        >
          Yenile
        </button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Personel yükleniyor...</div>
      ) : !person ? (
        <div className="erp-panel erp-empty-state">
          <strong>Personel bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Personel Bilgileri</h2>
                <p>{person.jobTitle || "Pozisyon belirtilmedi"}</p>
              </div>

              <div className="erp-actions">
                <Link
                  href={`/insan-kaynaklari/personel-360?personnelId=${person.id}`}
                  className="erp-button secondary"
                >
                  360° Görünüm
                </Link>
              </div>
            </div>

            <div className="erp-detail-grid">
              <div><span>Sicil No</span><strong>{person.employeeNumber}</strong></div>
              <div><span>Ad Soyad</span><strong>{person.fullName}</strong></div>
              <div><span>Şirket</span><strong>{person.companyName}</strong></div>
              <div><span>Şube</span><strong>{person.branchName || "—"}</strong></div>
              <div><span>Pozisyon</span><strong>{person.jobTitle || "—"}</strong></div>
              <div><span>Meslek / Departman</span><strong>{person.profession || "—"}</strong></div>
              <div><span>Telefon</span><strong>{person.phone || "—"}</strong></div>
              <div><span>E-posta</span><strong>{person.email || "—"}</strong></div>
              <div><span>TC Kimlik No</span><strong>{person.identityNumber || "—"}</strong></div>
              <div><span>Doğum Tarihi</span><strong>{formatDate(person.birthDate)}</strong></div>
              <div><span>İşe Giriş</span><strong>{formatDate(person.employmentStartDate)}</strong></div>
              <div><span>İşten Çıkış</span><strong>{formatDate(person.employmentEndDate)}</strong></div>
              <div><span>Aylık Maaş</span><strong>{formatMoney(person.monthlySalary)}</strong></div>
              <div><span>Durum</span><strong>{statusLabels[person.status] ?? "Bilinmiyor"}</strong></div>
              <div className="span-2">
                <span>Adres</span>
                <strong>{person.address || "—"}</strong>
              </div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Görevlendirmeleri</h2>
                <p>{person.assignments.length} kayıt</p>
              </div>
            </div>

            {person.assignments.length === 0 ? (
              <div className="erp-empty-state">Henüz proje ataması yok.</div>
            ) : (
              <div className="erp-project-list">
                {person.assignments.map((assignment) => (
                  <div className="erp-project-list-item" key={assignment.id}>
                    <div>
                      <strong>{assignment.projectCode} · {assignment.projectName}</strong>
                      <span>{assignment.role || "Görev belirtilmedi"}</span>
                      <span>
                        {formatDate(assignment.startDate)} —{" "}
                        {assignment.endDate ? formatDate(assignment.endDate) : "devam ediyor"}
                      </span>
                    </div>

                    <span className={`erp-status ${assignment.isActive ? "green" : "gray"}`}>
                      {assignment.isActive ? "Aktif" : "Kapalı"}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          {person.activeSiteAssignment && (
            <section className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <div>
                  <h2>Aktif Şantiye</h2>
                </div>
              </div>

              <div className="erp-detail-grid">
                <div>
                  <span>Şantiye</span>
                  <strong>
                    {person.activeSiteAssignment.siteCode} · {person.activeSiteAssignment.siteName}
                  </strong>
                </div>
                <div>
                  <span>Proje</span>
                  <strong>
                    {person.activeSiteAssignment.projectCode} · {person.activeSiteAssignment.projectName}
                  </strong>
                </div>
                <div>
                  <span>Görev</span>
                  <strong>{person.activeSiteAssignment.role || "—"}</strong>
                </div>
                <div>
                  <span>Başlangıç</span>
                  <strong>{formatDate(person.activeSiteAssignment.startDate)}</strong>
                </div>
              </div>
            </section>
          )}
        </>
      )}
    </ErpShell>
  );
}
