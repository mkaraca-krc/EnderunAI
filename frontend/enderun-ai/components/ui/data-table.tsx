"use client";

import {
  Fragment,
  useEffect,
  useMemo,
  useRef,
  useState,
  type HTMLAttributes,
  type ReactNode,
} from "react";
import { flushSync } from "react-dom";

import { whole } from "@/lib/format/turkish";

/**
 * STANDART LİSTE TABLOSU — sayfalama, arama/filtre yuvası, dışa
 * aktarma ve yazdırma tek yerde.
 *
 * NEDEN VAR: denetimde 143 liste ekranının HİÇBİRİNDE sayfalama yoktu
 * (`setPage · currentPage · pageSize · totalPages` araması kod
 * tabanının tamamında 0 sonuç), 3'ü dosya indirebiliyordu, 7'sinde
 * yazdır düğmesi vardı. Her ekran kendi tablosunu, kendi arama
 * kutusunu ve kendi gecikmesini ayrı yazmıştı.
 *
 * SÜTUNLAR NEDEN VERİ OLARAK TANIMLANIYOR: dışa aktarma ve yazdırma
 * ancak sütunları BİLEREK genel olabilir. JSX'ten metin kazımak
 * kırılgan olurdu — rozet, ikon ve düğme içeren hücrelerde saçma
 * çıktı üretirdi. Bu yüzden her sütun, ekranda ne gösterdiğinden
 * ayrı olarak "kâğıtta/dosyada ne yazmalı" bilgisini de taşır.
 *
 * İKİ KİP:
 * - `client`: bütün satırlar elde, bileşen dilimler. 100–200 satıra
 *   kadar doğru seçim; canlıda tabloların çok büyük çoğunluğu bu.
 * - `server`: uç `Paged<T>` döndürür, sayfa değişimi ebeveyne
 *   bildirilir. Yalnız GERÇEKTEN büyük listeler için (poz 23.531,
 *   birim fiyat 44.934, puantaj 5.637, denetim 1.580, hesap planı
 *   1.111 — geri kalan 208 tablo 100 satırın altında).
 */

export type DataTableColumn<T> = {
  /** React anahtarı ve dışa aktarma başlığı için kimlik. */
  key: string;
  header: string;
  /** Ekranda görünen hücre. Verilmezse `value` düz metin basılır. */
  render?: (row: T) => ReactNode;
  /**
   * Dosyaya ve kâğıda giden DÜZ değer.
   *
   * `render` rozet/ikon üretiyorsa bu alan zorunlu hâle gelir:
   * dışa aktarmada "▲ Onaylı" değil "Onaylı" yazmalı.
   */
  value?: (row: T) => string | number | null | undefined;
  align?: "left" | "right" | "center";
  /** Sayı sütunlarında hizalı rakam için `tabular-nums` eklenir. */
  numeric?: boolean;
  /**
   * ALT TOPLAM.
   *
   * İSTEMCİ kipinde TÜM satırlar geçilir — görünen sayfa değil.
   * "Toplam" diye etiketlenmiş bir satırın yalnız o sayfayı toplaması,
   * bu programın baştan beri kovaladığı hatanın ta kendisi olurdu
   * (poz ekranı 23.531 kayıt için "Toplam: 100" diyordu).
   *
   * SUNUCU kipinde bu çağrılmaz: elde yalnız bir sayfa var, toplam
   * hesaplanamaz. Toplam sunucudan gelmeli
   * (`server.totals`); gelmiyorsa alt toplam satırı HİÇ gösterilmez.
   */
  footer?: (rows: T[]) => ReactNode;
};

export type DataTableServerMode = {
  /** Süzgeçlere uyan TOPLAM kayıt — uçtan gelir, listeden sayılmaz. */
  total: number;
  /**
   * Sütun anahtarına göre alt toplamlar. Sunucu kipinde elde yalnız
   * bir sayfa olduğu için toplam BURADAN gelmek zorunda; verilmezse
   * alt toplam satırı gösterilmez. Uydurmaktansa göstermemek.
   */
  totals?: Record<string, ReactNode>;
  /** 1'den başlar. */
  page: number;
  pageSize: number;
  onChange: (page: number, pageSize: number) => void;
};

