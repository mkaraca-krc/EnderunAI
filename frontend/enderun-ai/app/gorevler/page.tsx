"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  hrOrganizationService,
  type HrDepartment,
} from "@/services/hr-organization.service";
import { Button, ConfirmDialog } from "@/components/ui";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  branchService,
  type BranchListItem,
} from "@/services/branch.service";

import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";

import {
  workTaskService,
  WorkTaskPriority,
  WorkTaskStatus,
  WorkTaskKind,
  DURUM_ETIKETLERI,
  ONCELIK_ETIKETLERI,
  TUR_ETIKETLERI,
  SECILEBILIR_TURLER,
  durumEtiketi,
  durumRengi,
  oncelikEtiketi,
  oncelikRengi,
  type WorkTask,
  type WorkTaskDashboard,
} from "@/services/work-task.service";

/*
 * ETİKET VE RENK HARİTALARI BURADAN KALDIRILDI.
 *
 * Burada `statusLabels`, `statusClasses` ve `priorityLabels` vardı;
 * detay ekranında da AYRI kopyaları vardı ve ikisi ayrıştı. Üstelik
 * buradaki kopya da eksikti: `Draft=0` ve `Waiting=3` arka uçta hiç
 * yok, `Approved=6` ve `Returned=7` ise burada yoktu — onaylanmış bir
 * görev listede İngilizce "Approved" olarak görünüyordu.
 *
 * Tek kaynak: `services/work-task.service.ts`.
 */

const initialForm = {
  companyId: "",
  /*
   * MASRAF MERKEZİ — ÖNCE TÜR, SONRA LİSTE.
   *
   * `merkezTuru` ekranın kendi durumu; sunucuya gönderilmiyor.
   * Sunucu türü seçimden TÜRETİYOR (tek kaynak). Buradaki alan
   * yalnızca "hangi listeyi göstereyim" sorusunu cevaplıyor.
   */
  merkezTuru: "proje" as "proje" | "sube" | "santiye",
  branchId: "",
  projectSiteId: "",
  projectId: "",
  /*
   * ATAMA KASKADI — `merkezTuru` ile AYNI DESEN.
   *
   * `atamaKaynagi` ekranın kendi durumu; sunucuya GÖNDERİLMİYOR.
   * Yalnızca "personel listesini neye göre daraltayım" sorusunu
   * cevaplıyor. Sunucuya giden tek şey `assignedToPersonnelId`.
   *
   * "TÜMÜ" HER ZAMAN AÇIK: kaskadın bir kolu boş olsa bile ekran
   * kullanılabilir kalmalı. Ölçüldü (2026-09-04): departman bağı
   * canlıda 0/79 idi; kaskadı zorunlu kılsaydık kimse görev
   * atayamazdı.
   */
  atamaKaynagi: "tumu" as "tumu" | "departman" | "proje",
  atamaDepartmanId: "",
  atamaProjeId: "",
  assignedToPersonnelId: "",
  title: "",
  description: "",
  /*
   * TÜR BOŞ BAŞLAR — VARSAYILAN YOK, BİLEREK.
   *
   * "İş emri"yi varsayılan yapmak kolaydı ama alanı ANLAMSIZ kılardı:
   * kimse seçmezse hepsi iş emri olurdu ve tür hiçbir şeyi ölçmezdi.
   * Sunucu da aynı gerekçeyle `Belirsiz`i reddediyor; burada boş
   * bırakmak o kararın ekran tarafındaki karşılığı.
   */
  kind: "",
  priority: String(WorkTaskPriority.Normal),
  startDate: "",
  dueDate: "",
  tags: "",
};

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString(
    "tr-TR"
  );
}

