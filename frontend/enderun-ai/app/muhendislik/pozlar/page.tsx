"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { decimal } from "@/lib/format/turkish";
import {
  EngineeringPositionListItem,
  EngineeringPositionSource,
  engineeringPositionService,
  positionSourceLabel,
} from "@/services/engineering-position.service";

const disciplineLabels: Record<number, string> = {
  0: "Genel",
  1: "Elektrik",
  2: "Orta Gerilim",
  3: "Zayıf Akım",
  4: "Veri Merkezi",
  5: "Fiber",
  6: "Mekanik",
  7: "İnşaat",
};

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Aktif",
  2: "Pasif",
  3: "Arşiv",
};

/**
 * İşçilik saati — iki ondalık.
 *
 * Sözleşmeye giren bir rakam değil, planlama miktarı; uygulamanın
 * her yerinde adam/saat iki hane yazılıyor (bkz. teklif hazırlama).
 * Alan veritabanında dört hane tutuyor ama saatin dördüncü hanesi
 * (0,36 saniye) ekranda gürültü.
 */
function formatHours(value: number) {
  return decimal(value, 2);
}

export default function EngineeringPositionsPage() {
  const [items, setItems] = useState<EngineeringPositionListItem[]>([]);
  const [search, setSearch] = useState("");
  const [discipline, setDiscipline] = useState("");
  const [status, setStatus] = useState("");
  const [source, setSource] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadPositions = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const result = await engineeringPositionService.getAll({
        search: search || undefined,
        discipline: discipline === "" ? undefined : Number(discipline),
        status: status === "" ? undefined : Number(status),
        source: source === "" ? undefined : Number(source),
      });

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Poz kütüphanesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [search, discipline, status, source]);

  useEffect(() => {
    const timer = window.setTimeout(loadPositions, 350);
    return () => window.clearTimeout(timer);
  }, [loadPositions]);

  const summary = useMemo(() => {
    return {
      total: items.length,
      active: items.filter((x) => x.status === 1).length,
      // Kaynak 0 RESMÎ kurum pozu, 1 şirkete özel. Bir dönem burada
      // 0 sayılıp "Enderun" diye gösteriliyordu; 23.500 resmî poz
      // şirketin kendi pozuymuş gibi görünüyordu.
      custom: items.filter(
        (x) => x.source === EngineeringPositionSource.Custom
      ).length,
      totalHours: items.reduce(
        (sum, x) => sum + (x.totalLaborHours ?? 0),
        0
      ),
    };
  }, [items]);

  return (
    <ErpShell
      design="redwood"
      title="Poz Kütüphanesi"
      description="Mühendislik pozları, adam/saat değerleri ve reçete altyapısı"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            MÜHENDİSLİK MERKEZİ
          </span>
          <h2>Poz kütüphanesi</h2>
          <p>
            Elektrik, OG, zayıf akım, fiber ve veri merkezi pozlarını ortak
            bir teknik kütüphanede yönetin.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          {/* Poz kütüphanesi içe aktarmayla ve başka kullanıcının
              poz eklemesiyle değişiyor; filtreye dokunmadan listeyi
              tazelemenin yolu yoktu. */}
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loading}
            onClick={() => void loadPositions()}
          >
            Yenile
          </button>
          <Link href="/muhendislik" className="erp-secondary-button">
            Mühendislik Merkezi
          </Link>
          <Link
            href="/muhendislik/pozlar/ice-aktar"
            className="erp-secondary-button"
          >
            Poz Kitabı İçe Aktar
          </Link>
          <Link
            href="/muhendislik/pozlar/yeni"
            className="erp-primary-button"
          >
            + Yeni Poz
          </Link>
        </div>
      </section>

      <div className="enderun-dashboard-stats">
        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">▦</div>
          <div>
            <span>Toplam Poz</span>
            <strong>{loading ? "…" : summary.total}</strong>
            <small>Kütüphanedeki kayıtlar</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">✓</div>
          <div>
            <span>Aktif Poz</span>
            <strong>{loading ? "…" : summary.active}</strong>
            <small>Kullanıma açık pozlar</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">Ö</div>
          <div>
            <span>Özel Poz</span>
            <strong>{loading ? "…" : summary.custom}</strong>
            <small>Şirkete özel pozlar</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">◷</div>
          <div>
            <span>Toplam Adam/Saat</span>
            <strong>{loading ? "…" : formatHours(summary.totalHours)}</strong>
            <small>Listelenen poz toplamı</small>
          </div>
        </div>
      </div>

      <section className="erp-panel">
        <div className="erp-panel-header">
          <div>
            <h2>Poz Listesi</h2>
            <p>Poz numarası, açıklama ve anahtar kelimeyle arama yapın</p>
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "minmax(260px, 1fr) 220px 180px auto",
            gap: 12,
            marginBottom: 20,
          }}
        >
          <input
            className="erp-input"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Poz no, açıklama veya anahtar kelime ara..."
          />

          <select
            className="erp-input"
            value={discipline}
            onChange={(event) => setDiscipline(event.target.value)}
          >
            <option value="">Tüm disiplinler</option>
            {Object.entries(disciplineLabels).map(([value, label]) => (
              <option value={value} key={value}>
                {label}
              </option>
            ))}
          </select>

          <select
            className="erp-input"
            value={source}
            onChange={(event) => setSource(event.target.value)}
          >
            <option value="">Tüm kaynaklar</option>
            <option value={EngineeringPositionSource.Official}>
              Resmî kurum (ÇŞB, TEDAŞ)
            </option>
            <option value={EngineeringPositionSource.Custom}>
              Özel (şirket)
            </option>
          </select>

          <select
            className="erp-input"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
          >
            <option value="">Tüm durumlar</option>
            {Object.entries(statusLabels).map(([value, label]) => (
              <option value={value} key={value}>
                {label}
              </option>
            ))}
          </select>

          <button
            className="erp-secondary-button"
            type="button"
            onClick={() => {
              setSearch("");
              setDiscipline("");
              setStatus("");
            }}
          >
            Filtreleri Temizle
          </button>
        </div>

        {loading ? (
          <div className="erp-loading">Pozlar yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <div className="enderun-empty-symbol">▦</div>
            <strong>Poz bulunamadı</strong>
            <p>Filtreleri değiştirin veya yeni bir mühendislik pozu ekleyin.</p>
            <Link
              href="/muhendislik/pozlar/yeni"
              className="erp-primary-button"
            >
              Yeni Poz Oluştur
            </Link>
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz No</th>
                  <th>Açıklama</th>
                  <th>Disiplin</th>
                  <th>Birim</th>
                  <th>Kaynak</th>
                  <th>Adam/Saat</th>
                  <th>Revizyon</th>
                  <th>Durum</th>
                  <th />
                </tr>
              </thead>

              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <strong>{item.code}</strong>
                      {item.officialCode && (
                        <small style={{ display: "block" }}>
                          {item.officialCode}
                        </small>
                      )}
                    </td>

                    <td>
                      <strong>{item.name}</strong>
                      <small style={{ display: "block" }}>
                        {item.category || item.companyName}
                      </small>
                    </td>

                    <td>
                      {disciplineLabels[item.discipline] ??
                        `Disiplin ${item.discipline}`}
                    </td>

                    <td>{item.unit}</td>

                    <td>
                      {positionSourceLabel(item.source, item.officialInstitution)}
                    </td>

                    <td>{formatHours(item.totalLaborHours)}</td>

                    <td>R{item.revisionNumber}</td>

                    <td>
                      <span
                        className={
                          item.status === 1
                            ? "erp-status green"
                            : "erp-status"
                        }
                      >
                        {statusLabels[item.status] ??
                          `Durum ${item.status}`}
                      </span>
                    </td>

                    <td>
                      <Link
                        href={`/muhendislik/pozlar/${item.id}`}
                        className="erp-row-link"
                      >
                        Aç →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ErpShell>
  );
}
