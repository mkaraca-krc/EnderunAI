"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { Button } from "@/components/ui";
import {
  isgService,
  type IsgDashboard,
  type IsgPersonnelSummary,
} from "@/services/isg.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

export default function IsgDashboardPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [panel, setPanel] = useState<IsgDashboard | null>(null);
  const [personnel, setPersonnel] = useState<IsgPersonnelSummary[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

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

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      const [dashboard, summary] = await Promise.all([
        isgService.getDashboard(companyId),
        isgService.getPersonnelSummary(companyId),
      ]);

      setPanel(dashboard);
      setPersonnel(summary);
    } catch (err) {
      setPanel(null);
      setPersonnel([]);
      setError(err instanceof Error ? err.message : "Panel verisi alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    // Şirket listesi yüklenip companyId'yi ilk kez atadığında iki istek
    // arka arkaya gitmesin diye küçük bir gecikme.
    const timer = window.setTimeout(() => void load(), 150);
    return () => window.clearTimeout(timer);
  }, [load]);

  // Süresi dolan veya dolmak üzere olan kaydı olan personel: panelin
  // asıl iş listesi. Sıralama en acilden başlar.
  const attention = personnel
    .filter(
      (person) =>
        person.expiredCount > 0 ||
        person.expiringSoonCount > 0 ||
        person.hasMissingRecords
    )
    .sort(
      (a, b) =>
        b.expiredCount - a.expiredCount ||
        b.expiringSoonCount - a.expiringSoonCount
    );

  return (
    <ErpShell
      design="redwood"
      title="İSG Paneli"
      description="Sağlık raporu, eğitim, sertifika ve saha belgesi geçerlilik takibi"
    >
      <div className="erp-page-toolbar">
        {/* Sertifika ve rapor süreleri gün geçtikçe doluyor; panel tazelenmeden eskiyordu. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <div>
          <strong>{panel?.aktifPersonel ?? 0} aktif personel</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Uyarı eşiği: son {panel?.uyariEsigiGun ?? 30} gün
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <Link className="erp-secondary-button" href="/isg/personel">
            Personel Kayıtları
          </Link>
          <Link className="erp-secondary-button" href="/isg/belgeler">
            Saha Belgeleri
          </Link>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading && <div className="erp-loading">Yükleniyor...</div>}

      {!loading && panel && (
        <>
          <div className="erp-stat-grid">
            <Link className="erp-stat-card" href="/isg/personel">
              <span className="erp-stat-label">Sağlık Raporu</span>
              <strong>{panel.saglikRaporu.suresiDoldu}</strong>
              <small>
                süresi doldu · {panel.saglikRaporu.yakindaDoluyor} yakında ·{" "}
                {panel.saglikRaporu.eksikPersonel} personelde hiç yok
              </small>
            </Link>

            <Link className="erp-stat-card" href="/isg/personel">
              <span className="erp-stat-label">İSG Eğitimi</span>
              <strong>{panel.egitim.suresiDoldu}</strong>
              <small>
                süresi doldu · {panel.egitim.yakindaDoluyor} yakında ·{" "}
                {panel.egitim.temelEgitimiEksikPersonel} temel eğitimi eksik
              </small>
            </Link>

            <Link className="erp-stat-card" href="/isg/personel">
              <span className="erp-stat-label">Yetki Belgesi</span>
              <strong>{panel.sertifika.suresiDoldu}</strong>
              <small>
                süresi doldu · {panel.sertifika.yakindaDoluyor} yakında doluyor
              </small>
            </Link>

            <Link className="erp-stat-card" href="/isg/belgeler">
              <span className="erp-stat-label">Saha Belgesi</span>
              <strong>{panel.sahaBelgeleri.suresiDoldu}</strong>
              <small>
                süresi doldu ·{" "}
                {panel.sahaBelgeleri.riskDegerlendirmesiOlanSantiye} şantiyede
                geçerli risk değerlendirmesi
              </small>
            </Link>

            <Link className="erp-stat-card" href="/isg/osgb">
              <span className="erp-stat-label">OSGB Sözleşmesi</span>
              <strong>{panel.osgb.aktifSozlesme}</strong>
              <small>
                aktif · {panel.osgb.suresiDoluyor} yakında bitiyor ·{" "}
                {panel.osgb.suresiDoldu} süresi doldu
              </small>
            </Link>
          </div>

          {panel.kaza ? (
            <div className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <h2>Kaza ve Ramak Kala</h2>
                <Link className="erp-row-link" href="/isg/kazalar">
                  Kayıt defterine git
                </Link>
              </div>

              {panel.kaza.sgkBildirimiGecikmis > 0 && (
                <div className="erp-alert error">
                  {panel.kaza.sgkBildirimiGecikmis} iş kazası SGK&apos;ya
                  bildirilmemiş ve yasal süre geçti. İş kazası üç iş günü
                  içinde bildirilmek zorunda.
                </div>
              )}

              <div className="erp-detail-grid">
                <div>
                  <span className="erp-stat-label">Açık kayıt</span>
                  <strong>{panel.kaza.acikKayit}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Ağır kayıt</span>
                  <strong>{panel.kaza.agirKayit}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Bu yıl iş kazası</span>
                  <strong>{panel.kaza.buYilKaza}</strong>
                </div>
                <div>
                  <span className="erp-stat-label">Bu yıl ramak kala</span>
                  <strong>{panel.kaza.buYilRamakKala}</strong>
                </div>
                <div className="span-2">
                  <span className="erp-stat-label">Bu yıl kayıp iş günü</span>
                  <strong>{panel.kaza.buYilKayipIsGunu}</strong>
                </div>
              </div>
            </div>
          ) : (
            <div className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <h2>Kaza ve Ramak Kala</h2>
              </div>
              <p>
                Kaza kayıtları ayrı yetkiyle korunuyor; bu bölüm size
                görünmüyor.
              </p>
            </div>
          )}

          <div className="erp-table-card erp-mt">
            <div className="erp-table-header">
              <h2>Takip Gereken Personel ({attention.length})</h2>
              <Link className="erp-row-link" href="/isg/personel">
                Tümünü gör
              </Link>
            </div>

            {attention.length === 0 ? (
              <div className="erp-empty-state">
                <p>Süresi dolan veya eksik kaydı olan personel yok.</p>
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
                      <th>Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {attention.slice(0, 25).map((person) => (
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
                            <span className="erp-status red">Geçerli rapor yok</span>
                          )}
                        </td>
                        <td>
                          {person.hasValidBasicTraining ? (
                            <span className="erp-status green">Var</span>
                          ) : (
                            <span className="erp-status red">Yok</span>
                          )}
                        </td>
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
