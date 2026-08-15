"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import {
  projectSiteService,
  type AssignablePersonnelItem,
  type ProjectSiteDetail,
} from "@/services/project-site.service";
import {
  dailyReportService,
  type DailyReportDetail,
  type DailyReportListItem,
  type DailyReportWorkItem,
  type SummaryQuickPick,
  type SummaryQuickPickItem,
} from "@/services/daily-report.service";

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

const emptyReportForm = {
  weatherCondition: "",
  engineerCount: 0,
  foremanCount: 0,
  craftsmanCount: 0,
  workerCount: 0,
  otherCount: 0,
  notes: "",
};

function DailyReportTab({ siteId }: { siteId: string }) {
  const [list, setList] = useState<DailyReportListItem[]>([]);
  const [listLoading, setListLoading] = useState(true);
  const [selectedDate, setSelectedDate] = useState(todayIso());
  const [detail, setDetail] = useState<DailyReportDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [form, setForm] = useState(emptyReportForm);
  const [workItems, setWorkItems] = useState<DailyReportWorkItem[]>([]);
  const [newWorkItem, setNewWorkItem] = useState({ description: "", quantity: "", unit: "" });

  // Hızlı seçim: icmal kalemleri, arama ve sık kullanılanlar.
  const [selectedBoqItemId, setSelectedBoqItemId] = useState("");
  const [quickPick, setQuickPick] = useState<SummaryQuickPick | null>(null);
  const [quickPickOpen, setQuickPickOpen] = useState(false);
  const [quickPickSearch, setQuickPickSearch] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [approveOpen, setApproveOpen] = useState(false);
  const [uploadingPhoto, setUploadingPhoto] = useState(false);
  const [photoVisible, setPhotoVisible] = useState(false);
  const [photoCaption, setPhotoCaption] = useState("");

  async function loadList() {
    setListLoading(true);
    try {
      setList(await dailyReportService.getAll(siteId));
    } catch {
      setList([]);
    } finally {
      setListLoading(false);
    }
  }

  async function loadDetailForDate(date: string) {
    setDetailLoading(true);
    setError("");
    setNotice("");

    try {
      const found = await dailyReportService.getByDate(siteId, date);
      setDetail(found);
      setForm({
        weatherCondition: found.weatherCondition ?? "",
        engineerCount: found.engineerCount,
        foremanCount: found.foremanCount,
        craftsmanCount: found.craftsmanCount,
        workerCount: found.workerCount,
        otherCount: found.otherCount,
        notes: found.notes ?? "",
      });
      setWorkItems(found.workItems);
    } catch {
      setDetail(null);
      setWorkItems([]);

      try {
        const suggested = await dailyReportService.getSuggestedHeadcount(siteId, date);
        setForm({ ...emptyReportForm, ...suggested });
      } catch {
        setForm(emptyReportForm);
      }
    } finally {
      setDetailLoading(false);
    }
  }

  useEffect(() => {
    void loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [siteId]);

  useEffect(() => {
    void loadDetailForDate(selectedDate);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [siteId, selectedDate]);

  // İcmal kalemleri: arama yazıldıkça süzülür. Projede icmal yoksa uç
  // boş döner ve ekran serbest metne düşer.
  useEffect(() => {
    if (!siteId) return;

    const timer = window.setTimeout(() => {
      void dailyReportService
        .getSummaryItems(siteId, quickPickSearch.trim() || undefined)
        .then(setQuickPick)
        .catch(() => setQuickPick(null));
    }, 300);

    return () => window.clearTimeout(timer);
  }, [siteId, quickPickSearch]);

  function updateForm<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function addWorkItem() {
    if (!newWorkItem.description.trim()) return;

    setWorkItems((current) => [
      ...current,
      {
        // İcmalden seçildiyse bağ taşınır; serbest metinde boş kalır ve
        // kalem yine kaydedilir.
        projectBoqItemId: selectedBoqItemId || null,
        description: newWorkItem.description.trim(),
        quantity: newWorkItem.quantity ? Number(newWorkItem.quantity) : null,
        unit: newWorkItem.unit || null,
      },
    ]);

    setNewWorkItem({ description: "", quantity: "", unit: "" });
    setSelectedBoqItemId("");
  }

  /** Hızlı seçimden gelen kalem: tanım ve birim karttan dolar. */
  function pickSummaryItem(item: SummaryQuickPickItem) {
    setSelectedBoqItemId(item.id);
    setNewWorkItem((current) => ({
      ...current,
      description: `${item.positionCode} — ${item.description}`,
      unit: item.unit,
    }));
    setQuickPickOpen(false);
  }

  function removeWorkItem(index: number) {
    setWorkItems((current) => current.filter((_, i) => i !== index));
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setNotice("");

    const payload = {
      reportDate: selectedDate,
      weatherCondition: form.weatherCondition || null,
      engineerCount: Number(form.engineerCount) || 0,
      foremanCount: Number(form.foremanCount) || 0,
      craftsmanCount: Number(form.craftsmanCount) || 0,
      workerCount: Number(form.workerCount) || 0,
      otherCount: Number(form.otherCount) || 0,
      notes: form.notes || null,
      workItems,
    };

    try {
      if (detail) {
        await dailyReportService.update(siteId, detail.id, payload);
      } else {
        await dailyReportService.create(siteId, payload);
      }

      setNotice("Günlük rapor kaydedildi.");
      await loadDetailForDate(selectedDate);
      await loadList();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Günlük rapor kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function uploadPhoto(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || !detail) return;

    setUploadingPhoto(true);
    setError("");

    try {
      await dailyReportService.uploadPhoto(siteId, detail.id, file, photoVisible, photoCaption || undefined);
      setPhotoCaption("");
      setPhotoVisible(false);
      await loadDetailForDate(selectedDate);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fotoğraf yüklenemedi.");
    } finally {
      setUploadingPhoto(false);
    }
  }

  async function togglePhotoVisibility(photoId: string, current: boolean) {
    if (!detail) return;
    try {
      await dailyReportService.setPhotoVisibility(siteId, detail.id, photoId, !current);
      await loadDetailForDate(selectedDate);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Görünürlük güncellenemedi.");
    }
  }

  async function deletePhoto(photoId: string) {
    if (!detail) return;
    try {
      await dailyReportService.deletePhoto(siteId, detail.id, photoId);
      await loadDetailForDate(selectedDate);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fotoğraf silinemedi.");
    }
  }

  /**
   * Günlük raporu onayla.
   *
   * GERİ ALINAMAZ ve rapor işverene açılıyor; onay bu yüzden ayrı bir
   * adım. Tarayıcı diyaloğu bu sonucu okunur biçimde anlatamıyordu —
   * tek satır düz metin, vurgu yok, iptal ile onay aynı görünüyordu.
   */
  async function approveReport() {
    if (!detail) return;

    setApproveOpen(false);
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await dailyReportService.approve(siteId, detail.id);
      setNotice("Rapor onaylandı; işveren portalına düştü.");
      await loadDetailForDate(selectedDate);
      await loadList();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Rapor onaylanamadı.");
    } finally {
      setSaving(false);
    }
  }

  const isApproved = detail?.status === 1;

  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>Günlük Rapor</h2>
          <p>Tarih seçerek o güne ait şantiye raporunu girin veya düzenleyin</p>
        </div>

        <div className="erp-actions" style={{ alignItems: "center" }}>
          {detail && (
            <span className={`erp-status ${isApproved ? "green" : "gray"}`}>
              {isApproved ? "Onaylandı" : "Taslak"}
            </span>
          )}
          {/* Günlük rapor sahadan giriliyor; ekranı açık bırakan
              kullanıcı yeni girilen raporu göremiyordu. */}
          <Button variant="secondary" disabled={detailLoading || listLoading} onClick={() => {
              void loadList();
              void loadDetailForDate(selectedDate);
            }}>Yenile</Button>

          <input
            className="erp-input"
            type="date"
            value={selectedDate}
            onChange={(e) => setSelectedDate(e.target.value)}
          />
          {detail && !isApproved && (
            <button
              type="button"
              className="erp-button"
              disabled={saving}
              onClick={() => setApproveOpen(true)}
            >
              Onayla
            </button>
          )}
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {detailLoading ? (
        <div className="erp-loading">Yükleniyor...</div>
      ) : (
        <form className="erp-form-card" onSubmit={save}>
          <fieldset disabled={isApproved} style={{ border: "none", padding: 0, margin: 0 }}>
          <div className="erp-form-grid">
            <label>
              <span>Hava Durumu</span>
              <input
                className="erp-input"
                value={form.weatherCondition}
                onChange={(e) => updateForm("weatherCondition", e.target.value)}
              />
            </label>
            <label>
              <span>Mühendis</span>
              <input
                className="erp-input"
                type="number"
                min={0}
                value={form.engineerCount}
                onChange={(e) => updateForm("engineerCount", Number(e.target.value))}
              />
            </label>
            <label>
              <span>Formen</span>
              <input
                className="erp-input"
                type="number"
                min={0}
                value={form.foremanCount}
                onChange={(e) => updateForm("foremanCount", Number(e.target.value))}
              />
            </label>
            <label>
              <span>Usta</span>
              <input
                className="erp-input"
                type="number"
                min={0}
                value={form.craftsmanCount}
                onChange={(e) => updateForm("craftsmanCount", Number(e.target.value))}
              />
            </label>
            <label>
              <span>İşçi</span>
              <input
                className="erp-input"
                type="number"
                min={0}
                value={form.workerCount}
                onChange={(e) => updateForm("workerCount", Number(e.target.value))}
              />
            </label>
            <label>
              <span>Diğer</span>
              <input
                className="erp-input"
                type="number"
                min={0}
                value={form.otherCount}
                onChange={(e) => updateForm("otherCount", Number(e.target.value))}
              />
            </label>
          </div>

          <label>
            <span>Notlar</span>
            <textarea
              className="erp-input"
              rows={3}
              value={form.notes}
              onChange={(e) => updateForm("notes", e.target.value)}
            />
          </label>

          <div className="erp-panel-header">
            <h3>İmalat Kalemleri</h3>
          </div>

          {workItems.length > 0 && (
            <div className="erp-project-list">
              {workItems.map((item, index) => (
                <div className="erp-project-list-item" key={index}>
                  <div>
                    <strong>{item.description}</strong>
                    <span>
                      {item.quantity ?? "—"} {item.unit ?? ""}
                      {item.projectBoqItemId ? " · icmale bağlı" : " · serbest"}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="erp-button secondary"
                    onClick={() => removeWorkItem(index)}
                  >
                    Kaldır
                  </button>
                </div>
              ))}
            </div>
          )}

          {quickPick?.hasContractSummary && (
            <div className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <div>
                  <h3>İcmalden Seç</h3>
                  <p>
                    Onaylı raporlardaki miktarlar seçilen kaleme birikir ve
                    hakediş önerisini besler.
                  </p>
                </div>

                <button
                  type="button"
                  className="erp-button secondary"
                  onClick={() => setQuickPickOpen((open) => !open)}
                >
                  {quickPickOpen ? "Kapat" : "Kalem Ara"}
                </button>
              </div>

              {quickPick.frequent.length > 0 && (
                <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                  {quickPick.frequent.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      className="erp-button secondary"
                      onClick={() => pickSummaryItem(item)}
                    >
                      {item.positionCode} · {item.description}
                    </button>
                  ))}
                </div>
              )}

              {quickPickOpen && (
                <div className="erp-mt">
                  <input
                    className="erp-input"
                    type="search"
                    value={quickPickSearch}
                    onChange={(event) => setQuickPickSearch(event.target.value)}
                    placeholder="Poz no veya tanım ara"
                  />

                  <div className="erp-project-list erp-mt">
                    {quickPick.items.length === 0 ? (
                      <div className="erp-empty-state">Kalem bulunamadı.</div>
                    ) : (
                      quickPick.items.slice(0, 40).map((item) => (
                        <div className="erp-project-list-item" key={item.id}>
                          <div>
                            <strong>
                              {item.positionCode} — {item.description}
                            </strong>
                            <span>
                              {item.sectionName ?? "Kısımsız"} · sözleşme{" "}
                              {item.contractQuantity} {item.unit}
                            </span>
                          </div>
                          <button
                            type="button"
                            className="erp-button secondary"
                            onClick={() => pickSummaryItem(item)}
                          >
                            Seç
                          </button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              )}
            </div>
          )}

          {selectedBoqItemId && (
            <div className="erp-alert">
              İcmal kalemi seçildi. Miktarı girip &quot;Kalem Ekle&quot;ye
              basın. Seçimi kaldırmak için açıklamayı elle değiştirin ve{" "}
              <button
                type="button"
                className="erp-button secondary"
                onClick={() => setSelectedBoqItemId("")}
              >
                bağı kaldırın
              </button>
              .
            </div>
          )}

          <div className="erp-form-grid">
            <label>
              <span>Açıklama</span>
              <input
                className="erp-input"
                value={newWorkItem.description}
                onChange={(e) =>
                  setNewWorkItem((current) => ({ ...current, description: e.target.value }))
                }
              />
            </label>
            <label>
              <span>Miktar</span>
              <input
                className="erp-input"
                type="number"
                value={newWorkItem.quantity}
                onChange={(e) =>
                  setNewWorkItem((current) => ({ ...current, quantity: e.target.value }))
                }
              />
            </label>
            <label>
              <span>Birim</span>
              <input
                className="erp-input"
                value={newWorkItem.unit}
                onChange={(e) =>
                  setNewWorkItem((current) => ({ ...current, unit: e.target.value }))
                }
              />
            </label>
          </div>

          <div className="erp-actions">
            <button type="button" className="erp-button secondary" onClick={addWorkItem}>
              + Kalem Ekle
            </button>
          </div>

          <div className="erp-actions">
            <button type="submit" disabled={saving}>
              {saving ? "Kaydediliyor..." : detail ? "Raporu Güncelle" : "Raporu Kaydet"}
            </button>
          </div>
          </fieldset>
        </form>
      )}

      {detail && (
        <div className="erp-mt">
          <div className="erp-panel-header">
            <h3>Fotoğraflar</h3>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Açıklama (opsiyonel)</span>
              <input
                className="erp-input"
                value={photoCaption}
                onChange={(e) => setPhotoCaption(e.target.value)}
              />
            </label>
            <label className="erp-checkbox-label">
              <input
                type="checkbox"
                checked={photoVisible}
                onChange={(e) => setPhotoVisible(e.target.checked)}
              />
              <span>İşverene görünür</span>
            </label>
            <label>
              <span>Fotoğraf Seç</span>
              <input type="file" accept="image/*" onChange={uploadPhoto} disabled={uploadingPhoto} />
            </label>
          </div>

          {detail.photos.length === 0 ? (
            <div className="erp-empty-state">Henüz fotoğraf eklenmedi.</div>
          ) : (
            <div className="erp-project-list">
              {detail.photos.map((photo) => (
                <div className="erp-project-list-item" key={photo.id}>
                  <div>
                    <strong>{photo.caption || photo.originalName}</strong>
                    <span className={`erp-status ${photo.isVisibleToEmployer ? "green" : "gray"}`}>
                      {photo.isVisibleToEmployer ? "İşverene görünür" : "İşverene kapalı"}
                    </span>
                  </div>
                  <div className="erp-actions">
                    <button
                      type="button"
                      className="erp-button secondary"
                      onClick={() => togglePhotoVisibility(photo.id, photo.isVisibleToEmployer)}
                    >
                      {photo.isVisibleToEmployer ? "Gizle" : "Göster"}
                    </button>
                    <button
                      type="button"
                      className="erp-button secondary"
                      onClick={() => deletePhoto(photo.id)}
                    >
                      Sil
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      <div className="erp-mt">
        <div className="erp-panel-header">
          <h3>Son Raporlar</h3>
        </div>

        {listLoading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : list.length === 0 ? (
          <div className="erp-empty-state">Henüz günlük rapor girilmedi.</div>
        ) : (
          <div className="erp-project-list">
            {list.map((item) => (
              <button
                type="button"
                key={item.id}
                className="erp-project-list-item"
                onClick={() => setSelectedDate(item.reportDate.slice(0, 10))}
              >
                <div>
                  <strong>{formatDate(item.reportDate)}</strong>
                  <span>
                    {item.totalHeadcount} personel · {item.workItemCount} imalat kalemi ·{" "}
                    {item.photoCount} fotoğraf
                  </span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>

      <ConfirmDialog
        open={approveOpen}
        title="Günlük Raporu Onayla"
        description={
          `${selectedDate} tarihli rapor onaylanacak. Onaydan sonra ` +
          "rapor DÜZENLENEMEZ ve işveren portalında görünür hâle gelir. " +
          "Bu işlem geri alınamaz."
        }
        confirmLabel="Raporu Onayla"
        busy={saving}
        error={error}
        onCancel={() => setApproveOpen(false)}
        onConfirm={() => void approveReport()}
      />
    </section>
  );
}

export default function ProjectSiteDetailPage() {
  const params = useParams<{ id: string; siteId: string }>();

  const [site, setSite] = useState<ProjectSiteDetail | null>(null);
  const [assignable, setAssignable] = useState<AssignablePersonnelItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [creatingWarehouse, setCreatingWarehouse] = useState(false);
  const [warehouseError, setWarehouseError] = useState("");
  const [warehouseForm, setWarehouseForm] = useState({
    code: "",
    name: "",
    address: "",
  });

  const [assigning, setAssigning] = useState(false);
  const [assignError, setAssignError] = useState("");
  const [form, setForm] = useState({
    personnelId: "",
    role: "",
    startDate: new Date().toISOString().slice(0, 10),
  });

  async function load() {
    setLoading(true);
    setError("");

    try {
      const [siteResult, assignableResult] = await Promise.all([
        projectSiteService.getById(params.siteId),
        projectSiteService.getAssignablePersonnel(params.siteId),
      ]);

      setSite(siteResult);
      setAssignable(assignableResult);
    } catch (err) {
      setSite(null);
      setError(
        err instanceof Error ? err.message : "Şantiye yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (params.siteId) {
      void load();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params.siteId]);

  function update(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function updateWarehouseForm(
    key: keyof typeof warehouseForm,
    value: string
  ) {
    setWarehouseForm((current) => ({ ...current, [key]: value }));
  }

  async function createWarehouse(event: React.FormEvent) {
    event.preventDefault();

    setCreatingWarehouse(true);
    setWarehouseError("");

    try {
      await projectSiteService.createWarehouse(params.siteId, {
        code: warehouseForm.code,
        name: warehouseForm.name,
        address: warehouseForm.address || null,
      });

      setWarehouseForm({ code: "", name: "", address: "" });

      await load();
    } catch (err) {
      setWarehouseError(
        err instanceof Error ? err.message : "Depo oluşturulamadı."
      );
    } finally {
      setCreatingWarehouse(false);
    }
  }

  async function assign(event: React.FormEvent) {
    event.preventDefault();

    setAssigning(true);
    setAssignError("");

    try {
      await projectSiteService.assignPersonnel(params.siteId, {
        personnelId: form.personnelId,
        startDate: form.startDate,
        role: form.role || null,
      });

      setForm({
        personnelId: "",
        role: "",
        startDate: new Date().toISOString().slice(0, 10),
      });

      await load();
    } catch (err) {
      setAssignError(
        err instanceof Error ? err.message : "Personel atanamadı."
      );
    } finally {
      setAssigning(false);
    }
  }

  async function closeAssignment(assignmentId: string) {
    try {
      await projectSiteService.closeAssignment(assignmentId);
      await load();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Atama kapatılamadı."
      );
    }
  }

  return (
    <ErpShell
      design="redwood"
      title={site?.name ?? "Şantiye Detayı"}
      description={
        site ? `${site.projectCode} · ${site.projectName}` : "Şantiye bilgileri yükleniyor"
      }
    >
      <div className="erp-project-breadcrumb">
        <Link href="/projeler">Projeler</Link>
        <span>›</span>
        <Link href={`/projeler/${params.id}`}>{site?.projectName ?? "Proje"}</Link>
        <span>›</span>
        <strong>{site?.name ?? "Şantiye"}</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Şantiye yükleniyor...</div>
      ) : !site ? (
        <div className="erp-panel erp-empty-state">
          <strong>Şantiye bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel" id="genel">
            <div className="erp-panel-header">
              <div>
                <h2>Şantiye Bilgileri</h2>
                <p>{site.code}</p>
              </div>

              <span className={`erp-status ${site.isActive ? "green" : "gray"}`}>
                {site.isActive ? "Aktif" : "Pasif"}
              </span>
            </div>

            <div className="erp-detail-grid">
              <div><span>Şantiye Kodu</span><strong>{site.code}</strong></div>
              <div><span>Şantiye Adı</span><strong>{site.name}</strong></div>
              <div><span>Konum</span><strong>{site.location || "—"}</strong></div>
              <div><span>Notlar</span><strong>{site.notes || "—"}</strong></div>
              <div><span>Oluşturulma</span><strong>{formatDate(site.createdAtUtc)}</strong></div>
              <div><span>Depo Sayısı</span><strong>{site.warehouses.length}</strong></div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Şantiye Deposu Ekle</h2>
                <p>Bu şantiyeye özel bir depo tanımlayın</p>
              </div>
            </div>

            {warehouseError && <div className="erp-alert error">{warehouseError}</div>}

            <form className="erp-form-card" onSubmit={createWarehouse}>
              <div className="erp-form-grid">
                <label>
                  <span>Depo Kodu *</span>
                  <input
                    className="erp-input"
                    required
                    value={warehouseForm.code}
                    onChange={(e) => updateWarehouseForm("code", e.target.value)}
                  />
                </label>

                <label>
                  <span>Depo Adı *</span>
                  <input
                    className="erp-input"
                    required
                    value={warehouseForm.name}
                    onChange={(e) => updateWarehouseForm("name", e.target.value)}
                  />
                </label>

                <label>
                  <span>Adres</span>
                  <input
                    className="erp-input"
                    value={warehouseForm.address}
                    onChange={(e) => updateWarehouseForm("address", e.target.value)}
                  />
                </label>
              </div>

              <div className="erp-actions">
                <button type="submit" disabled={creatingWarehouse}>
                  {creatingWarehouse ? "Kaydediliyor..." : "Depoyu Kaydet"}
                </button>
              </div>
            </form>

            {site.warehouses.length === 0 ? (
              <div className="erp-empty-state">
                Bu şantiyeye ait depo bulunmuyor.
              </div>
            ) : (
              <div className="erp-project-list">
                {site.warehouses.map((warehouse) => (
                  <div className="erp-project-list-item" key={warehouse.id}>
                    <div>
                      <strong>
                        {warehouse.code} · {warehouse.name}
                      </strong>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Personel Ata</h2>
                <p>Şantiyeye yeni personel görevlendirmesi ekleyin</p>
              </div>
            </div>

            {assignError && <div className="erp-alert error">{assignError}</div>}

            <form className="erp-form-card" onSubmit={assign}>
              <div className="erp-form-grid">
                <label>
                  <span>Personel *</span>
                  <select
                    required
                    value={form.personnelId}
                    onChange={(e) => update("personnelId", e.target.value)}
                  >
                    <option value="">Seçin</option>
                    {assignable.map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.employeeNumber} — {person.fullName}
                        {person.jobTitle ? ` (${person.jobTitle})` : ""}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Görev</span>
                  <input
                    className="erp-input"
                    value={form.role}
                    onChange={(e) => update("role", e.target.value)}
                  />
                </label>

                <label>
                  <span>Başlangıç Tarihi *</span>
                  <input
                    className="erp-input"
                    type="date"
                    required
                    value={form.startDate}
                    onChange={(e) => update("startDate", e.target.value)}
                  />
                </label>
              </div>

              <div className="erp-actions">
                <button type="submit" disabled={assigning || !form.personnelId}>
                  {assigning ? "Atanıyor..." : "Personeli Ata"}
                </button>
              </div>
            </form>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Atanmış Personel</h2>
                <p>{site.assignments.length} kayıt</p>
              </div>
            </div>

            {site.assignments.length === 0 ? (
              <div className="erp-empty-state">
                Henüz personel ataması yapılmamış.
              </div>
            ) : (
              <div className="erp-project-list">
                {site.assignments.map((assignment) => (
                  <div className="erp-project-list-item" key={assignment.id}>
                    <div>
                      <strong>
                        {assignment.employeeNumber} · {assignment.fullName}
                      </strong>
                      <span>
                        {assignment.role || "Görev belirtilmedi"}
                        {assignment.jobTitle ? ` · ${assignment.jobTitle}` : ""}
                      </span>
                      <span>
                        {formatDate(assignment.startDate)} —{" "}
                        {assignment.endDate ? formatDate(assignment.endDate) : "devam ediyor"}
                      </span>
                    </div>

                    {assignment.isActive && !assignment.endDate ? (
                      <button
                        type="button"
                        className="erp-button secondary"
                        onClick={() => void closeAssignment(assignment.id)}
                      >
                        Atamayı Kapat
                      </button>
                    ) : (
                      <span className="erp-status gray">Kapatıldı</span>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>

          <DailyReportTab siteId={params.siteId} />
        </>
      )}
    </ErpShell>
  );
}
