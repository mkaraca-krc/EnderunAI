"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";

import { currencyMoney } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";
import { usePermissions } from "@/lib/use-permissions";
import { useCurrentUser } from "@/lib/use-current-user";
import { apiClient } from "@/lib/api/api-client";
import {
  TODO_KIND_LABELS,
  sortByUrgency,
  terminDurumu,
  type TodoItem,
  type TodoKind,
} from "@/services/todo.service";

/**
 * YAPILACAKLAR — KULLANICININ GÜNE BAŞLARKEN AÇACAĞI EKRAN.
 *
 * ÜÇ BÖLÜM: onayımı bekleyenler (üstte), bana atananlar, gönderdiklerim.
 *
 * ONAY BÖLÜMÜ BEŞ KAYNAKTAN DERLENİYOR ama onay/ret hâlâ kendi
 * uçlarına gidiyor: kurallı akış bu pakete girmiyor, yalnız GÖRÜNÜM
 * birleşiyor. Kullanıcı iki ayrı "bekleyen iş" listesine bakmak
 * zorunda kalmasın diye.
 *
 * MOBİL ÖNCELİKLİ KART DÜZENİ: tablo değil kart. Saha personeli bu
 * ekranı telefondan açacak; 8 sütunlu bir tabloyu yatay kaydırarak
 * kullanmak "çalışıyor" sayılmaz.
 */

type KaynakDurumu = {
  kind: TodoKind;
  items: TodoItem[];
  failed: boolean;
  skipped: boolean;
};

/*
 * PARA BİÇİMİ ORTAK İŞLEVDEN.
 *
 * Kendi `Intl.NumberFormat`'ını kuran ekran, hane sayısını da kendi
 * seçer; iki ekran aynı tutarı farklı gösterir ve hangisinin doğru
 * olduğu bilinmez. Kural `lib/format/turkish.ts` içinde bir kez
 * yazılı — redwood sözleşme testi bunu zorluyor.
 */
function tutarYaz(item: TodoItem) {
  if (item.amount === null || item.amount === undefined) return null;
  return currencyMoney(item.amount, item.currencyCode ?? "TRY");
}

