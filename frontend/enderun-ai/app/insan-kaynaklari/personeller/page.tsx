"use client";

import { foldTurkish } from "@/lib/search/fold";
import {
  rehireCheckService,
  type RehireCheckResult,
} from "@/services/rehire-check.service";
import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { money } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Select,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  personnelService,
  type PersonnelDataCompleteness,
  type PersonnelDetail,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  branchService,
  type BranchListItem,
} from "@/services/branch.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";
import { hrSalaryService } from "@/services/hr-salary.service";
import {
  hrOrganizationService,
  type HrDepartment,
} from "@/services/hr-organization.service";
import { extraPaymentService } from "@/services/termination.service";
import {
  personnelOvertimeService,
  type PersonnelOvertimeSummary,
} from "@/services/personnel-overtime.service";
import { apiClient } from "@/lib/api/api-client";

type ViewMode = "table" | "cards";
type ActivityFilter = "all" | "active" | "passive";

type PersonnelForm = {
  companyId: string;
  branchId: string;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  identityNumber: string;
  birthDate: string;
  phone: string;
  email: string;
  address: string;
  jobTitle: string;
  profession: string;
  sgkRegistrationNumber: string;
  employmentStartDate: string;
  employmentEndDate: string;
  overtimeConsentYear: string;
  overtimeConsentDate: string;
  monthlySalary: string;
  status: string;
  isActive: boolean;
};

const emptyForm: PersonnelForm = {
  companyId: "",
  branchId: "",
  employeeNumber: "",
  firstName: "",
  lastName: "",
  identityNumber: "",
  birthDate: "",
  phone: "",
  email: "",
  address: "",
  jobTitle: "",
  profession: "",
  sgkRegistrationNumber: "",
  employmentStartDate: "",
  employmentEndDate: "",
  overtimeConsentYear: "",
  overtimeConsentDate: "",
  monthlySalary: "",
  status: "1",
  isActive: true,
};

const statusLabels: Record<number, string> = {
  0: "Aday",
  1: "Aktif",
  2: "İzinli",
  3: "Askıda",
  4: "İşten Ayrıldı",
};

const statusOptions = Object.entries(statusLabels).map(([value, label]) => ({
  value,
  label,
}));

function badgeVariant(status: number): "default" | "success" | "warning" | "danger" {
  if (status === 1) return "success";
  if (status === 2) return "warning";
  if (status === 3 || status === 4) return "danger";
  return "default";
}

function moneyFormat(value: number) {
  return money(value);
}

function dateValue(value?: string | null) {
  return value ? value.slice(0, 10) : "";
}

function displayDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function initials(item: Pick<PersonnelListItem, "firstName" | "lastName">) {
  // GÖSTERİM: avatar rozetindeki baş harfler.
  return `${item.firstName?.[0] ?? ""}${item.lastName?.[0] ?? ""}`.toLocaleUpperCase("tr-TR");
}

/**
 * SÜZGEÇ SEÇENEĞİ OLARAK "(boş)" — ÖLÇÜMLE GEREKLİ OLDU.
 *
 * Bu işlev boş değerleri ELİYORDU (`.filter(Boolean)`). Sonuç: alanı
 * boş olan personele hiçbir süzgeçle ulaşılamıyordu.
 *
 * ÖLÇÜLDÜ (2026-09-04, canlı): 79 aktif personelin 38'inde Meslek
 * boş, 39'unda ünvan boş — ve İKİSİ DE boş olanlar tam 38 kişi. Yani
 * meslek süzgecinden kaçan grup, ünvan süzgecinden de kaçıyordu.
 * Departman atanacak en büyük tek küme onlardı ve ekranda hiçbir
 * yoldan toplanamıyorlardı.
 *
 * SENTINEL DEĞER: gerçek bir meslek adıyla çakışmasın diye
 * `BOS_SECENEK` kullanılıyor; parantezli etiket kullanıcıya görünen
 * kısım.
 */
const BOS_SECENEK = "__BOS__";

function uniqueOptions(values: Array<string | null | undefined>) {
  const dolular = [
    ...new Set(values.map((value) => value?.trim()).filter(Boolean) as string[]),
  ]
    .sort((a, b) => a.localeCompare(b, "tr"))
    .map((value) => ({ value, label: value }));

  const bosSayisi = values.filter((value) => !value?.trim()).length;

  // BOŞ SEÇENEĞİ YALNIZ BOŞ KAYIT VARSA: yoksa kullanılmayan bir
  // seçenek göstermek olurdu.
  return bosSayisi > 0
    ? [{ value: BOS_SECENEK, label: `(boş) · ${bosSayisi}` }, ...dolular]
    : dolular;
}

function PersonnelShortcuts({ personnelId }: { personnelId: string }) {
  const links = [
    { label: "360°", href: `/insan-kaynaklari/personel-360?personnelId=${personnelId}` },
    { label: "Bordro", href: `/insan-kaynaklari/bordro?personnelId=${personnelId}` },
    { label: "Puantaj", href: `/insan-kaynaklari/puantaj?personnelId=${personnelId}` },
    { label: "İzin", href: `/insan-kaynaklari/izinler?personnelId=${personnelId}` },
    { label: "Zimmet", href: `/insan-kaynaklari/zimmetler?personnelId=${personnelId}` },
  ];

  return (
    <div className="flex flex-wrap gap-1.5">
      {links.map((link) => (
        <Link
          key={link.label}
          href={link.href}
          className="inline-flex h-8 items-center rounded-lg border border-slate-200 bg-white px-2.5 text-xs font-medium text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
        >
          {link.label}
        </Link>
      ))}
    </div>
  );
}

