"use client";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { unitPrice } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import { Button } from "@/components/ui";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  customPositionService,
  engineeringPositionService,
  engineeringPositionStatusService,
  positionPriceService,
  EngineeringPositionSource,
  PositionPriceInstitution,
  type EngineeringPositionListItem,
} from "@/services/engineering-position.service";

const disciplines: Array<[number, string]> = [
  [0, "Genel"],
  [1, "Elektrik"],
  [2, "Orta Gerilim"],
  [3, "Zayıf Akım"],
  [4, "Veri Merkezi"],
  [5, "Fiber"],
  [6, "Mekanik"],
  [7, "İnşaat"],
];

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Aktif",
  2: "Pasif",
  3: "Arşiv",
};

// BİRİM FİYAT: PositionUnitPrice.UnitPrice veritabanında
// numeric(18,4). Buradaki biçim iki haneye zorluyordu, yani
// girilen 12,4567 ekranda 12,46 görünüyordu; kullanıcı o rakamı
// metrajla çarptığında toplam tutmuyordu.
function money(value?: number | null) {
  return unitPrice(value);
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

const currentYear = new Date().getFullYear();

/**
 * Şirkete özel pozlar.
 *
 * Resmî kitaplarda (ÇŞB, TEDAŞ) karşılığı olmayan imalatlar için.
 * Kütüphanede 23 binin üzerinde resmî poz var; şirketin kendi
 * pozlarının onların arasında kaybolmaması için ayrı ekran.
 *
 * Poz oluşturma P4'teki tek adımlı uçtan gidiyor: kod şirket
 * serisinden üretiliyor, poz DOĞRUDAN AKTİF açılıyor ve fiyat aynı
 * istekte "Şirket" kurumuyla yazılıyor. Taslak bırakıp ayrı bir onay
 * adımına zorlamak, keşif hazırlarken akışı kesiyordu.
 */
export default function CustomPositionsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [items, setItems] = useState<EngineeringPositionListItem[]>([]);
  /* Uç 500'de kırpmış mı — kırpıldıysa liste eksik olabilir. */
  const [truncated, setTruncated] = useState(false);
  const [prices, setPrices] = useState<Record<string, number | null>>({});

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  const [formOpen, setFormOpen] = useState(false);
  const [name, setName] = useState("");
  const [unit, setUnit] = useState("Adet");
  const [discipline, setDiscipline] = useState("1");
  const [category, setCategory] = useState("");
  const [unitPrice, setUnitPrice] = useState("");
  const [notes, setNotes] = useState("");

  // Fiyat düzenleme: satır içinde, ayrı ekrana gitmeden.
  const [priceEditId, setPriceEditId] = useState<string | null>(null);
  const [priceEditValue, setPriceEditValue] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await companyService.getAll();
        if (cancelled) return;

        const active = result.filter((x) => x.isActive !== false);
        setCompanies(active);
        if (active.length === 1) setCompanyId(active[0].id);
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      setLoading(true);

      try {
        const result = await engineeringPositionService.getAll({
          source: EngineeringPositionSource.Custom,
          take: 500,
        });

        if (cancelled) return;

        /*
         * ŞİRKET SÜZGECİ İSTEMCİDE, TAVAN SUNUCUDA. Bugün özel poz
         * sayısı tavanın (500) çok altında olduğu için sorun değil;
         * aşılırsa bir şirketin pozları ilk 500'ün DIŞINDA kalıp
         * sessizce kaybolabilir. Uyarıyı bu yüzden gösteriyoruz.
         */
        const scoped = companyId
          ? result.items.filter((x) => x.companyId === companyId)
          : result.items;

        setItems(scoped);
        setTruncated(result.hasMore);

        // Fiyatlar poz listesinde gelmiyor; her poz için ayrı çözülüyor.
        // Şirkete özel poz sayısı düşük olduğu için bu kabul edilebilir;
        // liste büyürse uca toplu fiyat çözümü eklenmeli.
        const resolved = await Promise.all(
          scoped.map(async (position) => {
            try {
              const price = await positionPriceService.resolve(
                position.id,
                currentYear,
                PositionPriceInstitution.Company
              );
              return [position.id, price.unitPrice ?? null] as const;
            } catch {
              return [position.id, null] as const;
            }
          })
        );

        if (cancelled) return;

        setPrices(Object.fromEntries(resolved));
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [companyId, refreshKey]);

  const summary = useMemo(
    () => ({
      total: items.length,
      active: items.filter((x) => x.status === 1).length,
      priced: items.filter((x) => prices[x.id] != null).length,
    }),
    [items, prices]
  );

  async function handleCreate(event: FormEvent) {
    event.preventDefault();

    if (!companyId) {
      setError("Şirket seçilmelidir.");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await customPositionService.create({
        companyId,
        name: name.trim(),
        unit: unit.trim() || null,
        discipline: Number(discipline),
        category: category.trim() || null,
        notes: notes.trim() || null,
        unitPrice: unitPrice
          ? Number(unitPrice.replace(",", "."))
          : null,
        year: currentYear,
      });

      setNotice(
        `${result.code} açıldı${
          result.unitPrice != null
            ? ` — birim fiyat ${money(result.unitPrice)}`
            : " (fiyat girilmedi)"
        }.`
      );

      setName("");
      setCategory("");
      setUnitPrice("");
      setNotes("");
      setFormOpen(false);
      setRefreshKey((current) => current + 1);
    } catch (createError) {
      setError(errorMessage(createError));
    } finally {
      setSaving(false);
    }
  }

  async function savePrice(positionId: string) {
    const parsed = Number(priceEditValue.replace(",", "."));

    if (!Number.isFinite(parsed) || parsed <= 0) {
      setError("Birim fiyat sıfırdan büyük bir sayı olmalıdır.");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await positionPriceService.upsert(positionId, {
        year: currentYear,
        // Şirkete özel pozun fiyatı da şirkete ait; ÇŞB/TEDAŞ kurumuna
        // yazılsaydı resmî bir fiyatmış gibi görünürdü.
        institution: PositionPriceInstitution.Company,
        unitPrice: parsed,
        sourceNote: "Özel poz ekranından girildi",
      });

      setNotice(`${currentYear} birim fiyatı güncellendi.`);
      setPriceEditId(null);
      setPriceEditValue("");
      setRefreshKey((current) => current + 1);
    } catch (priceError) {
      setError(errorMessage(priceError));
    } finally {
      setSaving(false);
    }
  }

  async function changeStatus(positionId: string, status: number) {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await engineeringPositionStatusService.change(positionId, status);
      setNotice(`Poz ${statusLabels[status]?.toLocaleLowerCase("tr-TR")} yapıldı.`);
      setRefreshKey((current) => current + 1);
    } catch (statusError) {
      setError(errorMessage(statusError));
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Özel Pozlar"
      description="Resmî kitaplarda karşılığı olmayan, şirkete özel imalat pozları"
    >
      <div className="erp-page">
        <div className="enderun-dashboard-stats">
          <div className="enderun-dashboard-stat">
            <div className="enderun-dashboard-stat-icon">Ö</div>
            <div>
              <span>Özel Poz</span>
              <strong>{loading ? "…" : summary.total}</strong>
              <small>Şirkete özel</small>
            </div>
          </div>

          <div className="enderun-dashboard-stat">
            <div className="enderun-dashboard-stat-icon">✓</div>
            <div>
              <span>Aktif</span>
              <strong>{loading ? "…" : summary.active}</strong>
              <small>Keşifte kullanılabilir</small>
            </div>
          </div>

          <div className="enderun-dashboard-stat">
            <div className="enderun-dashboard-stat-icon">₺</div>
            <div>
              <span>Fiyatlı</span>
              <strong>{loading ? "…" : summary.priced}</strong>
              <small>{currentYear} birim fiyatı girilmiş</small>
            </div>
          </div>
        </div>

        {error && <div className="erp-alert erp-alert-danger">{error}</div>}
        {notice && <div className="erp-alert erp-alert-success">{notice}</div>}

        {/*
          Tavan doldu: şirket süzgeci istemcide çalıştığı için bu
          durumda bazı özel pozlar hiç gelmemiş olabilir.
        */}
        {truncated && (
          <div className="erp-alert warning">
            <strong>Liste eksik olabilir.</strong> Özel poz sayısı
            sunucu tavanına (500) ulaştı; bu ekran şirket süzgecini
            geldikten sonra uyguluyor.
          </div>
        )}

        <section className="erp-card">
          <div className="erp-card-header">
            <div>
              <h2>Şirkete özel pozlar</h2>
              <p>
                Kod şirket serisinden üretilir, poz doğrudan aktif açılır ve
                fiyatı &quot;Şirket&quot; kurumuna yazılır.
              </p>
            </div>

            <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
              <select
                className="erp-input"
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
              >
                <option value="">Tüm şirketler</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.name}
                  </option>
                ))}
              </select>

              {/* refreshKey zaten vardı ama yalnızca bu sayfanın kendi
                  işlemlerinden sonra artıyordu; başka kullanıcının
                  eklediği pozu görmenin yolu sayfayı yeniden yüklemekti. */}
              <Button variant="secondary" disabled={loading} onClick={() => setRefreshKey((current) => current + 1)}>Yenile</Button>

              <button
                type="button"
                className="erp-primary-button"
                onClick={() => setFormOpen((current) => !current)}
              >
                {formOpen ? "Vazgeç" : "Yeni Özel Poz"}
              </button>

              <Link href="/muhendislik/pozlar" className="erp-secondary-button">
                Tüm Kütüphane
              </Link>
            </div>
          </div>

          {formOpen && (
            <form onSubmit={handleCreate} className="erp-form-grid">
              <label>
                <span>Şirket</span>
                <select
                  className="erp-input"
                  value={companyId}
                  onChange={(event) => setCompanyId(event.target.value)}
                  required
                >
                  <option value="">Seçiniz</option>
                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>
                      {company.name}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Poz Tanımı</span>
                <input
                  className="erp-input"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder="A Blok pano montajı"
                  required
                />
              </label>

              <label>
                <span>Birim</span>
                <input
                  className="erp-input"
                  value={unit}
                  onChange={(event) => setUnit(event.target.value)}
                  placeholder="Adet"
                />
              </label>

              <label>
                <span>Disiplin</span>
                <select
                  className="erp-input"
                  value={discipline}
                  onChange={(event) => setDiscipline(event.target.value)}
                >
                  {disciplines.map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Kategori</span>
                <input
                  className="erp-input"
                  value={category}
                  onChange={(event) => setCategory(event.target.value)}
                  placeholder="Panolar"
                />
              </label>

              <label>
                <span>{currentYear} Birim Fiyatı</span>
                <input
                  className="erp-input"
                  value={unitPrice}
                  onChange={(event) => setUnitPrice(event.target.value)}
                  inputMode="decimal"
                  placeholder="0,00"
                />
                <small>
                  Boş bırakılabilir; fiyat sonradan bu ekrandan girilebilir.
                </small>
              </label>

              <label style={{ gridColumn: "1 / -1" }}>
                <span>Not</span>
                <input
                  className="erp-input"
                  value={notes}
                  onChange={(event) => setNotes(event.target.value)}
                />
              </label>

              <div style={{ gridColumn: "1 / -1" }}>
                <button
                  type="submit"
                  className="erp-primary-button"
                  disabled={saving || !name.trim()}
                >
                  {saving ? "Kaydediliyor..." : "Pozu Aç"}
                </button>
              </div>
            </form>
          )}

          <div className="erp-table-wrapper">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Tanım</th>
                  <th>Birim</th>
                  <th>Kategori</th>
                  <th>{currentYear} Birim Fiyatı</th>
                  <th>Durum</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {!loading && items.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ textAlign: "center", padding: 28 }}>
                      Şirkete özel poz yok. Resmî kitaplarda karşılığı olmayan
                      bir imalat için &quot;Yeni Özel Poz&quot; ile açabilirsiniz.
                    </td>
                  </tr>
                )}

                {items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <Link href={`/muhendislik/pozlar/${item.id}`}>
                        <strong>{item.code}</strong>
                      </Link>
                    </td>
                    <td>{item.name}</td>
                    <td>{item.unit}</td>
                    <td>{item.category ?? "—"}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>
                      {priceEditId === item.id ? (
                        <span style={{ display: "flex", gap: 6 }}>
                          <input
                            className="erp-input"
                            value={priceEditValue}
                            onChange={(event) =>
                              setPriceEditValue(event.target.value)
                            }
                            inputMode="decimal"
                            style={{ width: 130 }}
                          />
                          <button
                            type="button"
                            className="erp-primary-button"
                            disabled={saving}
                            onClick={() => void savePrice(item.id)}
                          >
                            Kaydet
                          </button>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => setPriceEditId(null)}
                          >
                            Vazgeç
                          </button>
                        </span>
                      ) : (
                        <span style={{ display: "flex", gap: 8, alignItems: "center" }}>
                          {money(prices[item.id])}
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => {
                              setPriceEditId(item.id);
                              setPriceEditValue(
                                prices[item.id] != null
                                  ? String(prices[item.id])
                                  : ""
                              );
                            }}
                          >
                            {prices[item.id] == null ? "Fiyat gir" : "Değiştir"}
                          </button>
                        </span>
                      )}
                    </td>
                    <td>
                      <span
                        className={`erp-status ${
                          item.status === 1 ? "green" : "gray"
                        }`}
                      >
                        {statusLabels[item.status] ?? item.status}
                      </span>
                    </td>
                    <td>
                      {/* Poz SİLİNMİYOR, pasife alınıyor: silinseydi onu
                          kullanan geçmiş keşif ve hakediş satırlarının
                          bağı kopardı. */}
                      {item.status === 1 ? (
                        <button
                          type="button"
                          className="erp-secondary-button"
                          disabled={saving}
                          onClick={() => void changeStatus(item.id, 2)}
                        >
                          Pasife al
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="erp-secondary-button"
                          disabled={saving}
                          onClick={() => void changeStatus(item.id, 1)}
                        >
                          Aktifleştir
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </ErpShell>
  );
}
