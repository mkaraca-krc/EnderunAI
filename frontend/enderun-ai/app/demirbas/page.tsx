"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
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
          <button type="button" className="erp-primary-button" onClick={openCreate}>
            Yeni Alet
          </button>
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
              <button type="submit" className="erp-primary-button" disabled={saving}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
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
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Alet</th>
                  <th>Seri No</th>
                  <th>Durum</th>
                  <th>Zimmetli</th>
                  <th>Garanti</th>
                  <th>Bedel</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {assets.map((asset) => {
                  const warrantyActive =
                    asset.warrantyEndDate != null &&
                    new Date(asset.warrantyEndDate) >= new Date();

                  return (
                    <tr key={asset.id}>
                      <td>{asset.code}</td>
                      <td>
                        {asset.name}
                        {(asset.brand || asset.model) && (
                          <small>
                            {[asset.brand, asset.model].filter(Boolean).join(" ")}
                          </small>
                        )}
                      </td>
                      <td>{asset.serialNumber ?? "—"}</td>
                      <td>
                        <span className={statusClass(asset.status)}>
                          {asset.statusName}
                        </span>
                      </td>
                      <td>
                        {asset.assignedPersonnelName ?? "—"}
                        {asset.siteName && <small>{asset.siteName}</small>}
                      </td>
                      <td>
                        {asset.warrantyEndDate ? (
                          <>
                            {dateFormat.format(new Date(asset.warrantyEndDate))}
                            <small>{warrantyActive ? "sürüyor" : "doldu"}</small>
                          </>
                        ) : (
                          "—"
                        )}
                      </td>
                      <td>
                        {asset.purchaseCost != null
                          ? amount(asset.purchaseCost)
                          : "—"}
                      </td>
                      <td>
                        <div style={{ display: "flex", gap: 6 }}>
                          <Link
                            className="erp-secondary-button"
                            href={`/demirbas/${asset.id}`}
                          >
                            Kart
                          </Link>
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => openEdit(asset)}
                          >
                            Düzenle
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