export default function YapilacaklarSayfasi() {
  const { loading: izinYukleniyor } = usePermissions();

  /*
   * BÖLÜM BÖLÜM KAPILI — TEK ANAHTARA BAĞLANAMAZ.
   *
   * Beş kaynağın onayını topluyor. Ekranı tek anahtara bağlamak,
   * yalnız satın alma onayı olan kullanıcıya hakediş bölümünü de
   * gösterirdi (ya da tersine sipariş bölümünü gizlerdi). Sözleşme
   * testi bu ayrımı zorluyor ve eski onay merkezinden devralınan
   * kural budur.
   */
  const taskActions = useModuleActions("tasks");
  const hakedisActions = useModuleActions("hakedis");
  const orderActions = useModuleActions("purchasing-orders");
  const requestActions = useModuleActions("purchasing-requests");
  const reportActions = useModuleActions("site-reports");
  const { user } = useCurrentUser();

  const [kaynaklar, setKaynaklar] = useState<KaynakDurumu[]>([]);
  const [bana, setBana] = useState<TodoItem[]>([]);
  const [benden, setBenden] = useState<TodoItem[]>([]);
  const [banaHata, setBanaHata] = useState(false);
  const [bendenHata, setBendenHata] = useState(false);
  const [yukleniyor, setYukleniyor] = useState(true);
  const [suzgec, setSuzgec] = useState<TodoKind | null>(null);

  const yukle = useCallback(async () => {
    /*
     * KİMLİK YOKSA HİÇ YÜKLEME.
     *
     * `CurrentUser.id` isteğe bağlı: oturum henüz çözülmemişse boş
     * gelebiliyor. O anda istek atmak, "bana atananlar" sorgusunu
     * kimliksiz çalıştırmak demek — sonuç ya boş ya yanlış olurdu.
     */
    const kullaniciId = user?.id;

    if (!kullaniciId) {
      /*
       * ERKEN ÇIKIŞTA YÜKLEME KAPANIR.
       *
       * Önce düz `return` vardı ve `yukleniyor` başlangıç değeri
       * `true`. Kimlik hiç gelmezse ekran SONSUZA KADAR
       * "Yükleniyor…" derdi — hata da göstermeden, çünkü ortada
       * bir hata yok.
       *
       * `auth/me` başarısız olursa `use-current-user.ts` hatayı
       * YUTUYOR ve `user` null kalıyor; yani bu yol teorik değil.
       * DURUM.md §5 kural 26: bir sayfa yükleme durumundan çıkışı
       * GARANTİ etmelidir — erken çıkış ve hata yollarında da.
       */
      setYukleniyor(false);
      return;
    }

    setYukleniyor(true);

    /*
     * İZNİ OLMAYAN KAYNAK HİÇ ÇAĞRILMIYOR.
     *
     * Boş dönmesini beklemek yerine hiç istememek: yetkisiz istek
     * sunucuda 403 üretir, günlüğü kirletir ve "bu kullanıcı neden
     * sürekli reddediliyor" diye bakan birini yanıltır.
     */
    const gorevVar = taskActions.can("view");
    const hakedisVar = hakedisActions.can("approve") || hakedisActions.can("view");
    const siparisVar = orderActions.can("approve") || orderActions.can("view");
    const talepVar = requestActions.can("approve") || requestActions.can("view");
    const raporVar = reportActions.can("approve") || reportActions.can("view");

    const istekler: {
      kind: TodoKind;
      izin: boolean;
      cagri: () => Promise<TodoItem[]>;
    }[] = [
      {
        kind: "task",
        izin: gorevVar,
        cagri: async () => {
          const yanit = await apiClient<{ items: RawTask[] }>(
            "tasks?status=4&pageSize=50",
          );

          return (yanit.items ?? [])
            .filter((x) => x.assignedByUserId === kullaniciId)
            .map(gorevToItem);
        },
      },
      {
        kind: "progressPayment",
        izin: hakedisVar,
        cagri: async () => {
          const yanit = await apiClient<RawProgress[]>(
            "progress-payments?status=1",
          );
          return (yanit ?? []).map(hakedisToItem);
        },
      },
      {
        kind: "purchaseOrder",
        izin: siparisVar,
        cagri: async () => {
          const yanit = await apiClient<RawPurchase[]>("purchase-orders?status=1");
          return (yanit ?? []).map((x) => satinAlmaToItem(x, "purchaseOrder"));
        },
      },
      {
        kind: "purchaseRequest",
        izin: talepVar,
        cagri: async () => {
          const yanit = await apiClient<RawPurchase[]>("purchase-requests?status=1");
          return (yanit ?? []).map((x) => satinAlmaToItem(x, "purchaseRequest"));
        },
      },
      {
        kind: "siteReport",
        izin: raporVar,
        cagri: async () => {
          const yanit = await apiClient<RawReport[]>(
            "site-reports/pending-approval",
          );
          return (yanit ?? []).map(raporToItem);
        },
      },
    ];

    /*
     * HER KAYNAK KENDİ HATA SINIRINDA.
     *
     * Biri patlarsa tüm ekran boş kalmıyor: o bölüm "yüklenemedi"
     * diyor, diğerleri görünmeye devam ediyor. Sayaç da eksik
     * olduğunu belli ediyor ("3+") — sessizce eksik sayı göstermek,
     * olmayan sayıdan kötüdür.
     */
    const sonuclar = await Promise.all(
      istekler.map(async (istek): Promise<KaynakDurumu> => {
        if (!istek.izin) {
          return { kind: istek.kind, items: [], failed: false, skipped: true };
        }

        try {
          return {
            kind: istek.kind,
            items: await istek.cagri(),
            failed: false,
            skipped: false,
          };
        } catch {
          return { kind: istek.kind, items: [], failed: true, skipped: false };
        }
      }),
    );

    setKaynaklar(sonuclar);

    if (gorevVar) {
      try {
        const yanit = await apiClient<{ items: RawTask[] }>(
          `tasks?assignedToUserId=${encodeURIComponent(kullaniciId)}&pageSize=50`,
        );

        setBana(
          (yanit.items ?? [])
            .filter((x) => [1, 2, 7].includes(x.status))
            .map(gorevToItem),
        );
        setBanaHata(false);
      } catch {
        setBanaHata(true);
      }

      try {
        const yanit = await apiClient<{ items: RawTask[] }>("tasks?pageSize=50");

        setBenden(
          (yanit.items ?? [])
            .filter(
              (x) => x.assignedByUserId === kullaniciId && [1, 2, 4, 7].includes(x.status),
            )
            .map(gorevToItem),
        );
        setBendenHata(false);
      } catch {
        setBendenHata(true);
      }
    }

    setYukleniyor(false);
  }, [
    user?.id,
    taskActions,
    hakedisActions,
    orderActions,
    requestActions,
    reportActions,
  ]);

  useEffect(() => {
    if (!izinYukleniyor) void yukle();
  }, [izinYukleniyor, yukle]);

  const onayKalemleri = useMemo(() => {
    const hepsi = kaynaklar.flatMap((x) => x.items);
    const suzulmus = suzgec ? hepsi.filter((x) => x.kind === suzgec) : hepsi;
    return sortByUrgency(suzulmus);
  }, [kaynaklar, suzgec]);

  const eksikKaynak = kaynaklar.some((x) => x.failed);
  const onaySayisi = kaynaklar.reduce((t, x) => t + x.items.length, 0);

  const hepsiBos =
    onaySayisi === 0 && bana.length === 0 && benden.length === 0 && !eksikKaynak;

  return (
    <div className="rw">
      <div className="erp-page-header">
        <h1>Yapılacaklar</h1>
        <p>Onayınızı bekleyen işler, size atanan ve gönderdiğiniz görevler.</p>
      </div>

      {yukleniyor && <div className="erp-alert">Yükleniyor…</div>}

      {!yukleniyor && hepsiBos && (
        <div className="erp-empty-state">
          <p>
            <strong>Bekleyen işiniz yok.</strong> Onayınızı bekleyen, size atanmış
            ya da takip ettiğiniz açık bir iş bulunmuyor.
          </p>
        </div>
      )}

      {!yukleniyor && !hepsiBos && (
        <>
          {/* ---------- ONAYIMI BEKLEYENLER ---------- */}
          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>
                  Onayımı bekleyenler{" "}
                  <span className="erp-todo-count">
                    {onaySayisi}
                    {/* EKSİK KAYNAK BELLİ EDİLİYOR: sessizce eksik
                        sayı göstermek, olmayan sayıdan kötüdür. */}
                    {eksikKaynak ? "+" : ""}
                  </span>
                </h2>
                {eksikKaynak && (
                  <p className="erp-todo-warning">
                    Bazı kaynaklar yüklenemedi; sayı eksik olabilir.
                  </p>
                )}
              </div>
            </div>

            {/* TÜR ROZETİYLE SÜZME: isteyen gruplu görünüm elde eder. */}
            <div className="erp-todo-filters">
              <button
                type="button"
                className={`erp-todo-chip ${suzgec === null ? "active" : ""}`}
                onClick={() => setSuzgec(null)}
              >
                Tümü
              </button>

              {kaynaklar
                .filter((x) => !x.skipped)
                .map((kaynak) => (
                  <button
                    key={kaynak.kind}
                    type="button"
                    className={`erp-todo-chip ${suzgec === kaynak.kind ? "active" : ""} ${
                      kaynak.failed ? "failed" : ""
                    }`}
                    onClick={() => setSuzgec(kaynak.kind)}
                  >
                    {TODO_KIND_LABELS[kaynak.kind]}
                    {kaynak.failed ? " ⚠" : ` (${kaynak.items.length})`}
                  </button>
                ))}
            </div>

            {kaynaklar
              .filter((x) => x.failed)
              .map((kaynak) => (
                <div key={kaynak.kind} className="erp-alert error">
                  {TODO_KIND_LABELS[kaynak.kind]} yüklenemedi.{" "}
                  <button type="button" className="erp-link" onClick={() => void yukle()}>
                    Tekrar dene
                  </button>
                </div>
              ))}

            {onayKalemleri.length === 0 ? (
              <div className="erp-empty-state">
                <p>Onayınızı bekleyen iş yok.</p>
              </div>
            ) : (
              <div className="erp-todo-list">
                {onayKalemleri.map((kalem) => (
                  <TodoKart key={`${kalem.kind}-${kalem.id}`} item={kalem} />
                ))}
              </div>
            )}
          </section>

          {/* ---------- BANA ATANANLAR ---------- */}
          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <h2>
                Bana atananlar <span className="erp-todo-count">{bana.length}</span>
              </h2>
            </div>

            {banaHata ? (
              <div className="erp-alert error">
                Görevler yüklenemedi.{" "}
                <button type="button" className="erp-link" onClick={() => void yukle()}>
                  Tekrar dene
                </button>
              </div>
            ) : bana.length === 0 ? (
              <div className="erp-empty-state">
                <p>Size atanmış açık görev yok.</p>
              </div>
            ) : (
              <div className="erp-todo-list">
                {sortByUrgency(bana).map((kalem) => (
                  <TodoKart key={kalem.id} item={kalem} />
                ))}
              </div>
            )}
          </section>

          {/* ---------- GÖNDERDİKLERİM ---------- */}
          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <h2>
                Gönderdiklerim <span className="erp-todo-count">{benden.length}</span>
              </h2>
            </div>

            {bendenHata ? (
              <div className="erp-alert error">
                Görevler yüklenemedi.{" "}
                <button type="button" className="erp-link" onClick={() => void yukle()}>
                  Tekrar dene
                </button>
              </div>
            ) : benden.length === 0 ? (
              <div className="erp-empty-state">
                <p>Gönderdiğiniz açık görev yok.</p>
              </div>
            ) : (
              <div className="erp-todo-list">
                {sortByUrgency(benden).map((kalem) => (
                  <TodoKart key={kalem.id} item={kalem} />
                ))}
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}

function TodoKart({ item }: { item: TodoItem }) {
  const tutar = tutarYaz(item);

  return (
    <Link
      href={item.href}
      className={`erp-todo-card ${item.isOverdue ? "overdue" : ""} ${
        item.isDueToday ? "due-today" : ""
      }`}
    >
      <div className="erp-todo-card-top">
        <span className="erp-todo-kind">{TODO_KIND_LABELS[item.kind]}</span>

        {item.isOverdue && <span className="erp-todo-flag overdue">Termini geçti</span>}
        {!item.isOverdue && item.isDueToday && (
          <span className="erp-todo-flag today">Bugün</span>
        )}

        {/* İADE SAYISI: üçüncü kez iade edilen iş, tek seferde biten
            işle aynı satırda görünmemeli. */}
        {item.returnCount ? (
          <span className="erp-todo-flag returned">{item.returnCount}. iade</span>
        ) : null}
      </div>

      <div className="erp-todo-card-title">{item.title}</div>

      {item.subtitle && <div className="erp-todo-card-sub">{item.subtitle}</div>}

      <div className="erp-todo-card-bottom">
        {/* TUTAR: 2.000 TL'lik onayla 200.000 TL'lik onay aynı
            ağırlıkta görünmemeli. */}
        {tutar && <span className="erp-todo-amount">{tutar}</span>}

        {item.dueDate && (
          <span className="erp-todo-due">
            Termin: {new Date(item.dueDate).toLocaleDateString("tr-TR")}
          </span>
        )}
      </div>
    </Link>
  );
}

// ---------------------------------------------------------------
// Ham yanıt biçimleri ve dönüşümler
// ---------------------------------------------------------------

type RawTask = {
  id: string;
  taskNumber: string;
  title: string;
  status: number;
  dueDate?: string | null;
  createdAtUtc?: string | null;
  assignedByUserId?: string | null;
  returnCount?: number | null;
  priority?: number | null;
};

type RawProgress = {
  id: string;
  progressPaymentNumber?: string | null;
  projectName?: string | null;
  netPayableAmount?: number | null;
  currencyCode?: string | null;
  createdAtUtc?: string | null;
};

type RawPurchase = {
  id: string;
  orderNumber?: string | null;
  requestNumber?: string | null;
  title?: string | null;
  totalAmount?: number | null;
  currencyCode?: string | null;
  createdAtUtc?: string | null;
};

type RawReport = {
  id: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  siteName?: string | null;
  reportDate?: string | null;
  createdAtUtc?: string | null;
};

function gorevToItem(x: RawTask): TodoItem {
  const durum = terminDurumu(x.dueDate);

  return {
    id: x.id,
    kind: "task",
    title: x.title,
    subtitle: x.taskNumber,
    dueDate: x.dueDate ?? null,
    waitingSince: x.createdAtUtc ?? null,
    href: `/gorevler/${x.id}`,
    returnCount: x.returnCount ?? null,
    priority: x.priority ?? null,
    ...durum,
  };
}

function hakedisToItem(x: RawProgress): TodoItem {
  return {
    id: x.id,
    kind: "progressPayment",
    title: x.progressPaymentNumber ?? "Hakediş",
    subtitle: x.projectName ?? null,
    amount: x.netPayableAmount ?? null,
    currencyCode: x.currencyCode ?? null,
    waitingSince: x.createdAtUtc ?? null,
    /*
     * DOĞRUDAN ONAY BÖLÜMÜNE.
     *
     * Kullanıcı sayfanın başında değil, KARARI VERECEĞİ yerde
     * açılıyor. Bir tık daha var ama o tık, bakmadan onaylamayı
     * engelleyen tık.
     */
    href: `/hakedis/${x.id}#onay`,
    isOverdue: false,
    isDueToday: false,
  };
}

function satinAlmaToItem(x: RawPurchase, kind: TodoKind): TodoItem {
  return {
    id: x.id,
    kind,
    title: x.orderNumber ?? x.requestNumber ?? "Satın alma",
    subtitle: x.title ?? null,
    amount: x.totalAmount ?? null,
    currencyCode: x.currencyCode ?? null,
    waitingSince: x.createdAtUtc ?? null,
    /*
     * GERÇEK ROTALAR — ilk yazışımda uydurmuştum.
     * Sipariş: /satin-alma/siparis/{id}, talep: /satin-alma/{id}.
     * Ölçmeden yazılan bir yol, kullanıcıyı 404'e götürürdü.
     */
    href:
      kind === "purchaseOrder"
        ? `/satin-alma/siparis/${x.id}#onay`
        : `/satin-alma/${x.id}#onay`,
    isOverdue: false,
    isDueToday: false,
  };
}

function raporToItem(x: RawReport): TodoItem {
  return {
    id: x.id,
    kind: "siteReport",
    title: x.siteName ?? "Saha raporu",
    subtitle: x.reportDate
      ? new Date(x.reportDate).toLocaleDateString("tr-TR")
      : null,
    waitingSince: x.createdAtUtc ?? x.reportDate ?? null,
    /*
     * SAHA RAPORUNUN KENDİ EKRANI YOK: raporlar şantiye ekranının
     * altında listeleniyor. Şantiye kimliği yoksa proje listesine
     * götürmek yerine satırı tıklanamaz bırakmak daha dürüst
     * olurdu — ama şantiye kimliği yanıtla geliyor.
     */
    href: x.projectSiteId
      ? `/projeler/${x.projectId ?? ""}/santiyeler/${x.projectSiteId}#raporlar`
      : "/projeler",
    isOverdue: false,
    isDueToday: false,
  };
}