export default function WorkTasksPage() {
  /**
   * Düğme -> uç -> izin (WorkTasksController):
   *   POST tasks               -> tasks.manage
   *   POST tasks/{id}/start    -> tasks.manage
   *   POST tasks/{id}/complete -> tasks.manage
   *   POST tasks/{id}/cancel   -> tasks.manage
   *
   * BU MODÜLDE YETKİ AYRIMI YOK: oluşturma, başlatma, tamamlama ve
   * İPTAL aynı anahtarda. "Yıkıcı aksiyon delete yetkisi ister"
   * kuralını burada uygulayamam — tasks.delete diye bir anahtar yok
   * ve uç tek anahtar zorluyor. Arayüzde uydursaydım tasks.manage'i
   * olan kullanıcı iptal düğmesini göremez ama uca yine erişirdi.
   * Ayrım isteniyorsa ÖNCE uç bölünmeli (bkz. TEMIZLIK-TARAMASI.md).
   */
  const actions = useModuleActions("tasks");
  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [branches, setBranches] = useState<BranchListItem[]>([]);

  /*
   * ŞANTİYE LİSTESİ PROJEYE BAĞLI.
   *
   * Uç `/api/projects/{projectId}/sites` — yani şantiye görebilmek için
   * önce proje seçilmiş olmalı. Kademe bu yüzden doğal: tür "şantiye"
   * ise önce proje, sonra o projenin şantiyeleri.
   */
  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);

  const [items, setItems] = useState<WorkTask[]>([]);

  /*
   * ATAMA KASKADI İÇİN VERİ.
   *
   * Personel ve departmanlar bir kez yükleniyor; kaskad istemcide
   * daraltıyor. Her seçimde sunucuya gitmek, üç tıklamada üç istek
   * demekti — ve liste zaten 79 satır.
   *
   * PERSONEL KAPSAMLI GELİYOR: uç `IScopedData` üzerinden süzüyor,
   * yani kullanıcı görmediği personeli burada da göremez.
   */
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [taskDepartments, setTaskDepartments] = useState<HrDepartment[]>([]);

  const [dashboard, setDashboard] =
    useState<WorkTaskDashboard | null>(null);

  const [form, setForm] = useState(initialForm);

  /*
   * ATAMA VERİSİ AYRI YÜKLENİYOR VE HATASI YUTULUYOR.
   *
   * Alınamazsa görev ekranı yine çalışır; yalnız atama seçicisi boş
   * kalır ve bunu SÖYLER. Görev açmayı, personel listesinin
   * alınamamasına bağlamak orantısız olurdu.
   */
  useEffect(() => {
    let iptal = false;

    (async () => {
      try {
        const [kisiler, departmanlar] = await Promise.all([
          personnelService.getAll(),
          hrOrganizationService.getDepartments(),
        ]);

        if (iptal) return;

        setPersonnel(
          kisiler.filter((x) => x.isActive && x.status === 1)
        );
        setTaskDepartments(
          departmanlar.filter((x) => x.isActive !== false)
        );
      } catch {
        if (!iptal) {
          setPersonnel([]);
          setTaskDepartments([]);
        }
      }
    })();

    return () => {
      iptal = true;
    };
  }, []);

  /**
   * Kaskadın o anki personel listesi.
   *
   * "TÜMÜ" HER ZAMAN AÇIK — kaskadın bir kolu boş olsa bile ekran
   * kullanılabilir kalır.
   */
  const atanabilirPersonel = useMemo(() => {
    if (form.atamaKaynagi === "departman") {
      if (!form.atamaDepartmanId) return [];
      return personnel.filter(
        (x) => x.departmentId === form.atamaDepartmanId
      );
    }

    if (form.atamaKaynagi === "proje") {
      if (!form.atamaProjeId) return [];
      return personnel.filter((x) =>
        x.activeAssignments?.some(
          (a) => a.projectId === form.atamaProjeId
        )
      );
    }

    return personnel;
  }, [
    personnel,
    form.atamaKaynagi,
    form.atamaDepartmanId,
    form.atamaProjeId,
  ]);
  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] =
    useState("");

  const [projectFilter, setProjectFilter] =
    useState("");

  const [statusFilter, setStatusFilter] =
    useState("");

  const [priorityFilter, setPriorityFilter] =
    useState("");

  /*
   * TÜR SÜZGECİ — BOŞ DEĞER "İŞ EMRİ" DEMEK, "HEPSİ" DEĞİL.
   *
   * Diğer süzgeçlerde boş dize "süzme" anlamına geliyor; burada
   * gelmiyor ve bu bilerek. Kütüğün varsayılanı dar: alan hiç
   * gönderilmiyor, sunucu yalnız iş emri döndürüyor. "Tümü" ayrı
   * ve AÇIK bir seçim (`0`).
   */
  const [kindFilter, setKindFilter] =
    useState("");

  const [overdueOnly, setOverdueOnly] =
    useState(false);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Onay bekleyen görev işlemi. */
  const [pending, setPending] = useState<{
    kind: "complete" | "cancel";
    id: string;
  } | null>(null);

  const [processingId, setProcessingId] =
    useState("");

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const filteredFormProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !form.companyId ||
          project.companyId === form.companyId
      ),
    [projects, form.companyId]
  );

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !companyFilter ||
          project.companyId === companyFilter
      ),
    [projects, companyFilter]
  );

  /*
   * ŞANTİYE LİSTESİ OLAY ANINDA ÇEKİLİR — EFEKTLE DEĞİL.
   *
   * İlk yazımım `useEffect` + `setSites` desenindeydi ve lint cırcırını
   * 154'ten 155'e çıkardı. Cırcır bir TAVAN ("toplam çizgiyi aşamaz")
   * ve yükseltilemez; test sayısı cırcırıyla karıştırmak üzereydim —
   * o yukarı serbest, bu değil.
   *
   * Doğru yer zaten olay işleyicisi: liste ancak kullanıcı tür ya da
   * proje değiştirdiğinde anlam kazanıyor. Efekt gereksizdi.
   *
   * PROJE DEĞİŞİNCE ŞANTİYE SIFIRLANIR: bir projenin şantiyesi başka
   * projeyle gönderilebilseydi iki kaynak çelişirdi. Sunucu bu bileşimi
   * ayrıca REDDEDİYOR (MasrafMerkeziKurali) — buradaki sıfırlama
   * kullanıcıyı reddedilecek bir seçimden koruyor, kapının kendisi orada.
   */
  /*
   * MERKEZ METNİ — TEK ÇÖZÜCÜ.
   *
   * Liste ve (varsa) diğer yerler aynı metni üretsin diye tek yerde.
   * Sıra önemli: şantiye en dar merkez, ondan sonra şube, en son proje.
   * Kayıtta şantiye varsa projesi de dolu olur; o durumda şantiyeyi
   * göstermek daha bilgilendirici.
   */
  function merkezMetni(item: WorkTask): string {
    /*
     * ADLAR SUNUCUDAN. İlk yazımım adları ekranın kendi listelerinden
     * çözüyordu; detay ekranı o listeleri çekmediği için aynı bilgi
     * iki ayrı yoldan üretilecekti. Tek kaynak sunucu (`ToDto`).
     *
     * Sıra önemli: şantiye en dar merkez, sonra şube, en son proje.
     * Şantiyede projesi de dolu olur; şantiyeyi göstermek daha
     * bilgilendirici, projeyi parantezde veriyoruz.
     */
    if (item.projectSiteId) {
      const ad = item.projectSiteName ?? "Şantiye";
      return item.projectName ? `${ad} (${item.projectName})` : ad;
    }

    if (item.branchId) return item.branchName ?? "Şube";
    if (item.projectId) return item.projectName ?? "Proje";

    return "—";
  }


  async function santiyeleriYukle(projectId: string) {
    /*
     * ÇIPLAK `return;` YOK — SESSİZ YÜKLENİYOR CIRCIRI İÇİN.
     *
     * İlk yazımım erken çıkışlıydı ve cırcır onu bildirdi. Cırcır
     * KONUMA bakıyor: `useState(true)` ile ilk `setLoading(false)`
     * arasındaki çıplak `return;`leri sayıyor. Bu fonksiyonun yükleme
     * durumuyla ilgisi yok, yani teknik olarak yanlış pozitif — ama
     * cırcırı gevşetmek yerine deseni değiştirdim: erken çıkış zaten
     * gerekli değildi ve if/else daha okunur.
     */
    if (!projectId) {
      setSites([]);
    } else {
      try {
        setSites(await projectSiteService.getAll(projectId));
      } catch {
        setSites([]);
      }
    }
  }

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [
        companyRows,
        projectRows,
        branchRows,
        taskRows,
        dashboardData,
      ] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        /*
         * ŞUBELER ANA YÜKLEMEDE, ŞANTİYELER DEĞİL.
         *
         * Şube listesi şirkete bağlı ve küçük; peşin çekiliyor.
         * Şantiye listesi PROJEYE bağlı (uç `/projects/{id}/sites`),
         * yani hangi projenin seçileceği bilinmeden çekilemez —
         * proje seçilince ayrı efektte geliyor.
         */
        branchService.getAll(),
        workTaskService.getAll({
          companyId:
            companyFilter || undefined,
          projectId:
            projectFilter || undefined,
          status:
            statusFilter === ""
              ? undefined
              : Number(statusFilter),
          priority:
            priorityFilter === ""
              ? undefined
              : Number(priorityFilter),
          kind:
            kindFilter === ""
              ? undefined
              : Number(kindFilter),
          overdueOnly,
        }),
        workTaskService.getDashboard(),
      ]);

      setCompanies(companyRows);
      setProjects(projectRows);
      setBranches(branchRows);
      setItems(taskRows.items);
      setDashboard(dashboardData);

      if (
        !form.companyId &&
        companyRows.length === 1
      ) {
        setForm((current) => ({
          ...current,
          companyId: companyRows[0].id,
        }));
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emirleri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyFilter,
    projectFilter,
    statusFilter,
    priorityFilter,
    kindFilter,
    overdueOnly,
    form.companyId,
  ]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (
      projectFilter &&
      !filteredProjects.some(
        (project) =>
          project.id === projectFilter
      )
    ) {
      setProjectFilter("");
    }
  }, [filteredProjects, projectFilter]);

  useEffect(() => {
    if (
      form.projectId &&
      !filteredFormProjects.some(
        (project) =>
          project.id === form.projectId
      )
    ) {
      setForm((current) => ({
        ...current,
        projectId: "",
      }));
    }
  }, [
    filteredFormProjects,
    form.projectId,
  ]);

  async function createTask(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await workTaskService.create({
        companyId: form.companyId,
        /*
         * MERKEZ GÖNDERİLİYOR — `centerType` GÖNDERİLMİYOR.
         *
         * Tür sunucuda seçimden türetiliyor (tek kaynak). Buradan
         * ayrıca göndermek ikinci bir kaynak yaratır; ikisi çelişirse
         * hangisinin doğru olduğu bilinemez.
         */
        projectId: form.projectId || null,
        branchId: form.branchId || null,
        projectSiteId: form.projectSiteId || null,
        title: form.title.trim(),
        description:
          form.description.trim() || null,
        priority: Number(
          form.priority
        ) as WorkTaskPriority,
        kind: Number(form.kind) as WorkTaskKind,
        assignedToUserId: null,
        /*
         * ATAMA İSTEĞE BAĞLI: Faz 1'in kuralı atamasız görevi kabul
         * ediyor. Boş dize `null`a çevriliyor — sunucu `Guid?`
         * bekliyor ve boş dize onun için geçersiz olurdu.
         *
         * KULLANICI ATAMASI FORMDA YOK, BİLEREK: ölçüldü ki 79
         * personelin 13 kullanıcıyla SIFIR bağı var; GM'nin "kime
         * verdim" sorusunun cevabı personel. Kullanıcıya devretme
         * zaten ayrı bir akışta (`delegate`) duruyor ve Faz 1'in
         * kuralı ikisinin aynı anda dolmasını reddediyor.
         */
        assignedToPersonnelId:
          form.assignedToPersonnelId || null,
        startDate: form.startDate || null,
        dueDate: form.dueDate || null,
        sourceModule: "MANUAL",
        sourceEntityId: null,
        sourceEventCode: null,
        tags: form.tags.trim() || null,
      });

      setSuccess(
        "İş emri başarıyla oluşturuldu."
      );

      setForm({
        ...initialForm,
        companyId: form.companyId,
      });

      setShowForm(false);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function startTask(id: string) {
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.start(id);
      setSuccess("İş emri başlatıldı.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri başlatılamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  /**
   * Görevi tamamla — not isteğe bağlı.
   *
   * Eskiden window.prompt sonucu `?? ""` ile karşılanıyordu: kullanıcı
   * "Vazgeç"e bassa bile null boş metne dönüşüyor ve GÖREV YİNE
   * TAMAMLANIYORDU. Diyalogdan çıkış yolu yoktu.
   */
  async function completeTask(id: string, note: string) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.complete(
        id,
        note.trim() || null
      );

      setSuccess("İş emri tamamlandı.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri tamamlanamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  async function cancelTask(id: string, reason: string) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.cancel(
        id,
        reason.trim()
      );

      setSuccess("İş emri iptal edildi.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri iptal edilemedi."
      );
    } finally {
      setProcessingId("");
    }
  }


  /* Sütunlar `projects`, `actions` ve `processingId` üzerine kapanıyor;
     belleğe alınmıyor (bayat kapanış riski — F4b desen kararı). */
  const columns: DataTableColumn<WorkTask>[] = [
    {
      key: "gorev",
      header: "İş Emri",
      value: (item) =>
        `${item.taskNumber} — ${item.title}${item.isOverdue ? " (Gecikti)" : ""}`,
      render: (item) => (
        <>
          {/*
            NUMARA BİR BAĞLANTI — DETAYA TEK GİRİŞ BURASI.

            Önce `<strong>` idi: `/gorevler/[id]` ekranı VARDI ama
            listeden oraya GİDİLEMİYORDU. Genel Müdür numaraya
            tıkladı, hiçbir şey olmadı.

            Yan etkisi: MERKEZ/1'in "detayda masraf merkezi görünüyor"
            iddiası DOĞRULANAMIYORDU — doğrulanacak ekrana
            ulaşılamıyordu.

            ÖLÇÜLDÜ: rota erişilebilirdi ama yalnız ARKA UÇTAN —
            `TaskDueNotificationScanner.cs:88` termin bildiriminde
            `/gorevler/{id}` üretiyor. Yani "bağlantı yok" değil,
            "kullanıcının bulunduğu yerden yok"du. Ön yüzü tarayan bir
            ölçüm bunu "bağlantı yok" diye okur ve yanılır
            (METİN-BAĞ/1'in dersi).

            Desen mevcut: `erp-row-link` üç ekranda kullanılıyor.
          */}
          <Link
            href={`/gorevler/${item.id}`}
            className="erp-row-link"
          >
            <strong>{item.taskNumber}</strong>
          </Link>
          <small>{item.title}</small>
          {item.isOverdue && <span className="erp-status red">Gecikti</span>}
        </>
      ),
    },
    {
      /*
       * MERKEZ SÜTUNU — "PROJE" SÜTUNUNUN YERİNE.
       *
       * Önce yalnız proje gösteriliyordu; şube ya da şantiyeye bağlı bir
       * iş emri listede "—" görünüyordu. Genel Müdür "iş emrinde merkez
       * çıkmıyor" derken bunu söylüyordu: veri uçtan geliyordu, ekran
       * okumuyordu bile.
       */
      key: "merkez",
      header: "Merkez",
      value: (item) => merkezMetni(item),
      render: (item) => {
        const metin = merkezMetni(item);

        if (metin === "—") return "—";

        // "KOD — Ad" biçimini iki satıra ayırıyoruz; ayıramıyorsak
        // metni olduğu gibi basıyoruz (uydurmak yok).
        const [kod, ...kalan] = metin.split(" — ");
        const ad = kalan.join(" — ");

        return ad ? (
          <>
            <strong>{kod}</strong>
            <small>{ad}</small>
          </>
        ) : (
          <strong>{metin}</strong>
        );
      },
    },
    {
      /*
       * ═══ "YAPACAK" SÜTUNU — FAZ 2 ═══
       *
       * Genel Müdür'ün asıl sorusu buydu: "kime verdim?"
       *
       * DEĞER SUNUCUDA HESAPLANIYOR (`assignedToDisplayName`). Ekran
       * kullanıcı adı ile personel adı arasında SEÇİM YAPMIYOR —
       * çünkü Faz 1'de çelişki kaynakta reddedildi: iki atama alanı
       * asla birlikte dolamaz, dolayısıyla bir öncelik kuralı da yok.
       *
       * Burada bir "ya öbürü doluysa" mantığı yazsaydık, kaynakta
       * olmayan bir belirsizliği ekranda uydurmuş olurduk.
       */
      key: "yapacak",
      header: "Yapacak",
      value: (item) => item.assignedToDisplayName ?? "—",
      render: (item) =>
        item.assignedToDisplayName ? (
          <span>{item.assignedToDisplayName}</span>
        ) : (
          // ATANMAMIŞ GÖREV HATA DEĞİL: Faz 1'in kuralı atamasız
          // görevi kabul ediyor. Uyarı rengi kullanılmıyor.
          <span className="erp-muted">—</span>
        ),
    },
    {
      key: "oncelik",
      header: "Öncelik",
      value: (item) => oncelikEtiketi(item.priority, item.priorityName),
      render: (item) => (
        <span className={`erp-status ${oncelikRengi(item.priority)}`}>
          {oncelikEtiketi(item.priority, item.priorityName)}
        </span>
      ),
    },
    {
      key: "durum",
      header: "Durum",
      value: (item) => durumEtiketi(item.status, item.statusName),
      render: (item) => (
        <span className={`erp-status ${durumRengi(item.status)}`}>
          {durumEtiketi(item.status, item.statusName)}
        </span>
      ),
    },
    {
      key: "baslangic",
      header: "Başlangıç",
      value: (item) => formatDate(item.startDate),
    },
    {
      key: "sonTarih",
      header: "Son Tarih",
      value: (item) => formatDate(item.dueDate),
    },
    {
      key: "kaynak",
      header: "Kaynak",
      value: (item) => item.sourceModule || "MANUAL",
    },
    {
      key: "islem",
      header: "İşlem",
      value: () => "",
      render: (item) => {
        const closed =
          item.status === WorkTaskStatus.Completed ||
          item.status === WorkTaskStatus.Cancelled;

        return (
          <div className="flex flex-wrap gap-2">
              {/* İkinci koşul önce `WorkTaskStatus.Waiting` idi ve o değer
                  arka uçta HİÇ YOKTU — hiçbir zaman eşleşmeyen ölü bir dal.
                  Yerine `Returned`: iade edilen görev yapana geri döner ve
                  yeniden başlatılabilmelidir. */}
            {(item.status === WorkTaskStatus.Open ||
              item.status === WorkTaskStatus.Returned) &&
              actions.can("manage") && (
                <button
                  type="button"
                  disabled={processingId === item.id}
                  onClick={() => void startTask(item.id)}
                >
                  Başlat
                </button>
              )}

            {!closed && actions.can("manage") && (
              <button
                type="button"
                disabled={processingId === item.id}
                onClick={() => setPending({ kind: "complete", id: item.id })}
              >
                Tamamla
              </button>
            )}

            {!closed && actions.can("manage") && (
              <button
                type="button"
                disabled={processingId === item.id}
                onClick={() => setPending({ kind: "cancel", id: item.id })}
              >
                İptal
              </button>
            )}
          </div>
        );
      },
    },
  ];


  return (
    <ErpShell
      design="redwood"
      title="İş Emirleri"
      description="Şirket, proje ve ERP süreçlerine bağlı iş emirlerini açın ve yönetin"
    >
      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      {success && (
        <div className="erp-alert success">
          {success}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-6">
        {[
          ["Açık", dashboard?.totalOpen ?? 0],
          [
            "Bana Atanan",
            dashboard?.assignedToMe ?? 0,
          ],
          [
            "Bugün Bitecek",
            dashboard?.dueToday ?? 0,
          ],
          ["Geciken", dashboard?.overdue ?? 0],
          ["Kritik", dashboard?.critical ?? 0],
          [
            "Bugün Tamamlanan",
            dashboard?.completedToday ?? 0,
          ],
        ].map(([label, value]) => (
          <div
            key={String(label)}
            className="rounded-xl border bg-white p-4"
          >
            <small>{label}</small>
            <strong className="mt-2 block text-2xl">
              {loading ? "…" : value}
            </strong>
          </div>
        ))}
      </div>

      <div className="erp-page-toolbar">
        {/* Görev ataması ve durum değişikliği ekip içinde yapılıyor. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <div>
          <strong>
            {loading ? "…" : items.length} iş emri
          </strong>
          <span> listelendi</span>
        </div>

        {actions.can("manage") && (
          <button
            type="button"
            className="erp-primary-button"
            onClick={() =>
              setShowForm((value) => !value)
            }
          >
            {showForm
              ? "Formu Kapat"
              : "+ Yeni İş Emri"}
          </button>
        )}
      </div>

      {showForm && (
        <form
          className="erp-form-card"
          onSubmit={createTask}
        >
          <div className="erp-form-header">
            <h2>Yeni İş Emri</h2>
            <p>
              Elle iş emri açın ve projeye
              bağlayın.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Şirket *</span>
              <select
                required
                value={form.companyId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    companyId:
                      event.target.value,
                    projectId: "",
                  })
                }
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.code} —{" "}
                    {company.name}
                  </option>
                ))}
              </select>
            </label>

            {/*
              MASRAF MERKEZİ — ÖNCE TÜR, SONRA LİSTE.

              Genel Müdür "iş emrinde merkez çıkmıyor" dedi. Ölçüldü:
              formda yalnız "Proje" vardı; şube ve şantiye seçicisi HİÇ
              yoktu ve gövde onları hiç göndermiyordu.

              Tür ayrı bir alan olarak SUNUCUYA GÖNDERİLMİYOR — sunucu
              onu seçimden türetiyor (tek kaynak). Buradaki seçim
              yalnızca hangi listenin gösterileceğini belirliyor.
            */}
            <label>
              <span>Masraf Merkezi Türü *</span>
              <select
                value={form.merkezTuru}
                onChange={(event) => {
                  const tur = event.target
                    .value as typeof form.merkezTuru;

                  // Tür değişince eski seçim taşınmaz: proje türünde
                  // şube kalırsa sunucu "tek merkez seçilebilir" der.
                  setForm({
                    ...form,
                    merkezTuru: tur,
                    projectId: "",
                    branchId: "",
                    projectSiteId: "",
                  });
                  setSites([]);
                }}
              >
                <option value="proje">Proje</option>
                <option value="sube">Şube</option>
                <option value="santiye">Şantiye</option>
              </select>
            </label>

            {form.merkezTuru === "sube" && (
              <label>
                <span>Şube *</span>
                <select
                  value={form.branchId}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      branchId: event.target.value,
                    })
                  }
                >
                  <option value="">Şube seçilmedi</option>

                  {branches
                    .filter(
                      (row) =>
                        !form.companyId ||
                        row.companyId === form.companyId
                    )
                    .map((row) => (
                      <option key={row.id} value={row.id}>
                        {row.code} — {row.name}
                      </option>
                    ))}
                </select>
              </label>
            )}

            <label>
              <span>
                {form.merkezTuru === "santiye"
                  ? "Şantiyenin Projesi *"
                  : form.merkezTuru === "proje"
                    ? "Proje *"
                    : "Proje"}
              </span>
              <select
                disabled={form.merkezTuru === "sube"}
                value={form.projectId}
                onChange={(event) => {
                  const projectId = event.target.value;

                  // ŞANTİYE SIFIRLANIR: eski şantiye yeni projeye ait
                  // olmayabilir; sunucu o bileşimi reddediyor.
                  setForm({
                    ...form,
                    projectId,
                    projectSiteId: "",
                  });

                  if (form.merkezTuru === "santiye") {
                    void santiyeleriYukle(projectId);
                  }
                }}
              >
                <option value="">
                  Proje seçilmedi
                </option>

                {filteredFormProjects.map(
                  (project) => (
                    <option
                      key={project.id}
                      value={project.id}
                    >
                      {project.code} —{" "}
                      {project.name}
                    </option>
                  )
                )}
              </select>
            </label>

            {form.merkezTuru === "santiye" && (
              <label>
                <span>Şantiye *</span>
                <select
                  disabled={!form.projectId}
                  value={form.projectSiteId}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      projectSiteId: event.target.value,
                    })
                  }
                >
                  <option value="">
                    {form.projectId
                      ? "Şantiye seçilmedi"
                      : "Önce proje seçin"}
                  </option>

                  {sites.map((row) => (
                    <option key={row.id} value={row.id}>
                      {row.code} — {row.name}
                    </option>
                  ))}
                </select>
              </label>
            )}

            <label className="span-2">
              <span>Başlık *</span>
              <input
                required
                maxLength={250}
                value={form.title}
                onChange={(event) =>
                  setForm({
                    ...form,
                    title: event.target.value,
                  })
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <textarea
                rows={4}
                value={form.description}
                onChange={(event) =>
                  setForm({
                    ...form,
                    description:
                      event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Tür *</span>
              <select
                required
                value={form.kind}
                onChange={(event) =>
                  setForm({
                    ...form,
                    kind: event.target.value,
                  })
                }
              >
                {/*
                  BOŞ SEÇENEK KALDIRILMIYOR: `required` ile birlikte
                  tarayıcı gönderimi engelliyor. Doğrudan "İş Emri"yle
                  başlasaydı kullanıcı seçim YAPMADAN gönderirdi ve
                  alan bir tercihi değil, varsayılanı kaydederdi.

                  LİSTE `SECILEBILIR_TURLER`DEN: `TUR_ETIKETLERI`
                  üzerinden dolaşsaydı "Türü belirtilmemiş" de
                  görünürdü ve sunucu onu reddederdi — kullanıcı
                  seçebildiği bir şeyin reddedilmesiyle karşılaşırdı.
                */}
                <option value="">Seçiniz…</option>
                {SECILEBILIR_TURLER.map((tur) => (
                  <option key={tur} value={tur}>
                    {TUR_ETIKETLERI[tur]}
                  </option>
                ))}
              </select>
            </label>

            {/* ═══════ ATAMA KASKADI ═══════ */}
            <label>
              <span>Kime verilecek</span>
              <select
                value={form.atamaKaynagi}
                onChange={(event) =>
                  setForm({
                    ...form,
                    atamaKaynagi: event.target
                      .value as typeof form.atamaKaynagi,
                    atamaDepartmanId: "",
                    atamaProjeId: "",
                    // KAYNAK DEĞİŞİNCE SEÇİM TEMİZLENİYOR: aksi hâlde
                    // artık listede olmayan bir kişi seçili kalırdı ve
                    // kullanıcı bunu göremezdi.
                    assignedToPersonnelId: "",
                  })
                }
              >
                <option value="tumu">Tüm personel</option>
                <option value="departman">Departmana göre</option>
                <option value="proje">Projeye göre</option>
              </select>
            </label>

            {form.atamaKaynagi === "departman" && (
              <label>
                <span>Departman</span>
                <select
                  value={form.atamaDepartmanId}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      atamaDepartmanId: event.target.value,
                      assignedToPersonnelId: "",
                    })
                  }
                >
                  <option value="">Seçiniz…</option>
                  {taskDepartments.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
              </label>
            )}

            {form.atamaKaynagi === "proje" && (
              <label>
                <span>Proje</span>
                <select
                  value={form.atamaProjeId}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      atamaProjeId: event.target.value,
                      assignedToPersonnelId: "",
                    })
                  }
                >
                  <option value="">Seçiniz…</option>
                  {projects.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.code} · {p.name}
                    </option>
                  ))}
                </select>
              </label>
            )}

            <label>
              <span>Personel</span>
              <select
                value={form.assignedToPersonnelId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    assignedToPersonnelId: event.target.value,
                  })
                }
              >
                {/* ATAMA İSTEĞE BAĞLI: Faz 1'in kuralı atamasız
                    görevi kabul ediyor. */}
                <option value="">Atanmadı</option>
                {atanabilirPersonel.map((kisi) => (
                  <option key={kisi.id} value={kisi.id}>
                    {kisi.fullName}
                    {kisi.departmentName
                      ? ` · ${kisi.departmentName}`
                      : ""}
                  </option>
                ))}
              </select>

              {/*
                  ═══ SESSİZ BOŞ LİSTE YOK ═══

                  Mesajın YERİ ölçümle düzeltildi: ilk tasarımda
                  "departman seçici boşsa" durumuna konacaktı. Ölçüm
                  gösterdi ki seçici BOŞ DEĞİL (canlıda 6 departman);
                  boş olan, seçimden SONRAKİ personel listesi.

                  Boş bir liste, sebebini söylemezse kullanıcı kendi
                  hatasını arar — oysa sorun verinin girilmemiş
                  olmasıdır.
              */}
              {form.atamaKaynagi === "departman" &&
                form.atamaDepartmanId &&
                atanabilirPersonel.length === 0 && (
                  <span className="gorev-bos-uyari">
                    Bu departmana atanmış personel yok — personel
                    departman ataması yapılmamış olabilir.{" "}
                    <Link href="/insan-kaynaklari/personeller">
                      Personel ekranından atayın
                    </Link>
                    , ya da <strong>Tüm personel</strong> seçeneğini
                    kullanın.
                  </span>
                )}

              {form.atamaKaynagi === "proje" &&
                form.atamaProjeId &&
                atanabilirPersonel.length === 0 && (
                  <span className="gorev-bos-uyari">
                    Bu projede görevli personel yok. Şantiye ataması
                    yapılmamış olabilir; <strong>Tüm personel</strong>
                    {" "}seçeneğini kullanabilirsiniz.
                  </span>
                )}

              {form.atamaKaynagi === "tumu" &&
                personnel.length === 0 && (
                  <span className="gorev-bos-uyari">
                    Personel listesi alınamadı; atama yapılamıyor.
                  </span>
                )}
            </label>

            <label>
              <span>Öncelik</span>
              <select
                value={form.priority}
                onChange={(event) =>
                  setForm({
                    ...form,
                    priority:
                      event.target.value,
                  })
                }
              >
                {Object.entries(
                  ONCELIK_ETIKETLERI
                ).map(([value, label]) => (
                  <option
                    key={value}
                    value={value}
                  >
                    {label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Etiketler</span>
              <input
                placeholder="satın alma, acil"
                value={form.tags}
                onChange={(event) =>
                  setForm({
                    ...form,
                    tags: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Başlangıç</span>
              <input
                type="date"
                value={form.startDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    startDate:
                      event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Son Tarih</span>
              <input
                type="date"
                min={form.startDate || undefined}
                value={form.dueDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    dueDate:
                      event.target.value,
                  })
                }
              />
            </label>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() =>
                setShowForm(false)
              }
            >
              Vazgeç
            </button>

            {actions.can("manage") && (
              <button
                type="submit"
                className="erp-primary-button"
                disabled={saving}
              >
                {saving
                  ? "Kaydediliyor..."
                  : "İş Emrini Kaydet"}
              </button>
            )}
          </div>
        </form>
      )}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>
            <select
              value={companyFilter}
              onChange={(event) => {
                setCompanyFilter(
                  event.target.value
                );
                setProjectFilter("");
              }}
            >
              <option value="">
                Tüm şirketler
              </option>

              {companies.map((company) => (
                <option
                  key={company.id}
                  value={company.id}
                >
                  {company.code} —{" "}
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Proje</span>
            <select
              value={projectFilter}
              onChange={(event) =>
                setProjectFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm projeler
              </option>

              {filteredProjects.map(
                (project) => (
                  <option
                    key={project.id}
                    value={project.id}
                  >
                    {project.code} —{" "}
                    {project.name}
                  </option>
                )
              )}
            </select>
          </label>

          <label>
            <span>Durum</span>
            <select
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm durumlar
              </option>

              {Object.entries(DURUM_ETIKETLERI).map(
                ([value, label]) => (
                  <option
                    key={value}
                    value={value}
                  >
                    {label}
                  </option>
                )
              )}
            </select>
          </label>

          <label>
            <span>Tür</span>
            <select
              value={kindFilter}
              onChange={(event) =>
                setKindFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                İş emirleri
              </option>

              <option
                value={String(
                  WorkTaskKind.Hatirlatma
                )}
              >
                Hatırlatmalar
              </option>

              <option value="0">
                Tümü
              </option>
            </select>
          </label>

          <label>
            <span>Öncelik</span>
            <select
              value={priorityFilter}
              onChange={(event) =>
                setPriorityFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm öncelikler
              </option>

              {Object.entries(
                ONCELIK_ETIKETLERI
              ).map(([value, label]) => (
                <option
                  key={value}
                  value={value}
                >
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Gecikme</span>
            <select
              value={
                overdueOnly ? "true" : ""
              }
              onChange={(event) =>
                setOverdueOnly(
                  event.target.value ===
                    "true"
                )
              }
            >
              <option value="">
                Tüm iş emirleri
              </option>
              <option value="true">
                Yalnızca gecikenler
              </option>
            </select>
          </label>
        </div>
      </div>

      <div className="erp-table-card">
        <DataTable
            rows={items}
            columns={columns}
            rowKey={(item) => item.id}
            loading={loading}
            title="İş Emirleri"
            emptyText="İş emri bulunamadı. Yeni bir iş emri açın veya filtreleri değiştirin."
            resetKey={`${projectFilter}|${statusFilter}|${priorityFilter}|${kindFilter}`}
          />
      </div>
      {pending && (
        <ConfirmDialog
          key={`${pending.kind}-${pending.id}`}
          open
          title={
            pending.kind === "complete" ? "İş Emrini Tamamla" : "İş Emrini İptal Et"
          }
          description={
            pending.kind === "complete"
              ? "İş emri tamamlandı olarak işaretlenecek. Tamamlama notu isteğe bağlı ama kayda geçer."
              : "İş emri iptal edilecek. İptal nedeni zorunlu; iş emrini açan kişi bunu görecek."
          }
          confirmLabel={
            pending.kind === "complete" ? "İş Emrini Tamamla" : "İş Emrini İptal Et"
          }
          requireReason={pending.kind === "cancel"}
          showReason
          reasonLabel={
            pending.kind === "complete"
              ? "Tamamlama notu (isteğe bağlı)"
              : "İptal nedeni (zorunlu)"
          }
          busy={processingId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={(text) =>
            pending.kind === "complete"
              ? void completeTask(pending.id, text)
              : void cancelTask(pending.id, text)
          }
        />
      )}
    </ErpShell>
  );
}