export default function HrPersonnelPage() {
  /**
   * Düğme -> uç -> izin:
   *   POST hr/personnel                    -> personnel.create
   *   PUT  hr/personnel/{id}               -> personnel.edit
   *   PUT  hr/personnel/{id}/gorev-yeri    -> personnel.edit
   *   POST projects/{id}/sites             -> SITES.create (şantiye kısayolu)
   *   POST/PUT personnel-extra-payments    -> extra_payment.manage
   *
   * ELDEN ÖDEME ALANI BU KAPILARIN DIŞINDA: aşağıda kendi
   * `permissions.has("extra_payment.manage")` kontrolüyle korunuyor ve
   * `salary.view` olmayana blok hiç açılmıyor. O mantığa DOKUNULMADI —
   * maske "görebilir mi", buradaki kapılar "yazabilir mi" sorusu.
   *
   * "Kaydet" AYNI DÜĞME İKİ AYRI UÇ: düzenlemede PUT, yenide POST.
   */
  const actions = useModuleActions("personnel");
  const siteActions = useModuleActions("sites");

  const [items, setItems] = useState<PersonnelListItem[]>([]);

  /** Personel kimliği → eksik veri özeti. Eksiği olmayan kayıt yok. */
  const [dataGaps, setDataGaps] = useState<
    Record<string, PersonnelDataCompleteness>
  >({});
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [search, setSearch] = useState("");
  const [activity, setActivity] = useState<ActivityFilter>("all");
  const [companyId, setCompanyId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [profession, setProfession] = useState("");
  const [jobTitle, setJobTitle] = useState("");
  const [projectId, setProjectId] = useState("");
  const [formOpen, setFormOpen] = useState(false);

  /*
   * DEPARTMAN ATAMASI SATIR İÇİNDE.
   *
   * NEDEN AYRI EKRAN DEĞİL: 79 personelin tamamına departman
   * girilecek. Her satır için bir panel açtırmak 79 × (aç, seç,
   * kaydet, kapat) demekti. Kolon + satır içi seçici, listenin
   * kendisini toplu atama görünümüne çeviriyor — yeni ekran ve yeni
   * uç açmadan.
   *
   * `departmentSaving` satır kimliğini tutuyor: aynı anda birden çok
   * satır kaydedilebilir, tek bir "kaydediliyor" bayrağı hepsini
   * birden kilitlerdi.
   */
  const [departments, setDepartments] = useState<HrDepartment[]>([]);
  const [departmentSaving, setDepartmentSaving] = useState<string | null>(null);
  const [departmentError, setDepartmentError] = useState("");

  /*
   * TOPLU DEPARTMAN ATAMA.
   *
   * NEDEN: 79 aktif personelin tamamına departman girilecek ve
   * ölçüldü ki ~40'ı aynı departmana (SAHA) gidiyor. Satır satır
   * seçmek 79 ayrı işlem demekti.
   *
   * ARKA UÇTA DEĞİŞİKLİK YOK: satır başına uç zaten var ve tek yazma
   * noktasından (`ParolaYazici` deseninin departman karşılığı,
   * `PUT .../departman`) geçiyor. Toplu uç açmak ikinci bir yazma
   * yolu doğururdu — bu kod tabanının en sık hatası.
   *
   * KISMİ BAŞARISIZLIK SESSİZ GEÇMİYOR: satırlar tek tek uygulanıyor
   * ve biri düşerse (ör. sürüm çakışması) "tamamlandı" denmiyor —
   * başarısızlar SEÇİLİ KALIYOR ve sayısı yazılıyor.
   */
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDepartmentId, setBulkDepartmentId] = useState("");
  const [bulkRunning, setBulkRunning] = useState(false);
  const [bulkResult, setBulkResult] = useState("");

  // Görev yeri paneli: atama mevcut bir personel üzerinde yapılan bir
  // işlem, oluşturma formunun alanı değil — bu yüzden ayrı panel.
  const [locationTarget, setLocationTarget] =
    useState<PersonnelListItem | null>(null);
  const [locationType, setLocationType] = useState("0");
  const [locationProjectId, setLocationProjectId] = useState("");
  const [locationSiteId, setLocationSiteId] = useState("");
  const [locationRole, setLocationRole] = useState("");
  const [locationSites, setLocationSites] = useState<ProjectSiteListItem[]>([]);
  const [locationBranchId, setLocationBranchId] = useState("");
  const [locationSaving, setLocationSaving] = useState(false);
  const [locationError, setLocationError] = useState("");

  // Kısayol: seçilen projede hiç şantiye yoksa kullanıcıyı başka bir
  // ekrana göndermeden buradan şantiye açılabilir. Şantiye yönetimi
  // proje merkezindeki Şantiyeler sekmesinde; buradaki yalnızca
  // "atama yapamıyorum" tıkanmasını açan bir kestirme.
  const [siteShortcutOpen, setSiteShortcutOpen] = useState(false);
  const [siteShortcutCode, setSiteShortcutCode] = useState("");
  const [siteShortcutName, setSiteShortcutName] = useState("");
  const [siteShortcutLocation, setSiteShortcutLocation] = useState("");
  const [siteShortcutSaving, setSiteShortcutSaving] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  // İşe alım öncesi TC kontrolü: form dolmadan, kimlik alanından
  // çıkınca çalışır. Kırmızı eşleşmede kayıt zaten uçta engellenir;
  // buradaki kutu engeli GEREKÇESİYLE gösterir.
  const [rehireCheck, setRehireCheck] =
    useState<RehireCheckResult | null>(null);
  const [checkingRehire, setCheckingRehire] = useState(false);
  const [rehireOverrideReason, setRehireOverrideReason] = useState("");

  const [form, setForm] = useState<PersonnelForm>(emptyForm);

  // Ek ödeme bloğu. Resmî net RAKAMI maaş kartı ucundan geliyor
  // (Maaş Kartları ekranıyla aynı kaynak); iki ekranda ayrı hesap
  // yapılsaydı er ya da geç ayrışırdı.
  const [permissions, setPermissions] = useState<Set<string>>(new Set());
  const [officialNet, setOfficialNet] = useState<number | null>(null);
  const [officialNetSource, setOfficialNetSource] = useState("");
  const [extraPayment, setExtraPayment] = useState("");
  const [extraPaymentId, setExtraPaymentId] = useState<string | null>(null);
  const [extraPaymentLoading, setExtraPaymentLoading] = useState(false);

  /**
   * Bu ayın mesaisi: saat ve ELDEN tutar.
   *
   * Kaynak, personel kartındaki mesai paneliyle AYNI uç — orada iki
   * kaynak (fazla mesai talebi + puantaj cetveli) zaten birleştirilip
   * talebin sahiplendiği gün elendiği için burada çift sayım riski
   * doğmuyor. Ayrıca hesaplasaydık iki yer ayrı rakam gösterirdi.
   */
  const [overtimeSummary, setOvertimeSummary] =
    useState<PersonnelOvertimeSummary | null>(null);

  async function loadScreen() {
    setLoading(true);
    setError("");

    try {
      const [personnelResult, companyResult, branchResult, projectResult] =
        await Promise.all([
          personnelService.getAll(),
          companyService.getAll(),
          branchService.getAll(),
          projectService.getAll(),
        ]);

      setItems(personnelResult);

      /*
       * DEPARTMANLAR AYRI UÇTAN VE HATASI YUTULUYOR: alınamazsa liste
       * yine çalışır, yalnızca seçici doldurulamaz. Departman listesi
       * bu ekranın ASIL işi değil; onu düşürmesi orantısız olurdu.
       * (Eksik veri rozetlerinde de aynı desen kullanılıyor.)
       */
      try {
        setDepartments(
          (await hrOrganizationService.getDepartments()).filter(
            (department) => department.isActive !== false
          )
        );
      } catch {
        setDepartments([]);
      }

      // Eksik veri özeti ayrı uçtan; alınamazsa liste yine çalışır,
      // yalnızca rozetler çıkmaz.
      try {
        const completeness = await personnelService.dataCompleteness();

        setDataGaps(
          Object.fromEntries(
            completeness.items
              .filter((entry) => entry.issues.length > 0)
              .map((entry) => [entry.personnelId, entry])
          )
        );
      } catch {
        setDataGaps({});
      }

      setCompanies(companyResult.filter((company) => company.isActive !== false));
      setBranches(branchResult.filter((branch) => branch.isActive !== false));
      setProjects(projectResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel ekranı yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  /**
   * Satır içi departman ataması.
   *
   * SÜRÜM DAMGASI GERİ GÖNDERİLİYOR: liste 79 satırı aynı anda
   * gösteriyor ve iki kişinin aynı satırı değiştirmesi olağan.
   * Sunucu çakışmayı 409 ile bildiriyor; o durumda satır sessizce
   * eski değerinde kalmıyor, kullanıcıya söyleniyor ve liste
   * tazeleniyor.
   */
  async function assignDepartment(
    item: PersonnelListItem,
    departmentId: string
  ) {
    const yeni = departmentId ? departmentId : null;

    if ((item.departmentId ?? null) === yeni) return;

    setDepartmentSaving(item.id);
    setDepartmentError("");

    try {
      const sonuc = await personnelService.setDepartment(item.id, {
        departmentId: yeni,
        recordVersion: item.recordVersion ?? "",
      });

      // Yalnız değişen satır güncelleniyor: tüm listeyi yeniden
      // çekmek, kullanıcı sırayla 79 satır girerken her seferinde
      // ekranı zıplatırdı.
      setItems((mevcut) =>
        mevcut.map((satir) =>
          satir.id === item.id
            ? {
                ...satir,
                departmentId: sonuc.departmentId,
                departmentName: sonuc.departmentName,
                recordVersion: sonuc.recordVersion,
              }
            : satir
        )
      );
    } catch (err) {
      setDepartmentError(
        err instanceof Error ? err.message : "Departman ataması kaydedilemedi."
      );

      // Çakışmada elimizdeki sürüm eskimiştir; tazelenmezse sonraki
      // deneme de aynı hatayı alır.
      await reloadPersonnel().catch(() => undefined);
    } finally {
      setDepartmentSaving(null);
    }
  }

  /**
   * Seçili satırlara aynı departmanı uygular.
   *
   * TEK TEK UYGULANIYOR, TOPLU UÇ AÇILMADI: satır başına uç zaten var
   * ve her satırın KENDİ sürüm damgası var. Toplu bir uç, ya sürüm
   * kontrolünü atlamak ya da onu ikinci kez yazmak zorunda kalırdı.
   *
   * SIRAYLA, PARALEL DEĞİL: 40 eşzamanlı istek sunucuyu gereksiz
   * yükler ve hata sırasını okunmaz hâle getirir. 40 satır sırayla
   * saniyeler sürüyor.
   */
  async function applyBulkDepartment() {
    if (selectedIds.size === 0) return;

    const hedef = bulkDepartmentId ? bulkDepartmentId : null;

    setBulkRunning(true);
    setBulkResult("");
    setDepartmentError("");

    let basarili = 0;
    const basarisiz: string[] = [];

    for (const id of Array.from(selectedIds)) {
      const satir = items.find((x) => x.id === id);
      if (!satir) continue;

      if ((satir.departmentId ?? null) === hedef) {
        // Zaten aynı: sunucuya gitmeye gerek yok.
        basarili += 1;
        continue;
      }

      try {
        const sonuc = await personnelService.setDepartment(id, {
          departmentId: hedef,
          recordVersion: satir.recordVersion ?? "",
        });

        setItems((mevcut) =>
          mevcut.map((x) =>
            x.id === id
              ? {
                  ...x,
                  departmentId: sonuc.departmentId,
                  departmentName: sonuc.departmentName,
                  recordVersion: sonuc.recordVersion,
                }
              : x
          )
        );

        basarili += 1;
      } catch (err) {
        basarisiz.push(
          `${satir.fullName}: ${
            err instanceof Error ? err.message : "bilinmeyen hata"
          }`
        );
      }
    }

    /*
     * BAŞARISIZLAR SEÇİLİ KALIYOR.
     *
     * Hepsini temizlemek "tamamlandı" izlenimi verirdi; kullanıcı
     * hangi satırın atlandığını aramak zorunda kalırdı. Seçim,
     * yeniden denemenin de hazır hâli.
     */
    setSelectedIds(
      new Set(
        Array.from(selectedIds).filter((id) => {
          const satir = items.find((x) => x.id === id);
          return satir ? basarisiz.some((b) => b.startsWith(satir.fullName)) : false;
        })
      )
    );

    setBulkRunning(false);

    if (basarisiz.length === 0) {
      setBulkResult(`${basarili} personelin departmanı güncellendi.`);
    } else {
      setBulkResult(
        `${basarili} başarılı, ${basarisiz.length} BAŞARISIZ. ` +
          "Başarısız satırlar seçili bırakıldı."
      );
      setDepartmentError(basarisiz.join(" · "));
    }
  }

  async function reloadPersonnel() {
    const result = await personnelService.getAll();
    setItems(result);
  }

  useEffect(() => {
    void loadScreen();
  }, []);

  // Ek ödeme bloğunun görünürlüğü izne bağlı. Sunucu zaten yetkisiz
  // kullanıcıya tutar döndürmüyor; buradaki kontrol kullanıcıyı
  // dolduramayacağı bir alanla karşılaştırmamak için.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const session = await apiClient<{ permissions: string[] }>("auth/me");
        if (!cancelled) setPermissions(new Set(session.permissions));
      } catch {
        if (!cancelled) setPermissions(new Set());
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (branchId && !branches.some((branch) => branch.id === branchId && (!companyId || branch.companyId === companyId))) {
      setBranchId("");
    }
  }, [branchId, branches, companyId]);

  const visibleBranches = useMemo(
    () => branches.filter((branch) => !companyId || branch.companyId === companyId),
    [branches, companyId]
  );

  const visibleProjects = useMemo(
    () => projects.filter((project) => !companyId || project.companyId === companyId),
    [projects, companyId]
  );

  const professionOptions = useMemo(
    () => uniqueOptions(items.map((item) => item.profession)),
    [items]
  );

  const jobTitleOptions = useMemo(
    () => uniqueOptions(items.map((item) => item.jobTitle)),
    [items]
  );

  const filteredItems = useMemo(() => {
    // TÜRKÇE KATLAMA TEK KAYNAKTAN: "SCHNEIDER" yazan schneider'ı,
    // "İNŞAAT" yazan insaat'ı bulmalı. Kültüre bağlı küçültme "I"yı
    // noktasız "ı" yapıp bunları kaçırıyordu.
    const term = foldTurkish(search);

    return items.filter((item) => {
      const searchable = [
        item.fullName,
        item.employeeNumber,
        item.identityNumber,
        item.phone,
        item.email,
      ]
        .filter(Boolean)
        .join(" ");

      if (term && !foldTurkish(searchable).includes(term)) return false;
      if (activity === "active" && !item.isActive) return false;
      if (activity === "passive" && item.isActive) return false;
      if (companyId && item.companyId !== companyId) return false;
      if (branchId && item.branchId !== branchId) return false;
      // BOŞ SEÇENEĞİ: alanın gerçekten boş olduğu kayıtlar.
      if (profession) {
        const bos = !item.profession?.trim();
        if (profession === BOS_SECENEK ? !bos : item.profession !== profession)
          return false;
      }

      if (jobTitle) {
        const bos = !item.jobTitle?.trim();
        if (jobTitle === BOS_SECENEK ? !bos : item.jobTitle !== jobTitle)
          return false;
      }
      if (
        projectId &&
        !item.activeAssignments?.some((assignment) => assignment.projectId === projectId)
      ) {
        return false;
      }

      return true;
    });
  }, [
    activity,
    branchId,
    companyId,
    items,
    jobTitle,
    profession,
    projectId,
    search,
  ]);

  const summary = useMemo(
    () => ({
      total: items.length,
      active: items.filter((item) => item.isActive).length,
      assigned: items.filter((item) => item.activeAssignments?.length > 0).length,
      onLeave: items.filter((item) => item.status === 2 && item.isActive).length,
    }),
    [items]
  );

  const formBranches = useMemo(
    () => branches.filter((branch) => !form.companyId || branch.companyId === form.companyId),
    [branches, form.companyId]
  );

  function updateForm<K extends keyof PersonnelForm>(key: K, value: PersonnelForm[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function resetFilters() {
    setSearch("");
    setActivity("all");
    setCompanyId("");
    setBranchId("");
    setProfession("");
    setJobTitle("");
    setProjectId("");
  }

  function openCreate() {
    const defaultCompanyId = companies.length === 1 ? companies[0].id : "";
    setEditingId(null);
    setForm({ ...emptyForm, companyId: defaultCompanyId });
    resetExtraPayment();
    setError("");
    setSuccess("");
    setFormOpen(true);
  }

  function resetExtraPayment() {
    setOfficialNet(null);
    setOfficialNetSource("");
    setExtraPayment("");
    setExtraPaymentId(null);
    setOvertimeSummary(null);
  }

  /**
   * Personelin resmî neti ve yürürlükteki elden ödemesi.
   *
   * Resmî net MAAŞ KARTI ucundan geliyor — Maaş Kartları ekranıyla aynı
   * kaynak. Kart yoksa personel kartındaki Aylık Ücret'e düşülüyor ve
   * bunun nereden geldiği ekranda yazıyor; sessizce bir rakam
   * göstermek, hangi tutara baktığını bilmeyen bir kullanıcı üretirdi.
   */
  async function loadPayDetails(personnelId: string, fallbackSalary: string) {
    if (!permissions.has("salary.view")) return;

    setExtraPaymentLoading(true);

    try {
      const cards = await hrSalaryService.getAll({ personnelId });
      const today = new Date().toISOString().slice(0, 10);

      const active =
        cards.find(
          (card) =>
            card.effectiveStartDate.slice(0, 10) <= today &&
            (!card.effectiveEndDate ||
              card.effectiveEndDate.slice(0, 10) >= today)
        ) ?? cards[0];

      if (active?.officialNetSalary != null) {
        setOfficialNet(active.officialNetSalary);
        setOfficialNetSource("Maaş kartından");
      } else if (fallbackSalary) {
        setOfficialNet(Number(fallbackSalary));
        setOfficialNetSource("Maaş kartı yok; personel kartındaki aylık ücret");
      } else {
        setOfficialNet(null);
        setOfficialNetSource("Maaş kartı ve aylık ücret tanımlı değil");
      }

      // Elden ödeme AYRI tabloda; yetkisi olmayanın sorgusu 403 alır ve
      // blok zaten görünmez.
      if (permissions.has("extra_payment.view")) {
        const entries = await extraPaymentService.list(personnelId);
        const effective = entries
          .filter(
            (x) =>
              x.effectiveStartDate.slice(0, 10) <= today &&
              (!x.effectiveEndDate || x.effectiveEndDate.slice(0, 10) >= today)
          )
          .sort((a, b) =>
            b.effectiveStartDate.localeCompare(a.effectiveStartDate)
          )[0];

        setExtraPaymentId(effective?.id ?? null);
        setExtraPayment(effective ? String(effective.monthlyAmount) : "");

        // Mesai tutarı da elden tarafında: aynı yetki kapısından
        // geçiyor, ayrı bir kapı açılmıyor.
        try {
          setOvertimeSummary(
            await personnelOvertimeService.get(personnelId)
          );
        } catch {
          setOvertimeSummary(null);
        }
      }
    } catch {
      // Ücret bilgisi alınamadıysa blok boş kalır; personel kaydının
      // kendisi bundan etkilenmemeli.
      setOfficialNet(null);
      setOfficialNetSource("Ücret bilgisi okunamadı");
    } finally {
      setExtraPaymentLoading(false);
    }
  }

  function openWorkLocation(item: PersonnelListItem) {
    setLocationTarget(item);
    setLocationType(String(item.workLocationType ?? 0));
    setLocationProjectId(item.activeSiteAssignment?.projectId ?? "");
    setLocationSiteId(item.activeSiteAssignment?.projectSiteId ?? "");
    setLocationRole(item.activeSiteAssignment?.role ?? "");
    setLocationSites([]);

    // Merkez seçilirse öntanımlı birim şirketin merkez ofisi. Personelin
    // eski şubesi bir şantiye şubesi olabilir; onu taşımak merkeze
    // atanan kişiyi yanlış masraf merkezine yazardı.
    const headOffice = branches.find(
      (branch) => branch.companyId === item.companyId && branch.isHeadOffice
    );

    setLocationBranchId(
      item.workLocationType === 1 && item.branchId
        ? item.branchId
        : headOffice?.id ?? ""
    );

    setLocationError("");
  }

  async function loadSitesForProject(projectId: string) {
    setLocationProjectId(projectId);
    setLocationSiteId("");
    setLocationSites([]);
    setSiteShortcutOpen(false);

    if (!projectId) return;

    try {
      setLocationSites(await projectSiteService.getAll(projectId));
    } catch (err) {
      setLocationError(
        err instanceof Error ? err.message : "Şantiyeler alınamadı."
      );
    }
  }

  /**
   * Görev yeri penceresinden şantiye açar ve yeni şantiyeyi seçili hale
   * getirir. Kod zorunlu ve proje içinde tekil olduğu için öneri
   * doldurulur; kullanıcı görüp değiştirebilir.
   */
  async function createSiteShortcut() {
    if (!locationProjectId) return;

    const code = siteShortcutCode.trim();
    const name = siteShortcutName.trim();

    if (!code || !name) {
      setLocationError("Şantiye kodu ve adı zorunludur.");
      return;
    }

    setSiteShortcutSaving(true);
    setLocationError("");

    try {
      const created = await projectSiteService.create(locationProjectId, {
        code,
        name,
        location: siteShortcutLocation.trim() || null,
        notes: null,
      });

      const refreshed = await projectSiteService.getAll(locationProjectId);
      setLocationSites(refreshed);
      setLocationSiteId(created.id);

      setSiteShortcutOpen(false);
      setSiteShortcutCode("");
      setSiteShortcutName("");
      setSiteShortcutLocation("");
    } catch (err) {
      setLocationError(
        err instanceof Error ? err.message : "Şantiye oluşturulamadı."
      );
    } finally {
      setSiteShortcutSaving(false);
    }
  }

  async function saveWorkLocation() {
    if (!locationTarget) return;

    const type = Number(locationType);

    if (type === 2 && !locationSiteId) {
      setLocationError("Şantiye seçilmelidir.");
      return;
    }

    if (type === 1 && !locationBranchId) {
      setLocationError(
        "Merkez birimi seçilmelidir. Şirkette merkez ofis tanımlı değilse " +
          "Şubeler ekranından tanımlayın."
      );
      return;
    }

    setLocationSaving(true);
    setLocationError("");

    try {
      await personnelService.setWorkLocation(locationTarget.id, {
        workLocationType: type,
        projectSiteId: type === 2 ? locationSiteId : null,
        branchId: type === 1 ? locationBranchId || null : null,
        startDate: null,
        role: locationRole.trim() || null,
        notes: null,
      });

      setLocationTarget(null);
      setSuccess("Görev yeri güncellendi.");
      await reloadPersonnel();
    } catch (err) {
      setLocationError(
        err instanceof Error ? err.message : "Görev yeri kaydedilemedi."
      );
    } finally {
      setLocationSaving(false);
    }
  }

  async function openEdit(id: string) {
    setEditingId(id);
    setFormOpen(true);
    setDetailLoading(true);
    setError("");
    setSuccess("");

    try {
      const detail: PersonnelDetail = await personnelService.getById(id);
      setForm({
        companyId: detail.companyId,
        branchId: detail.branchId ?? "",
        employeeNumber: detail.employeeNumber,
        firstName: detail.firstName,
        lastName: detail.lastName,
        identityNumber: detail.identityNumber ?? "",
        birthDate: dateValue(detail.birthDate),
        phone: detail.phone ?? "",
        email: detail.email ?? "",
        address: detail.address ?? "",
        jobTitle: detail.jobTitle ?? "",
        profession: detail.profession ?? "",
        sgkRegistrationNumber: detail.sgkRegistrationNumber ?? "",
        employmentStartDate: dateValue(detail.employmentStartDate),
        employmentEndDate: dateValue(detail.employmentEndDate),
        overtimeConsentYear: detail.overtimeConsentYear
          ? String(detail.overtimeConsentYear)
          : "",
        overtimeConsentDate: dateValue(detail.overtimeConsentDate),
        monthlySalary: detail.monthlySalary == null ? "" : String(detail.monthlySalary),
        status: String(detail.status),
        isActive: detail.isActive,
      });

      resetExtraPayment();
      await loadPayDetails(
        id,
        detail.monthlySalary == null ? "" : String(detail.monthlySalary));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel detayı yüklenemedi.");
      setFormOpen(false);
    } finally {
      setDetailLoading(false);
    }
  }

  /**
   * Elden ödemeyi yazar.
   *
   * AYRI TABLOYA gider (PersonnelExtraPayment): personel kartına kolon
   * olsaydı personnel.view üzerinden sızardı. Resmî bordroya, muhasebe
   * fişine ve SGK matrahına HİÇ girmez; değişen yalnızca görünürlük.
   *
   * Yürürlükteki kayıt varsa üzerine yazılır; iki kayıt bırakmak
   * "hangisi geçerli" belirsizliği doğururdu.
   */
  async function saveExtraPaymentAsync(personnelId: string) {
    if (!permissions.has("extra_payment.manage")) return;

    const raw = extraPayment.trim();

    // Alan hiç ellenmediyse dokunma: boş bırakmak "sıfır gir" demek
    // değil, "bu ekrandan yönetmiyorum" demek.
    if (!raw && !extraPaymentId) return;

    const amount = raw ? Number(raw.replace(",", ".")) : 0;

    if (!Number.isFinite(amount) || amount < 0)
      throw new Error("Ek ödeme tutarı geçerli bir sayı olmalıdır.");

    const payload = {
      personnelId,
      monthlyAmount: amount,
      effectiveStartDate:
        form.employmentStartDate || new Date().toISOString().slice(0, 10),
      effectiveEndDate: null,
      note: null,
    };

    if (extraPaymentId) {
      await extraPaymentService.update(extraPaymentId, payload);
    } else if (amount > 0) {
      await extraPaymentService.create(payload);
    }
  }

  /**
   * Kimlik alanından çıkınca çalışır. Uç, geçersiz numarada 400
   * döndüğü için sessizce yutulur — asıl doğrulama kaydetmede.
   */
  async function checkRehire(identity: string) {
    const value = identity.trim();

    if (value.length !== 11) {
      setRehireCheck(null);
      return;
    }

    try {
      setCheckingRehire(true);

      setRehireCheck(await rehireCheckService.check(value));
    } catch {
      setRehireCheck(null);
    } finally {
      setCheckingRehire(false);
    }
  }

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (editingId) {
        await personnelService.update(editingId, {
          branchId: form.branchId || null,
          firstName: form.firstName,
          lastName: form.lastName,
          identityNumber: form.identityNumber || null,
          rehireOverrideReason: rehireOverrideReason.trim() || null,
          birthDate: form.birthDate || null,
          phone: form.phone || null,
          email: form.email || null,
          address: form.address || null,
          jobTitle: form.jobTitle || null,
          profession: form.profession || null,
          sgkRegistrationNumber: form.sgkRegistrationNumber || null,
          employmentStartDate: form.employmentStartDate || null,
          employmentEndDate: form.employmentEndDate || null,
          // Muvafakat: yıl girilmemişse tarih de anlamsız kalır.
          overtimeConsentYear: form.overtimeConsentYear
            ? Number(form.overtimeConsentYear)
            : null,
          overtimeConsentDate: form.overtimeConsentDate || null,
          monthlySalary: form.monthlySalary ? Number(form.monthlySalary) : null,
          status: Number(form.status),
          isActive: form.isActive,
        });
        await saveExtraPaymentAsync(editingId);
        setSuccess("Personel kaydı güncellendi.");
      } else {
        const created = await personnelService.create({
          companyId: form.companyId,
          branchId: form.branchId || null,
          employeeNumber: form.employeeNumber,
          firstName: form.firstName,
          lastName: form.lastName,
          identityNumber: form.identityNumber || null,
          rehireOverrideReason: rehireOverrideReason.trim() || null,
          birthDate: form.birthDate || null,
          phone: form.phone || null,
          email: form.email || null,
          address: form.address || null,
          jobTitle: form.jobTitle || null,
          profession: form.profession || null,
          sgkRegistrationNumber: form.sgkRegistrationNumber || null,
          employmentStartDate: form.employmentStartDate || null,
          monthlySalary: form.monthlySalary ? Number(form.monthlySalary) : null,
        });

        // Ek ödeme personelin kimliğine bağlı; kayıt oluşmadan
        // yazılamaz, o yüzden oluşturmadan SONRA.
        await saveExtraPaymentAsync(created.id);
        setSuccess("Yeni personel kaydı oluşturuldu.");
      }

      await reloadPersonnel();
      setFormOpen(false);
      setEditingId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel kaydı tamamlanamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Personeller"
      description="Çalışan kartları, organizasyon bilgileri ve İK işlemleri"
    >
      {error && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button type="button" onClick={() => setError("")} aria-label="Uyarıyı kapat">
            ×
          </button>
        </div>
      )}

      {departmentError && (
        <div className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {/* SESSİZ BAŞARISIZLIK YOK: satır içi seçici, kaydetme
              başarısız olduğunda eski değerine dönüyor. Şerit olmasa
              kullanıcı atamanın yapıldığını sanırdı. */}
          {departmentError}
        </div>
      )}

      {success && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          <span>{success}</span>
          <button type="button" onClick={() => setSuccess("")} aria-label="Bildirimi kapat">
            ×
          </button>
        </div>
      )}

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Toplam Personel" value={loading ? "…" : summary.total} description="Tüm personel kayıtları" icon="♙" />
        <StatCard title="Aktif Personel" value={loading ? "…" : summary.active} description="Çalışmaya devam eden" icon="✓" />
        <StatCard title="Şantiyede Görevli" value={loading ? "…" : summary.assigned} description="Aktif proje ataması" icon="▣" />
        <StatCard title="İzinli Personel" value={loading ? "…" : summary.onLeave} description="Güncel izin durumu" icon="◷" />
      </div>

      <Card className="mb-6">
        <CardHeader className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Personel havuzu</h2>
            <p className="mt-1 text-sm text-slate-500">
              Gerçek personel servisinden gelen kayıtları filtreleyin ve yönetin.
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            <div className="inline-flex rounded-lg border border-slate-200 bg-slate-50 p-1">
              <button
                type="button"
                onClick={() => setViewMode("table")}
                className={`h-8 rounded-md px-3 text-sm font-medium transition ${
                  viewMode === "table" ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"
                }`}
              >
                Tablo
              </button>
              <button
                type="button"
                onClick={() => setViewMode("cards")}
                className={`h-8 rounded-md px-3 text-sm font-medium transition ${
                  viewMode === "cards" ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"
                }`}
              >
                Kart
              </button>
            </div>
            {actions.can("create") && (
              <Button onClick={openCreate}>+ Yeni Personel</Button>
            )}
          </div>
        </CardHeader>

        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Ad, sicil, TC, telefon veya e-posta ara"
            className="xl:col-span-2"
          />
          <Select
            value={activity}
            onChange={(event) => setActivity(event.target.value as ActivityFilter)}
            options={[
              { value: "all", label: "Tüm durumlar" },
              { value: "active", label: "Aktif kayıtlar" },
              { value: "passive", label: "Pasif kayıtlar" },
            ]}
          />
          <Select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            placeholder="Tüm şirketler"
            options={companies.map((company) => ({
              value: company.id,
              label: `${company.code} · ${company.name}`,
            }))}
          />
          <Select
            value={branchId}
            onChange={(event) => setBranchId(event.target.value)}
            placeholder="Tüm şubeler"
            options={visibleBranches.map((branch) => ({
              value: branch.id,
              label: `${branch.code} · ${branch.name}`,
            }))}
          />
          <Select
            value={profession}
            onChange={(event) => setProfession(event.target.value)}
            placeholder="Tüm meslekler"
            options={professionOptions}
          />
          <Select
            value={jobTitle}
            onChange={(event) => setJobTitle(event.target.value)}
            placeholder="Tüm pozisyonlar"
            options={jobTitleOptions}
          />
          <Select
            value={projectId}
            onChange={(event) => setProjectId(event.target.value)}
            placeholder="Tüm şantiyeler"
            options={visibleProjects.map((project) => ({
              value: project.id,
              label: `${project.code} · ${project.name}`,
            }))}
          />

          <div className="flex items-center justify-between gap-3 md:col-span-2 xl:col-span-4">
            <span className="text-sm text-slate-500">
              {filteredItems.length} / {items.length} personel gösteriliyor
              {(() => {
                /*
                 * DEPARTMANI BOŞ SAYACI — SÜZGECİ YOK SAYAR.
                 *
                 * Bu sayaç "işin tamamı bitti mi" sorusunu cevaplıyor;
                 * tablo başlığındaki "hepsini seç" ise "şu an ne
                 * değiştiriyorum" sorusunu. İkisi zıt görünüyor ama
                 * sebepleri farklı.
                 *
                 * Süzgeçli bir sayaç burada YANILTIRDI: süzgeç
                 * daraldıkça sıfıra iner ve "bitti" izlenimi verir.
                 * Atamanın bittiği, TÜM aktif personelde sayacın
                 * sıfırlanmasıyla bilinir.
                 */
                const bosDepartman = items.filter(
                  (x) => x.isActive && x.status === 1 && !x.departmentId
                ).length;

                if (bosDepartman === 0) {
                  return (
                    <strong className="ml-2 text-emerald-700">
                      · departmanı boş personel yok
                    </strong>
                  );
                }

                return (
                  <strong className="ml-2 text-amber-700">
                    · departmanı boş: {bosDepartman}
                  </strong>
                );
              })()}
            </span>
            <Button variant="ghost" size="sm" onClick={resetFilters}>
              Filtreleri Temizle
            </Button>
          </div>
        </CardContent>
      </Card>

      {loading ? (
        <Card>
          <CardContent className="py-16 text-center text-sm text-slate-500">
            Personel kayıtları yükleniyor...
          </CardContent>
        </Card>
      ) : filteredItems.length === 0 ? (
        <Card>
          <CardContent>
            <EmptyState
              title="Filtrelere uygun personel bulunamadı"
              description="Filtreleri temizleyin veya yeni bir personel kaydı oluşturun."
              icon="♙"
              action={
                actions.can("create") ? (
                  <Button onClick={openCreate}>Yeni Personel</Button>
                ) : undefined
              }
            />
          </CardContent>
        </Card>
      ) : viewMode === "cards" ? (
        <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-3">
          {filteredItems.map((item) => {
            return (
              <Card key={item.id} className="overflow-hidden">
                <CardContent className="p-0">
                  <div className="flex items-start gap-4 border-b border-slate-100 p-5">
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-brand-700 text-sm font-semibold text-white">
                      {initials(item)}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <h3 className="truncate font-semibold text-slate-900">{item.fullName}</h3>
                          <p className="mt-1 text-xs text-slate-500">{item.employeeNumber}</p>
                        </div>
                        <Badge variant={badgeVariant(item.status)}>
                          {statusLabels[item.status] ?? "Bilinmiyor"}
                        </Badge>
                      </div>
                    </div>
                  </div>

                  <div className="grid gap-3 p-5 text-sm">
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Pozisyon</span>
                      <strong className="font-medium text-slate-800">{item.jobTitle || "—"}</strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Departman</span>
                      <strong className="font-medium text-slate-800">{item.profession || "—"}</strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Şirket / Şube</span>
                      <strong className="font-medium text-slate-800">
                        {item.companyName}
                        {item.branchName ? ` · ${item.branchName}` : ""}
                      </strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Görev yeri</span>
                      <strong className="font-medium text-slate-800">
                        {item.activeSiteAssignment
                          ? `${item.activeSiteAssignment.siteCode} · ${item.activeSiteAssignment.siteName}`
                          : item.workLocationType === 1
                            ? "Merkez"
                            : "Atama bekliyor"}
                      </strong>
                    </div>
                  </div>

                  <div className="border-t border-slate-100 bg-slate-50/70 p-4">
                    <PersonnelShortcuts personnelId={item.id} />
                    {actions.can("edit") && (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="mt-3 w-full"
                        onClick={() => void openEdit(item.id)}
                      >
                        Personeli Düzenle
                      </Button>
                    )}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      ) : (
        <Card className="overflow-hidden">
          <CardHeader className="flex items-center justify-between gap-4">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">Personel listesi</h2>
              <p className="mt-1 text-sm text-slate-500">Organizasyon ve aktif şantiye dağılımı</p>
            </div>
            <Badge variant="info">{filteredItems.length} kayıt</Badge>
          </CardHeader>

          {/*
              TOPLU ATAMA ŞERİDİ — YALNIZ SEÇİM VARKEN.
              Boşken göstermek, her açılışta kullanılmayan bir kontrol
              göstermek olurdu.
          */}
          {actions.can("edit") && selectedIds.size > 0 && (
            <div className="flex flex-wrap items-center gap-3 border-y border-slate-200 bg-slate-50 px-4 py-3">
              <strong className="text-sm text-slate-800">
                {selectedIds.size} personel seçili
              </strong>

              <select
                className="rounded-lg border border-slate-300 px-2 py-1 text-sm"
                value={bulkDepartmentId}
                disabled={bulkRunning}
                onChange={(event) => setBulkDepartmentId(event.target.value)}
                aria-label="Toplu atanacak departman"
              >
                {/* BOŞ SEÇENEK: toplu DEPARTMANDAN ÇIKARMA da mümkün. */}
                <option value="">— Departman yok —</option>
                {departments.map((department) => (
                  <option key={department.id} value={department.id}>
                    {department.name}
                  </option>
                ))}
              </select>

              <button
                type="button"
                disabled={bulkRunning}
                onClick={() => void applyBulkDepartment()}
                className="rounded-lg bg-brand-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
              >
                {bulkRunning ? "Uygulanıyor…" : "Seçililere uygula"}
              </button>

              <button
                type="button"
                disabled={bulkRunning}
                onClick={() => {
                  setSelectedIds(new Set());
                  setBulkResult("");
                }}
                className="text-sm text-slate-600 underline disabled:opacity-50"
              >
                Seçimi temizle
              </button>

              {bulkResult && (
                <span className="text-sm text-slate-700">{bulkResult}</span>
              )}
            </div>
          )}

          <CardContent className="overflow-x-auto p-0">
            <Table>
              <TableHeader>
                <TableRow>
                  {actions.can("edit") && (
                    <TableHead className="w-10">
                      {/*
                          SÜZÜLENLERİN HEPSİNİ SEÇ — tüm listeyi değil.
                          Kullanıcı önce süzer (ör. meslek = SAHA
                          GÖREVLİSİ), sonra hepsini seçer. Süzgeci
                          yok sayan bir "hepsini seç", görmediği
                          satırları da değiştirirdi.
                      */}
                      <input
                        type="checkbox"
                        aria-label="Süzülen personelin hepsini seç"
                        className="h-4 w-4 rounded border-slate-300"
                        checked={
                          filteredItems.length > 0 &&
                          filteredItems.every((x) => selectedIds.has(x.id))
                        }
                        onChange={(event) => {
                          setBulkResult("");
                          setSelectedIds(
                            event.target.checked
                              ? new Set(filteredItems.map((x) => x.id))
                              : new Set()
                          );
                        }}
                      />
                    </TableHead>
                  )}
                  <TableHead>Personel</TableHead>
                  <TableHead>Şirket / Şube</TableHead>
                  {/*
                      BAŞLIK DÜZELTİLDİ — YANILTIYORDU.

                      Bu kolon "Departman / Pozisyon" yazıyordu ama
                      gösterdiği alanlar `profession` ve `jobTitle`,
                      yani MESLEK ve ünvan. Personelin departmanı
                      (`departmentId`) hiç gösterilmiyordu — ve canlıda
                      79 personelin hiçbirinde dolu değildi. Ekran
                      "Departman" yazan bir kolonda başka bir şey
                      gösterdiği için, alanın boş olduğu da fark
                      edilmiyordu.
                  */}
                  <TableHead>Meslek / Pozisyon</TableHead>
                  <TableHead>Departman</TableHead>
                  <TableHead>Şantiye</TableHead>
                  <TableHead>İşe Giriş</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead>Hızlı İşlemler</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredItems.map((item) => {
                  return (
                    <TableRow key={item.id}>
                      {actions.can("edit") && (
                        <TableCell>
                          <input
                            type="checkbox"
                            aria-label={`${item.fullName} seç`}
                            className="h-4 w-4 rounded border-slate-300"
                            checked={selectedIds.has(item.id)}
                            onChange={(event) => {
                              setBulkResult("");
                              setSelectedIds((mevcut) => {
                                const yeni = new Set(mevcut);
                                if (event.target.checked) yeni.add(item.id);
                                else yeni.delete(item.id);
                                return yeni;
                              });
                            }}
                          />
                        </TableCell>
                      )}
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-xs font-semibold text-slate-700">
                            {initials(item)}
                          </div>
                          <div>
                            <strong className="block whitespace-nowrap text-slate-900">{item.fullName}</strong>
                            <span className="mt-1 block text-xs text-slate-500">{item.employeeNumber}</span>
                            {/* Eksik veri rozeti: ayrıntı ve tamamlama
                                ayrı ekranda; burada yalnızca ağırlık
                                gösteriliyor. */}
                            {dataGaps[item.id] && (
                              <Link
                                href="/insan-kaynaklari/veri-eksikleri"
                                className="mt-1 inline-block"
                              >
                                <Badge
                                  variant={
                                    dataGaps[item.id].payrollReady
                                      ? "warning"
                                      : "danger"
                                  }
                                  title={dataGaps[item.id].issues
                                    .map((issue) => issue.label)
                                    .join(", ")}
                                >
                                  {dataGaps[item.id].payrollReady
                                    ? `${dataGaps[item.id].issues.length} eksik alan`
                                    : "Bordroya giremez"}
                                </Badge>
                              </Link>
                            )}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell>
                        <span className="block whitespace-nowrap text-slate-800">{item.companyName}</span>
                        <span className="mt-1 block whitespace-nowrap text-xs text-slate-500">{item.branchName || "Şube belirtilmedi"}</span>
                      </TableCell>
                      <TableCell>
                        <span className="block whitespace-nowrap text-slate-800">{item.profession || "—"}</span>
                        <span className="mt-1 block whitespace-nowrap text-xs text-slate-500">{item.jobTitle || "Pozisyon belirtilmedi"}</span>
                      </TableCell>
                      <TableCell>
                        {actions.can("edit") ? (
                          <select
                            className="w-44 rounded-lg border border-slate-300 px-2 py-1 text-sm disabled:opacity-50"
                            value={item.departmentId ?? ""}
                            disabled={
                              departmentSaving === item.id ||
                              departments.length === 0
                            }
                            onChange={(event) =>
                              void assignDepartment(item, event.target.value)
                            }
                            aria-label={`${item.fullName} departmanı`}
                          >
                            {/* BOŞ SEÇENEK BİR KARAR: departmandan
                                çıkarmanın yolu bu. Kaldırılırsa yanlış
                                atanan personel düzeltilemez. */}
                            <option value="">— Departman yok —</option>
                            {departments.map((department) => (
                              <option key={department.id} value={department.id}>
                                {department.name}
                              </option>
                            ))}
                            {/* SİLİNMİŞ/LİSTEDE OLMAYAN DEPARTMAN SESSİZCE
                                KAYBOLMAZ: seçili kimlik listede yoksa
                                seçici boşa düşer ve kullanıcı departmanı
                                silinmiş sanırdı. */}
                            {item.departmentId &&
                              !departments.some(
                                (department) => department.id === item.departmentId
                              ) && (
                                <option value={item.departmentId}>
                                  {item.departmentName ?? "(bilinmeyen departman)"}
                                </option>
                              )}
                          </select>
                        ) : (
                          <span className="block whitespace-nowrap text-slate-800">
                            {item.departmentName ?? "—"}
                          </span>
                        )}
                        {departments.length === 0 && actions.can("edit") && (
                          <span className="mt-1 block text-xs text-amber-700">
                            Departman listesi alınamadı.
                          </span>
                        )}
                      </TableCell>
                      <TableCell>
                        {item.activeSiteAssignment ? (
                          <>
                            <span className="block max-w-52 truncate text-slate-800">{item.activeSiteAssignment.siteName}</span>
                            <span className="mt-1 block text-xs text-slate-500">{item.activeSiteAssignment.siteCode}</span>
                          </>
                        ) : item.workLocationType === 1 ? (
                          <span className="block text-slate-800">Merkez</span>
                        ) : (
                          <Badge variant="warning">Atama bekliyor</Badge>
                        )}
                      </TableCell>
                      <TableCell className="whitespace-nowrap">{displayDate(item.employmentStartDate)}</TableCell>
                      <TableCell>
                        <Badge variant={badgeVariant(item.status)}>
                          {statusLabels[item.status] ?? "Bilinmiyor"}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <PersonnelShortcuts personnelId={item.id} />
                      </TableCell>
                      <TableCell className="text-right">
                        {actions.can("edit") && (
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => openWorkLocation(item)}
                          >
                            Görev Yeri
                          </Button>
                        )}
                        {actions.can("edit") && (
                          <Button variant="secondary" size="sm" onClick={() => void openEdit(item.id)}>
                            Düzenle
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {locationTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/40 p-4 backdrop-blur-[1px]">
          <button
            type="button"
            className="absolute inset-0 cursor-default"
            onClick={() => !locationSaving && setLocationTarget(null)}
            aria-label="Paneli kapat"
          />

          <aside className="relative w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl">
            <h2 className="text-lg font-semibold text-slate-900">
              Görev Yeri — {locationTarget.fullName}
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Şantiye seçilirse önceki atama kapatılır ve yenisi açılır.
            </p>

            {locationError && (
              <div className="mt-4 rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700">
                {locationError}
              </div>
            )}

            <div className="mt-5 space-y-4">
              <label className="block">
                <span className="mb-1.5 block text-sm font-medium text-slate-700">
                  Görev yeri
                </span>
                <Select
                  value={locationType}
                  onChange={(event) => {
                    setLocationType(event.target.value);
                    setLocationError("");
                  }}
                  options={[
                    { value: "0", label: "Atanmadı" },
                    { value: "1", label: "Merkez" },
                    { value: "2", label: "Şantiye" },
                  ]}
                />
              </label>

              {locationType === "1" && (
                <label className="block">
                  <span className="mb-1.5 block text-sm font-medium text-slate-700">
                    Merkez birimi
                  </span>
                  <Select
                    value={locationBranchId}
                    onChange={(event) => {
                      setLocationBranchId(event.target.value);
                      setLocationError("");
                    }}
                    placeholder="Birim seçin"
                    options={branches
                      .filter(
                        (branch) =>
                          branch.companyId === locationTarget.companyId
                      )
                      .map((branch) => ({
                        value: branch.id,
                        label: branch.isHeadOffice
                          ? `${branch.name} (merkez)`
                          : branch.name,
                      }))}
                  />
                  <span className="mt-1 block text-xs text-slate-500">
                    Bordro gideri bu birimin masraf merkezine yazılır.
                  </span>
                </label>
              )}

              {locationType === "2" && (
                <>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">
                      Proje
                    </span>
                    <Select
                      value={locationProjectId}
                      onChange={(event) =>
                        void loadSitesForProject(event.target.value)
                      }
                      placeholder="Proje seçin"
                      options={projects
                        .filter(
                          (project) =>
                            project.companyId === locationTarget.companyId
                        )
                        .map((project) => ({
                          value: project.id,
                          label: `${project.code} — ${project.name}`,
                        }))}
                    />
                  </label>

                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">
                      Şantiye
                    </span>
                    <Select
                      value={locationSiteId}
                      onChange={(event) => {
                        setLocationSiteId(event.target.value);
                        setLocationError("");
                      }}
                      disabled={!locationProjectId}
                      placeholder={
                        locationProjectId
                          ? "Şantiye seçin"
                          : "Önce proje seçin"
                      }
                      options={locationSites.map((site) => ({
                        value: site.id,
                        label: `${site.code} — ${site.name}`,
                      }))}
                    />

                    {locationProjectId && locationSites.length === 0 && (
                      <span className="mt-1 block text-xs text-amber-700">
                        Bu projede tanımlı şantiye yok — atama yapılabilmesi
                        için önce bir şantiye açılmalı.
                      </span>
                    )}
                  </label>

                  {locationProjectId && locationSites.length === 0 && (
                    <div className="rounded-md border border-slate-200 p-3">
                      {!siteShortcutOpen ? (
                        <div className="flex flex-wrap items-center gap-2">
                          <Button
                            variant="secondary"
                            onClick={() => {
                              // Liste boş olduğu için ilk şantiye; kod
                              // önerisi doldurulur, kullanıcı değiştirebilir.
                              setSiteShortcutCode("SANTIYE-1");
                              setSiteShortcutName("");
                              setSiteShortcutLocation("");
                              setSiteShortcutOpen(true);
                            }}
                          >
                            + Yeni şantiye oluştur
                          </Button>

                          <Link
                            href={`/projeler/${locationProjectId}/santiyeler`}
                            className="text-sm text-slate-600 underline"
                          >
                            Şantiye yönetimini aç
                          </Link>
                        </div>
                      ) : (
                        <div className="space-y-2">
                          <label className="block">
                            <span className="mb-1.5 block text-sm font-medium text-slate-700">
                              Şantiye Kodu *
                            </span>
                            <Input
                              value={siteShortcutCode}
                              maxLength={30}
                              onChange={(event) =>
                                setSiteShortcutCode(event.target.value)
                              }
                            />
                          </label>

                          <label className="block">
                            <span className="mb-1.5 block text-sm font-medium text-slate-700">
                              Şantiye Adı *
                            </span>
                            <Input
                              value={siteShortcutName}
                              placeholder="Merkez Şantiye"
                              onChange={(event) =>
                                setSiteShortcutName(event.target.value)
                              }
                            />
                          </label>

                          <label className="block">
                            <span className="mb-1.5 block text-sm font-medium text-slate-700">
                              Lokasyon (ops.)
                            </span>
                            <Input
                              value={siteShortcutLocation}
                              onChange={(event) =>
                                setSiteShortcutLocation(event.target.value)
                              }
                            />
                          </label>

                          <div className="flex gap-2">
                            {siteActions.can("create") && (
                              <Button
                                onClick={() => void createSiteShortcut()}
                                disabled={siteShortcutSaving}
                              >
                                {siteShortcutSaving
                                  ? "Oluşturuluyor..."
                                  : "Oluştur ve seç"}
                              </Button>
                            )}
                            <Button
                              variant="secondary"
                              onClick={() => setSiteShortcutOpen(false)}
                              disabled={siteShortcutSaving}
                            >
                              Vazgeç
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">
                      Görev (ops.)
                    </span>
                    <Input
                      value={locationRole}
                      onChange={(event) => setLocationRole(event.target.value)}
                      placeholder="Mühendis / Formen / Usta / İşçi"
                    />
                    <span className="mt-1 block text-xs text-slate-500">
                      Şantiye günlük raporundaki personel sayısı önerisi bu
                      görev metnine göre dağıtılır.
                    </span>
                  </label>
                </>
              )}
            </div>

            <div className="mt-6 flex justify-end gap-2">
              <Button
                variant="secondary"
                onClick={() => setLocationTarget(null)}
                disabled={locationSaving}
              >
                Vazgeç
              </Button>
              {actions.can("edit") && (
                <Button
                  onClick={() => void saveWorkLocation()}
                  disabled={locationSaving}
                >
                  {locationSaving ? "Kaydediliyor..." : "Kaydet"}
                </Button>
              )}
            </div>
          </aside>
        </div>
      )}

      {formOpen && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/40 backdrop-blur-[1px]">
          <button
            type="button"
            className="min-w-0 flex-1 cursor-default"
            onClick={() => !saving && setFormOpen(false)}
            aria-label="Formu kapat"
          />
          <aside className="h-full w-full max-w-3xl overflow-y-auto bg-white shadow-2xl">
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white px-6 py-5">
              <div>
                <span className="text-xs font-semibold tracking-widest text-slate-500">PERSONEL KARTI</span>
                <h2 className="mt-1 text-xl font-semibold text-slate-900">
                  {editingId ? "Personeli Düzenle" : "Yeni Personel"}
                </h2>
              </div>
              <button
                type="button"
                onClick={() => !saving && setFormOpen(false)}
                className="flex h-10 w-10 items-center justify-center rounded-lg border border-slate-200 text-xl text-slate-500 hover:bg-slate-50"
                aria-label="Formu kapat"
              >
                ×
              </button>
            </div>

            {detailLoading ? (
              <div className="p-12 text-center text-sm text-slate-500">Personel bilgileri yükleniyor...</div>
            ) : (
              <form onSubmit={handleSave} className="p-6">
                <div className="mb-6 rounded-xl border border-slate-200 bg-slate-50 p-4">
                  <h3 className="font-semibold text-slate-900">Organizasyon bilgileri</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Select
                      label="Şirket"
                      value={form.companyId}
                      onChange={(event) => {
                        updateForm("companyId", event.target.value);
                        updateForm("branchId", "");
                      }}
                      placeholder="Şirket seçin"
                      required
                      disabled={Boolean(editingId)}
                      options={companies.map((company) => ({
                        value: company.id,
                        label: `${company.code} · ${company.name}`,
                      }))}
                    />
                    <Select
                      label="Şube"
                      value={form.branchId}
                      onChange={(event) => updateForm("branchId", event.target.value)}
                      placeholder="Şube seçin"
                      options={formBranches.map((branch) => ({
                        value: branch.id,
                        label: `${branch.code} · ${branch.name}`,
                      }))}
                    />
                    <Input
                      label="Sicil / Personel Numarası"
                      value={form.employeeNumber}
                      onChange={(event) => updateForm("employeeNumber", event.target.value)}
                      disabled={Boolean(editingId)}
                      required
                    />
                    <Select
                      label="Personel Durumu"
                      value={form.status}
                      onChange={(event) => updateForm("status", event.target.value)}
                      options={statusOptions}
                      disabled={!editingId}
                    />
                    <Input
                      label="Departman / Meslek"
                      value={form.profession}
                      onChange={(event) => updateForm("profession", event.target.value)}
                    />
                    <Input
                      label="Pozisyon / Görev"
                      value={form.jobTitle}
                      onChange={(event) => updateForm("jobTitle", event.target.value)}
                    />
                  </div>
                </div>

                <div className="mb-6">
                  <h3 className="font-semibold text-slate-900">Kimlik ve iletişim</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Input label="Ad" value={form.firstName} onChange={(event) => updateForm("firstName", event.target.value)} required />
                    <Input label="Soyad" value={form.lastName} onChange={(event) => updateForm("lastName", event.target.value)} required />
                    <Input label="TC Kimlik Numarası" value={form.identityNumber} onChange={(event) => updateForm("identityNumber", event.target.value)} onBlur={(event) => void checkRehire(event.target.value)} maxLength={11} inputMode="numeric" />
                    <Input label="Doğum Tarihi" type="date" value={form.birthDate} onChange={(event) => updateForm("birthDate", event.target.value)} />
                    <Input label="Telefon" value={form.phone} onChange={(event) => updateForm("phone", event.target.value)} />
                    <Input label="E-posta" type="email" value={form.email} onChange={(event) => updateForm("email", event.target.value)} />
                    <Input label="Adres" value={form.address} onChange={(event) => updateForm("address", event.target.value)} className="md:col-span-2" />
                  </div>
                </div>

                {/* İşe alım öncesi kontrol: eski personel eşleşmesi.
                    Engel körlemesine değil — kod, tarih ve GEREKÇE
                    burada görünür. */}
                {checkingRehire ? (
                  <div className="mb-6 rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
                    Geçmiş kayıt kontrol ediliyor...
                  </div>
                ) : null}

                {rehireCheck && rehireCheck.matched &&
                 rehireCheck.decision !== "clear" ? (
                  <div
                    className={`mb-6 rounded-xl border p-4 text-sm ${
                      rehireCheck.decision === "blocked"
                        ? "border-red-300 bg-red-50 text-red-900"
                        : "border-amber-300 bg-amber-50 text-amber-900"
                    }`}
                  >
                    <div className="font-bold">
                      {rehireCheck.decision === "blocked"
                        ? "İşe alım engellendi"
                        : "Dikkat: şartlı değerlendirme"}
                    </div>

                    <div className="mt-2">{rehireCheck.message}</div>

                    <dl className="mt-3 grid gap-1 text-xs">
                      <div>
                        <span className="font-semibold">Kişi: </span>
                        {rehireCheck.personnelFullName}
                      </div>

                      {rehireCheck.terminationDate ? (
                        <div>
                          <span className="font-semibold">Ayrılış: </span>
                          {new Date(
                            rehireCheck.terminationDate
                          ).toLocaleDateString("tr-TR")}
                        </div>
                      ) : null}

                      <div>
                        <span className="font-semibold">Değerlendirme: </span>
                        {rehireCheck.rehireCodeName}
                      </div>

                      {rehireCheck.rehireNote ? (
                        <div>
                          <span className="font-semibold">Gerekçe: </span>
                          {rehireCheck.rehireNote}
                        </div>
                      ) : null}

                      {rehireCheck.rehireMarkedByName ? (
                        <div>
                          <span className="font-semibold">İşaretleyen: </span>
                          {rehireCheck.rehireMarkedByName}
                        </div>
                      ) : null}
                    </dl>

                    {rehireCheck.decision === "blocked" ? (
                      <label className="mt-4 block">
                        <span className="text-xs font-semibold">
                          Engeli geçme gerekçesi (yalnız Genel Müdür)
                        </span>

                        <textarea
                          value={rehireOverrideReason}
                          onChange={(event) =>
                            setRehireOverrideReason(event.target.value)
                          }
                          rows={2}
                          placeholder="Neden bu kişiyle yeniden çalışılıyor?"
                          className="mt-1 w-full rounded-lg border border-red-300 p-2 text-sm"
                        />

                        <span className="mt-1 block text-xs opacity-80">
                          Gerekçe girilmeden kayıt açılamaz. Her geçiş
                          kim/ne zaman/hangi gerekçe olarak denetim izine
                          yazılır.
                        </span>
                      </label>
                    ) : null}
                  </div>
                ) : null}

                <div className="mb-8 rounded-xl border border-slate-200 bg-slate-50 p-4">
                  <h3 className="font-semibold text-slate-900">İstihdam ve ücret</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Input label="SGK Sicil Numarası" value={form.sgkRegistrationNumber} onChange={(event) => updateForm("sgkRegistrationNumber", event.target.value)} />
                    <Input label="Aylık Ücret" type="number" min="0" step="0.01" value={form.monthlySalary} onChange={(event) => updateForm("monthlySalary", event.target.value)} />
                    <Input label="İşe Giriş Tarihi" type="date" value={form.employmentStartDate} onChange={(event) => updateForm("employmentStartDate", event.target.value)} />
                    <Input label="İşten Çıkış Tarihi" type="date" value={form.employmentEndDate} onChange={(event) => updateForm("employmentEndDate", event.target.value)} disabled={!editingId} />
                    <Input label="Fazla Mesai Muvafakati (yıl)" type="number" min={2000} max={2100} placeholder="Alınmadı" value={form.overtimeConsentYear} onChange={(event) => updateForm("overtimeConsentYear", event.target.value)} />
                    <Input label="Muvafakat Tarihi" type="date" value={form.overtimeConsentDate} onChange={(event) => updateForm("overtimeConsentDate", event.target.value)} />

                    {/* EK ÖDEME (ELDEN).
                        Resmî net + elden + toplam ele geçen tek yerde:
                        üçü ayrı ekranlarda durduğu sürece "eline ne
                        geçiyor" sorusu hiçbir yerde cevaplanmıyordu.
                        Blok salary.view olmayana hiç açılmaz. */}
                    {permissions.has("salary.view") && (
                      <div className="md:col-span-2 rounded-lg border border-slate-200 bg-white p-4">
                        <div className="flex items-baseline justify-between gap-3">
                          <strong className="text-sm text-slate-800">
                            Ek ödeme ve ele geçen
                          </strong>
                          {extraPaymentLoading && (
                            <span className="text-xs text-slate-500">
                              yükleniyor...
                            </span>
                          )}
                        </div>

                        <div className="mt-3 grid gap-3 md:grid-cols-4">
                          <div className="rounded-lg border border-slate-200 px-3 py-2">
                            <div className="text-xs text-slate-500">Resmî Net Maaş</div>
                            <div className="mt-1 text-lg font-semibold tabular-nums">
                              {officialNet == null ? "—" : moneyFormat(officialNet)}
                            </div>
                            {officialNetSource && (
                              <div className="mt-1 text-xs text-slate-400">
                                {officialNetSource}
                              </div>
                            )}
                          </div>

                          <div className="rounded-lg border border-slate-200 px-3 py-2">
                            <label className="text-xs text-slate-500">
                              Aylık Ek Ödeme (elden)
                            </label>
                            <input
                              value={extraPayment}
                              onChange={(event) => setExtraPayment(event.target.value)}
                              inputMode="decimal"
                              placeholder="0,00"
                              disabled={!permissions.has("extra_payment.manage")}
                              className="mt-1 w-full rounded-md border border-slate-300 px-2 py-1.5 text-lg font-semibold tabular-nums disabled:bg-slate-50 disabled:text-slate-400"
                            />
                            <div className="mt-1 text-xs text-slate-400">
                              {permissions.has("extra_payment.manage")
                                ? "Resmî bordroya, muhasebe fişine ve SGK matrahına girmez."
                                : "Değiştirme yetkiniz yok."}
                            </div>
                          </div>

                          {/* BU AYIN MESAİSİ.
                              Saat ve tutar personel kartındaki mesai
                              paneliyle AYNI uçtan: orada fazla mesai
                              talebi ve puantaj cetveli birleştirilip
                              talebin sahiplendiği gün elendiği için
                              rakam bir kez sayılıyor. Burada yeniden
                              hesaplasaydık iki ekran ayrı rakam
                              gösterirdi. */}
                          <div className="rounded-lg border border-slate-200 px-3 py-2">
                            <div className="text-xs text-slate-500">
                              Bu Ay Mesai (elden)
                            </div>
                            <div className="mt-1 text-lg font-semibold tabular-nums">
                              {overtimeSummary?.currentMonth.amount == null
                                ? "—"
                                : moneyFormat(
                                    overtimeSummary.currentMonth.amount
                                  )}
                            </div>
                            <div className="mt-1 text-xs text-slate-400">
                              {overtimeSummary
                                ? `${overtimeSummary.currentMonth.hours} saat · talep + cetvel`
                                : "Mesai bilgisi yok"}
                            </div>
                          </div>

                          <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2">
                            <div className="text-xs text-slate-500">Toplam Ele Geçen</div>
                            <div className="mt-1 text-lg font-bold tabular-nums">
                              {officialNet == null
                                ? "—"
                                : moneyFormat(
                                    officialNet +
                                      (Number(extraPayment.replace(",", ".")) || 0) +
                                      (overtimeSummary?.currentMonth.amount ?? 0)
                                  )}
                            </div>
                            <div className="mt-1 text-xs text-slate-400">
                              Resmî net + elden + bu ayın mesaisi
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                    {editingId && (
                      <label className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 md:col-span-2">
                        <input
                          type="checkbox"
                          checked={form.isActive}
                          onChange={(event) => updateForm("isActive", event.target.checked)}
                          className="h-4 w-4 rounded border-slate-300"
                        />
                        <span>
                          <strong className="block text-sm text-slate-800">Aktif personel kaydı</strong>
                          <span className="text-xs text-slate-500">Pasife alınan personel aktif işlem listelerinden çıkarılır.</span>
                        </span>
                      </label>
                    )}
                  </div>
                </div>

                <div className="sticky bottom-0 flex justify-end gap-3 border-t border-slate-200 bg-white py-4">
                  <Button type="button" variant="secondary" onClick={() => setFormOpen(false)} disabled={saving}>
                    Vazgeç
                  </Button>
                  {(editingId ? actions.can("edit") : actions.can("create")) && (
                    <Button type="submit" loading={saving}>
                      {editingId ? "Değişiklikleri Kaydet" : "Personeli Kaydet"}
                    </Button>
                  )}
                </div>
              </form>
            )}
          </aside>
        </div>
      )}
    </ErpShell>
  );
}
