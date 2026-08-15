"use client";

import { useCallback, useEffect, useState } from "react";

import { usePermissions } from "@/lib/use-permissions";
import {
  terminationService,
  type RehireAssessment,
} from "@/services/termination.service";

/**
 * Ayrılış değerlendirmesi paneli.
 *
 * Yasal çıkış nedeninden AYRI: neden SGK/İş Kanunu tarafını, kod
 * İK'nın "bu kişiyi yeniden alır mıyız" değerlendirmesini tutar.
 *
 * Kırmızı ve sarıda gerekçe zorunlu — gerekçesiz bir engel, itiraz
 * edilemez bir engeldir ve işe alan kişi neyi geçtiğini bilmeden
 * karar veremez.
 *
 * GİZLİLİK: yalnız personnel.edit olan (İK/GM) görür ve atar.
 */
export default function RehireAssessmentPanel({
  terminationId,
  personnelFullName,
}: {
  terminationId: string;
  personnelFullName?: string;
}) {
  const { has } = usePermissions();
  const canManage = has("personnel.edit");

  const [data, setData] = useState<RehireAssessment | null>(null);
  const [code, setCode] = useState<number | null>(null);
  const [note, setNote] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    if (!terminationId || !canManage) return;

    try {
      const result = await terminationService.getRehireAssessment(terminationId);

      setData(result);
      setCode(result.rehireCode);
      setNote(result.rehireNote ?? "");
      setError("");
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Değerlendirme alınamadı."
      );
    }
  }, [terminationId, canManage]);

  useEffect(() => {
    let active = true;

    void (async () => {
      await load();

      if (active) setLoading(false);
    })();

    return () => {
      active = false;
    };
  }, [load]);

  if (!canManage) return null;
  if (loading) return <div style={box}>Değerlendirme yükleniyor...</div>;

  const needsNote = code === 1 || code === 2;

  async function save() {
    if (needsNote && !note.trim()) {
      setError("Kırmızı ve sarı değerlendirmede gerekçe zorunludur.");
      return;
    }

    try {
      setSaving(true);
      setError("");
      setNotice("");

      const result = await terminationService.setRehireAssessment(
        terminationId,
        code,
        note.trim() || null
      );

      setNotice(result.message);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div style={panel}>
      <div style={{ fontSize: 14, fontWeight: 700, color: "var(--erp-text)" }}>
        Ayrılış Değerlendirmesi
        {personnelFullName ? ` — ${personnelFullName}` : ""}
      </div>

      <p style={{ marginTop: 6, fontSize: 12, color: "var(--erp-muted)" }}>
        Yasal çıkış nedeninden ayrıdır. Bu kod, kişi ileride yeniden işe
        alınmak istendiğinde İK&apos;ya gösterilir. Boş bırakılırsa
        &quot;değerlendirilmedi&quot; sayılır ve hiçbir engel ya da uyarı
        üretmez.
      </p>

      {error ? <div style={errorBox}>{error}</div> : null}
      {notice ? <div style={noticeBox}>{notice}</div> : null}

      <div style={{ marginTop: 12, display: "grid", gap: 8 }}>
        {OPTIONS.map((option) => (
          <label
            key={String(option.value)}
            style={{
              ...optionRow,
              ...(code === option.value ? option.tone : {}),
            }}
          >
            <input
              type="radio"
              name={`rehire-${terminationId}`}
              checked={code === option.value}
              onChange={() => {
                setCode(option.value);
                setError("");
              }}
            />

            <span>
              <strong>{option.label}</strong>
              <span style={{ display: "block", fontSize: 12, opacity: 0.85 }}>
                {option.hint}
              </span>
            </span>
          </label>
        ))}
      </div>

      <label style={{ display: "grid", gap: 6, marginTop: 12, fontSize: 12 }}>
        <span style={{ color: "var(--erp-text)" }}>
          Gerekçe {needsNote ? "(zorunlu)" : "(isteğe bağlı)"}
        </span>

        <textarea
          value={note}
          onChange={(event) => setNote(event.target.value)}
          rows={3}
          placeholder="Ne oldu? Örn. sık devamsızlık, ekip içi uyumsuzluk, iş güvenliği kuralına tekrarlanan aykırılık"
          style={textarea}
        />
      </label>

      {/* KVKK: gerekçe faktüel ve mesleki olmalı. */}
      <div style={kvkkBox}>
        Gerekçeyi <strong>faktüel ve mesleki</strong> yazın: davranış, uyum,
        performans, devamsızlık. Sağlık durumu, inanç, sendika üyeliği gibi
        özel nitelikli kişisel veri <strong>yazmayın</strong>. Bu kayıt
        sınırlı erişimlidir ve yalnızca işe alım kararında kullanılır.
      </div>

      {data?.rehireMarkedAtUtc ? (
        <div style={{ marginTop: 10, fontSize: 11, color: "var(--erp-muted)" }}>
          Son işaretleme: {new Date(data.rehireMarkedAtUtc).toLocaleString("tr-TR")}
        </div>
      ) : null}

      <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
        <button
          type="button"
          onClick={() => void save()}
          disabled={saving}
          style={primaryButton}
        >
          {saving ? "Kaydediliyor..." : "Değerlendirmeyi Kaydet"}
        </button>

        {code !== null ? (
          <button
            type="button"
            onClick={() => {
              setCode(null);
              setNote("");
            }}
            disabled={saving}
            style={secondaryButton}
          >
            İşareti Kaldır
          </button>
        ) : null}
      </div>
    </div>
  );
}

