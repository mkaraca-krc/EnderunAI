"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import {
  EMPTY_VALUE,
  currencyMoney,
  date,
  dateTime,
  decimal,
} from "@/lib/format/turkish";
import { usePermissions } from "@/lib/use-permissions";
import { useModuleActions } from "@/lib/auth/module-actions";
import ProjectDocumentsSection from "@/components/projects/project-documents-section";
import ProjectDangerZone from "@/components/projects/project-danger-zone";
import { projectService } from "@/services/project.service";
import { CONTRACT_TYPE_LABELS } from "@/services/progress-tracking.service";
import { PROGRESS_PAYMENT_PERIODS } from "@/services/offer.service";

const PROGRESS_PAYMENT_PERIOD_LABELS: Record<number, string> =
  Object.fromEntries(PROGRESS_PAYMENT_PERIODS);

import {
  projectProfitabilityService,
  type ProjectProfitability,
} from "@/services/project-profitability.service";

import {
  projectDailyReportsRollupService,
  type ProjectDailyReportRollupItem,
} from "@/services/project-daily-reports-rollup.service";

import {
  employerPortalService,
  type EmployerPortalLink,
  type EmployerPortalEmailLogItem,
} from "@/services/employer-portal.service";

import {
  projectSiteAnalysisService,
  type ProjectSiteAnalysisResponse,
} from "@/services/project-site-analysis.service";

import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";

import {
  projectCostService,
  type ProjectCostBreakdown,
} from "@/services/project-cost.service";

import {
  projectLaborCostService,
  type ProjectLaborCostBreakdown,
} from "@/services/project-labor-cost.service";

import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  expenseService,
  type ExpenseCategory,
} from "@/services/expense.service";




type Warehouse = {
  id: string;
  code: string;
  name: string;
  type: number;
  isActive: boolean;
};

type ProjectDetail = {
  id: string;
  /** Uç zaten döndürüyordu; tipte eksikti. Gider kaydı şirket ister. */
  companyId: string;
  companyName: string;
  branchName: string;
  employerName: string;
  code: string;
  name: string;
  contractNumber?: string | null;
  contractDate?: string | null;
  contractAmount?: number | null;
  currencyCode: string;
  vatRate: number;
  withholdingRate?: string | null;
  increaseRate: number;
  cashRetentionRate: number;
  withholdingTaxRate: number;
  materialDeductionRate: number;
  /** Keşif–gerçekleşen sapmasının nasıl yorumlanacağını belirler. */
  contractType: number;
  progressPaymentPeriod?: number | null;
  paymentTerms?: string | null;
  /** Bu proje hangi kazanılan tekliften doğdu (varsa). */
  sourceOfferId?: string | null;
  sourceOfferNumber?: string | null;
  sourceOfferTitle?: string | null;
  deviationAlertThresholdRate: number;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  city?: string | null;
  district?: string | null;
  address?: string | null;
  status: number;
  healthStatus: number;
  isArchived: boolean;
  archivedAtUtc?: string | null;
  archiveReason?: string | null;
  warehouses: Warehouse[];
};

type ProjectModule = {
  label: string;
  href: string;
  icon: string;
  text: string;
  permission?: string;
};

const modules: ProjectModule[] = [
  // Proje bazlı olanlar "santiyeler" gibi göreli yazılır; aşağıdaki
  // bağlantı kurucusu başına /projeler/{id}/ ekler.
  //
  // permission alanı yalnızca kâr marjı / maliyet tutarı taşıyan
  // kartlarda dolu: uçları hakediş görüntüleme iznine bağlı, kart da
  // aynı kapıda olmalı ki kullanıcı 403 alacağı ekrana tıklamasın.
  { label: "Şantiyeler", href: "santiyeler", icon: "▨", text: "Lokasyon kırılımı, personel atamaları ve depolar" },
  { label: "İş Programı", href: "is-programi", icon: "▰", text: "Gantt, kritik yol ve gecikme takibi" },
  { permission: "hakedis.view", label: "İcmal İlerlemesi", href: "icmal-ilerleme", icon: "◱", text: "Sözleşme, saha ve işveren kabulü" },
  { label: "İcmal Kısımları", href: "kisimlar", icon: "▤", text: "Projenin imalat kırılımı" },
  { permission: "hakedis.view", label: "Maliyet Analizi", href: "maliyet-analizi", icon: "₸", text: "İcmal öngörüsü, gerçekleşen maliyet ve kâr" },
  { permission: "hakedis.view", label: "Poz Kâr Analizi", href: "kar-analizi", icon: "◈", text: "Poz bazında dört fiyat ve kâr" },
  { label: "Hakedişler", href: "/hakedis", icon: "▧", text: "Hakediş kayıtları ve kontrolleri" },
  { permission: "purchasing-requests.view", label: "Malzeme İhtiyacı", href: "malzeme-ihtiyaci", icon: "⊞", text: "İcmal + reçeteden ihtiyaç, depo mevcudu ve eksik" },
  { label: "Satın Alma", href: "/satin-alma", icon: "⌑", text: "Malzeme talepleri ve teklifler" },
  { label: "Personel", href: "/personel", icon: "♙", text: "Projeye bağlı personel" },
  { label: "Depo & Stok", href: "/depo-stok", icon: "⌂", text: "Şantiye deposu ve stoklar" },
  { label: "Finans", href: "/finans", icon: "₺", text: "Proje finansal görünümü" },
  { label: "Dokümanlar", href: "/dokumanlar", icon: "□", text: "Sözleşme ve proje evrakları" },
  { label: "AI Analizleri", href: "/ai-asistan", icon: "⌘", text: "Risk, eksik ve öneriler" },
];

function formatDate(value?: string | null) {
  return date(value);
}

function formatMoney(value?: number | null, currency = "TRY") {
  return currencyMoney(value, currency);
}

