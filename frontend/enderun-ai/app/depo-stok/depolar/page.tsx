"use client";

import { useCallback, useEffect, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { usePermissions } from "@/lib/use-permissions";
import { branchService, type BranchListItem } from "@/services/branch.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import { Button } from "@/components/ui";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";
import {
  warehouseService,
  WAREHOUSE_TYPES,
  type WarehouseListItem,
} from "@/services/warehouse.service";

function typeLabel(type: number) {
  return WAREHOUSE_TYPES.find((option) => option.value === type)?.label ?? "—";
}

export default function WarehousesPage() {
  const { has } = usePermissions();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);

  const [warehouses, setWarehouses] = useState<WarehouseListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const [branchId, setBranchId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [projectSiteId, setProjectSiteId] = useState("");
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [type, setType] = useState("0");
  const [address, setAddress] = useState("");
  const [isActive, setIsActive] = useState(true);

  const canManage = has("inventory.manage");

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
      const [warehouseList, branchList, projectList] = await Promise.all([
        warehouseService.getAll({ companyId, includeInactive: true }),
        branchService.getAll(companyId).catch(() => []),
        projectService.getAll(companyId).catch(() => []),
      ]);

      setWarehouses(warehouseList);
      setBranches(branchList);
      setProjects(projectList);
    } catch (err) {
      setWarehouses([]);
      setError(err instanceof Error ? err.message : "Depolar alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 150);
    return () => window.clearTimeout(timer);
  }, [load]);

  const loadSites = useCallback(async (selectedProjectId: string) => {
    if (!selectedProjectId) {
      setSites([]);
      return;
    }

    try {
      setSites(await projectSiteService.getAll(selectedProjectId));
    } catch {
      setSites([]);
    }
  }, []);

  function resetForm() {
    setEditingId(null);
    setBranchId(branches.find((branch) => branch.isHeadOffice)?.id ?? "");
    setProjectId("");
    setProjectSiteId("");
    setCode("");
    setName("");
    setType("0");
    setAddress("");
    setIsActive(true);
    setSites([]);
  }

  async function startEdit(warehouse: WarehouseListItem) {
    setEditingId(warehouse.id);
    setBranchId(warehouse.branchId);
    setProjectId(warehouse.projectId ?? "");
    await loadSites(warehouse.projectId ?? "");
    setProjectSiteId(warehouse.projectSiteId ?? "");
    setCode(warehouse.code);
    setName(warehouse.name);
    setType(String(warehouse.type));
    setAddress(warehouse.address ?? "");
    setIsActive(warehouse.isActive);
    setFormOpen(true);
    setNotice("");
  }

  const validationErrors: string[] = [];
  if (formOpen) {
    if (!branchId) validationErrors.push("Şube seçin.");
    if (!editingId && !code.trim()) validationErrors.push("Depo kodu girin.");
    if (!name.trim()) validationErrors.push("Depo adı girin.");
    if (projectSiteId && !projectId) {
      validationErrors.push("Şantiye seçildiyse proje de seçilmeli.");
    }
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    try {
      if (editingId) {
        await warehouseService.update(editingId, {
          branchId,
          projectId: projectId || null,
          projectSiteId: projectSiteId || null,
          name: name.trim(),
          type: Number(type),
          address: address.trim() || null,
          isActive,
        });
        setNotice("Depo güncellendi.");
      } else {
        await warehouseService.create({
          companyId,
          branchId,
          projectId: projectId || null,
          projectSiteId: projectSiteId || null,
          code: code.trim(),
          name: name.trim(),
          type: Number(type),
          address: address.trim() || null,
        });
        setNotice("Depo oluşturuldu.");
      }

      setFormOpen(false);
      resetForm();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Depo kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  const totalValue = warehouses.reduce(
    (sum, warehouse) => sum + warehouse.stockValue,
    0
  );


  /*
   * SÜTUNLAR YETKİYE BAĞLI. "Düzenle" sütunu yalnız yetkiliye
   * gösterilir; dışa aktarmada da yer kaplamaz.
   */
  /*
   * SÜTUNLAR HER RENDER'DA KURULUYOR — bilerek. `useMemo` ile
   * belleğe almak `startEdit`i bağımlılıktan çıkarmayı gerektiriyordu;
   * o da bayat kapanış demek.
   */
  const columns: DataTableColumn<WarehouseListItem>[] = (() => {
    const base: DataTableColumn<WarehouseListItem>[] = [
      {
        key: "kod",
        header: "Kod",
        value: (warehouse) => warehouse.code,
        render: (warehouse) => <strong>{warehouse.code}</strong>,
      },
      {
        key: "depo",
        header: "Depo",
        value: (warehouse) =>
          [warehouse.name, warehouse.address].filter(Boolean).join(" — "),
        render: (warehouse) => (
          <>
            {warehouse.name}
            {warehouse.address && <small>{warehouse.address}</small>}
          </>
        ),
      },
      {
        key: "tip",
        header: "Tip",
        value: (warehouse: WarehouseListItem) => typeLabel(warehouse.type),
      },
      {
        key: "yer",
        header: "Proje / Şantiye",
        value: (warehouse) =>
          [warehouse.projectCode ?? "Merkez", warehouse.siteName]
            .filter(Boolean)
            .join(" / "),
        render: (warehouse) => (
          <>
            {warehouse.projectCode ?? "Merkez"}
            {warehouse.siteName && <small>{warehouse.siteName}</small>}
          </>
        ),
      },
      {
        key: "kalem",
        header: "Kalem",
        numeric: true,
        value: (warehouse) => warehouse.stockLineCount,
      },
      {
        key: "deger",
        header: "Stok Değeri",
        numeric: true,
        value: (warehouse) => warehouse.stockValue,
        render: (warehouse) => money(warehouse.stockValue),
      },
      {
        key: "durum",
        header: "Durum",
        value: (warehouse) => (warehouse.isActive ? "Aktif" : "Kapalı"),
        render: (warehouse) => (
          <span
            className={`erp-status ${warehouse.isActive ? "green" : "gray"}`}
          >
            {warehouse.isActive ? "Aktif" : "Kapalı"}
          </span>
        ),
      },
    ];

    if (!canManage) return base;

    return [
      ...base,
      {
        key: "duzenle",
        header: "",
        value: () => "",
        render: (warehouse) => (
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => void startEdit(warehouse)}
          >
            Düzenle
          </button>
        ),
      },
    ];
  })();


  return (
    <ErpShell
      design="redwood"
      title="Depolar"
      description="Merkez, şantiye, araç ve geçici depoların tanımı"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

      <div className="erp-page-toolbar">
        <div>
          <strong>{warehouses.length} depo</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Toplam stok değeri: {money(totalValue)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => {
              setCompanyId(event.target.value);
              setFormOpen(false);
            }}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          {canManage && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => {
                resetForm();
                setFormOpen(true);
                setNotice("");
              }}
            >
              + Yeni Depo
            </button>
          )}
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {formOpen && (
        <form className="erp-form-card" onSubmit={submit}>
          <div className="erp-form-header">
            <h2>{editingId ? "Depoyu Düzenle" : "Yeni Depo"}</h2>
            <p>
              Şantiye deposu için proje ve şantiye seçin; merkez deposunda
              boş bırakın.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Depo Kodu *</span>
              <input
                type="text"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                disabled={Boolean(editingId)}
                placeholder="Örn. MRK-01"
              />
              {editingId && (
                <small>
                  Kod değiştirilemez: hareket belgelerinde geçiyor.
                </small>
              )}
            </label>

            <label>
              <span>Depo Adı *</span>
              <input
                type="text"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </label>

            <label>
              <span>Depo Tipi *</span>
              <select
                value={type}
                onChange={(event) => setType(event.target.value)}
              >
                {WAREHOUSE_TYPES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şube *</span>
              <select
                value={branchId}
                onChange={(event) => setBranchId(event.target.value)}
              >
                <option value="">Şube seçin</option>
                {branches.map((branch) => (
                  <option key={branch.id} value={branch.id}>
                    {branch.isHeadOffice ? `${branch.name} (merkez)` : branch.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Proje (ops.)</span>
              <select
                value={projectId}
                onChange={(event) => {
                  setProjectId(event.target.value);
                  setProjectSiteId("");
                  void loadSites(event.target.value);
                }}
              >
                <option value="">Projesiz (merkez)</option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} — {project.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şantiye (ops.)</span>
              <select
                value={projectSiteId}
                onChange={(event) => setProjectSiteId(event.target.value)}
                disabled={!projectId}
              >
                <option value="">Proje geneli</option>
                {sites.map((site) => (
                  <option key={site.id} value={site.id}>
                    {site.code} — {site.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="span-2">
              <span>Adres</span>
              <input
                type="text"
                value={address}
                onChange={(event) => setAddress(event.target.value)}
              />
            </label>

            {editingId && (
              <label className="erp-check-label">
                <input
                  type="checkbox"
                  checked={isActive}
                  onChange={(event) => setIsActive(event.target.checked)}
                />
                <span>Depo aktif</span>
              </label>
            )}
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => {
                setFormOpen(false);
                resetForm();
              }}
            >
              Vazgeç
            </button>

            <button type="submit" className="erp-primary-button" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Depo Listesi</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : warehouses.length === 0 ? (
          <div className="erp-empty-state">
            <p>Bu şirkette tanımlı depo yok.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <DataTable
              rows={warehouses}
              columns={columns}
              rowKey={(warehouse) => warehouse.id}
              title="Depolar"
              emptyText="Henüz depo tanımlanmamış."
              /* FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER. Sayfalama F4'te eklendi
                 ama bu bağ kurulmamıştı: kullanıcı 7. sayfadayken filtreyi
                 daraltınca son sayfada kalıyordu. */
              resetKey={`${companyId}|${projectId}|${type}`}
            />
          </div>
        )}
      </div>
    </ErpShell>
  );
}