const OPTIONS = [
  {
    value: 0,
    label: "Yeşil — sorunsuz",
    hint: "Yeniden alınabilir.",
    tone: { background: "var(--color-semantic-success-tint)", borderColor: "var(--color-semantic-success-border)" },
  },
  {
    value: 1,
    label: "Sarı — dikkat, şartlı",
    hint: "İşe alımda uyarı çıkar, engellemez.",
    tone: { background: "var(--color-semantic-warning-tint)", borderColor: "var(--color-semantic-warning-border)" },
  },
  {
    value: 2,
    label: "Kırmızı — işe alınamaz",
    hint: "İşe alım engellenir; yalnız Genel Müdür gerekçeyle geçebilir.",
    tone: { background: "var(--color-semantic-danger-tint)", borderColor: "var(--color-semantic-danger-border)" },
  },
] as const;

const panel = {
  padding: 16,
  border: "1px solid var(--erp-border)",
  borderRadius: 12,
  background: "var(--color-surface-card)",
} as const;

const box = {
  padding: 20,
  textAlign: "center",
  borderRadius: 12,
  background: "var(--color-surface-bg)",
  border: "1px solid var(--erp-border)",
  color: "var(--erp-muted)",
} as const;

const optionRow = {
  display: "flex",
  alignItems: "flex-start",
  gap: 10,
  padding: 10,
  border: "1px solid var(--erp-border)",
  borderRadius: 10,
  cursor: "pointer",
} as const;

const textarea = {
  padding: 10,
  borderRadius: 10,
  border: "1px solid var(--erp-border)",
  font: "inherit",
  resize: "vertical",
} as const;

const kvkkBox = {
  marginTop: 10,
  padding: 10,
  borderRadius: 10,
  background: "var(--color-surface-bg)",
  border: "1px solid var(--erp-border)",
  fontSize: 11,
  color: "var(--erp-text)",
} as const;

const errorBox = {
  marginTop: 10,
  padding: 10,
  borderRadius: 10,
  background: "var(--color-semantic-danger-tint)",
  border: "1px solid var(--color-semantic-danger-border)",
  color: "var(--color-semantic-danger)",
  fontSize: 12,
  fontWeight: 700,
} as const;

const noticeBox = {
  marginTop: 10,
  padding: 10,
  borderRadius: 10,
  background: "var(--color-semantic-success-tint)",
  border: "1px solid var(--color-semantic-success-border)",
  color: "var(--color-semantic-success)",
  fontSize: 12,
  fontWeight: 700,
} as const;

const primaryButton = {
  height: 38,
  padding: "0 16px",
  borderRadius: 10,
  border: "none",
  background: "var(--erp-primary)",
  color: "var(--color-surface-card)",
  fontWeight: 700,
  cursor: "pointer",
} as const;

const secondaryButton = {
  height: 38,
  padding: "0 14px",
  borderRadius: 10,
  border: "1px solid var(--erp-border)",
  background: "var(--color-surface-card)",
  color: "var(--erp-text)",
  fontWeight: 600,
  cursor: "pointer",
} as const;
