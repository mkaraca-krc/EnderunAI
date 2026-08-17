"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import RecipeEditor from "@/components/engineering/recipe-editor";
import PositionPriceHistory from "@/components/engineering/position-price-history";
import PositionPurchaseIntelligence from "@/components/engineering/position-purchase-intelligence";
import { decimal } from "@/lib/format/turkish";
import {
  EngineeringPositionDetail,
  engineeringPositionDetailService,
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

type FormState = {
  name: string;
  unit: string;
  discipline: string;
  status: string;
  officialInstitution: string;
  officialCode: string;
  category: string;
  description: string;
  technicalSpecification: string;
  searchKeywords: string;
  defaultLaborHours: string;
  defaultHelperHours: string;
  defaultMachineHours: string;
};

const emptyForm: FormState = {
  name: "",
  unit: "",
  discipline: "0",
  status: "0",
  officialInstitution: "",
  officialCode: "",
  category: "",
  description: "",
  technicalSpecification: "",
  searchKeywords: "",
  defaultLaborHours: "0",
  defaultHelperHours: "0",
  defaultMachineHours: "0",
};

export default function EngineeringPositionDetailPage() {
  /**
   * Düğme -> uç -> izin:
   *   PUT engineering-positions/{id} -> engineering.manage
   */
  const actions = useModuleActions("engineering");

  const params = useParams<{ id: string }>();
  const id = params.id;

  const [item, setItem] = useState<EngineeringPositionDetail | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [activeTab, setActiveTab] = useState("general");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result = await engineeringPositionDetailService.getById(id);

      setItem(result);
      setForm({
        name: result.name ?? "",
        unit: result.unit ?? "",
        discipline: String(result.discipline ?? 0),
        status: String(result.status ?? 0),
        officialInstitution: result.officialInstitution ?? "",
        officialCode: result.officialCode ?? "",
        category: result.category ?? "",
        description: result.description ?? "",
        technicalSpecification: result.technicalSpecification ?? "",
        searchKeywords: result.searchKeywords ?? "",
        defaultLaborHours: String(result.defaultLaborHours ?? 0),
        defaultHelperHours: String(result.defaultHelperHours ?? 0),
        defaultMachineHours: String(result.defaultMachineHours ?? 0),
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Poz yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, [id]);

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await engineeringPositionDetailService.update(id, {
        name: form.name,
        unit: form.unit,
        discipline: Number(form.discipline),
        status: Number(form.status),
        officialInstitution: form.officialInstitution || null,
        officialCode: form.officialCode || null,
        category: form.category || null,
        description: form.description || null,
        technicalSpecification: form.technicalSpecification || null,
        searchKeywords: form.searchKeywords || null,
        defaultLaborHours: Number(form.defaultLaborHours || 0),
        defaultHelperHours: Number(form.defaultHelperHours || 0),
        defaultMachineHours: Number(form.defaultMachineHours || 0),
      });

      setSuccess("Poz bilgileri güncellendi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Poz güncellenemedi.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <ErpShell
      design="redwood"
        title="Poz Detayı"
        description="Mühendislik pozu yükleniyor"
      >
        <div className="erp-loading">Poz bilgileri yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell design="redwood" title="Poz Detayı" description="Poz bulunamadı">
        <div className="erp-alert error">
          {error || "İstenen poz bulunamadı."}
        </div>
      </ErpShell>
    );
  }

  const totalLabor =
    Number(form.defaultLaborHours || 0) +
    Number(form.defaultHelperHours || 0);

  const tabs = [
    ["general", "Genel Bilgi"],
    ["prices", "Birim Fiyatlar"],
    ["recipe", "Reçete"],
    ["purchase", "Gerçek Alış"],
    ["materials", "Malzemeler"],
    ["labor", "İşçilik"],
    ["machines", "Makinalar"],
    ["documents", "Dokümanlar"],
    ["ai", "AI Analizi"],
    ["revisions", "Revizyonlar"],
  ];

  return (
    <ErpShell
      design="redwood"
      title={`${item.code} · ${item.name}`}
      description="Poz detayları, reçete ve teknik analiz"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {success && <div className="erp-alert success">{success}</div>}

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            MÜHENDİSLİK POZU
          </span>

          <h2>{item.code}</h2>
          <p>{item.name}</p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <Link
            href="/muhendislik/pozlar"
            className="erp-secondary-button"
          >
            ← Poz Listesi
          </Link>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setActiveTab("recipe")}
          >
            Reçeteyi Aç
          </button>
        </div>
      </section>

      <div className="enderun-dashboard-stats">
        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">▦</div>
          <div>
            <span>Disiplin</span>
            <strong>
              {disciplineLabels[item.discipline] ?? item.discipline}
            </strong>
            <small>{item.category || "Kategori tanımsız"}</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">◷</div>
          <div>
            <span>Toplam Adam/Saat</span>
            <strong>{decimal(totalLabor, 2)}</strong>
            <small>Usta ve yardımcı toplamı</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">R</div>
          <div>
            <span>Revizyon</span>
            <strong>R{item.revisionNumber}</strong>
            <small>Son kayıt revizyonu</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">✓</div>
          <div>
            <span>Durum</span>
            <strong>{statusLabels[item.status] ?? item.status}</strong>
            <small>{item.companyName}</small>
          </div>
        </div>
      </div>

      <section className="erp-panel">
        <div
          style={{
            display: "flex",
            gap: 8,
            flexWrap: "wrap",
            marginBottom: 24,
          }}
        >
          {tabs.map(([key, label]) => (
            <button
              key={key}
              type="button"
              className={
                activeTab === key
                  ? "erp-primary-button"
                  : "erp-secondary-button"
              }
              onClick={() => setActiveTab(key)}
            >
              {label}
            </button>
          ))}
        </div>

        {activeTab === "general" && (
          <form onSubmit={save}>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
                gap: 16,
              }}
            >
              <label>
                <span>Poz Kodu</span>
                <input className="erp-input" value={item.code} disabled />
              </label>

              <label>
                <span>Poz Adı</span>
                <input
                  className="erp-input"
                  value={form.name}
                  onChange={(e) =>
                    setForm((v) => ({ ...v, name: e.target.value }))
                  }
                  required
                />
              </label>

              <label>
                <span>Birim</span>
                <input
                  className="erp-input"
                  value={form.unit}
                  onChange={(e) =>
                    setForm((v) => ({ ...v, unit: e.target.value }))
                  }
                  required
                />
              </label>

              <label>
                <span>Disiplin</span>
                <select
                  className="erp-input"
                  value={form.discipline}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      discipline: e.target.value,
                    }))
                  }
                >
                  {Object.entries(disciplineLabels).map(([value, label]) => (
                    <option value={value} key={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Durum</span>
                <select
                  className="erp-input"
                  value={form.status}
                  onChange={(e) =>
                    setForm((v) => ({ ...v, status: e.target.value }))
                  }
                >
                  {Object.entries(statusLabels).map(([value, label]) => (
                    <option value={value} key={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Kategori</span>
                <input
                  className="erp-input"
                  value={form.category}
                  onChange={(e) =>
                    setForm((v) => ({ ...v, category: e.target.value }))
                  }
                />
              </label>

              <label>
                <span>Resmî Kurum</span>
                <input
                  className="erp-input"
                  value={form.officialInstitution}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      officialInstitution: e.target.value,
                    }))
                  }
                />
              </label>

              <label>
                <span>Resmî Poz Kodu</span>
                <input
                  className="erp-input"
                  value={form.officialCode}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      officialCode: e.target.value,
                    }))
                  }
                />
              </label>

              <label>
                <span>Anahtar Kelimeler</span>
                <input
                  className="erp-input"
                  value={form.searchKeywords}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      searchKeywords: e.target.value,
                    }))
                  }
                />
              </label>

              <label>
                <span>Usta Adam/Saat</span>
                <input
                  type="number"
                  step="0.01"
                  className="erp-input"
                  value={form.defaultLaborHours}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      defaultLaborHours: e.target.value,
                    }))
                  }
                />
              </label>

              <label>
                <span>Yardımcı Adam/Saat</span>
                <input
                  type="number"
                  step="0.01"
                  className="erp-input"
                  value={form.defaultHelperHours}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      defaultHelperHours: e.target.value,
                    }))
                  }
                />
              </label>

              <label>
                <span>Makine Saati</span>
                <input
                  type="number"
                  step="0.01"
                  className="erp-input"
                  value={form.defaultMachineHours}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      defaultMachineHours: e.target.value,
                    }))
                  }
                />
              </label>
            </div>

            <div style={{ marginTop: 20 }}>
              <label>
                <span>Açıklama</span>
                <textarea
                  className="erp-input"
                  rows={4}
                  value={form.description}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      description: e.target.value,
                    }))
                  }
                />
              </label>
            </div>

            <div style={{ marginTop: 20 }}>
              <label>
                <span>Teknik Şartname</span>
                <textarea
                  className="erp-input"
                  rows={7}
                  value={form.technicalSpecification}
                  onChange={(e) =>
                    setForm((v) => ({
                      ...v,
                      technicalSpecification: e.target.value,
                    }))
                  }
                />
              </label>
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                marginTop: 24,
              }}
            >
              {actions.can("manage") && (
                <button
                  type="submit"
                  className="erp-primary-button"
                  disabled={saving}
                >
                  {saving ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
                </button>
              )}
            </div>
          </form>
        )}

        {activeTab === "prices" && (
          <PositionPriceHistory positionId={id} />
        )}

        {activeTab === "recipe" && (
          <RecipeEditor positionId={id} />
        )}

        {activeTab === "purchase" && item && (
          <PositionPurchaseIntelligence
            positionId={id}
            companyId={item.companyId}
          />
        )}

        {activeTab !== "general" &&
          activeTab !== "recipe" &&
          activeTab !== "purchase" &&
          activeTab !== "prices" && (
          <div className="erp-empty-state">
            <div className="enderun-empty-symbol">▧</div>
            <strong>
              {tabs.find(([key]) => key === activeTab)?.[1]} ekranı
            </strong>
            <p>Bu bölüm sıradaki geliştirme adımında bağlanacak.</p>
          </div>
        )}
      </section>
    </ErpShell>
  );
}