type Props<T> = {
  rows: T[];
  columns: DataTableColumn<T>[];
  rowKey: (row: T) => string;

  /** Verilirse SUNUCU kipi; yoksa istemci kipi. */
  server?: DataTableServerMode;

  /** Arama kutusu ve filtre denetimleri buraya konur. */
  toolbar?: ReactNode;

  /**
   * FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER.
   *
   * Ekran, filtre durumundan türeyen bir değer geçirir (ör.
   * `${search}|${status}`). Geçilmezse kullanıcı 7. sayfadayken
   * filtreyi daraltınca boş sayfada kalırdı — sayfalamanın en sık
   * görülen hatası budur.
   */
  resetKey?: string | number;

  loading?: boolean;
  emptyText?: string;

  /** Yazdırma başlığı ve dosya adı. */
  title?: string;

  /**
   * TÜM KAYITLARI dışa aktarmak için. Sunucu kipinde eldeki sayfa
   * her şey değildir; bu olmadan yalnızca "bu sayfa" sunulur.
   * Veremediğimiz şeyi düğme olarak göstermek yalan olurdu.
   */
  fetchAll?: () => Promise<T[]>;

  /** Varsayılan sayfa boyutu (istemci kipi). */
  defaultPageSize?: number;

  /**
   * Çıktının üstüne basılacak bağlam — hangi süzgeçlerle alındığı
   * gibi. Kâğıda çıkan bir liste "neyin listesi" olduğunu söylemezse
   * bir hafta sonra kimse hatırlamıyor.
   */
  printMeta?: ReactNode;

  /** Alt toplam satırının etiketi. */
  footerLabel?: string;

  /**
   * SATIRIN KENDİSİNE ÖZELLİK EKLER — seçilebilir satırlar için.
   *
   * Bazı ekranlarda satır tıklanabilir ve KLAVYEYLE ERİŞİLEBİLİR
   * olmak zorunda (`tabIndex`, `aria-current`, `onKeyDown`).
   * `finans/kasa-banka` bunun örneği: hesap seçilmeden ekstre
   * görünmüyor, yani satır seçimi bir gezinme aracı.
   *
   * Bileşen bunu desteklemeseydi o ekranlar ham tabloda kalır ve
   * sayfalama/çıktı kazanamazdı.
   */
  rowProps?: (row: T) => HTMLAttributes<HTMLTableRowElement>;

  /**
   * GRUPLAMA — satırlar bir anahtara göre öbeklenir ve her öbeğin
   * başına kendi ALT TOPLAMINI taşıyan bir başlık satırı girer.
   *
   * NEDEN VAR: çek listesi aya göre gruplu ve ay toplamı bir nakit
   * planlama sayısı ("bu ay ne kadar çek ödeyeceğim"). Bileşen bunu
   * desteklemeseydi o ekranlar ham tabloda kalır, sayfalama ve dışa
   * aktarma kazanamazdı — ya da gruplama düşürülüp bilgi kaybedilirdi.
   *
   * SIRA KORUNUR: gruplar, satırların GELİŞ SIRASINDA ilk göründükleri
   * yere göre sıralanır. Yeniden sıralamak, ekranın kendi sıralamasını
   * (vade, tarih) sessizce bozardı.
   *
   * SAYFALAMA SATIRA UYGULANIR, gruba değil: bir grup sayfa sınırını
   * aşarsa başlığı sonraki sayfanın başında TEKRAR EDER. Gruplar
   * sayfalansaydı "sayfa başına 25 kayıt" ayarı anlamını yitirirdi.
   */
  groupBy?: {
    /** Satırı gruba bağlayan anahtar. */
    key: (row: T) => string;

    /**
     * Grup başlığının DÜZ metni — dosyaya ve kâğıda bu gider.
     * Sütunlardaki `value`/`render` ayrımının aynısı: zengin içerik
     * dışa aktarmada saçmalamasın diye metin ayrı veriliyor.
     */
    label: (rows: T[], key: string) => string;

    /** Ekranda görünen zengin başlık; verilmezse `label` basılır. */
    render?: (rows: T[], key: string) => ReactNode;

    /** Grubun alt toplamı — düz metin, dosyaya da girer. */
    summary?: (rows: T[], key: string) => string;

    /** Alt toplamın ekran karşılığı; verilmezse `summary` basılır. */
    renderSummary?: (rows: T[], key: string) => ReactNode;
  };
};

