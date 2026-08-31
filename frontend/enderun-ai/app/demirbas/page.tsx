"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";

import { useIstemciTarihi } from "@/lib/use-istemci-zamani";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { amount } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { Button } from "@/components/ui";
import {
  TOOL_ASSET_STATUSES,
  ToolAssetLocationType,
  ToolAssetStatus,
  toolAssetService,
  type SaveToolAssetPayload,
  type ToolAsset,
} from "@/services/tool-asset.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

/** Duruma göre rozet rengi — hurda ve serviste dikkat çekmeli. */
function statusClass(status: number) {
  if (status === ToolAssetStatus.Scrapped) return "erp-status red";
  if (status === ToolAssetStatus.InService) return "erp-status orange";
  if (status === ToolAssetStatus.InUse) return "erp-status green";
  return "erp-status gray";
}

const emptyForm: SaveToolAssetPayload = {
  companyId: "",
  code: "",
  name: "",
  brand: "",
  model: "",
  serialNumber: "",
  purchaseDate: null,
  purchaseCost: null,
  warrantyEndDate: null,
  locationType: ToolAssetLocationType.HeadOffice,
  projectSiteId: null,
  notes: "",
};

/**
 * Demirbaş / el aletleri listesi.
 *
 * Sarftan ayrıdır: alet tüketilmez, kullanılır ve geri gelir. Her
 * aletin kalıcı bir kartı olduğu için servis geçmişi ve garanti takibi
 * alet bazında birikir.
 */
