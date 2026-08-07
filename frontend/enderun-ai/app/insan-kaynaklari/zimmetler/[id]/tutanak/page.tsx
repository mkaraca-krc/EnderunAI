"use client";

import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  hrAssetService,
  HrAssetAssignmentStatus,
  type AssetAssignment,
} from "@/services/hr-asset.service";
import { personnelService, type PersonnelDetail } from "@/services/personnel.service";
import { projectService, type ProjectListItem } from "@/services/project.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

/**
 * Zimmet tutanağı — imzalanıp dosyalanacak tek resmi çıktı.
 *
 * ErpShell KULLANILMIYOR: menü ve kabuk kağıda basılır, tutanak
 * kayardı. Sayfa doğrudan yazdırılabilir bir belge olarak kuruluyor;
 * ekrandaki tek etkileşim yazdırma düğmesi ve o da @media print ile
 * gizleniyor.
 *
 * Zimmet iade edilmişse belge TESLİM/İADE tutanağına dönüşür ve iade
 * satırları görünür; iade bilgisi olmayan bir tutanak imzalatmak
 * ekipmanın geri geldiğini belgelemezdi.
 */
export default function AssetHandoverPage() {
  const params = useParams<{ id: string }>();
  const assignmentId = params.id;

  const [assignment, setAssignment] = useState<AssetAssignment | null>(null);
  const [company, setCompany] = useState<CompanyListItem | null>(null);
  const [personnel, setPersonnel] = useState<PersonnelDetail | null>(null);
  const [project, setProject] = useState<ProjectListItem | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const record = await hrAssetService.getById(assignmentId);
        if (cancelled) return;

        setAssignment(record);

        const [companies, employee, projects] = await Promise.all([
          companyService.getAll(),
          personnelService.getById(record.personnelId),
          record.projectId
            ? projectService.getAll().catch(() => [] as ProjectListItem[])
            : Promise.resolve([] as ProjectListItem[]),
        ]);
        if (cancelled) return;

        setCompany(
          companies.find((x) => x.id === record.companyId) ?? companies[0] ?? null
        );
        setPersonnel(employee);
        setProject(projects.find((x) => x.id === record.projectId) ?? null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Tutanak alınamadı.");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [assignmentId]);

  if (error) return <main style={{ padding: 32 }}>{error}</main>;
  if (!assignment) return <main style={{ padding: 32 }}>Tutanak yükleniyor...</main>;

  const returned = assignment.status === HrAssetAssignmentStatus.Returned;

  return (
    <main className="handover">
      <style>{`
        .handover {
          max-width: 210mm;
          margin: 0 auto;
          padding: 24px;
          background: #fff;
          color: #111;
          font-size: 13px;
          line-height: 1.6;
        }
        .handover table { width: 100%; border-collapse: collapse; margin: 16px 0; }
        .handover th, .handover td {
          border: 1px solid #999;
          padding: 8px;
          text-align: left;
          vertical-align: top;
        }
        .handover th { background: #f2f2f2; width: 38%; }
        .handover .letterhead {
          display: flex;
          justify-content: space-between;
          align-items: flex-end;
          border-bottom: 2px solid #18797c;
          padding-bottom: 12px;
          margin-bottom: 20px;
        }
        .handover h1 { font-size: 16px; text-align: center; margin: 20px 0; }
        .handover .signatures {
          display: flex;
          gap: 32px;
          margin-top: 56px;
        }
        .handover .signatures > div {
          flex: 1;
          border-top: 1px solid #111;
          padding-top: 8px;
          text-align: center;
        }
        .handover .footer {
          margin-top: 32px;
          font-size: 11px;
          color: #555;
        }
        .handover .print-actions { margin-bottom: 16px; }
        @media print {
          .handover { padding: 0; max-width: none; }
          .handover .print-actions { display: none; }
        }
      `}</style>

      <div className="print-actions">
        <button type="button" onClick={() => window.print()}>
          Yazdır
        </button>
      </div>

      <header className="letterhead">
        <strong style={{ fontSize: 16 }}>{company?.name ?? "—"}</strong>
        <span style={{ fontSize: 12 }}>
          Tutanak No: <strong>{assignment.id.slice(0, 8).toUpperCase()}</strong>
        </span>
      </header>

      <h1>
        {returned
          ? "ZİMMET TESLİM / İADE TUTANAĞI"
          : "ZİMMET TESLİM TUTANAĞI"}
      </h1>

      <p>
        Aşağıda bilgileri yer alan demirbaş/alet, teslim alan personele
        zimmetlenmiştir. Personel, teslim aldığı malzemeyi özenle kullanmayı
        ve iş akdinin sona ermesi hâlinde eksiksiz iade etmeyi kabul eder.
      </p>

      <table>
        <tbody>
          <tr>
            <th>Teslim Alan</th>
            <td>{personnel?.fullName ?? "—"}</td>
          </tr>
          <tr>
            <th>Sicil No</th>
            <td>{personnel?.employeeNumber ?? "—"}</td>
          </tr>
          <tr>
            <th>Proje</th>
            <td>
              {project ? `${project.code} — ${project.name}` : "Projesiz"}
            </td>
          </tr>
          <tr>
            <th>Demirbaş Kodu</th>
            <td>{assignment.assetCode}</td>
          </tr>
          <tr>
            <th>Demirbaş Adı</th>
            <td>{assignment.assetName}</td>
          </tr>
          <tr>
            <th>Tür</th>
            <td>{assignment.assetType}</td>
          </tr>
          <tr>
            <th>Seri No</th>
            <td>{assignment.serialNumber ?? "—"}</td>
          </tr>
          <tr>
            <th>Zimmet Tarihi</th>
            <td>{formatDate(assignment.assignmentDate)}</td>
          </tr>
          <tr>
            <th>Planlanan İade</th>
            <td>
              {assignment.plannedReturnDate
                ? formatDate(assignment.plannedReturnDate)
                : "Süresiz"}
            </td>
          </tr>
          <tr>
            <th>Teslim Anındaki Durum</th>
            <td>{assignment.conditionAtAssignment ?? "Sağlam"}</td>
          </tr>
          {returned && (
            <>
              <tr>
                <th>Gerçek İade Tarihi</th>
                <td>{formatDate(assignment.actualReturnDate)}</td>
              </tr>
              <tr>
                <th>İade Anındaki Durum</th>
                <td>{assignment.conditionAtReturn ?? "—"}</td>
              </tr>
            </>
          )}
          <tr>
            <th>Kayıt Durumu</th>
            <td>{assignment.statusName}</td>
          </tr>
          {assignment.notes && (
            <tr>
              <th>Not</th>
              <td style={{ whiteSpace: "pre-wrap" }}>{assignment.notes}</td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="signatures">
        <div>
          Teslim Eden
          <br />
          <small>Ad Soyad / İmza</small>
        </div>
        <div>
          Teslim Alan Personel
          <br />
          <strong>{personnel?.fullName ?? ""}</strong>
          <br />
          <small>İmza</small>
        </div>
        <div>
          İK / Birim Yetkilisi
          <br />
          <small>Ad Soyad / İmza</small>
        </div>
      </div>

      <div className="footer">
        Enderun AI Yönetim Sistemi tarafından oluşturulmuştur.
      </div>
    </main>
  );
}
