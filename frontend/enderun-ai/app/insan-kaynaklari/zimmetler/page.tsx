"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import QRCode from "qrcode";
import ErpShell from "@/components/erp/erp-shell";
import HrAssetInventoryDialog from "@/components/hr/hr-asset-inventory-dialog";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { personnelService, type PersonnelListItem } from "@/services/personnel.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  hrAssetService,
  HrAssetAssignmentStatus,
  type AssetAssignment,
  type AssetDashboard,
  type PersonnelAssetAnalysis,
  type CreateAssetAssignmentRequest,
  type UpdateAssetAssignmentRequest,
} from "@/services/hr-asset.service";

type FormState = {
  personnelId: string;
  projectId: string;
  assetType: string;
  assetCode: string;
  assetName: string;
  serialNumber: string;
  assignmentDate: string;
  plannedReturnDate: string;
  actualReturnDate: string;
  conditionAtAssignment: string;
  conditionAtReturn: string;
  documentPath: string;
  status: number;
  notes: string;
};

type ActionMode =
  | "return"
  | "lost"
  | "damaged"
  | "project"
  | "transfer"
  | "cancel"
  | null;

const today = () => new Date().toISOString().slice(0, 10);

const emptyForm = (): FormState => ({
  personnelId: "",
  projectId: "",
  assetType: "",
  assetCode: "",
  assetName: "",
  serialNumber: "",
  assignmentDate: today(),
  plannedReturnDate: "",
  actualReturnDate: "",
  conditionAtAssignment: "",
  conditionAtReturn: "",
  documentPath: "",
  status: HrAssetAssignmentStatus.Assigned,
  notes: "",
});

const panel = {
  background: "#fff",
  border: "1px solid #e2e8f0",
  borderRadius: 16,
  boxShadow: "0 8px 24px rgba(15,23,42,.05)",
} as const;

const input = {
  width: "100%",
  minHeight: 42,
  border: "1px solid #cbd5e1",
  borderRadius: 10,
  padding: "8px 11px",
  background: "#fff",
  color: "#0f172a",
  boxSizing: "border-box",
} as const;

const statusNames: Record<number, string> = {
  0: "Aktif",
  1: "İade Edildi",
  2: "Kayıp",
  3: "Hasarlı",
  4: "İptal",
};

const statusColors: Record<number, { bg: string; fg: string }> = {
  0: { bg: "#dcfce7", fg: "#166534" },
  1: { bg: "#dbeafe", fg: "#1d4ed8" },
  2: { bg: "#fee2e2", fg: "#b91c1c" },
  3: { bg: "#ffedd5", fg: "#c2410c" },
  4: { bg: "#e2e8f0", fg: "#475569" },
};

