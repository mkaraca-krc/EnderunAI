"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { projectSiteService } from "@/services/project-site.service";

export default function NewProjectSitePage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const [form, setForm] = useState({
    code: "",
    name: "",
    location: "",
    notes: "",
  });

  function update(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");

    try {
      await projectSiteService.create(params.id, {
        code: form.code,
        name: form.name,
        location: form.location || null,
        notes: form.notes || null,
      });

      router.push(`/projeler/${params.id}`);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Şantiye oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Şantiye"
      description="Proje altına yeni bir şantiye (lokasyon) ekleyin"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <form className="erp-form-card" onSubmit={save}>
        <div className="erp-form-grid">
          <label>
            <span>Şantiye Kodu *</span>
            <input
              className="erp-input"
              required
              value={form.code}
              onChange={(e) => update("code", e.target.value)}
            />
          </label>

          <label>
            <span>Şantiye Adı *</span>
            <input
              className="erp-input"
              required
              value={form.name}
              onChange={(e) => update("name", e.target.value)}
            />
          </label>

          <label>
            <span>Konum / Adres</span>
            <input
              className="erp-input"
              value={form.location}
              onChange={(e) => update("location", e.target.value)}
            />
          </label>

          <label>
            <span>Notlar</span>
            <input
              className="erp-input"
              value={form.notes}
              onChange={(e) => update("notes", e.target.value)}
            />
          </label>
        </div>

        <div className="erp-actions">
          <button type="submit" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Şantiyeyi Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