const PAGE_SIZES = [25, 50, 100];

function cellText<T>(column: DataTableColumn<T>, row: T): string {
  if (column.value) {
    const raw = column.value(row);
    return raw === null || raw === undefined ? "" : String(raw);
  }

  const rendered = column.render?.(row);
  return typeof rendered === "string" || typeof rendered === "number"
    ? String(rendered)
    : "";
}

/**
 * CSV üretir — Excel Türkçe kurulumunda ÇİFT TIKLA doğru açılsın diye
 * noktalı virgül ayracı ve UTF-8 BOM kullanılır.
 *
 * Neden gerçek `.xlsx` değil: projede Excel kütüphanesi yok
 * (bağımlılıklar next · react · react-dom · qrcode). Biçimlendirme
 * ya da formül gereken yerler ayrı bir iş; 143 ekrana dışa aktarma
 * yaymanın bedeli sıfır bağımlılıkla karşılanıyor.
 */
function toCsv<T>(
  columns: DataTableColumn<T>[],
  rows: T[],
  /*
   * GRUP BAŞLIKLARI DOSYAYA DA GİRER. Girmeseydi dışa aktarılan liste
   * ekrandan FARKLI bir şey anlatırdı: ay toplamları kaybolur, satırlar
   * hangi aya ait belirsizleşirdi. Etiket ilk sütuna, özet SON sütuna
   * yazılıyor — ekrandaki yerleşimin aynısı.
   */
  groups?: {
    keyOf: (row: T) => string;
    line: (rows: T[], key: string) => { label: string; summary: string };
    members: Map<string, T[]>;
  }
): string {
  const escape = (value: string) =>
    /[";\n\r]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;

  const lines = [columns.map((column) => escape(column.header)).join(";")];

  let lastKey: string | undefined;

  for (const row of rows) {
    if (groups) {
      const key = groups.keyOf(row);

      if (key !== lastKey) {
        lastKey = key;

        const { label, summary } = groups.line(groups.members.get(key) ?? [], key);
        const cells = columns.map(() => "");
        cells[0] = escape(label);
        if (summary) cells[cells.length - 1] = escape(summary);

        lines.push(cells.join(";"));
      }
    }

    lines.push(columns.map((column) => escape(cellText(column, row))).join(";"));
  }

  return `﻿${lines.join("\r\n")}\r\n`;
}

function download(name: string, content: string) {
  const blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);

  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();

  URL.revokeObjectURL(url);
}

