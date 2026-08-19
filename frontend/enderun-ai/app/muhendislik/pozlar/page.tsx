"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { decimal, whole } from "@/lib/format/turkish";
import { Button } from "@/components/ui";
import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
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

/**
 * SÜTUNLAR — ekranda görünen ile dosyaya/kâğıda giden ayrı.
 * Rozet ve bağlantı içeren hücrelerin düz karşılığı `value`.
 */
const columns: DataTableColumn<EngineeringPositionListItem>[] = [
  {
    key: "kod",
    header: "Poz No",
    value: (item) => item.officialCode || item.code,
    render: (item) => (
      <>
        <strong>{item.code}</strong>
        {item.officialCode && (
          <small style={{ display: "block" }}>{item.officialCode}</small>
        )}
      </>
    ),
  },
  {
    key: "ad",
    header: "Açıklama",
    value: (item) => item.name,
    render: (item) => (
      <>
        <strong>{item.name}</strong>
        <small style={{ display: "block" }}>
          {item.category || item.companyName}
        </small>
      </>
    ),
  },
  {
    key: "disiplin",
    header: "Disiplin",
    value: (item) =>
      disciplineLabels[item.discipline] ?? `Disiplin ${item.discipline}`,
  },
  { key: "birim", header: "Birim", value: (item) => item.unit },
  {
    key: "kaynak",
    header: "Kaynak",
    value: (item) =>
      positionSourceLabel(item.source, item.officialInstitution),
  },
  {
    key: "adamsaat",
    header: "Adam/Saat",
    numeric: true,
    value: (item) => formatHours(item.totalLaborHours),
  },
  {
    key: "revizyon",
    header: "Revizyon",
    value: (item) => `R${item.revisionNumber}`,
  },
  {
    key: "durum",
    header: "Durum",
    value: (item) => statusLabels[item.status] ?? `Durum ${item.status}`,
    render: (item) => (
      <span className={item.status === 1 ? "erp-status green" : "erp-status"}>
        {statusLabels[item.status] ?? `Durum ${item.status}`}
      </span>
    ),
  },
  {
    key: "ac",
    header: "",
    // Bağlantının dosyada karşılığı yok.
    value: () => "",
    render: (item) => (
      <Link href={`/muhendislik/pozlar/${item.id}`} className="erp-row-link">
        Aç →
      </Link>
    ),
  },
];

export default function EngineeringPositionsPage() {
  const [items, setItems] = useState<EngineeringPositionListItem[]>([]);
  /*
   * KÜTÜPHANEDEKİ GERÇEK KAYIT SAYISI — listelenen kayıt sayısıyla
   * AYNI ŞEY DEĞİL. Uç bir tavan uyguluyor (varsayılan 100); bu ekran
   * eskiden gelen dizinin uzunluğunu "Toplam Poz" diye gösteriyordu,
   * yani 23.531 pozluk kütüphane için ekranda 100 yazıyordu.
   */
  const [total, setTotal] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  /*
   * SAYFA SUNUCUDA ATLANIYOR. 23.531 poz istemciye yollanamaz; uç
   * `page` ve `take` alıp `Skip` uyguluyor.
   */
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  /*
   * FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER — ve bu effect'te DEĞİL, filtreyi
   * değiştiren yerde yapılır. Effect'te yapmak art arda render
   * tetikliyordu (React'ın "you might not need an effect" uyarısı) ve
   * daha kötüsü: istek bir kez eski sayfayla gidip boş dönüyordu.
   *
   * Sıfırlanmazsa kullanıcı 7. sayfadayken arama yaptığında uçtan boş
   * sayfa gelir — sayfalamanın en sık görülen hatası.
   */
  function applyFilter(apply: () => void) {
    apply();
    setPage(1);
  }
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
        take: pageSize,
        page,
        search: search || undefined,
        discipline: discipline === "" ? undefined : Number(discipline),
        status: status === "" ? undefined : Number(status),
        source: source === "" ? undefined : Number(source),
      });

      setItems(result.items);
      setTotal(result.total);
      setHasMore(result.hasMore);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Poz kütüphanesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [search, discipline, status, source, page, pageSize]);

  useEffect(() => {
    const timer = window.setTimeout(loadPositions, 350);
    return () => window.clearTimeout(timer);
  }, [loadPositions]);


  const summary = useMemo(() => {
    return {
      /*
       * TOPLAM UÇTAN GELİR, listeden sayılmaz. Aşağıdaki `active` ve
       * `custom` ise hâlâ listelenen kümeden sayılıyor — bu yüzden
       * etiketleri de "listelenen" diyor. Kütüphane geneli için
       * kırılım gerekiyorsa uç ayrıca döndürmeli; olmayan bilgiyi
       * ekranda varmış gibi göstermek bu ekranın ta baştaki hatasıydı.
       */
      total,
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
  }, [items, total]);

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
          <Button variant="secondary" disabled={loading} onClick={() => void loadPositions()}>Yenile</Button>
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
            <strong>
              {loading ? "…" : whole(summary.total)}
            </strong>
            <small>
              {hasMore
                ? `${items.length} tanesi listeleniyor`
                : "Kütüphanedeki kayıtlar"}
            </small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">✓</div>
          <div>
            <span>Aktif Poz</span>
            <strong>{loading ? "…" : summary.active}</strong>
            <small>
              {hasMore ? "Listelenenler içinde" : "Kullanıma açık pozlar"}
            </small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">Ö</div>
          <div>
            <span>Özel Poz</span>
            <strong>{loading ? "…" : summary.custom}</strong>
            <small>
              {hasMore ? "Listelenenler içinde" : "Şirkete özel pozlar"}
            </small>
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
            onChange={(event) =>
              applyFilter(() => setSearch(event.target.value))
            }
            placeholder="Poz no, açıklama veya anahtar kelime ara..."
          />

          <select
            className="erp-input"
            value={discipline}
            onChange={(event) =>
              applyFilter(() => setDiscipline(event.target.value))
            }
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
            onChange={(event) =>
              applyFilter(() => setSource(event.target.value))
            }
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
            onChange={(event) =>
              applyFilter(() => setStatus(event.target.value))
            }
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
              applyFilter(() => {
                setSearch("");
                setDiscipline("");
                setStatus("");
              });
            }}
          >
            Filtreleri Temizle
          </button>
        </div>

        <DataTable
          rows={items}
          columns={columns}
          rowKey={(item) => item.id}
          loading={loading}
          title="Poz Kütüphanesi"
          emptyText="Poz bulunamadı. Filtreleri değiştirin veya yeni bir mühendislik pozu ekleyin."
          server={{
            total,
            page,
            pageSize,
            onChange: (nextPage, nextSize) => {
              setPage(nextPage);
              setPageSize(nextSize);
            },
          }}
          /* Filtre değişince sayfa 1'e döner. */
          resetKey={`${search}|${discipline}|${status}|${source}`}
        />
      </section>
    </ErpShell>
  );
}
