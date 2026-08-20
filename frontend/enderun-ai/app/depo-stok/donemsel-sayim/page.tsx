"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { Button, EmptyState, Input, Select } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money } from "@/lib/format/turkish";
import {
  STOCK_COUNT_STATUS,
  stockCountService,
  type StockCountRow,
} from "@/services/stock-count.service";
import {
  warehouseService,
  type WarehouseListItem,
  type WarehouseZoneListItem,
} from "@/services/warehouse.service";

/**
 * DÖNEMSEL SAYIM — oturum listesi ve yeni oturum.
 *
 * Tek kalemlik anlık düzeltme AYRI ekranda (`/depo-stok/sayim`) ve
 * duruyor. Bu ekran bir DÖNEMİN sayımı: sistem miktarları dondurulur,
 * fiziki miktar girilir, fark gerekçelenir ve yetkili onayından geçer.
 */
export default function PeriodicCountPage() {
  const router = useRouter();

  const [warehouses, setWarehouses] = useState<WarehouseListItem[]>([]);
  const [zones, setZones] = useState<WarehouseZoneListItem[]>([]);
  const [sessions, setSessions] = useState<StockCountRow[]>([]);

  const [warehouseId, setWarehouseId] = useState("");
  const [zoneId, setZoneId] = useState("");
  const [name, setName] = useState("");
  const [countDate, setCountDate] = useState(new Date().toISOString().slice(0, 10));

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [warehouseList, sessionList] = await Promise.all([
        warehouseService.getAll(),
        stockCountService.getAll(),
      ]);

      setWarehouses(warehouseList);
      setSessions(sessionList);
      setWarehouseId((current) => current || warehouseList[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sayımlar alınamadı.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!warehouseId) {
      setZones([]);
      return;
    }

    let active = true;

    void warehouseService
      .getZones(warehouseId)
      .then((list) => {
        if (active) setZones(list);
      })
      .catch(() => {
        if (active) setZones([]);
      });

    return () => {
      active = false;
    };
  }, [warehouseId]);

  /**
   * Sütunlar veri olarak duruyor: `render` ekrana, `value` dosyaya ve
   * kâğıda gidiyor. Rozet basan sütunda ikisi ayrılmak zorunda —
   * dışa aktarmada "Aç" düğmesi değil durumun adı yazmalı.
   */
  const columns: DataTableColumn<StockCountRow>[] = [
    { key: "belge", header: "Belge No", value: (row) => row.documentNumber },
    { key: "donem", header: "Dönem", value: (row) => row.name },
    { key: "depo", header: "Depo", value: (row) => row.warehouseName },
    { key: "bolge", header: "Bölge", value: (row) => row.zoneName ?? "Tüm depo" },
    {
      key: "tarih",
      header: "Tarih",
      value: (row) => new Date(row.countDate).toLocaleDateString("tr-TR"),
    },
    {
      key: "sayilan",
      header: "Sayılan",
      numeric: true,
      value: (row) => `${row.countedCount} / ${row.lineCount}`,
    },
    {
      key: "farkli",
      header: "Farklı",
      numeric: true,
      value: (row) => row.varianceCount,
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => STOCK_COUNT_STATUS[row.status],
      render: (row) => (
        <>
          {STOCK_COUNT_STATUS[row.status]}
          {row.decisionReason && (
            <small style={{ display: "block" }}>{row.decisionReason}</small>
          )}
        </>
      ),
    },
    {
      key: "ac",
      header: "",
      value: () => "",
      render: (row) => (
        <Button
          variant="secondary"
          onClick={() => router.push(`/depo-stok/donemsel-sayim/${row.id}`)}
        >
          Aç
        </Button>
      ),
    },
  ];

  async function start() {
    const warehouse = warehouses.find((x) => x.id === warehouseId);
    if (!warehouse || !name.trim()) return;

    setSaving(true);
    setError("");

    try {
      const created = await stockCountService.start({
        companyId: warehouse.companyId,
        warehouseId,
        warehouseZoneId: zoneId || null,
        name: name.trim(),
        countDate,
      });

      router.push(`/depo-stok/donemsel-sayim/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sayım başlatılamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Dönemsel Sayım"
      description="Oturum aç, fiziki miktarları gir, farkı gerekçelendir ve onaya gönder"
    >
      {error && <p className="erp-form-error">{error}</p>}

      <section className="erp-card">
        <div className="erp-form-header">
          <h2>Yeni sayım oturumu</h2>
          <p>
            Oturum açıldığı anda sistem miktarları dondurulur ve sayılan
            bölge stok hareketine <strong>kapanır</strong> — sayım
            sırasında mal girip çıkarsa fark gerçeği yansıtmaz.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Depo *</span>
            <Select
              value={warehouseId}
              onChange={(event) => {
                setWarehouseId(event.target.value);
                setZoneId("");
              }}
              options={warehouses.map((item) => ({
                value: item.id,
                label: item.name,
              }))}
            />
          </label>

          <label>
            <span>Bölge</span>
            <Select
              value={zoneId}
              onChange={(event) => setZoneId(event.target.value)}
              options={[
                { value: "", label: "Tüm depo" },
                ...zones.map((zone) => ({ value: zone.id, label: zone.name })),
              ]}
            />
            <small>
              Bölge seçilirse yalnız o bölge kilitlenir; depodaki diğer
              bölgeler çalışmaya devam eder.
            </small>
          </label>

          <label>
            <span>Dönem adı *</span>
            <Input
              value={name}
              placeholder="2026 1. Yarıyıl"
              onChange={(event) => setName(event.target.value)}
            />
          </label>

          <label>
            <span>Sayım tarihi *</span>
            <Input
              type="date"
              value={countDate}
              onChange={(event) => setCountDate(event.target.value)}
            />
          </label>
        </div>

        <Button onClick={() => void start()} disabled={saving || !name.trim() || !warehouseId}>
          {saving ? "Başlatılıyor…" : "Sayımı Başlat"}
        </Button>
      </section>

      <section className="erp-card">
        <div className="erp-form-header">
          <h2>Sayım geçmişi</h2>
          <p>Onaylanan, reddedilen ve iptal edilen tüm oturumlar arşivde kalır.</p>
        </div>

        <Button variant="secondary" onClick={() => void load()}>Yenile</Button>

        {loading ? (
          <p>Yükleniyor…</p>
        ) : sessions.length === 0 ? (
          <EmptyState title="Henüz sayım yapılmamış" />
        ) : (
          <DataTable
            rows={sessions}
            columns={columns}
            rowKey={(row) => row.id}
            title="Dönemsel Sayım Geçmişi"
          />
        )}
      </section>
    </ErpShell>
  );
}
