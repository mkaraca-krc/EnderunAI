"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";

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
};

export type DataTableServerMode = {
  /** Süzgeçlere uyan TOPLAM kayıt — uçtan gelir, listeden sayılmaz. */
  total: number;
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
function toCsv<T>(columns: DataTableColumn<T>[], rows: T[]): string {
  const escape = (value: string) =>
    /[";\n\r]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;

  const lines = [
    columns.map((column) => escape(column.header)).join(";"),
    ...rows.map((row) =>
      columns.map((column) => escape(cellText(column, row))).join(";")
    ),
  ];

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
}: Props<T>) {
  const [clientPage, setClientPage] = useState(1);
  const [clientPageSize, setClientPageSize] = useState(defaultPageSize);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState("");

  const page = server ? server.page : clientPage;
  const pageSize = server ? server.pageSize : clientPageSize;
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
    if (server) return rows;

    const start = (safePage - 1) * pageSize;
    return rows.slice(start, start + pageSize);
  }, [rows, server, safePage, pageSize]);

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
      download(`${fileBase}-sayfa-${safePage}.csv`, toCsv(columns, visible));
      return;
    }

    setExporting(true);

    try {
      const all = fetchAll ? await fetchAll() : rows;
      download(`${fileBase}-tum-kayitlar.csv`, toCsv(columns, all));
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
              onClick={() => window.print()}
              disabled={loading || total === 0}
            >
              Yazdır
            </button>
          </div>
        </div>
      )}

      {exportError && (
        <div className="erp-alert error no-print">{exportError}</div>
      )}

      {title && <h2 className="print-only erp-print-title">{title}</h2>}

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
              visible.map((row) => (
                <tr key={rowKey(row)}>
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
              ))
            )}
          </tbody>
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