export function DataTable<T>({
  rows,
  columns,
  rowKey,
  server,
  toolbar,
  resetKey,
  loading = false,
  emptyText = "Kayıt bulunamadı.",
  title,
  fetchAll,
  defaultPageSize = 25,
  printMeta,
  footerLabel = "Toplam",
  rowProps,
  groupBy,
}: Props<T>) {
  const [clientPage, setClientPage] = useState(1);
  const [clientPageSize, setClientPageSize] = useState(defaultPageSize);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState("");

  /*
   * TÜMÜNÜ YAZDIRMA.
   *
   * Sayfalama gelince yazdırma sessizce "yalnız bu sayfa"ya döndü —
   * kullanıcı 12 sayfalık listeyi yazdırdığını sanıp 1 sayfa alırdı.
   * Bu yüzden kapsam AÇIKÇA seçiliyor. Tümü seçilince satırlar
   * geçici olarak tam listeye çevriliyor, tarayıcı yazdırma penceresi
   * ondan sonra açılıyor.
   */
  const [printAllRows, setPrintAllRows] = useState<T[] | null>(null);
  const [preparingPrint, setPreparingPrint] = useState(false);

  const page = server ? server.page : clientPage;
  const pageSize = server ? server.pageSize : clientPageSize;
  /*
   * GRUPLAR BİTİŞİK OLMAK ZORUNDA, yoksa sayfalama aynı grubu ikiye
   * bölüp arada başka grup gösterirdi. Sıra, grubun İLK GÖRÜNDÜĞÜ yere
   * göre kuruluyor; grup içinde satırların kendi sırası korunuyor —
   * ekranın vade/tarih sıralamasını sessizce bozmamak için.
   */
  const groupedRows = useMemo(() => {
    if (!groupBy) return rows;

    const buckets = new Map<string, T[]>();

    for (const row of rows) {
      const key = groupBy.key(row);
      const bucket = buckets.get(key);

      if (bucket) bucket.push(row);
      else buckets.set(key, [row]);
    }

    return [...buckets.values()].flat();
  }, [rows, groupBy]);

  /** Grup başlığındaki alt toplam TÜM grubu görür, görünen sayfayı değil. */
  const groupMembers = useMemo(() => {
    const map = new Map<string, T[]>();
    if (!groupBy) return map;

    for (const row of groupedRows) {
      const key = groupBy.key(row);
      const bucket = map.get(key);

      if (bucket) bucket.push(row);
      else map.set(key, [row]);
    }

    return map;
  }, [groupedRows, groupBy]);

  /** CSV'ye grup satırı basmak için düz metin köprüsü. */
  const csvGroups = groupBy
    ? {
        keyOf: groupBy.key,
        members: groupMembers,
        line: (members: T[], key: string) => ({
          label: groupBy.label(members, key),
          summary: groupBy.summary ? groupBy.summary(members, key) : "",
        }),
      }
    : undefined;

  const total = server ? server.total : rows.length;

  const pageCount = Math.max(1, Math.ceil(total / pageSize));

  // Filtre daralınca 7. sayfada kalmak boş ekran demek.
  const firstReset = useRef(true);
  useEffect(() => {
    if (firstReset.current) {
      firstReset.current = false;
      return;
    }

    if (server) server.onChange(1, server.pageSize);
    else setClientPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetKey]);

  /*
   * SIKIŞTIRMA RENDER'DA YAPILIR, EFFECT'TE DEĞİL.
   *
   * Önce iki effect vardı — biri filtre değişince sayfayı 1'e alıyor,
   * diğeri sayfa sayısı küçülünce son sayfaya sıkıştırıyordu. İkisi
   * YARIŞIYORDU: sıfırlama 1 yazdıktan sonra sıkıştırma bayat `page`
   * değerini görüp 2'ye çekiyordu ve filtre sonrası sayfa 1'e dönmüyordu.
   * Test bunu yakaladı.
   *
   * Türetilmiş değer olarak hesaplamak yarışı tamamen ortadan kaldırır.
   */
  const safePage = Math.min(Math.max(1, page), pageCount);

  const visible = useMemo(() => {
    // Tümünü yazdırma sırasında sayfa değil TAM liste basılır.
    if (printAllRows) return printAllRows;
    if (server) return rows;

    const start = (safePage - 1) * pageSize;
    return groupedRows.slice(start, start + pageSize);
  }, [groupedRows, server, safePage, pageSize, printAllRows]);

  async function printScope(scope: "page" | "all") {
    if (scope === "page") {
      window.print();
      return;
    }

    setExportError("");
    setPreparingPrint(true);

    try {
      const all = fetchAll ? await fetchAll() : rows;

      /*
       * `flushSync` ŞART: `window.print()` tarayıcıyı bloklar ve o an
       * EKRANDA NE VARSA onu basar. Normal setState toplu güncelleme
       * yapıyor, yani print hâlâ tek sayfayı görürdü — kullanıcı
       * "tümünü yazdır" deyip 1 sayfa alırdı.
       *
       * Bunu bir effect'e taşımak da işe yarardı ama o zaman
       * "effect içinde setState" oluyordu; burada akış düz ve
       * okunur: tam listeyi bas, yazdır, geri al.
       */
      flushSync(() => setPrintAllRows(all));
      window.print();
    } catch (error) {
      setExportError(
        error instanceof Error ? error.message : "Kayıtlar alınamadı."
      );
    } finally {
      setPrintAllRows(null);
      setPreparingPrint(false);
    }
  }

  function goto(next: number) {
    const clamped = Math.min(Math.max(1, next), pageCount);

    if (server) server.onChange(clamped, server.pageSize);
    else setClientPage(clamped);
  }

  function changeSize(size: number) {
    if (server) server.onChange(1, size);
    else {
      setClientPageSize(size);
      setClientPage(1);
    }
  }

  const fileBase = (title ?? "liste")
    .toLocaleLowerCase("tr-TR")
    .replace(/[^a-z0-9ğüşıöç]+/gi, "-")
    .replace(/^-|-$/g, "");

  async function exportCsv(scope: "page" | "all") {
    setExportError("");

    if (scope === "page") {
      download(
        `${fileBase}-sayfa-${safePage}.csv`,
        toCsv(columns, visible, csvGroups)
      );
      return;
    }

    setExporting(true);

    try {
      const all = fetchAll ? await fetchAll() : rows;
      download(`${fileBase}-tum-kayitlar.csv`, toCsv(columns, all, csvGroups));
    } catch (error) {
      setExportError(
        error instanceof Error ? error.message : "Kayıtlar indirilemedi."
      );
    } finally {
      setExporting(false);
    }
  }

  // "Tüm kayıtlar" ancak GERÇEKTEN verilebiliyorsa sunulur.
  const canExportAll = !server || Boolean(fetchAll);

  /*
   * Alt toplam hücresi. Sunucu kipinde ebeveynin verdiği değer,
   * istemci kipinde sütunun kendi hesabı (TÜM satırlar üzerinden).
   */
  function footerCell(column: DataTableColumn<T>) {
    if (server) return server.totals?.[column.key] ?? null;
    return column.footer ? column.footer(rows) : null;
  }

  const showFooter =
    total > 0 &&
    (server
      ? Boolean(server.totals)
      : columns.some((column) => Boolean(column.footer)));

  const from = total === 0 ? 0 : (safePage - 1) * pageSize + 1;
  const to = server
    ? from + visible.length - 1
    : Math.min(safePage * pageSize, total);

  return (
    <div className="erp-data-table">
      {(toolbar || columns.length > 0) && (
        <div className="erp-data-table-toolbar no-print">
          <div className="erp-data-table-filters">{toolbar}</div>

          <div className="erp-data-table-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => exportCsv("page")}
              disabled={loading || total === 0}
            >
              Bu Sayfayı İndir
            </button>

            {canExportAll && (
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => void exportCsv("all")}
                disabled={loading || exporting || total === 0}
              >
                {exporting ? "Hazırlanıyor…" : "Tümünü İndir"}
              </button>
            )}

            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => printScope("page")}
              disabled={loading || total === 0}
            >
              Bu Sayfayı Yazdır
            </button>

            {/*
              TÜMÜNÜ YAZDIRMA da ancak GERÇEKTEN verilebiliyorsa
              sunulur — indirmedeki kuralın aynısı.
            */}
            {canExportAll && (
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => void printScope("all")}
                disabled={loading || preparingPrint || total === 0}
              >
                {preparingPrint ? "Hazırlanıyor…" : "Tümünü Yazdır"}
              </button>
            )}
          </div>
        </div>
      )}

      {exportError && (
        <div className="erp-alert error no-print">{exportError}</div>
      )}

      {/*
        ÇIKTI ÜST BİLGİSİ — yalnız kâğıtta görünür.
        Kâğıda çıkmış bir liste hangi süzgeçlerle, ne zaman alındığını
        söylemezse bir hafta sonra kimse hatırlamıyor.

        ŞİRKET ADI BURAYA YAZILMIYOR: bileşen şirket bağlamını
        bilmiyor ve uydurmak, bu programın kaldırdığı hataların
        aynısı olurdu. Bağlamı olan ekran `printMeta` ile geçirir.
      */}
      {(title || printMeta) && (
        <div className="print-only erp-print-header">
          {title && <h2 className="erp-print-title">{title}</h2>}
          {printMeta && <div className="erp-print-meta">{printMeta}</div>}
          <div className="erp-print-meta">
            {new Date().toLocaleString("tr-TR")} · {whole(total)} kayıt
            {printAllRows ? " (tamamı)" : ` · sayfa ${whole(safePage)}/${whole(pageCount)}`}
          </div>
        </div>
      )}

      <div className="erp-table-scroll">
        <table className="erp-data-table-grid">
          <thead>
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  style={{ textAlign: column.align ?? "left" }}
                >
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>

          <tbody>
            {loading ? (
              <tr>
                <td colSpan={columns.length} className="erp-data-table-empty">
                  Yükleniyor…
                </td>
              </tr>
            ) : visible.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="erp-data-table-empty">
                  {emptyText}
                </td>
              </tr>
            ) : (
              visible.map((row, index) => {
                /*
                 * GRUP BAŞLIĞI: grubun İLK satırından önce basılır.
                 * Bir grup sayfa sınırını aşarsa sonraki sayfanın ilk
                 * satırı da "yeni grup" sayılır ve başlık TEKRAR EDER —
                 * aksi halde ikinci sayfa hangi aya ait olduğunu
                 * söylemeyen bir satır yığını olurdu.
                 */
                const groupKey = groupBy?.key(row);

                const startsGroup =
                  groupBy !== undefined &&
                  (index === 0 || groupBy.key(visible[index - 1]) !== groupKey);

                const members = groupKey ? groupMembers.get(groupKey) ?? [] : [];

                return (
                  <Fragment key={rowKey(row)}>
                    {startsGroup && groupBy && groupKey !== undefined && (
                      <tr className="erp-data-table-group">
                        <td
                          colSpan={
                            groupBy.summary || groupBy.renderSummary
                              ? columns.length - 1
                              : columns.length
                          }
                        >
                          {groupBy.render
                            ? groupBy.render(members, groupKey)
                            : groupBy.label(members, groupKey)}
                        </td>

                        {(groupBy.summary || groupBy.renderSummary) && (
                          <td
                            style={{
                              textAlign: "right",
                              fontVariantNumeric: "tabular-nums",
                            }}
                          >
                            {groupBy.renderSummary
                              ? groupBy.renderSummary(members, groupKey)
                              : groupBy.summary?.(members, groupKey)}
                          </td>
                        )}
                      </tr>
                    )}

                    <tr {...rowProps?.(row)}>
                      {columns.map((column) => (
                        <td
                          key={column.key}
                          style={{
                            textAlign: column.align ?? (column.numeric ? "right" : "left"),
                            fontVariantNumeric: column.numeric ? "tabular-nums" : undefined,
                          }}
                        >
                          {column.render ? column.render(row) : cellText(column, row)}
                        </td>
                      ))}
                    </tr>
                  </Fragment>
                );
              })
            )}
          </tbody>

          {/*
            ALT TOPLAM SATIRI.

            İSTEMCİ kipinde TÜM satırlar üzerinden hesaplanır — görünen
            sayfa değil. "Toplam" etiketli bir satırın yalnız o sayfayı
            toplaması, bu programın baştan beri kovaladığı hatanın ta
            kendisi olurdu.

            SUNUCU kipinde elde yalnız bir sayfa var; toplam
            hesaplanamaz. `server.totals` verilmediyse satır HİÇ
            GÖSTERİLMEZ — yanlış toplam göstermektense hiç
            göstermemek.
          */}
          {showFooter && (
            <tfoot>
              <tr>
                {columns.map((column, index) => (
                  <td
                    key={column.key}
                    style={{
                      textAlign:
                        column.align ?? (column.numeric ? "right" : "left"),
                      fontVariantNumeric: column.numeric
                        ? "tabular-nums"
                        : undefined,
                    }}
                  >
                    {index === 0 && !footerCell(column)
                      ? footerLabel
                      : footerCell(column)}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
      </div>

      <div className="erp-data-table-footer">
        <span className="erp-data-table-count">
          {total === 0
            ? "Kayıt yok"
            : `Toplam ${whole(total)} kayıt · ${whole(from)}–${whole(to)} arası gösteriliyor`}
        </span>

        <div className="erp-data-table-pager no-print">
          <label className="erp-data-table-size">
            Sayfa başına
            <select
              className="erp-input"
              value={pageSize}
              onChange={(event) => changeSize(Number(event.target.value))}
              aria-label="Sayfa başına kayıt"
            >
              {PAGE_SIZES.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </label>

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => goto(1)}
            disabled={safePage <= 1}
            aria-label="İlk sayfa"
          >
            «
          </button>

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => goto(safePage - 1)}
            disabled={safePage <= 1}
          >
            Önceki
          </button>

          <span className="erp-data-table-page">
            Sayfa {whole(safePage)} / {whole(pageCount)}
          </span>

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => goto(safePage + 1)}
            disabled={safePage >= pageCount}
          >
            Sonraki
          </button>

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => goto(pageCount)}
            disabled={safePage >= pageCount}
            aria-label="Son sayfa"
          >
            »
          </button>
        </div>
      </div>
    </div>
  );
}

export default DataTable;