export default function ToolAssetsPage() {
  /**
   * Düğme -> uç -> izin (ToolAssetsController):
   *   POST tool-assets      -> PERSONNEL.create
   *   PUT  tool-assets/{id} -> PERSONNEL.edit
   *
   * DEMİRBAŞ EKRANI AMA İZİN PERSONEL AİLESİNDE. Ekran adına bakıp
   * inventory.* demek tahmin olurdu; uçlar personnel.* zorluyor
   * (alet zimmeti personel kaydına bağlı).
   *
   * "Kaydet" AYNI DÜĞME İKİ AYRI UÇ: düzenlemede PUT, yenide POST.
   */
  const actions = useModuleActions("personnel");

  /*
   * "BUGÜN" ÇİZİMDE OKUNMAZ.
   *
   * `new Date()` çizim sırasında sunucuda (derleme anı) ve istemcide
   * (açılış anı) farklı sonuç verir; garanti bitişi ikisinin arasında
   * kalırsa aynı satır sunucuda "sürüyor", istemcide "doldu" yazar —
   * hidrasyon uyuşmazlığı. Bağlanma sonrası dolduruluyor: ilk çizimde
   * iki taraf da AYNI tabanı kullanır.
   */
  const bugun = useIstemciTarihi();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [assets, setAssets] = useState<ToolAsset[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [status, setStatus] = useState<number | "">("");
  const [search, setSearch] = useState("");

  const [form, setForm] = useState<SaveToolAssetPayload>(emptyForm);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  const [reloadToken, setReloadToken] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const list = await companyService.getAll();
        if (cancelled) return;

        setCompanies(list);
        setCompanyId((current) => current || list[0]?.id || "");
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const list = await toolAssetService.getAll({
          companyId: companyId || undefined,
          status,
          search: search.trim() || undefined,
        });

        if (!cancelled) {
          setAssets(list);
          setError("");
        }
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [companyId, status, search, reloadToken]);

  function openCreate() {
    setForm({ ...emptyForm, companyId });
    setEditingId(null);
    setShowForm(true);
    setNotice("");
  }

  function openEdit(asset: ToolAsset) {
    setForm({
      companyId: asset.companyId,
      code: asset.code,
      name: asset.name,
      brand: asset.brand ?? "",
      model: asset.model ?? "",
      serialNumber: asset.serialNumber ?? "",
      purchaseDate: asset.purchaseDate?.slice(0, 10) ?? null,
      purchaseCost: asset.purchaseCost ?? null,
      warrantyEndDate: asset.warrantyEndDate?.slice(0, 10) ?? null,
      locationType: asset.locationType,
      projectSiteId: asset.projectSiteId ?? null,
      notes: asset.notes ?? "",
    });
    setEditingId(asset.id);
    setShowForm(true);
    setNotice("");
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    if (!form.code.trim() || !form.name.trim()) {
      setError("Alet kodu ve adı zorunludur.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      const payload: SaveToolAssetPayload = {
        ...form,
        companyId: form.companyId || companyId,
        brand: form.brand?.trim() || null,
        model: form.model?.trim() || null,
        serialNumber: form.serialNumber?.trim() || null,
        notes: form.notes?.trim() || null,
      };

      if (editingId) {
        await toolAssetService.update(editingId, payload);
        setNotice("Alet kartı güncellendi.");
      } else {
        await toolAssetService.create(payload);
        setNotice("Alet kartı oluşturuldu.");
      }

      setShowForm(false);
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  const filterKey = `${companyId}|${status}|${search}`;

  /*
   * SÜTUNLAR VERİ OLARAK (F4k). Eylem sütunu `actions` ve `openEdit`
   * üzerine kapandığı için dizi belleğe ALINMIYOR (F4b desen kararı).
   */
  const assetColumns: DataTableColumn<(typeof assets)[number]>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    {
      key: "alet",
      header: "Alet",
      value: (row) =>
        [row.name, row.brand, row.model].filter(Boolean).join(" · "),
      render: (row) => (
        <>
          {row.name}
          {(row.brand || row.model) && (
            <small>{[row.brand, row.model].filter(Boolean).join(" ")}</small>
          )}
        </>
      ),
    },
    { key: "seri", header: "Seri No", value: (row) => row.serialNumber ?? "—" },
    {
      key: "durum",
      header: "Durum",
      value: (row) => row.statusName,
      render: (row) => (
        <span className={statusClass(row.status)}>{row.statusName}</span>
      ),
    },
    {
      key: "zimmet",
      header: "Zimmetli",
      value: (row) =>
        [row.assignedPersonnelName ?? "—", row.siteName].filter(Boolean).join(" · "),
      render: (row) => (
        <>
          {row.assignedPersonnelName ?? "—"}
          {row.siteName && <small>{row.siteName}</small>}
        </>
      ),
    },
    {
      key: "garanti",
      header: "Garanti",
      value: (row) => {
        if (!row.warrantyEndDate) return "—";

        const active = bugun !== null && new Date(row.warrantyEndDate) >= bugun;

        return `${dateFormat.format(new Date(row.warrantyEndDate))} · ${
          active ? "sürüyor" : "doldu"
        }`;
      },
      render: (row) => {
        if (!row.warrantyEndDate) return "—";

        const active = bugun !== null && new Date(row.warrantyEndDate) >= bugun;

        return (
          <>
            {dateFormat.format(new Date(row.warrantyEndDate))}
            <small>{active ? "sürüyor" : "doldu"}</small>
          </>
        );
      },
    },
    {
      key: "bedel",
      header: "Bedel",
      numeric: true,
      value: (row) => (row.purchaseCost != null ? amount(row.purchaseCost) : "—"),
      // Demirbaş yatırımının toplamı: listenin tamamından.
      footer: (rows) =>
        amount(rows.reduce((sum, row) => sum + (row.purchaseCost ?? 0), 0)),
    },
    {
      key: "islem",
      header: "",
      value: () => "",
      render: (row) => (
        <div style={{ display: "flex", gap: 6 }}>
          <Link className="erp-secondary-button" href={`/demirbas/${row.id}`}>
            Kart
          </Link>
          {actions.can("edit") && (
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => openEdit(row)}
            >
              Düzenle
            </button>
          )}
        </div>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Demirbaş / El Aletleri"
      description="Alet kartları, zimmet durumu ve servis geçmişi"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-page-toolbar">
        {/* Zimmet ve iade işlemleri başka kullanıcılarca yapılıyor. */}
        <Button variant="secondary" disabled={saving} onClick={() => setReloadToken((value) => value + 1)}>Yenile</Button>

        <div
          style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}
        >
          {companies.length > 1 && (
            <label>
              <span style={{ display: "block", fontSize: 11 }}>Şirket</span>
              <select
                value={companyId}
                onChange={(e) => setCompanyId(e.target.value)}
              >
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.name}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label>
            <span style={{ display: "block", fontSize: 11 }}>Durum</span>
            <select
              value={status}
              onChange={(e) =>
                setStatus(e.target.value === "" ? "" : Number(e.target.value))
              }
            >
              <option value="">Tümü</option>
              {TOOL_ASSET_STATUSES.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span style={{ display: "block", fontSize: 11 }}>Ara</span>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="kod, ad, seri no"
            />
          </label>
        </div>

        <div style={{ display: "flex", gap: 8 }}>
          <Link className="erp-secondary-button" href="/demirbas/servis">
            Servis Talepleri
          </Link>
          {actions.can("create") && (
            <button type="button" className="erp-primary-button" onClick={openCreate}>
              Yeni Alet
            </button>
          )}
        </div>
      </div>

      {showForm && (
        <section className="erp-panel" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>
            {editingId ? "Alet Kartını Düzenle" : "Yeni Alet Kartı"}
          </h2>

          <form
            onSubmit={handleSubmit}
            style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}
          >
            <label>
              <span style={{ display: "block", fontSize: 11 }}>Kod *</span>
              <input
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                placeholder="ALT-001"
              />
            </label>

            <label style={{ flex: "1 1 220px" }}>
              <span style={{ display: "block", fontSize: 11 }}>Ad *</span>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="Darbeli matkap"
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Marka</span>
              <input
                value={form.brand ?? ""}
                onChange={(e) => setForm({ ...form, brand: e.target.value })}
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Model</span>
              <input
                value={form.model ?? ""}
                onChange={(e) => setForm({ ...form, model: e.target.value })}
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Seri No</span>
              <input
                value={form.serialNumber ?? ""}
                onChange={(e) => setForm({ ...form, serialNumber: e.target.value })}
              />
              <small className="rw-value-muted" style={{ display: "block" }}>
                Girilirse benzersiz olmalı
              </small>
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Alım tarihi</span>
              <input
                type="date"
                value={form.purchaseDate ?? ""}
                onChange={(e) =>
                  setForm({ ...form, purchaseDate: e.target.value || null })
                }
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Alım bedeli</span>
              <input
                type="number"
                step="0.01"
                value={form.purchaseCost ?? ""}
                onChange={(e) =>
                  setForm({
                    ...form,
                    purchaseCost: e.target.value ? Number(e.target.value) : null,
                  })
                }
              />
            </label>

            <label>
              <span style={{ display: "block", fontSize: 11 }}>Garanti bitişi</span>
              <input
                type="date"
                value={form.warrantyEndDate ?? ""}
                onChange={(e) =>
                  setForm({ ...form, warrantyEndDate: e.target.value || null })
                }
              />
            </label>

            <div style={{ display: "flex", gap: 8 }}>
              {(editingId ? actions.can("edit") : actions.can("create")) && (
                <button type="submit" className="erp-primary-button" disabled={saving}>
                  {saving ? "Kaydediliyor..." : "Kaydet"}
                </button>
              )}
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setShowForm(false)}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </section>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Aletler</h2>
          <small>{assets.length} kayıt</small>
        </div>

        {assets.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Alet kaydı yok</strong>
            <p>
              Demirbaş ve el aletleri sarftan ayrı tutulur; buraya
              eklediğiniz her aletin servis geçmişi ve garanti takibi
              kendi kartında birikir.
            </p>
          </div>
        ) : (
          <DataTable
            rows={assets}
            columns={assetColumns}
            rowKey={(row) => row.id}
            title="Demirbaş ve Aletler"
            resetKey={filterKey}
          />
        )}
      </div>
    </ErpShell>
  );
}
