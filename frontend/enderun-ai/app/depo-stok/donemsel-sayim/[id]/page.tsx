"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { Button, Input, Modal, Select } from "@/components/ui";
import { money, quantity as formatQuantity } from "@/lib/format/turkish";
import { parseScannedItem } from "@/lib/inventory/qr";
import { usePermissions } from "@/lib/use-permissions";
import {
  STOCK_COUNT_STATUS,
  VARIANCE_REASON,
  stockCountService,
  type StockCountDetail,
  type StockCountVarianceReport,
} from "@/services/stock-count.service";

type Draft = {
  counted: string;
  reason: string;
  note: string;
};

export default function PeriodicCountDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const { has } = usePermissions();

  const canApprove = has("accounting.approve");
  const canCount = has("inventory.edit");

  const [session, setSession] = useState<StockCountDetail | null>(null);
  const [report, setReport] = useState<StockCountVarianceReport | null>(null);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});

  const [scan, setScan] = useState("");
  const [scanNotice, setScanNotice] = useState("");
  const [highlighted, setHighlighted] = useState("");

  const [decisionOpen, setDecisionOpen] = useState<"reject" | "cancel" | null>(null);
  const [decisionReason, setDecisionReason] = useState("");

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    try {
      const [detail, variance] = await Promise.all([
        stockCountService.getById(params.id),
        stockCountService.getVarianceReport(params.id).catch(() => null),
      ]);

      setSession(detail);
      setReport(variance);

      setDrafts(
        Object.fromEntries(
          detail.lines.map((line) => [
            line.id,
            {
              counted: line.countedQuantity === null || line.countedQuantity === undefined
                ? ""
                : String(line.countedQuantity),
              reason:
                line.varianceReason === null || line.varianceReason === undefined
                  ? ""
                  : String(line.varianceReason),
              note: line.note ?? "",
            },
          ])
        )
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sayım alınamadı.");
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const editable = session?.status === 0 && canCount;

  /**
   * Fark, DRAFT üzerinden anlık hesaplanıyor: sayan kişi kaydetmeden
   * önce farkı görmeli, yoksa gerekçeyi neye yazdığını bilemez.
   */
  const rows = useMemo(() => {
    if (!session) return [];

    return session.lines.map((line) => {
      const draft = drafts[line.id];
      const raw = draft?.counted ?? "";
      const counted = raw === "" ? null : Number(raw);
      const difference =
        counted === null || Number.isNaN(counted) ? null : counted - line.systemQuantity;

      return { line, draft, counted, difference };
    });
  }, [session, drafts]);

  const missingReasons = rows.filter(
    (row) => row.difference !== null && row.difference !== 0 && !row.draft?.reason
  ).length;

  const uncounted = rows.filter((row) => row.counted === null).length;

  function patch(lineId: string, next: Partial<Draft>) {
    setDrafts((current) => ({
      ...current,
      [lineId]: { ...(current[lineId] ?? { counted: "", reason: "", note: "" }), ...next },
    }));
  }

  /**
   * QR / barkod okutuldu: satırı bulur, işaretler ve miktar kutusuna
   * odaklanır. Sayım listesi yüzlerce satır olabiliyor; aranan
   * malzemeyi gözle bulmak sayımı yavaşlatan asıl adım.
   */
  function handleScan() {
    if (!session) return;

    const parsed = parseScannedItem(scan);
    if (!parsed) return;

    const match = session.lines.find((line) =>
      parsed.kind === "id"
        ? line.inventoryItemId === parsed.id
        : line.code.toLowerCase() === parsed.term.toLowerCase() ||
          (line.barcode ?? "").toLowerCase() === parsed.term.toLowerCase()
    );

    if (!match) {
      setScanNotice("Okutulan kod bu sayım listesinde yok.");
      setScan("");
      return;
    }

    setHighlighted(match.id);
    setScanNotice(`${match.name} — miktarı girin.`);
    setScan("");

    document.getElementById(`count-${match.id}`)?.focus();
  }

  async function save() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await stockCountService.saveCounts(
        params.id,
        rows.map((row) => ({
          lineId: row.line.id,
          countedQuantity:
            row.counted === null || Number.isNaN(row.counted) ? null : row.counted,
          varianceReason: row.draft?.reason ? Number(row.draft.reason) : null,
          note: row.draft?.note || null,
        }))
      );

      setNotice("Sayım miktarları kaydedildi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function run(action: () => Promise<{ message: string }>) {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await action();
      setNotice(result.message);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem tamamlanamadı.");
    } finally {
      setSaving(false);
    }
  }

  if (!session) {
    return (
      <ErpShell design="redwood" title="Dönemsel Sayım" description="Yükleniyor…">
        {error && <p className="erp-form-error">{error}</p>}
      </ErpShell>
    );
  }

  return (
    <ErpShell
      design="redwood"
      title={`Sayım ${session.documentNumber}`}
      description={`${session.name} · ${session.warehouseName} · ${session.zoneName ?? "Tüm depo"} · ${STOCK_COUNT_STATUS[session.status]}`}
    >
      {error && <p className="erp-form-error">{error}</p>}
      {notice && <p className="erp-form-notice">{notice}</p>}

      {session.status === 0 && (
        <section className="erp-card">
          <div className="erp-form-header">
            <h2>QR / barkod ile hızlı sayım</h2>
            <p>Okutun; satır işaretlenir ve miktar kutusu açılır.</p>
          </div>

          <Input
            value={scan}
            placeholder="Okutun veya kodu yazıp Enter'a basın"
            onChange={(event) => setScan(event.target.value)}
            onKeyDown={(event) => {
              if (event.key !== "Enter") return;
              event.preventDefault();
              handleScan();
            }}
          />
          {scanNotice && <small>{scanNotice}</small>}
        </section>
      )}

      <section className="erp-card">
        <div className="erp-form-header">
          <h2>Sayım listesi</h2>
          <p>
            Sistem miktarı oturum açıldığında donduruldu. Miktarı
            girilmeyen satır onayda <strong>atlanır</strong>, stoğu
            değişmez.
          </p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Kod</th>
                <th>Malzeme</th>
                <th>Bölge</th>
                <th style={{ textAlign: "right" }}>Sistem</th>
                <th style={{ width: 110 }}>Sayılan</th>
                <th style={{ textAlign: "right" }}>Fark</th>
                <th style={{ width: 150 }}>Gerekçe</th>
                <th style={{ width: 160 }}>Not</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ line, draft, difference }) => (
                <tr
                  key={line.id}
                  style={highlighted === line.id ? { outline: "2px solid currentColor" } : undefined}
                >
                  <td>{line.code}</td>
                  <td>{line.name}</td>
                  <td>{line.zoneName ?? "—"}</td>
                  <td style={{ textAlign: "right" }}>
                    {formatQuantity(line.systemQuantity)} {line.unit}
                  </td>
                  <td>
                    <Input
                      id={`count-${line.id}`}
                      value={draft?.counted ?? ""}
                      disabled={!editable}
                      onChange={(event) => patch(line.id, { counted: event.target.value })}
                    />
                  </td>
                  <td style={{ textAlign: "right" }}>
                    {difference === null ? "—" : formatQuantity(difference)}
                  </td>
                  <td>
                    <Select
                      value={draft?.reason ?? ""}
                      disabled={!editable || difference === null || difference === 0}
                      onChange={(event) => patch(line.id, { reason: event.target.value })}
                      options={[
                        { value: "", label: "Seçin" },
                        ...Object.entries(VARIANCE_REASON).map(([value, label]) => ({
                          value,
                          label,
                        })),
                      ]}
                    />
                  </td>
                  <td>
                    <Input
                      value={draft?.note ?? ""}
                      disabled={!editable}
                      onChange={(event) => patch(line.id, { note: event.target.value })}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <p>
          {uncounted} satır henüz sayılmadı
          {missingReasons > 0 && ` · ${missingReasons} farklı satırda gerekçe eksik`}
        </p>

        {session.status === 0 && canCount && (
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <Button onClick={() => void save()} disabled={saving}>
              Miktarları Kaydet
            </Button>
            <Button
              variant="secondary"
              disabled={saving || missingReasons > 0}
              onClick={() => void run(() => stockCountService.submit(params.id))}
            >
              Onaya Gönder
            </Button>
            <Button
              variant="danger"
              disabled={saving}
              onClick={() => setDecisionOpen("cancel")}
            >
              Sayımı İptal Et
            </Button>
          </div>
        )}

        {session.status === 1 && canApprove && (
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <Button
              disabled={saving}
              onClick={() => void run(() => stockCountService.approve(params.id))}
            >
              Onayla — stok ve muhasebe düzeltilsin
            </Button>
            <Button variant="danger" disabled={saving} onClick={() => setDecisionOpen("reject")}>
              Reddet
            </Button>
          </div>
        )}
      </section>

      {report && (
        <section className="erp-card">
          <div className="erp-form-header">
            <h2>Fark raporu</h2>
            <p>
              Tekrar eden kayıp aynı bölgede ya da kategoride toplanıyorsa
              sebebi oradadır.
            </p>
          </div>

          <div className="erp-detail-grid">
            <div>
              <small>Noksan</small>
              <strong>{money(report.shortageValue)}</strong>
            </div>
            <div>
              <small>Fazla</small>
              <strong>{money(report.surplusValue)}</strong>
            </div>
            <div>
              <small>Net</small>
              <strong>{money(report.netValue)}</strong>
            </div>
            <div>
              <small>Sayılmayan satır</small>
              <strong>{report.uncountedLines}</strong>
            </div>
          </div>

          {(
            [
              ["Bölgeye göre", report.byZone.map((x) => ({ label: x.zone, ...x }))],
              ["Kategoriye göre", report.byCategory.map((x) => ({ label: x.category, ...x }))],
              ["Gerekçeye göre", report.byReason.map((x) => ({ label: x.reasonLabel, ...x }))],
            ] as const
          ).map(([title, group]) => (
            <div key={title} className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>{title}</th>
                    <th style={{ textAlign: "right" }}>Satır</th>
                    <th style={{ textAlign: "right" }}>Tutar</th>
                  </tr>
                </thead>
                <tbody>
                  {group.length === 0 ? (
                    <tr>
                      <td colSpan={3}>Fark yok.</td>
                    </tr>
                  ) : (
                    group.map((row) => (
                      <tr key={row.label}>
                        <td>{row.label}</td>
                        <td style={{ textAlign: "right" }}>{row.lines}</td>
                        <td style={{ textAlign: "right" }}>{money(row.value)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          ))}
        </section>
      )}

      <Modal
        open={decisionOpen !== null}
        title={decisionOpen === "reject" ? "Sayımı reddet" : "Sayımı iptal et"}
        onClose={() => setDecisionOpen(null)}
        footer={
          <Button
            variant="danger"
            disabled={saving || !decisionReason.trim()}
            onClick={() => {
              const action = decisionOpen;
              setDecisionOpen(null);
              void run(() =>
                action === "reject"
                  ? stockCountService.reject(params.id, decisionReason.trim())
                  : stockCountService.cancel(params.id, decisionReason.trim())
              ).then(() => setDecisionReason(""));
            }}
          >
            Onayla
          </Button>
        }
      >
        <p>Gerekçe zorunludur; karar geçmişte görünür kalır.</p>
        <Input
          value={decisionReason}
          onChange={(event) => setDecisionReason(event.target.value)}
        />
      </Modal>

      <Button variant="secondary" onClick={() => router.push("/depo-stok/donemsel-sayim")}>
        Listeye dön
      </Button>
    </ErpShell>
  );
}