function formatDate(value?: string | null) {
  return value
    ? new Intl.DateTimeFormat("tr-TR").format(new Date(value))
    : "-";
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function Field({
  label,
  children,
  required,
}: {
  label: string;
  children: React.ReactNode;
  required?: boolean;
}) {
  return (
    <label style={{ display: "grid", gap: 7 }}>
      <span style={{ fontSize: 13, fontWeight: 800, color: "#334155" }}>
        {label}{required ? " *" : ""}
      </span>
      {children}
    </label>
  );
}

function StatusBadge({ item }: { item: AssetAssignment }) {
  const color = statusColors[item.status] ?? statusColors[4];
  return (
    <span
      style={{
        display: "inline-flex",
        padding: "5px 9px",
        borderRadius: 999,
        background: item.isOverdue ? "#fef3c7" : color.bg,
        color: item.isOverdue ? "#92400e" : color.fg,
        fontWeight: 800,
        fontSize: 12,
      }}
    >
      {item.isOverdue
        ? `Gecikmiş · ${item.overdueDays ?? 0} gün`
        : item.statusName || statusNames[item.status]}
    </span>
  );
}

export default function HrAssetsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [items, setItems] = useState<AssetAssignment[]>([]);
  const [dashboard, setDashboard] = useState<AssetDashboard | null>(null);

  const [personnelId, setPersonnelId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [status, setStatus] = useState<number | "">("");
  const [assetType, setAssetType] = useState("");
  const [search, setSearch] = useState("");
  const [overdueOnly, setOverdueOnly] = useState(false);

  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState("");
  const [success, setSuccess] = useState("");
  const [error, setError] = useState("");

  const [inventoryDialogOpen, setInventoryDialogOpen] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<AssetAssignment | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm());

  const [actionMode, setActionMode] = useState<ActionMode>(null);
  const [actionItem, setActionItem] = useState<AssetAssignment | null>(null);
  const [actionText, setActionText] = useState("");
  const [actionDate, setActionDate] = useState(today());
  const [actionProjectId, setActionProjectId] = useState("");
  const [actionPersonnelId, setActionPersonnelId] = useState("");

  const [analysis, setAnalysis] = useState<PersonnelAssetAnalysis | null>(null);
  const [analysisOpen, setAnalysisOpen] = useState(false);
  const [analysisLoading, setAnalysisLoading] = useState(false);

  const [qrItem, setQrItem] = useState<AssetAssignment | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState("");

  const personnelMap = useMemo(
    () => new Map(personnel.map((x) => [x.id, x])),
    [personnel]
  );
  const projectMap = useMemo(
    () => new Map(projects.map((x) => [x.id, x])),
    [projects]
  );

  const assetTypes = useMemo(
    () =>
      Array.from(new Set(items.map((x) => x.assetType).filter(Boolean))).sort(
        (a, b) => a.localeCompare(b, "tr")
      ),
    [items]
  );

  const loadCompanies = useCallback(async () => {
    const rows = await companyService.getAll();
    setCompanies(rows);
    const selected = rows.find((x) => x.isActive !== false) ?? rows[0];
    if (selected) setCompanyId((current) => current || selected.id);
  }, []);

  const loadLookups = useCallback(async () => {
    if (!companyId) return;
    const [people, projectRows] = await Promise.all([
      personnelService.getAll({ companyId }),
      projectService.getAll(companyId),
    ]);
    setPersonnel(people);
    setProjects(projectRows);
  }, [companyId]);

  const loadData = useCallback(async () => {
    if (!companyId) return;
    setLoading(true);
    setError("");
    try {
      const [rows, summary] = await Promise.all([
        hrAssetService.getAll({
          companyId,
          personnelId: personnelId || undefined,
          projectId: projectId || undefined,
          status,
          assetType: assetType || undefined,
          search: search || undefined,
          overdueOnly,
        }),
        hrAssetService.getDashboard(companyId, projectId || undefined),
      ]);
      setItems(rows);
      setDashboard(summary);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId, personnelId, projectId, status, assetType, search, overdueOnly]);

  // Personel kartından "Zimmet" bağlantısıyla gelindiğinde liste o
  // kişiyle açılır. useSearchParams yerine doğrudan adres çubuğu
  // okunuyor; bu sayfa Suspense sınırı istemiyor.
  useEffect(() => {
    void (async () => {
      const fromLink = new URLSearchParams(window.location.search)
        .get("personnelId");

      if (fromLink) setPersonnelId(fromLink);
    })();
  }, []);

  useEffect(() => {
    void loadCompanies().catch((err) => setError(messageOf(err)));
  }, [loadCompanies]);

  useEffect(() => {
    void loadLookups().catch((err) => setError(messageOf(err)));
  }, [loadLookups]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  function openCreate() {
    setEditing(null);
    setForm(emptyForm());
    setFormOpen(true);
    setSuccess("");
    setError("");
  }

  function openEdit(item: AssetAssignment) {
    setEditing(item);
    setForm({
      personnelId: item.personnelId,
      projectId: item.projectId ?? "",
      assetType: item.assetType,
      assetCode: item.assetCode,
      assetName: item.assetName,
      serialNumber: item.serialNumber ?? "",
      assignmentDate: item.assignmentDate.slice(0, 10),
      plannedReturnDate: item.plannedReturnDate?.slice(0, 10) ?? "",
      actualReturnDate: item.actualReturnDate?.slice(0, 10) ?? "",
      conditionAtAssignment: item.conditionAtAssignment ?? "",
      conditionAtReturn: item.conditionAtReturn ?? "",
      documentPath: item.documentPath ?? "",
      status: item.status,
      notes: item.notes ?? "",
    });
    setFormOpen(true);
    setSuccess("");
    setError("");
  }

  async function submitForm(event: React.FormEvent) {
    event.preventDefault();
    if (!companyId || !form.personnelId || !form.assetType.trim() ||
        !form.assetCode.trim() || !form.assetName.trim()) {
      setError("Şirket, personel, ekipman türü, kodu ve adı zorunludur.");
      return;
    }

    setBusyId(editing?.id ?? "create");
    setError("");
    setSuccess("");

    try {
      if (editing) {
        const payload: UpdateAssetAssignmentRequest = {
          personnelId: form.personnelId,
          projectId: form.projectId || null,
          assetType: form.assetType.trim(),
          assetCode: form.assetCode.trim(),
          assetName: form.assetName.trim(),
          serialNumber: form.serialNumber.trim() || null,
          assignmentDate: form.assignmentDate,
          plannedReturnDate: form.plannedReturnDate || null,
          actualReturnDate: form.actualReturnDate || null,
          conditionAtAssignment: form.conditionAtAssignment.trim() || null,
          conditionAtReturn: form.conditionAtReturn.trim() || null,
          documentPath: form.documentPath.trim() || null,
          status: form.status,
          notes: form.notes.trim() || null,
        };
        await hrAssetService.update(editing.id, payload);
        setSuccess("Zimmet kaydı güncellendi.");
      } else {
        const payload: CreateAssetAssignmentRequest = {
          companyId,
          personnelId: form.personnelId,
          projectId: form.projectId || null,
          assetType: form.assetType.trim(),
          assetCode: form.assetCode.trim(),
          assetName: form.assetName.trim(),
          serialNumber: form.serialNumber.trim() || null,
          assignmentDate: form.assignmentDate,
          plannedReturnDate: form.plannedReturnDate || null,
          conditionAtAssignment: form.conditionAtAssignment.trim() || null,
          documentPath: form.documentPath.trim() || null,
          notes: form.notes.trim() || null,
        };
        await hrAssetService.create(payload);
        setSuccess("Yeni zimmet kaydı oluşturuldu.");
      }

      setFormOpen(false);
      await loadData();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId("");
    }
  }

  function openAction(mode: Exclude<ActionMode, null>, item: AssetAssignment) {
    setActionMode(mode);
    setActionItem(item);
    setActionText("");
    setActionDate(today());
    setActionProjectId(item.projectId ?? "");
    setActionPersonnelId("");
    setError("");
    setSuccess("");
  }

  async function submitAction() {
    if (!actionItem || !actionMode) return;
    setBusyId(actionItem.id);
    setError("");
    setSuccess("");

    try {
      if (actionMode === "return") {
        await hrAssetService.returnAsset(actionItem.id, {
          returnDate: actionDate,
          conditionAtReturn: actionText.trim() || null,
          notes: null,
        });
        setSuccess("Ekipman iade alındı.");
      } else if (actionMode === "lost") {
        if (!actionText.trim()) throw new Error("Kayıp nedeni zorunludur.");
        await hrAssetService.markLost(actionItem.id, {
          eventDate: actionDate,
          reason: actionText.trim(),
        });
        setSuccess("Ekipman kayıp olarak işaretlendi.");
      } else if (actionMode === "damaged") {
        if (!actionText.trim()) throw new Error("Hasar açıklaması zorunludur.");
        await hrAssetService.markDamaged(actionItem.id, {
          eventDate: actionDate,
          damageDescription: actionText.trim(),
        });
        setSuccess("Ekipman hasarlı olarak işaretlendi.");
      } else if (actionMode === "project") {
        await hrAssetService.changeProject(actionItem.id, {
          projectId: actionProjectId || null,
          notes: actionText.trim() || null,
        });
        setSuccess("Zimmet projesi değiştirildi.");
      } else if (actionMode === "transfer") {
        if (!actionPersonnelId) throw new Error("Yeni personel seçilmelidir.");
        await hrAssetService.transferPersonnel(actionItem.id, {
          newPersonnelId: actionPersonnelId,
          newProjectId: actionProjectId || null,
          transferDate: actionDate,
          conditionAtTransfer: actionText.trim() || null,
          notes: null,
        });
        setSuccess("Ekipman başka personele devredildi.");
      } else if (actionMode === "cancel") {
        if (!actionText.trim()) throw new Error("İptal nedeni zorunludur.");
        await hrAssetService.cancel(actionItem.id, actionText.trim());
        setSuccess("Zimmet kaydı iptal edildi.");
      }

      setActionMode(null);
      setActionItem(null);
      await loadData();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId("");
    }
  }

  async function openPersonnelAnalysis(selectedPersonnelId: string) {
    if (!selectedPersonnelId) {
      setError("Risk analizi için personel seçilmelidir.");
      return;
    }

    setAnalysisLoading(true);
    setAnalysisOpen(true);
    setAnalysis(null);
    setError("");

    try {
      setAnalysis(await hrAssetService.analyzePersonnel(selectedPersonnelId));
    } catch (err) {
      setError(messageOf(err));
      setAnalysisOpen(false);
    } finally {
      setAnalysisLoading(false);
    }
  }

  async function openQr(item: AssetAssignment) {
    setQrItem(item);
    setQrDataUrl("");
    setError("");

    try {
      const value = JSON.stringify({
        type: "ENDERUN_HR_ASSET",
        id: item.id,
        assetCode: item.assetCode,
        serialNumber: item.serialNumber ?? null,
        personnelId: item.personnelId,
        projectId: item.projectId ?? null,
        status: item.status,
      });

      const dataUrl = await QRCode.toDataURL(value, {
        width: 320,
        margin: 2,
        errorCorrectionLevel: "M",
      });

      setQrDataUrl(dataUrl);
    } catch (err) {
      setQrItem(null);
      setError(messageOf(err));
    }
  }

  function printAssetDocument(item: AssetAssignment) {
    const employee = personnelMap.get(item.personnelId);
    const project = item.projectId ? projectMap.get(item.projectId) : null;
    const company = companies.find((x) => x.id === item.companyId);

    const popup = window.open("", "_blank", "width=980,height=760");
    if (!popup) {
      setError("Yazdırma penceresi açılamadı. Tarayıcı açılır pencere iznini kontrol edin.");
      return;
    }

    const safe = (value: unknown) =>
      String(value ?? "-")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;");

    popup.document.write(`<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8"/>
<title>Zimmet Tutanağı - ${safe(item.assetCode)}</title>
<style>
  body{font-family:Arial,sans-serif;color:#111827;margin:36px}
  .head{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:3px solid #0f766e;padding-bottom:16px;margin-bottom:24px}
  h1{font-size:24px;margin:0}.sub{color:#64748b;margin-top:6px}
  table{width:100%;border-collapse:collapse;margin-top:18px}
  td,th{border:1px solid #cbd5e1;padding:10px;text-align:left}
  th{background:#f1f5f9;width:24%}
  .note{margin-top:22px;border:1px solid #cbd5e1;padding:14px;min-height:70px}
  .signatures{display:grid;grid-template-columns:repeat(3,1fr);gap:28px;margin-top:70px;text-align:center}
  .line{border-top:1px solid #111827;padding-top:8px}
  .footer{margin-top:40px;font-size:11px;color:#64748b}
  @media print{button{display:none}body{margin:18mm}}
</style>
</head>
<body>
  <div class="head">
    <div>
      <h1>ENDERUN ENERJİ</h1>
      <div class="sub">Personel Zimmet Teslim / İade Tutanağı</div>
    </div>
    <div><strong>Tutanak No:</strong> ${safe(item.id.slice(0,8).toUpperCase())}</div>
  </div>
  <table>
    <tr><th>Şirket</th><td>${safe(company?.name)}</td><th>Durum</th><td>${safe(item.statusName || statusNames[item.status])}</td></tr>
    <tr><th>Personel</th><td>${safe(employee?.fullName)}</td><th>Sicil No</th><td>${safe(employee?.employeeNumber)}</td></tr>
    <tr><th>Proje</th><td>${safe(project ? `${project.code} - ${project.name}` : "Projesiz")}</td><th>Ekipman Türü</th><td>${safe(item.assetType)}</td></tr>
    <tr><th>Ekipman Kodu</th><td>${safe(item.assetCode)}</td><th>Ekipman Adı</th><td>${safe(item.assetName)}</td></tr>
    <tr><th>Seri Numarası</th><td>${safe(item.serialNumber)}</td><th>Teslim Tarihi</th><td>${safe(formatDate(item.assignmentDate))}</td></tr>
    <tr><th>Planlanan İade</th><td>${safe(formatDate(item.plannedReturnDate))}</td><th>Gerçek İade</th><td>${safe(formatDate(item.actualReturnDate))}</td></tr>
    <tr><th>Teslim Durumu</th><td colspan="3">${safe(item.conditionAtAssignment)}</td></tr>
    <tr><th>İade Durumu</th><td colspan="3">${safe(item.conditionAtReturn)}</td></tr>
  </table>
  <div class="note"><strong>Açıklama / Notlar</strong><br/><br/>${safe(item.notes)}</div>
  <p>Yukarıda bilgileri bulunan ekipman, belirtilen durumu ile personele teslim edilmiş / personelden teslim alınmıştır.</p>
  <div class="signatures">
    <div class="line">Teslim Eden</div>
    <div class="line">Teslim Alan Personel</div>
    <div class="line">İK / Birim Yetkilisi</div>
  </div>
  <div class="footer">Enderun AI Yönetim Sistemi tarafından oluşturulmuştur · ${safe(new Date().toLocaleString("tr-TR"))}</div>
  <script>window.onload=()=>setTimeout(()=>window.print(),300)<\/script>
</body>
</html>`);
    popup.document.close();
  }

  async function deleteItem(item: AssetAssignment) {
    if (!window.confirm(`${item.assetCode} zimmet kaydı kalıcı olarak silinsin mi?`)) return;
    setBusyId(item.id);
    setError("");
    try {
      await hrAssetService.delete(item.id);
      setSuccess("Zimmet kaydı silindi.");
      await loadData();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusyId("");
    }
  }

  const cards = [
    ["Toplam", dashboard?.totalCount ?? 0, "#0f172a"],
    ["Aktif", dashboard?.assignedCount ?? 0, "#166534"],
    ["İade", dashboard?.returnedCount ?? 0, "#1d4ed8"],
    ["Kayıp", dashboard?.lostCount ?? 0, "#b91c1c"],
    ["Hasarlı", dashboard?.damagedCount ?? 0, "#c2410c"],
    ["Geciken", dashboard?.overdueCount ?? 0, "#92400e"],
  ] as const;

  return (
    <ErpShell
      title="Zimmet Yönetim Merkezi"
      description="Personel ve proje ekipmanlarının teslim, iade, devir ve risk takibi"
    >
      <div style={{ display: "grid", gap: 18 }}>
        {success && (
          <section style={{ ...panel, padding: 14, background: "#f0fdf4", color: "#166534", fontWeight: 800 }}>
            {success}
          </section>
        )}
        {error && (
          <section style={{ ...panel, padding: 14, background: "#fef2f2", color: "#b91c1c", fontWeight: 800 }}>
            {error}
          </section>
        )}

        <section style={{ ...panel, padding: 18, display: "flex", justifyContent: "space-between", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
          <div>
            <span style={{ color: "#0f766e", fontWeight: 900, fontSize: 12 }}>ENDERUN AI · İNSAN KAYNAKLARI</span>
            <h2 style={{ margin: "6px 0 3px", color: "#0f172a" }}>Zimmet ve Ekipman Takibi</h2>
            <p style={{ margin: 0, color: "#64748b" }}>Aktif zimmetleri ve riskli ekipmanları tek merkezden yönetin.</p>
          </div>
          <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
            <button
              type="button"
              onClick={() => void openPersonnelAnalysis(personnelId)}
              disabled={!personnelId}
              style={{
                minHeight: 42,
                border: "1px solid #7c3aed",
                borderRadius: 10,
                padding: "0 16px",
                background: "#fff",
                color: "#6d28d9",
                fontWeight: 900,
                cursor: personnelId ? "pointer" : "not-allowed",
                opacity: personnelId ? 1 : .5,
              }}
            >
              Personel Risk Analizi
            </button>
            <button
              type="button"
              onClick={() => setInventoryDialogOpen(true)}
              style={{ minHeight: 42, border: 0, borderRadius: 10, padding: "0 18px", background: "#0369a1", color: "#fff", fontWeight: 900, cursor: "pointer" }}
            >
              + Depodan Zimmet
            </button>
            <button type="button" onClick={openCreate} style={{ minHeight: 42, border: 0, borderRadius: 10, padding: "0 18px", background: "#0f766e", color: "#fff", fontWeight: 900, cursor: "pointer" }}>
              + Manuel Zimmet
            </button>
          </div>
        </section>

        <section style={{ display: "grid", gridTemplateColumns: "repeat(6,minmax(0,1fr))", gap: 12 }}>
          {cards.map(([label, value, color]) => (
            <article key={label} style={{ ...panel, padding: 16 }}>
              <span style={{ color: "#64748b", fontSize: 12, fontWeight: 800 }}>{label}</span>
              <strong style={{ display: "block", marginTop: 8, fontSize: 28, color }}>{value}</strong>
            </article>
          ))}
        </section>

        <section style={{ ...panel, padding: 16, display: "grid", gridTemplateColumns: "repeat(6,minmax(150px,1fr))", gap: 12 }}>
          <Field label="Şirket">
            <select value={companyId} onChange={(e) => setCompanyId(e.target.value)} style={input}>
              {companies.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </Field>
          <Field label="Personel">
            <select value={personnelId} onChange={(e) => setPersonnelId(e.target.value)} style={input}>
              <option value="">Tümü</option>
              {personnel.map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
            </select>
          </Field>
          <Field label="Proje">
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)} style={input}>
              <option value="">Tümü</option>
              {projects.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
            </select>
          </Field>
          <Field label="Durum">
            <select value={status} onChange={(e) => setStatus(e.target.value === "" ? "" : Number(e.target.value))} style={input}>
              <option value="">Tümü</option>
              {Object.entries(statusNames).map(([key, value]) => <option key={key} value={key}>{value}</option>)}
            </select>
          </Field>
          <Field label="Ekipman Türü">
            <select value={assetType} onChange={(e) => setAssetType(e.target.value)} style={input}>
              <option value="">Tümü</option>
              {assetTypes.map((x) => <option key={x} value={x}>{x}</option>)}
            </select>
          </Field>
          <Field label="Arama">
            <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Kod, ad, seri no..." style={input} />
          </Field>

          <label style={{ display: "flex", alignItems: "center", gap: 8, fontWeight: 800, color: "#334155" }}>
            <input type="checkbox" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
            Sadece gecikenler
          </label>

          <button type="button" onClick={() => void loadData()} disabled={loading} style={{ minHeight: 42, border: 0, borderRadius: 10, padding: "0 16px", background: "#334155", color: "#fff", fontWeight: 800, cursor: "pointer" }}>
            {loading ? "Yükleniyor..." : "Yenile"}
          </button>
        </section>

        {dashboard && dashboard.assetTypes.length > 0 && (
          <section style={{ ...panel, padding: 16 }}>
            <h3 style={{ margin: "0 0 12px" }}>Ekipman Türü Dağılımı</h3>
            <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
              {dashboard.assetTypes.map((x) => (
                <button key={x.assetType} type="button" onClick={() => setAssetType(x.assetType)} style={{ border: "1px solid #cbd5e1", borderRadius: 999, padding: "8px 11px", background: "#f8fafc", cursor: "pointer" }}>
                  <strong>{x.assetType}</strong> · {x.totalCount} kayıt · {x.overdueCount} geciken
                </button>
              ))}
            </div>
          </section>
        )}

        <section style={{ ...panel, overflow: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 1350 }}>
            <thead>
              <tr style={{ background: "#f8fafc" }}>
                {["Personel","Proje","Tür","Kod / Ekipman","Seri No","Teslim","Planlanan İade","Durum","İşlemler"].map((h) => (
                  <th key={h} style={{ padding: 13, textAlign: "left", borderBottom: "1px solid #e2e8f0" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const active = item.status === HrAssetAssignmentStatus.Assigned;
                return (
                  <tr key={item.id}>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7", fontWeight: 800 }}>
                      {personnelMap.get(item.personnelId)?.fullName ?? "Personel"}
                    </td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>
                      {item.projectId ? projectMap.get(item.projectId)?.name ?? "Proje" : "-"}
                    </td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>{item.assetType}</td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>
                      <strong>{item.assetCode}</strong><br/><span style={{ color: "#64748b", fontSize: 12 }}>{item.assetName}</span>
                    </td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>{item.serialNumber || "-"}</td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>{formatDate(item.assignmentDate)}</td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>{formatDate(item.plannedReturnDate)}</td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}><StatusBadge item={item}/></td>
                    <td style={{ padding: 13, borderBottom: "1px solid #eef2f7" }}>
                      <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                        <button onClick={() => openEdit(item)} disabled={busyId === item.id} style={actionButton("#334155")}>Düzenle</button>
                        <button onClick={() => printAssetDocument(item)} style={actionButton("#0369a1")}>Tutanak</button>
                        <button onClick={() => void openQr(item)} style={actionButton("#6d28d9")}>QR</button>
                        <button onClick={() => void openPersonnelAnalysis(item.personnelId)} style={actionButton("#9333ea")}>Risk</button>
                        {active && <>
                          <button onClick={() => openAction("return", item)} style={actionButton("#1d4ed8")}>İade</button>
                          <button onClick={() => openAction("transfer", item)} style={actionButton("#7c3aed")}>Personel Devri</button>
                          <button onClick={() => openAction("project", item)} style={actionButton("#0f766e")}>Proje</button>
                          <button onClick={() => openAction("damaged", item)} style={actionButton("#c2410c")}>Hasarlı</button>
                          <button onClick={() => openAction("lost", item)} style={actionButton("#b91c1c")}>Kayıp</button>
                          <button onClick={() => openAction("cancel", item)} style={actionButton("#475569")}>İptal</button>
                        </>}
                        {item.status === HrAssetAssignmentStatus.Cancelled && (
                          <button onClick={() => void deleteItem(item)} style={actionButton("#7f1d1d")}>Sil</button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
              {!loading && items.length === 0 && (
                <tr><td colSpan={9} style={{ padding: 36, textAlign: "center", color: "#64748b" }}>Filtrelere uygun zimmet kaydı bulunmuyor.</td></tr>
              )}
            </tbody>
          </table>
        </section>
      </div>

      {inventoryDialogOpen && (
        <HrAssetInventoryDialog
          companies={companies}
          initialCompanyId={companyId}
          personnel={personnel}
          projects={projects}
          onClose={() => setInventoryDialogOpen(false)}
          onSuccess={async (message) => {
            setSuccess(message);
            await loadData();
          }}
        />
      )}

      {analysisOpen && (
        <div style={overlay} role="dialog" aria-modal="true">
          <section style={{ ...modal, width: "min(900px,100%)" }}>
            <header style={modalHeader}>
              <div>
                <h2 style={{ margin: 0 }}>Personel Zimmet Risk Analizi</h2>
                <p style={{ margin: "5px 0 0", color: "#64748b" }}>
                  Kayıp, hasar, gecikme ve aktif zimmet yoğunluğu analizi
                </p>
              </div>
              <button type="button" onClick={() => setAnalysisOpen(false)} style={closeButton}>×</button>
            </header>

            <div style={{ padding: 20, maxHeight: "72vh", overflow: "auto" }}>
              {analysisLoading && <p>Analiz yükleniyor...</p>}

              {analysis && (
                <div style={{ display: "grid", gap: 16 }}>
                  <section style={{ ...panel, padding: 16, display: "flex", justifyContent: "space-between", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
                    <div>
                      <h3 style={{ margin: 0 }}>{analysis.fullName}</h3>
                      <p style={{ margin: "7px 0 0", color: "#64748b" }}>{analysis.summary}</p>
                    </div>
                    <div style={{
                      minWidth: 130,
                      textAlign: "center",
                      padding: 14,
                      borderRadius: 14,
                      background: analysis.riskLevel === "High" ? "#fee2e2" : analysis.riskLevel === "Medium" ? "#fef3c7" : "#dcfce7",
                      color: analysis.riskLevel === "High" ? "#b91c1c" : analysis.riskLevel === "Medium" ? "#92400e" : "#166534",
                    }}>
                      <strong style={{ display: "block", fontSize: 30 }}>{analysis.riskScore}</strong>
                      <span style={{ fontWeight: 900 }}>
                        {analysis.riskLevel === "High" ? "Yüksek Risk" : analysis.riskLevel === "Medium" ? "Orta Risk" : "Düşük Risk"}
                      </span>
                    </div>
                  </section>

                  <section style={{ display: "grid", gridTemplateColumns: "repeat(6,minmax(0,1fr))", gap: 10 }}>
                    {[
                      ["Toplam", analysis.totalAssignmentCount],
                      ["Aktif", analysis.activeAssignmentCount],
                      ["İade", analysis.returnedCount],
                      ["Kayıp", analysis.lostCount],
                      ["Hasarlı", analysis.damagedCount],
                      ["Geciken", analysis.overdueCount],
                    ].map(([label, value]) => (
                      <article key={String(label)} style={{ ...panel, padding: 13 }}>
                        <span style={{ color: "#64748b", fontSize: 12, fontWeight: 800 }}>{label}</span>
                        <strong style={{ display: "block", marginTop: 6, fontSize: 24 }}>{value}</strong>
                      </article>
                    ))}
                  </section>

                  <section style={{ display: "grid", gridTemplateColumns: "repeat(2,minmax(0,1fr))", gap: 14 }}>
                    <article style={{ ...panel, padding: 16 }}>
                      <h3 style={{ marginTop: 0 }}>Tespitler</h3>
                      <ul style={{ marginBottom: 0, paddingLeft: 20 }}>
                        {analysis.findings.map((x) => <li key={x} style={{ marginBottom: 8 }}>{x}</li>)}
                      </ul>
                    </article>
                    <article style={{ ...panel, padding: 16 }}>
                      <h3 style={{ marginTop: 0 }}>Önerilen Aksiyonlar</h3>
                      <ul style={{ marginBottom: 0, paddingLeft: 20 }}>
                        {analysis.recommendations.map((x) => <li key={x} style={{ marginBottom: 8 }}>{x}</li>)}
                      </ul>
                    </article>
                  </section>

                  <article style={{ ...panel, padding: 16 }}>
                    <h3 style={{ marginTop: 0 }}>Zimmet Geçmişi</h3>
                    <div style={{ overflow: "auto" }}>
                      <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 720 }}>
                        <thead><tr style={{ background: "#f8fafc" }}>
                          {["Kod","Ekipman","Teslim","İade","Durum"].map((x) => <th key={x} style={{ padding: 10, textAlign: "left" }}>{x}</th>)}
                        </tr></thead>
                        <tbody>
                          {analysis.assets.map((x) => (
                            <tr key={x.id}>
                              <td style={{ padding: 10, borderTop: "1px solid #e2e8f0" }}>{x.assetCode}</td>
                              <td style={{ padding: 10, borderTop: "1px solid #e2e8f0" }}>{x.assetName}</td>
                              <td style={{ padding: 10, borderTop: "1px solid #e2e8f0" }}>{formatDate(x.assignmentDate)}</td>
                              <td style={{ padding: 10, borderTop: "1px solid #e2e8f0" }}>{formatDate(x.actualReturnDate)}</td>
                              <td style={{ padding: 10, borderTop: "1px solid #e2e8f0" }}><StatusBadge item={x}/></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </article>
                </div>
              )}
            </div>
          </section>
        </div>
      )}

      {qrItem && (
        <div style={overlay} role="dialog" aria-modal="true">
          <section style={{ ...modal, width: "min(500px,100%)" }}>
            <header style={modalHeader}>
              <div>
                <h2 style={{ margin: 0 }}>QR Kodlu Ekipman Kartı</h2>
                <p style={{ margin: "5px 0 0", color: "#64748b" }}>{qrItem.assetCode} · {qrItem.assetName}</p>
              </div>
              <button type="button" onClick={() => setQrItem(null)} style={closeButton}>×</button>
            </header>
            <div style={{ padding: 24, textAlign: "center" }}>
              {qrDataUrl ? (
                <>
                  <img src={qrDataUrl} alt="Ekipman QR kodu" width={300} height={300} style={{ maxWidth: "100%", height: "auto" }}/>
                  <h3 style={{ marginBottom: 5 }}>{qrItem.assetCode}</h3>
                  <p style={{ margin: 0, color: "#64748b" }}>{qrItem.serialNumber || "Seri numarası yok"}</p>
                </>
              ) : <p>QR kod oluşturuluyor...</p>}
            </div>
            <footer style={modalFooter}>
              <button type="button" onClick={() => window.print()} style={secondaryButton}>Yazdır</button>
              {qrDataUrl && <a href={qrDataUrl} download={`Zimmet-QR-${qrItem.assetCode}.png`} style={{ ...primaryButton, display: "inline-flex", alignItems: "center", textDecoration: "none" }}>PNG İndir</a>}
            </footer>
          </section>
        </div>
      )}

      {formOpen && (
        <div style={overlay} role="dialog" aria-modal="true">
          <form onSubmit={submitForm} style={modal}>
            <header style={modalHeader}>
              <div>
                <h2 style={{ margin: 0 }}>{editing ? "Zimmet Kaydını Düzenle" : "Yeni Zimmet Kaydı"}</h2>
                <p style={{ margin: "5px 0 0", color: "#64748b" }}>Ekipman, personel ve teslim bilgilerini girin.</p>
              </div>
              <button type="button" onClick={() => setFormOpen(false)} style={closeButton}>×</button>
            </header>
            <div style={{ padding: 20, display: "grid", gridTemplateColumns: "repeat(2,minmax(0,1fr))", gap: 13, maxHeight: "70vh", overflow: "auto" }}>
              <Field label="Personel" required>
                <select value={form.personnelId} onChange={(e) => setForm({ ...form, personnelId: e.target.value })} style={input}>
                  <option value="">Seçiniz</option>
                  {personnel.map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
                </select>
              </Field>
              <Field label="Proje">
                <select value={form.projectId} onChange={(e) => setForm({ ...form, projectId: e.target.value })} style={input}>
                  <option value="">Projesiz</option>
                  {projects.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
                </select>
              </Field>
              <Field label="Ekipman Türü" required><input value={form.assetType} onChange={(e) => setForm({ ...form, assetType: e.target.value })} placeholder="Laptop, telefon, el aleti..." style={input}/></Field>
              <Field label="Ekipman Kodu" required><input value={form.assetCode} onChange={(e) => setForm({ ...form, assetCode: e.target.value })} style={input}/></Field>
              <Field label="Ekipman Adı" required><input value={form.assetName} onChange={(e) => setForm({ ...form, assetName: e.target.value })} style={input}/></Field>
              <Field label="Seri Numarası"><input value={form.serialNumber} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} style={input}/></Field>
              <Field label="Teslim Tarihi" required><input type="date" value={form.assignmentDate} onChange={(e) => setForm({ ...form, assignmentDate: e.target.value })} style={input}/></Field>
              <Field label="Planlanan İade"><input type="date" value={form.plannedReturnDate} onChange={(e) => setForm({ ...form, plannedReturnDate: e.target.value })} style={input}/></Field>
              <Field label="Teslim Durumu"><input value={form.conditionAtAssignment} onChange={(e) => setForm({ ...form, conditionAtAssignment: e.target.value })} placeholder="Yeni, iyi, kullanılmış..." style={input}/></Field>
              <Field label="Belge Yolu"><input value={form.documentPath} onChange={(e) => setForm({ ...form, documentPath: e.target.value })} style={input}/></Field>
              <div style={{ gridColumn: "1 / -1" }}><Field label="Notlar"><textarea rows={4} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} style={{ ...input, resize: "vertical" }}/></Field></div>
            </div>
            <footer style={modalFooter}>
              <button type="button" onClick={() => setFormOpen(false)} style={secondaryButton}>Vazgeç</button>
              <button type="submit" disabled={busyId === (editing?.id ?? "create")} style={primaryButton}>{busyId ? "Kaydediliyor..." : "Kaydet"}</button>
            </footer>
          </form>
        </div>
      )}

      {actionMode && actionItem && (
        <div style={overlay} role="dialog" aria-modal="true">
          <section style={modal}>
            <header style={modalHeader}>
              <div>
                <h2 style={{ margin: 0 }}>{actionTitle(actionMode)}</h2>
                <p style={{ margin: "5px 0 0", color: "#64748b" }}>{actionItem.assetCode} · {actionItem.assetName}</p>
              </div>
              <button type="button" onClick={() => setActionMode(null)} style={closeButton}>×</button>
            </header>
            <div style={{ padding: 20, display: "grid", gap: 13 }}>
              {["return","lost","damaged","transfer"].includes(actionMode) && (
                <Field label={actionMode === "transfer" ? "Devir Tarihi" : "İşlem Tarihi"}>
                  <input type="date" value={actionDate} onChange={(e) => setActionDate(e.target.value)} style={input}/>
                </Field>
              )}
              {actionMode === "transfer" && (
                <Field label="Yeni Personel" required>
                  <select value={actionPersonnelId} onChange={(e) => setActionPersonnelId(e.target.value)} style={input}>
                    <option value="">Seçiniz</option>
                    {personnel.filter((x) => x.id !== actionItem.personnelId).map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
                  </select>
                </Field>
              )}
              {(actionMode === "project" || actionMode === "transfer") && (
                <Field label="Yeni Proje">
                  <select value={actionProjectId} onChange={(e) => setActionProjectId(e.target.value)} style={input}>
                    <option value="">Projesiz</option>
                    {projects.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
                  </select>
                </Field>
              )}
              <Field label={actionTextLabel(actionMode)}>
                <textarea rows={5} value={actionText} onChange={(e) => setActionText(e.target.value)} style={{ ...input, resize: "vertical" }}/>
              </Field>
            </div>
            <footer style={modalFooter}>
              <button type="button" onClick={() => setActionMode(null)} style={secondaryButton}>Vazgeç</button>
              <button type="button" onClick={() => void submitAction()} disabled={busyId === actionItem.id} style={{ ...primaryButton, background: actionMode === "lost" || actionMode === "cancel" ? "#b91c1c" : "#0f766e" }}>
                {busyId ? "İşleniyor..." : "İşlemi Tamamla"}
              </button>
            </footer>
          </section>
        </div>
      )}
    </ErpShell>
  );
}

function actionButton(background: string) {
  return {
    minHeight: 32,
    border: 0,
    borderRadius: 8,
    padding: "0 10px",
    background,
    color: "#fff",
    fontSize: 11,
    fontWeight: 800,
    cursor: "pointer",
  } as const;
}

function actionTitle(mode: Exclude<ActionMode, null>) {
  return {
    return: "Ekipmanı İade Al",
    lost: "Kayıp Bildirimi",
    damaged: "Hasar Bildirimi",
    project: "Proje Değiştir",
    transfer: "Başka Personele Devret",
    cancel: "Zimmeti İptal Et",
  }[mode];
}

function actionTextLabel(mode: Exclude<ActionMode, null>) {
  return {
    return: "İade Durumu / Açıklama",
    lost: "Kayıp Nedeni *",
    damaged: "Hasar Açıklaması *",
    project: "Değişiklik Notu",
    transfer: "Devir Durumu / Açıklama",
    cancel: "İptal Nedeni *",
  }[mode];
}

const overlay = {
  position: "fixed",
  inset: 0,
  zIndex: 1200,
  display: "grid",
  placeItems: "center",
  padding: 20,
  background: "rgba(15,23,42,.58)",
} as const;

const modal = {
  width: "min(760px,100%)",
  maxHeight: "92vh",
  overflow: "hidden",
  borderRadius: 18,
  background: "#fff",
  border: "1px solid #e2e8f0",
  boxShadow: "0 24px 70px rgba(15,23,42,.3)",
} as const;

const modalHeader = {
  display: "flex",
  justifyContent: "space-between",
  alignItems: "flex-start",
  gap: 12,
  padding: "18px 20px",
  borderBottom: "1px solid #e2e8f0",
} as const;

const modalFooter = {
  display: "flex",
  justifyContent: "flex-end",
  gap: 10,
  padding: "16px 20px",
  borderTop: "1px solid #e2e8f0",
} as const;

const closeButton = {
  width: 34,
  height: 34,
  border: 0,
  borderRadius: 9,
  background: "#f1f5f9",
  color: "#334155",
  fontSize: 22,
  cursor: "pointer",
} as const;

const primaryButton = {
  minHeight: 40,
  border: 0,
  borderRadius: 10,
  padding: "0 18px",
  background: "#0f766e",
  color: "#fff",
  fontWeight: 900,
  cursor: "pointer",
} as const;

const secondaryButton = {
  minHeight: 40,
  border: "1px solid #cbd5e1",
  borderRadius: 10,
  padding: "0 18px",
  background: "#fff",
  color: "#334155",
  fontWeight: 900,
  cursor: "pointer",
} as const;
