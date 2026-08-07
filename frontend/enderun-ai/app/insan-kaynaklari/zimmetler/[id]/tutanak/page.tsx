"use client";

import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import { companyService, type CompanyListItem } from "@/services/company.service";
import { hrAssetService, type AssetAssignment } from "@/services/hr-asset.service";
import { personnelService, type PersonnelDetail } from "@/services/personnel.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Zimmet tutanağı — imzalanıp dosyalanacak çıktı.
 *
 * ErpShell KULLANILMIYOR: menü ve kabuk kağıda basılır, tutanak
 * kayardı. Sayfa doğrudan yazdırılabilir bir belge olarak kuruluyor;
 * ekrandaki tek etkileşim yazdırma düğmesi ve o da @media print ile
 * gizleniyor.
 */
export default function AssetHandoverPage() {
  const params = useParams<{ id: string }>();
  const assignmentId = params.id;

  const [assignment, setAssignment] = useState<AssetAssignment | null>(null);
  const [company, setCompany] = useState<CompanyListItem | null>(null);
  const [personnel, setPersonnel] = useState<PersonnelDetail | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const record = await hrAssetService.getById(assignmentId);
        if (cancelled) return;

        setAssignment(record);

        const [companies, employee] = await Promise.all([
          companyService.getAll(),
          personnelService.getById(record.personnelId),
        ]);
        if (cancelled) return;

        setCompany(
          companies.find((x) => x.id === record.companyId) ?? companies[0] ?? null
        );
        setPersonnel(employee);
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
          border-bottom: 2px solid #18797c;
          padding-bottom: 12px;
          margin-bottom: 20px;
        }
        .handover h1 { font-size: 16px; text-align: center; margin: 20px 0; }
        .handover .signatures {
          display: flex;
          gap: 48px;
          margin-top: 48px;
        }
        .handover .signatures > div {
          flex: 1;
          border-top: 1px solid #111;
          padding-top: 8px;
          text-align: center;
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
      </header>

      <h1>ZİMMET TESLİM TUTANAĞI</h1>

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
            <td>{dateFormat.format(new Date(assignment.assignmentDate))}</td>
          </tr>
          <tr>
            <th>Planlanan İade</th>
            <td>
              {assignment.plannedReturnDate
                ? dateFormat.format(new Date(assignment.plannedReturnDate))
                : "Süresiz"}
            </td>
          </tr>
          <tr>
            <th>Teslim Anındaki Durum</th>
            <td>{assignment.conditionAtAssignment ?? "Sağlam"}</td>
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
          Teslim Alan
          <br />
          <strong>{personnel?.fullName ?? ""}</strong>
          <br />
          <small>İmza</small>
        </div>
      </div>
    </main>
  );
}