/**
 * Sözleşme oranları — dört haneye kadar, sondaki sıfırlar yazılmadan.
 *
 * Sabit hane olamaz: stopaj %0, teminat %5, artış %12,3456 olabiliyor
 * ve hepsi "%5,0000" gibi yazılsaydı sözleşme künyesi okunmazdı.
 * Kırpma da olamaz: %12,3456 sözleşmede yazan rakam.
 */
function formatPercentage(value?: number | null) {
  if (value === null || value === undefined) return EMPTY_VALUE;
  return `%${decimal(value, 4)}`;
}

export default function ProjectCenterPage() {
  const { has } = usePermissions();

  // Gider kategorileri: elle girilebilenler (otomatik kategoriler uçtan
  // zaten gelmiyor). Kategori listesi gelmezse form kaydedemez ve
  // kullanıcı bunu boş açılan listeden görür.
  const canManageExpense = has("expense.manage");

  /*
   * İşveren portalı ve işçilik kaydı BU EKRANDA ama izinleri proje
   * modülünde değil — ucun kendi RequirePermission'ı:
   *   POST projects/{id}/employer-portal-link            -> employer-portal.create
   *   POST projects/{id}/employer-portal-link/revoke     -> employer-portal.delete
   *   POST projects/{id}/employer-portal-link/send-email -> employer-portal.edit
   *   POST projects/{id}/labor-costs                     -> personnel.create
   */
  const portalActions = useModuleActions("employer-portal");
  const laborActions = useModuleActions("personnel");

  // İzin isteyen kartlar yalnızca yetkiliye görünür; izin alanı boş
  // olan kartlar herkeste durur.
  const visibleModules = useMemo(
    () => modules.filter((x) => !x.permission || has(x.permission)),
    [has]
  );
  const params = useParams<{ id: string }>();
  const [project, setProject] = useState<ProjectDetail | null>(null);

  const [profitability, setProfitability] =
    useState<ProjectProfitability | null>(null);

  const [dailyReports, setDailyReports] =
    useState<ProjectDailyReportRollupItem[]>([]);

  const [portalLink, setPortalLink] = useState<EmployerPortalLink>(null);
  const [emailConfigured, setEmailConfigured] = useState(true);
  const [portalLoading, setPortalLoading] = useState(false);
  const [revokeOpen, setRevokeOpen] = useState(false);
  const [portalError, setPortalError] = useState("");
  const [portalCopied, setPortalCopied] = useState(false);

  const [emailForm, setEmailForm] = useState({ employerName: "", employerEmail: "" });
  const [sendingEmail, setSendingEmail] = useState(false);
  const [emailNotice, setEmailNotice] = useState("");
  const [emailLog, setEmailLog] = useState<EmployerPortalEmailLogItem[]>([]);


  const [siteAnalysis, setSiteAnalysis] =
    useState<ProjectSiteAnalysisResponse | null>(null);

  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);
  const [breakdown, setBreakdown] = useState<ProjectCostBreakdown | null>(null);

  const [costSaving, setCostSaving] = useState(false);
  const [costError, setCostError] = useState("");
  /**
   * ELLE MALİYET ARTIK GİDER KAYDIDIR.
   *
   * Proje maliyet defterine doğrudan yazan uç kaldırıldı: aynı maliyeti
   * iki yoldan sisteme sokabilmek ayrışma üretiyordu. Bu form gider
   * kaydı açıyor; proje maliyeti o kaydı zaten okuyor. Kazanç yalnız
   * tek kaynak değil — ödeme yöntemi, elden maskesi, belge ve nakit
   * akış da bu yoldan geliyor.
   */
  const [costForm, setCostForm] = useState({
    projectSiteId: "",
    expenseCategoryId: "",
    costDate: new Date().toISOString().slice(0, 10),
    amount: 0,
    description: "",
    paymentMethod: 0,
    documentType: 2,
  });

  const [expenseCategories, setExpenseCategories] = useState<ExpenseCategory[]>(
    []
  );

  const [projectPersonnel, setProjectPersonnel] = useState<PersonnelListItem[]>([]);
  const [laborBreakdown, setLaborBreakdown] =
    useState<ProjectLaborCostBreakdown | null>(null);

  const [laborSaving, setLaborSaving] = useState(false);
  const [laborError, setLaborError] = useState("");
  const [laborForm, setLaborForm] = useState({
    personnelId: "",
    projectSiteId: "",
    workDate: new Date().toISOString().slice(0, 10),
    normalHours: 8,
    overtimeHours: 0,
    normalCost: 0,
    overtimeCost: 0,
    otherCost: 0,
  });

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // Yükleme işlevi effect'in İÇİNDE tanımlı (on küsur uç birden
  // çağrılıyor ve hepsi params.id'ye bağlı). Dışarı çıkarmak yerine
  // effect'i bir sayaçla yeniden tetikliyoruz: tazeleme düğmesinin
  // ihtiyacı olan tek şey bu.
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const result = (await projectService.getById(
          params.id
        )) as ProjectDetail;

        setProject(result);

        // Gider kategorileri şirkete bağlı; proje gelmeden şirket
        // bilinmiyor. Yetki yoksa hiç istenmiyor — yetkisiz kullanıcıya
        // form da render edilmeyecek.
        if (canManageExpense && result?.companyId) {
          try {
            setExpenseCategories(
              await expenseService.listCategories(result.companyId)
            );
          } catch {
            setExpenseCategories([]);
          }
        }
      } catch (err) {
        setProject(null);
        setError(
          err instanceof Error
            ? err.message
            : "Proje yüklenemedi."
        );
        setLoading(false);
        return;
      }

      const [
        profitabilityResult,
        dailyReportResult,
        siteAnalysisResult,
        sitesResult,
        breakdownResult,
        laborBreakdownResult,
        projectPersonnelResult,
        portalLinkResult,
      ] = await Promise.allSettled([
        projectProfitabilityService.getById(params.id),
        projectDailyReportsRollupService.getRecent(params.id),
        projectSiteAnalysisService.getById(params.id),
        projectSiteService.getAll(params.id),
        projectCostService.getBreakdown(params.id),
        projectLaborCostService.getBreakdown(params.id),
        personnelService.getAll({ projectId: params.id }),
        employerPortalService.get(params.id),
      ]);

      if (profitabilityResult.status === "fulfilled") {
        setProfitability(profitabilityResult.value);
      } else {
        setProfitability(null);
        console.warn(
          "Proje karlılık verisi yüklenemedi:",
          profitabilityResult.reason
        );
      }

      if (dailyReportResult.status === "fulfilled") {
        setDailyReports(dailyReportResult.value);
      } else {
        setDailyReports([]);
        console.warn(
          "Proje günlükleri yüklenemedi:",
          dailyReportResult.reason
        );
      }

      if (siteAnalysisResult.status === "fulfilled") {
        setSiteAnalysis(siteAnalysisResult.value);
      } else {
        setSiteAnalysis(null);
        console.warn(
          "AI şantiye analizi yüklenemedi:",
          siteAnalysisResult.reason
        );
      }

      if (sitesResult.status === "fulfilled") {
        setSites(sitesResult.value);
      } else {
        setSites([]);
        console.warn(
          "Şantiye listesi yüklenemedi:",
          sitesResult.reason
        );
      }

      if (breakdownResult.status === "fulfilled") {
        setBreakdown(breakdownResult.value);
      } else {
        setBreakdown(null);
        console.warn(
          "Maliyet dağılımı yüklenemedi:",
          breakdownResult.reason
        );
      }

      if (laborBreakdownResult.status === "fulfilled") {
        setLaborBreakdown(laborBreakdownResult.value);
      } else {
        setLaborBreakdown(null);
        console.warn(
          "Personel maliyeti dağılımı yüklenemedi:",
          laborBreakdownResult.reason
        );
      }

      if (projectPersonnelResult.status === "fulfilled") {
        setProjectPersonnel(projectPersonnelResult.value);
      } else {
        setProjectPersonnel([]);
        console.warn(
          "Proje personeli yüklenemedi:",
          projectPersonnelResult.reason
        );
      }

      if (portalLinkResult.status === "fulfilled") {
        setPortalLink(portalLinkResult.value.link);
        setEmailConfigured(portalLinkResult.value.emailConfigured);
        setEmailForm({
          employerName: portalLinkResult.value.link?.employerName ?? "",
          employerEmail: portalLinkResult.value.link?.employerEmail ?? "",
        });
      } else {
        setPortalLink(null);
        console.warn(
          "İşveren portalı linki yüklenemedi:",
          portalLinkResult.reason
        );
      }

      try {
        setEmailLog(await employerPortalService.getEmailLog(params.id));
      } catch (err) {
        console.warn("Gönderim logu yüklenemedi:", err);
      }

      setLoading(false);
    }

    if (params.id) {
      load();
    }
  }, [params.id, reloadToken]);

  async function createPortalLink() {
    setPortalLoading(true);
    setPortalError("");
    setPortalCopied(false);

    try {
      await employerPortalService.create(params.id);
      const status = await employerPortalService.get(params.id);
      setPortalLink(status.link);
      setEmailConfigured(status.emailConfigured);
    } catch (err) {
      setPortalError(
        err instanceof Error ? err.message : "İşveren portalı linki oluşturulamadı."
      );
    } finally {
      setPortalLoading(false);
    }
  }

  /**
   * İşveren portal linkini iptal et.
   *
   * YIKICI: iptal edilen link geri gelmiyor, işveren erişimini anında
   * kaybediyor ve yeni link üretilip yeniden gönderilmesi gerekiyor.
   */
  async function revokePortalLink() {
    setRevokeOpen(false);
    setPortalLoading(true);
    setPortalError("");

    try {
      await employerPortalService.revoke(params.id);
      const status = await employerPortalService.get(params.id);
      setPortalLink(status.link);
      setEmailConfigured(status.emailConfigured);
    } catch (err) {
      setPortalError(
        err instanceof Error ? err.message : "İşveren portalı linki iptal edilemedi."
      );
    } finally {
      setPortalLoading(false);
    }
  }

  async function sendPortalEmail(event: React.FormEvent) {
    event.preventDefault();
    if (!portalLink) return;

    setSendingEmail(true);
    setPortalError("");
    setEmailNotice("");

    try {
      const portalUrl = `${window.location.origin}/portal/${portalLink.token}`;

      await employerPortalService.sendEmail(params.id, {
        employerName: emailForm.employerName || undefined,
        employerEmail: emailForm.employerEmail,
        portalUrl,
      });

      setEmailNotice("E-posta gönderildi.");
      setEmailLog(await employerPortalService.getEmailLog(params.id));
    } catch (err) {
      setPortalError(
        err instanceof Error ? err.message : "E-posta gönderilemedi."
      );
    } finally {
      setSendingEmail(false);
    }
  }

  async function copyPortalLink() {
    if (!portalLink) return;
    const url = `${window.location.origin}/portal/${portalLink.token}`;

    try {
      await navigator.clipboard.writeText(url);
      setPortalCopied(true);
      setTimeout(() => setPortalCopied(false), 2000);
    } catch {
      setPortalError("Link panoya kopyalanamadı.");
    }
  }

  async function reloadBreakdown() {
    try {
      const result = await projectCostService.getBreakdown(params.id);
      setBreakdown(result);
    } catch (err) {
      console.warn("Maliyet dağılımı yenilenemedi:", err);
    }
  }

  async function reloadLaborBreakdown() {
    try {
      const result = await projectLaborCostService.getBreakdown(params.id);
      setLaborBreakdown(result);
    } catch (err) {
      console.warn("Personel maliyeti dağılımı yenilenemedi:", err);
    }
  }

  function updateLaborForm<K extends keyof typeof laborForm>(
    key: K,
    value: (typeof laborForm)[K]
  ) {
    setLaborForm((current) => ({ ...current, [key]: value }));
  }

  async function createLaborCost(event: React.FormEvent) {
    event.preventDefault();

    setLaborSaving(true);
    setLaborError("");

    try {
      await projectLaborCostService.create(params.id, {
        personnelId: laborForm.personnelId,
        projectSiteId: laborForm.projectSiteId || null,
        workDate: laborForm.workDate,
        normalHours: laborForm.normalHours,
        overtimeHours: laborForm.overtimeHours,
        normalCost: laborForm.normalCost,
        overtimeCost: laborForm.overtimeCost,
        otherCost: laborForm.otherCost,
      });

      setLaborForm({
        personnelId: "",
        projectSiteId: "",
        workDate: new Date().toISOString().slice(0, 10),
        normalHours: 8,
        overtimeHours: 0,
        normalCost: 0,
        overtimeCost: 0,
        otherCost: 0,
      });

      await reloadLaborBreakdown();
    } catch (err) {
      setLaborError(
        err instanceof Error
          ? err.message
          : "Personel maliyet kaydı oluşturulamadı."
      );
    } finally {
      setLaborSaving(false);
    }
  }

  function updateCostForm<K extends keyof typeof costForm>(
    key: K,
    value: (typeof costForm)[K]
  ) {
    setCostForm((current) => ({ ...current, [key]: value }));
  }

  async function createCostTransaction(event: React.FormEvent) {
    event.preventDefault();

    setCostSaving(true);
    setCostError("");

    try {
      if (!costForm.expenseCategoryId) {
        setCostError("Gider kategorisi seçilmelidir.");
        setCostSaving(false);
        return;
      }

      // Merkez formdan türetiliyor: şantiye seçilmişse şantiye,
      // seçilmemişse projenin kendisi. Kullanıcı "hangi merkez"
      // sorusunu ikinci kez cevaplamaz.
      await expenseService.createEntry({
        companyId: project!.companyId,
        centerType: costForm.projectSiteId ? 2 : 1,
        centerId: costForm.projectSiteId || params.id,
        expenseCategoryId: costForm.expenseCategoryId,
        expenseDate: costForm.costDate,
        amount: costForm.amount,
        description: costForm.description,
        paymentMethod: costForm.paymentMethod,
        documentType: costForm.documentType,
      });

      setCostForm({
        projectSiteId: "",
        expenseCategoryId: "",
        costDate: new Date().toISOString().slice(0, 10),
        amount: 0,
        description: "",
        paymentMethod: 0,
        documentType: 2,
      });

      await reloadBreakdown();
    } catch (err) {
      setCostError(
        err instanceof Error ? err.message : "Gider kaydı oluşturulamadı."
      );
    } finally {
      setCostSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title={project?.name ?? "Proje Merkezi"}
      description={
        project
          ? `${project.code} · ${project.employerName}`
          : "Proje bilgileri yükleniyor"
      }
    >
      <div className="erp-project-breadcrumb rw-breadcrumb-bar">
        <div>
          <Link href="/projeler">Projeler</Link>
          <span>›</span>
          <strong>{project?.name ?? "Proje Merkezi"}</strong>
        </div>

        {/* Proje merkezi on küsur uçtan besleniyor (hakediş, günlük
            rapor, maliyet, portal); hepsi başka kullanıcıların işiyle
            değişiyor ve tazelemenin yolu sayfayı yeniden yüklemekti. */}
        <Button variant="secondary" disabled={loading} onClick={() => setReloadToken((value) => value + 1)}>Yenile</Button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Proje yükleniyor...</div>
      ) : !project ? (
        <div className="erp-panel erp-empty-state">
          <strong>Proje bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="enderun-project-center-hero">
            <div className="enderun-project-center-title">
              <span
                className={`erp-status ${project.isArchived ? "gray" : "green"}`}
              >
                {project.isArchived ? "Arşivlenmiş Proje" : "Aktif Proje"}
              </span>

              {/* Sözleşme tipi rozeti — sapmanın nasıl yorumlanacağını
                  belirlediği için proje kartında görünür olmalı. */}
              <span
                className={`erp-status ${
                  project.contractType === 0 ? "yellow" : "blue"
                }`}
                style={{ marginLeft: 6 }}
              >
                {CONTRACT_TYPE_LABELS[project.contractType] ?? "Belirlenmedi"}
              </span>

              <h2>{project.name}</h2>
              <p>{project.employerName}</p>

              {project.contractType === 0 && (
                <p className="rw-value-warning" style={{ fontSize: 12 }}>
                  Sözleşme tipi belirlenmedi — keşif–gerçekleşen sapması
                  yorumlanamaz. Proje düzenleme ekranından seçin.
                </p>
              )}

              {project.sourceOfferId && (
                <p style={{ marginTop: 6, fontSize: 13 }}>
                  Bu proje{" "}
                  <Link href={`/teklifler/${project.sourceOfferId}`}>
                    {project.sourceOfferNumber} · {project.sourceOfferTitle}
                  </Link>{" "}
                  teklifinden doğdu.
                </p>
              )}

              {project.paymentTerms && (
                <p className="rw-value-muted" style={{ marginTop: 6, fontSize: 13 }}>
                  Ödeme koşulları: {project.paymentTerms}
                </p>
              )}

              <p style={{ marginTop: 6 }}>
                <Link href={`/projeler/${project.id}/metraj-takip`}>
                  Metraj Takip (Keşif vs Gerçekleşen)
                </Link>
              </p>
            </div>

            <div className="enderun-project-center-metrics">
              <div>
                <span>Sözleşme Bedeli</span>
                <strong>
                  {formatMoney(project.contractAmount, project.currencyCode)}
                </strong>
              </div>
              <div>
                <span>Sözleşme No</span>
                <strong>{project.contractNumber || "—"}</strong>
              </div>
              <div>
                <span>Hakediş Periyodu</span>
                <strong>
                  {PROGRESS_PAYMENT_PERIOD_LABELS[
                    project.progressPaymentPeriod ?? 0
                  ] ?? "Belirlenmedi"}
                </strong>
              </div>
              <div>
                <span>Şube</span>
                <strong>{project.branchName}</strong>
              </div>
              <div>
                <span>Şantiye Deposu</span>
                <strong>{project.warehouses.length}</strong>
              </div>
            </div>
          </section>

          <div className="enderun-project-center-tabs">
            <a className="active" href="#genel">Genel</a>
            {visibleModules.map((module) => (
              <Link
                key={module.label}
                href={
                  // Göreli hedefler (başında / yok) proje altına açılır.
                  module.href.startsWith("/")
                    ? module.href
                    : `/projeler/${project.id}/${module.href}`
                }
              >
                {module.label}
              </Link>
            ))}
          </div>

          <section className="erp-panel" id="genel">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Genel Bilgileri</h2>
                <p>Sözleşme ve işveren özeti</p>
              </div>
            </div>

            <div className="erp-detail-grid">
              <div><span>Proje Kodu</span><strong>{project.code}</strong></div>
              <div><span>Şirket</span><strong>{project.companyName}</strong></div>
              <div><span>İşveren</span><strong>{project.employerName}</strong></div>
              <div><span>Şube</span><strong>{project.branchName}</strong></div>
              <div><span>Sözleşme No</span><strong>{project.contractNumber || "—"}</strong></div>
              <div><span>Sözleşme Tarihi</span><strong>{formatDate(project.contractDate)}</strong></div>
              <div><span>Başlangıç</span><strong>{formatDate(project.plannedStartDate)}</strong></div>
              <div><span>Bitiş</span><strong>{formatDate(project.plannedEndDate)}</strong></div>
              <div><span>KDV</span><strong>%{project.vatRate}</strong></div>
              <div><span>Tevkifat</span><strong>{project.withholdingRate || "—"}</strong></div>
              <div className="span-2">
                <span>Şantiye Adresi</span>
                <strong>
                  {[project.address, project.district, project.city]
                    .filter(Boolean)
                    .join(", ") || "—"}
                </strong>
              </div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Şantiyeler</h2>
                <p>Proje altındaki lokasyon kırılımı ve depo/personel bağlantıları</p>
              </div>

              <div style={{ display: "flex", gap: 8 }}>
                <Link
                  href={`/projeler/${project.id}/santiyeler`}
                  className="erp-button secondary"
                >
                  Tümünü Yönet
                </Link>
                <Link
                  href={`/projeler/${project.id}/santiyeler/yeni`}
                  className="erp-button secondary"
                >
                  + Yeni Şantiye
                </Link>
              </div>
            </div>

            {sites.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Henüz şantiye tanımlanmamış</strong>
                <p>
                  Personelin görev yeri şantiye olarak atanabilmesi ve şantiye
                  deposu açılabilmesi için önce en az bir şantiye tanımlanmalı.
                </p>
              </div>
            ) : (
              <div className="erp-project-list">
                {sites.map((site) => (
                  <Link
                    className="erp-project-list-item"
                    href={`/projeler/${project.id}/santiyeler/${site.id}`}
                    key={site.id}
                  >
                    <div>
                      <strong>
                        {site.code} · {site.name}
                      </strong>
                      <span>{site.location || "Konum belirtilmedi"}</span>
                      <span>
                        {site.assignmentCount} personel · {site.warehouseCount} depo
                      </span>
                    </div>

                    <span className={`erp-status ${site.isActive ? "green" : "gray"}`}>
                      {site.isActive ? "Aktif" : "Pasif"}
                    </span>
                  </Link>
                ))}
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>İşveren Portalı</h2>
                <p>
                  Giriş gerektirmeyen, sadece günlük rapor ve izinli fotoğrafları gösteren
                  dış erişim linki
                </p>
              </div>
            </div>

            {portalError && <div className="erp-alert error">{portalError}</div>}

            {!portalLink || !portalLink.isActive ? (
              <div className="erp-empty-state">
                <p>Bu proje için aktif bir işveren portalı linki yok.</p>
                {portalActions.can("create") && (
                  <button
                    type="button"
                    className="erp-button"
                    onClick={() => void createPortalLink()}
                    disabled={portalLoading}
                  >
                    {portalLoading ? "Oluşturuluyor..." : "Portal Linki Oluştur"}
                  </button>
                )}
              </div>
            ) : (
              <div className="erp-form-card">
                <label>
                  <span>Portal Linki</span>
                  <input
                    className="erp-input"
                    readOnly
                    value={
                      typeof window !== "undefined"
                        ? `${window.location.origin}/portal/${portalLink.token}`
                        : `/portal/${portalLink.token}`
                    }
                    onFocus={(e) => e.currentTarget.select()}
                  />
                </label>

                <div className="erp-actions">
                  <button
                    type="button"
                    className="erp-button secondary"
                    onClick={() => void copyPortalLink()}
                  >
                    {portalCopied ? "Kopyalandı ✓" : "Linki Kopyala"}
                  </button>
                  {portalActions.can("create") && (
                    <button
                      type="button"
                      className="erp-button secondary"
                      onClick={() => void createPortalLink()}
                      disabled={portalLoading}
                    >
                      Yeni Link Üret (Eskisini Geçersiz Kılar)
                    </button>
                  )}
                  {/* Linki iptal etmek işverenin erişimini kesiyor:
                      uç employer-portal.delete istiyor, create değil. */}
                  {portalActions.can("delete") && (
                    <button
                      type="button"
                      className="erp-button secondary"
                      onClick={() => setRevokeOpen(true)}
                      disabled={portalLoading}
                    >
                      İptal Et
                    </button>
                  )}
                </div>

                {!emailConfigured ? (
                  <div className="erp-alert error erp-mt">
                    E-posta yapılandırılmamış. Portal linkini şimdilik yalnızca kopyalayıp
                    manuel olarak paylaşabilirsiniz.
                  </div>
                ) : (
                  <>
                    {emailNotice && <div className="erp-alert success erp-mt">{emailNotice}</div>}

                    <form className="erp-form-grid erp-mt" onSubmit={sendPortalEmail}>
                      <label>
                        <span>İşveren Adı</span>
                        <input
                          className="erp-input"
                          value={emailForm.employerName}
                          onChange={(e) =>
                            setEmailForm((current) => ({ ...current, employerName: e.target.value }))
                          }
                        />
                      </label>

                      <label>
                        <span>İşveren E-postası *</span>
                        <input
                          className="erp-input"
                          type="email"
                          required
                          value={emailForm.employerEmail}
                          onChange={(e) =>
                            setEmailForm((current) => ({ ...current, employerEmail: e.target.value }))
                          }
                        />
                      </label>

                      <div className="erp-actions">
                        {portalActions.can("edit") && (
                          <button type="submit" disabled={sendingEmail}>
                            {sendingEmail ? "Gönderiliyor..." : "E-posta ile Gönder"}
                          </button>
                        )}
                      </div>
                    </form>
                  </>
                )}

                {emailLog.length > 0 && (
                  <div className="erp-mt">
                    <div className="erp-panel-header">
                      <h3>Son Gönderimler</h3>
                    </div>

                    <div className="erp-project-list">
                      {emailLog.map((entry) => (
                        <div className="erp-project-list-item" key={entry.id}>
                          <div>
                            <strong>
                              {entry.recipientName || entry.recipientEmail}
                            </strong>
                            <span>{entry.recipientEmail}</span>
                            <span>{dateTime(entry.sentAtUtc)}</span>
                            {!entry.isSuccess && entry.errorMessage && (
                              <span>{entry.errorMessage}</span>
                            )}
                          </div>
                          <span className={`erp-status ${entry.isSuccess ? "green" : "gray"}`}>
                            {entry.isSuccess ? "Gönderildi" : "Başarısız"}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Maliyet Dağılımı</h2>
                <p>
                  Şantiye harcamaları, ortak giderler ve proje toplamı.
                  Buradan girilen kalem GİDER KAYDI olarak açılır ve proje
                  maliyetine oradan yansır; malzeme, işçilik ve taşeron
                  kalemleri kendi kaynaklarından (satın alma, puantaj,
                  taşeron hakedişi) gelir, elle girilmez.
                </p>
              </div>
            </div>

            {costError && <div className="erp-alert error">{costError}</div>}

            {/* YETKİSİZE FORM HİÇ RENDER EDİLMEZ: uç expense.manage
                istiyor, göstermek çalışmayan düğme bırakmak olurdu.
                projects.create olup gider yetkisi olmayan roller
                (Teknik Ofis, Teknik Koordinatör) burada yalnız dağılımı
                okur. */}
            {canManageExpense ? (
            <form className="erp-form-card" onSubmit={createCostTransaction}>
              <div className="erp-form-grid">
                <label>
                  <span>Şantiye</span>
                  <select
                    value={costForm.projectSiteId}
                    onChange={(e) =>
                      updateCostForm("projectSiteId", e.target.value)
                    }
                  >
                    <option value="">Ortak / Merkez Gider</option>
                    {sites.map((site) => (
                      <option key={site.id} value={site.id}>
                        {site.code} · {site.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Gider Kategorisi *</span>
                  <select
                    value={costForm.expenseCategoryId}
                    onChange={(e) =>
                      updateCostForm("expenseCategoryId", e.target.value)
                    }
                  >
                    <option value="">Seçin</option>
                    {expenseCategories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Ödeme Şekli</span>
                  <select
                    value={costForm.paymentMethod}
                    onChange={(e) =>
                      updateCostForm("paymentMethod", Number(e.target.value))
                    }
                  >
                    <option value={0}>Banka / kasa</option>
                    <option value={1}>Elden</option>
                    <option value={2}>Şahıs carisinden mahsup</option>
                    <option value={3}>Kredi kartı</option>
                  </select>
                </label>

                <label>
                  <span>Belge</span>
                  <select
                    value={costForm.documentType}
                    onChange={(e) =>
                      updateCostForm("documentType", Number(e.target.value))
                    }
                  >
                    <option value={2}>Fatura</option>
                    <option value={1}>Fiş</option>
                    <option value={0}>Belgesiz</option>
                  </select>
                </label>

                <label>
                  <span>Tarih *</span>
                  <input
                    className="erp-input"
                    type="date"
                    required
                    value={costForm.costDate}
                    onChange={(e) =>
                      updateCostForm("costDate", e.target.value)
                    }
                  />
                </label>

                <label>
                  <span>Tutar *</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.01"
                    required
                    value={costForm.amount}
                    onChange={(e) =>
                      updateCostForm("amount", Number(e.target.value))
                    }
                  />
                </label>

                <label>
                  <span>Açıklama *</span>
                  <input
                    className="erp-input"
                    required
                    value={costForm.description}
                    onChange={(e) =>
                      updateCostForm("description", e.target.value)
                    }
                  />
                </label>
              </div>

              <div className="erp-actions">
                <button type="submit" disabled={costSaving}>
                  {costSaving ? "Kaydediliyor..." : "Gider Kaydını Ekle"}
                </button>
              </div>
            </form>
            ) : (
              <p className="erp-muted">
                Gider kaydı açma yetkiniz yok; bu bölümde yalnız maliyet
                dağılımını görüyorsunuz.
              </p>
            )}

            {!breakdown ? (
              <div className="erp-empty-state">
                Maliyet dağılımı bulunamadı.
              </div>
            ) : (
              <div className="erp-detail-grid">
                {breakdown.sites.map((site) => (
                  <div key={site.id}>
                    <span>{site.code} · {site.name}</span>
                    <strong>
                      {formatMoney(site.amount, project.currencyCode)}
                    </strong>
                  </div>
                ))}

                <div>
                  <span>Ortak Giderler</span>
                  <strong>
                    {formatMoney(breakdown.sharedCost, project.currencyCode)}
                  </strong>
                </div>

                <div>
                  <span>Proje Toplamı</span>
                  <strong>
                    {formatMoney(breakdown.projectTotal, project.currencyCode)}
                  </strong>
                </div>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Personel Maliyeti Dağılımı</h2>
                <p>Şantiye bazlı işçilik maliyeti, ortak giderler ve proje toplamı</p>
              </div>
            </div>

            {laborError && <div className="erp-alert error">{laborError}</div>}

            <form className="erp-form-card" onSubmit={createLaborCost}>
              <div className="erp-form-grid">
                <label>
                  <span>Personel *</span>
                  <select
                    required
                    value={laborForm.personnelId}
                    onChange={(e) =>
                      updateLaborForm("personnelId", e.target.value)
                    }
                  >
                    <option value="">Seçin</option>
                    {projectPersonnel.map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.employeeNumber} — {person.fullName}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Şantiye</span>
                  <select
                    value={laborForm.projectSiteId}
                    onChange={(e) =>
                      updateLaborForm("projectSiteId", e.target.value)
                    }
                  >
                    <option value="">Ortak / Merkez Gider</option>
                    {sites.map((site) => (
                      <option key={site.id} value={site.id}>
                        {site.code} · {site.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Tarih *</span>
                  <input
                    className="erp-input"
                    type="date"
                    required
                    value={laborForm.workDate}
                    onChange={(e) =>
                      updateLaborForm("workDate", e.target.value)
                    }
                  />
                </label>

                <label>
                  <span>Normal Saat</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.5"
                    value={laborForm.normalHours}
                    onChange={(e) =>
                      updateLaborForm("normalHours", Number(e.target.value))
                    }
                  />
                </label>

                <label>
                  <span>Fazla Mesai Saat</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.5"
                    value={laborForm.overtimeHours}
                    onChange={(e) =>
                      updateLaborForm("overtimeHours", Number(e.target.value))
                    }
                  />
                </label>

                <label>
                  <span>Normal Maliyet *</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.01"
                    required
                    value={laborForm.normalCost}
                    onChange={(e) =>
                      updateLaborForm("normalCost", Number(e.target.value))
                    }
                  />
                </label>

                <label>
                  <span>Fazla Mesai Maliyeti</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.01"
                    value={laborForm.overtimeCost}
                    onChange={(e) =>
                      updateLaborForm("overtimeCost", Number(e.target.value))
                    }
                  />
                </label>

                <label>
                  <span>Diğer Giderler</span>
                  <input
                    className="erp-input"
                    type="number"
                    min="0"
                    step="0.01"
                    value={laborForm.otherCost}
                    onChange={(e) =>
                      updateLaborForm("otherCost", Number(e.target.value))
                    }
                  />
                </label>
              </div>

              <div className="erp-actions">
                {laborActions.can("create") && (
                  <button type="submit" disabled={laborSaving}>
                    {laborSaving ? "Kaydediliyor..." : "Personel Maliyet Kaydı Ekle"}
                  </button>
                )}
              </div>
            </form>

            {!laborBreakdown ? (
              <div className="erp-empty-state">
                Personel maliyeti dağılımı bulunamadı.
              </div>
            ) : (
              <div className="erp-detail-grid">
                {laborBreakdown.sites.map((site) => (
                  <div key={site.id}>
                    <span>{site.code} · {site.name}</span>
                    <strong>
                      {formatMoney(
                        site.actualAmount ?? site.amount,
                        project.currencyCode
                      )}
                    </strong>
                    {/* Elden payı yetkiliye ayrıca gösteriliyor;
                        gerçek maliyetin neyden oluştuğu görünmeli. */}
                    {site.extraPaymentAmount != null &&
                      site.extraPaymentAmount > 0 && (
                        <small>
                          resmî{" "}
                          {formatMoney(
                            site.officialAmount ?? site.amount,
                            project.currencyCode
                          )}{" "}
                          + elden{" "}
                          {formatMoney(
                            site.extraPaymentAmount,
                            project.currencyCode
                          )}
                        </small>
                      )}
                  </div>
                ))}

                <div>
                  <span>Ortak Giderler</span>
                  <strong>
                    {formatMoney(laborBreakdown.sharedCost, project.currencyCode)}
                  </strong>
                  {laborBreakdown.sharedExtraPaymentCost != null &&
                    laborBreakdown.sharedExtraPaymentCost > 0 && (
                      <small>
                        şantiyesiz elden{" "}
                        {formatMoney(
                          laborBreakdown.sharedExtraPaymentCost,
                          project.currencyCode
                        )}
                      </small>
                    )}
                </div>

                <div>
                  <span>Proje Toplamı</span>
                  <strong>
                    {formatMoney(
                      laborBreakdown.projectActualTotal ??
                        laborBreakdown.projectTotal,
                      project.currencyCode
                    )}
                  </strong>
                  {laborBreakdown.extraPaymentHidden ? (
                    <small>Elden ödemeler dahil değil (yetki yok)</small>
                  ) : (
                    laborBreakdown.projectExtraPaymentTotal != null &&
                    laborBreakdown.projectExtraPaymentTotal > 0 && (
                      <small>
                        resmî{" "}
                        {formatMoney(
                          laborBreakdown.projectOfficialTotal ??
                            laborBreakdown.projectTotal,
                          project.currencyCode
                        )}{" "}
                        + elden{" "}
                        {formatMoney(
                          laborBreakdown.projectExtraPaymentTotal,
                          project.currencyCode
                        )}
                      </small>
                    )
                  )}
                </div>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Finansal Sözleşme Oranları</h2>
                <p>
                  Hakediş hesaplamalarında varsayılan olarak kullanılacak
                  proje oranları
                </p>
              </div>

              {/* "Kesinti Politikasını Aç" bağlantısı buradaydı.
                  Gittiği ekran ÖLÜYDÜ: hem listesi hem kaydı
                  `progress-payment-deduction-rules` ucuna gidiyordu, o uç
                  backend'de hiç yazılmamış (controller yok, model bile
                  yok). Kesintiler pratikte hakediş belgesinin kendi
                  içinde giriliyor (ProgressPaymentsController.ApplyDeductions),
                  o akış çalışıyor. Bkz. TEMIZLIK-TARAMASI.md. */}
            </div>

            <div className="erp-detail-grid">
              <div>
                <span>Sözleşme Artış Oranı</span>
                <strong>
                  {formatPercentage(project.increaseRate)}
                </strong>
              </div>

              <div>
                <span>Nakit Teminat Kesintisi</span>
                <strong>
                  {formatPercentage(project.cashRetentionRate)}
                </strong>
              </div>

              <div>
                <span>Stopaj Kesintisi</span>
                <strong>
                  {formatPercentage(project.withholdingTaxRate)}
                </strong>
              </div>

              <div>
                <span>Malzeme Kesintisi</span>
                <strong>
                  {formatPercentage(project.materialDeductionRate)}
                </strong>
              </div>

              <div>
                <span>KDV Oranı</span>
                <strong>
                  {formatPercentage(project.vatRate)}
                </strong>
              </div>

              <div>
                <span>Tevkifat Oranı</span>
                <strong>
                  {project.withholdingRate || "—"}
                </strong>
              </div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Karlılık Analizi</h2>
                <p>Gelir, maliyet ve kârlılık durumu</p>
              </div>
            </div>

            {!profitability ? (
              <div className="erp-empty-state">
                <strong>Karlılık verisi bulunamadı</strong>
              </div>
            ) : (
              <div className="erp-detail-grid">

                <div>
                  <span>Gelir</span>
                  <strong>
                    {formatMoney(
                      profitability.revenue,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Toplam Maliyet</span>
                  <strong>
                    {formatMoney(
                      profitability.totalCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Kar</span>
                  <strong>
                    {formatMoney(
                      profitability.profit,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Kar Marjı</span>
                  <strong>
                    %{profitability.profitMargin}
                  </strong>
                </div>

                <div>
                  <span>Malzeme</span>
                  <strong>
                    {formatMoney(
                      profitability.materialCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>İşçilik</span>
                  <strong>
                    {formatMoney(
                      profitability.laborCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

              </div>
            )}
          </section>



          <section className="erp-panel erp-mt">

            <div className="erp-panel-header">
              <div>
                <h2>Proje Şantiye Günlükleri</h2>
                <p>Saha ilerleme ve günlük operasyon kayıtları</p>
              </div>
            </div>


            {dailyReports.length === 0 ? (

              <div className="erp-empty-state">
                Günlük rapor bulunmuyor.
              </div>

            ) : (

              <div className="erp-project-list">

                {dailyReports.map(report => (

                  <Link
                    className="erp-project-list-item"
                    href={`/projeler/${params.id}/santiyeler/${report.projectSiteId}`}
                    key={report.id}
                  >

                    <div>

                      <strong>
                        {formatDate(report.reportDate)} · {report.siteName}
                      </strong>

                      <span>
                        {report.notes || "Not girilmedi"}
                      </span>

                      <span>
                        Personel: {report.totalHeadcount}
                      </span>

                    </div>


                    <div>

                      <span>
                        {report.weatherCondition || "—"}
                      </span>

                    </div>

                  </Link>

                ))}

              </div>

            )}

          </section>



          <section className="erp-panel erp-mt">

            <div className="erp-panel-header">
              <div>
                <h2>AI Şantiye Analizi</h2>
                <p>Günlük saha verilerine göre yapay zeka değerlendirmesi</p>
              </div>
            </div>


            {!siteAnalysis ? (

              <div className="erp-empty-state">
                AI analizi bulunamadı.
              </div>

            ) : (

              <div className="erp-project-list">

                {siteAnalysis.items.map((item,index)=>(

                  <div
                    className="erp-project-list-item"
                    key={index}
                  >

                    <div>
                      <strong>
                        {item.title}
                      </strong>

                      <span>
                        {item.message}
                      </span>
                    </div>

                    <span>
                      {item.module}
                    </span>

                  </div>

                ))}

              </div>

            )}

          </section>


          <div className="enderun-project-module-grid">
            {visibleModules.map((module) => (
              <Link
                key={module.label}
                href={
                  module.href.startsWith("/")
                    ? module.href
                    : `/projeler/${project.id}/${module.href}`
                }
              >
                <div className="enderun-project-module-icon">{module.icon}</div>
                <strong>{module.label}</strong>
                <span>{module.text}</span>
              </Link>
            ))}
          </div>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Depoları</h2>
                <p>Projeye bağlı depo kayıtları</p>
              </div>
            </div>

            {project.warehouses.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Depo bulunmuyor</strong>
              </div>
            ) : (
              <div className="erp-project-list">
                {project.warehouses.map((warehouse) => (
                  <div className="erp-project-list-item" key={warehouse.id}>
                    <div>
                      <strong>{warehouse.name}</strong>
                      <span>{warehouse.code}</span>
                    </div>
                    <span className="erp-status green">
                      {warehouse.isActive ? "Aktif" : "Pasif"}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          <ProjectDocumentsSection projectId={project.id} sites={sites} />

          <ProjectDangerZone
            projectId={project.id}
            projectCode={project.code}
            isArchived={project.isArchived}
          />
        </>
      )}

      <ConfirmDialog
        open={revokeOpen}
        title="İşveren Portal Linkini İptal Et"
        description={
          "Link iptal edilecek ve işveren bu adresten projeye ERİŞEMEYECEK. " +
          "İptal geri alınamaz; erişimi geri vermek için yeni link üretip " +
          "yeniden göndermek gerekir."
        }
        confirmLabel="Linki İptal Et"
        busy={portalLoading}
        error={portalError}
        onCancel={() => setRevokeOpen(false)}
        onConfirm={() => void revokePortalLink()}
      />
    </ErpShell>
  );
}
