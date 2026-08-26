"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog, EmptyState, Input, Modal, Select } from "@/components/ui";
import { money, quantity as formatQuantity, unitPrice } from "@/lib/format/turkish";
import { parseScannedItem } from "@/lib/inventory/qr";
import { usePermissions } from "@/lib/use-permissions";
import { kasaHesapEtiketi } from "@/lib/finans/kasa-hesap-etiketi";
import {
  RETAIL_PAYMENT,
  RETAIL_STATUS,
  retailSaleService,
  type RetailProduct,
  type RetailSaleItemRow,
  type RetailSaleRow,
} from "@/services/retail-sale.service";

type Line = {
  key: string;
  product: RetailProduct;
  quantity: string;
  discountRate: string;
};

type Resources = {
  warehouses: { id: string; code: string; name: string; companyId: string }[];
  cashAccounts: {
    id: string;
    code: string;
    name: string;
    bankName?: string | null;
    type: number;
    companyId: string;
  }[];
  customers: { id: string; code: string; title: string }[];
};

/** Nakit ve kartta tahsilat anında oluşur; çek ve vadede alacak açık kalır. */
const COLLECTS_IMMEDIATELY = new Set([0, 1]);

export default function RetailSalesPage() {
  const { has } = usePermissions();
  const canSell = has("sales.create");
  const canApprove = has("sales.approve");
  const canMarkCash = has("sales.cash");

  const [resources, setResources] = useState<Resources | null>(null);
  const [sales, setSales] = useState<RetailSaleRow[]>([]);
  const [hiddenCount, setHiddenCount] = useState(0);
  const [profitHidden, setProfitHidden] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [warehouseId, setWarehouseId] = useState("");
  const [search, setSearch] = useState("");
  const [scan, setScan] = useState("");
  const [scanError, setScanError] = useState("");
  const [products, setProducts] = useState<RetailProduct[]>([]);
  const [lines, setLines] = useState<Line[]>([]);

  const [paymentMethod, setPaymentMethod] = useState(0);
  const [customerId, setCustomerId] = useState("");
  const [walkInName, setWalkInName] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [cashAccountId, setCashAccountId] = useState("");
  const [overallDiscount, setOverallDiscount] = useState("0");
  const [cashAmount, setCashAmount] = useState("0");

  const [rejecting, setRejecting] = useState<RetailSaleRow | null>(null);
  const [cancelling, setCancelling] = useState<RetailSaleRow | null>(null);
  const [returning, setReturning] = useState<RetailSaleRow | null>(null);
  const [returnItems, setReturnItems] = useState<RetailSaleItemRow[]>([]);
  const [returnQuantities, setReturnQuantities] = useState<Record<string, string>>({});
  const [returnReason, setReturnReason] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [resourceData, saleData] = await Promise.all([
        fetch("/api/backend/perakende/kaynaklar", { credentials: "include" })
          .then((response) => response.json() as Promise<Resources>),
        retailSaleService.list(),
      ]);

      setResources(resourceData);
      setSales(saleData.items);
      setHiddenCount(saleData.hiddenCount);
      setProfitHidden(Boolean(saleData.profitHidden));

      if (!warehouseId && resourceData.warehouses.length > 0) {
        setWarehouseId(resourceData.warehouses[0].id);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Perakende satışlar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [warehouseId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!warehouseId) return;

    let cancelled = false;

    void (async () => {
      try {
        const found = await retailSaleService.products(warehouseId, search);
        if (!cancelled) setProducts(found);
      } catch {
        if (!cancelled) setProducts([]);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [warehouseId, search]);

  const warehouse = resources?.warehouses.find((x) => x.id === warehouseId);

  /**
   * Toplam SUNUCUDAKİ İLE AYNI FORMÜLLE hesaplanıyor: satır iskontosu →
   * ara toplam → fiş iskontosu → KDV. Ekranda başka bir toplam
   * görünseydi kullanıcı hangisinin doğru olduğunu bilemezdi. Yine de
   * bağlayıcı olan sunucunun hesabıdır; bu yalnız önizleme.
   */
  const totals = useMemo(() => {
    let subtotal = 0;
    let vat = 0;

    for (const line of lines) {
      const count = Number(line.quantity) || 0;
      const discount = Number(line.discountRate) || 0;
      const net = count * line.product.salesPrice * (1 - discount / 100);

      subtotal += net;
      vat += (net * line.product.vatRate) / 100;
    }

    const overall = Number(overallDiscount) || 0;
    const discountAmount = (subtotal * overall) / 100;
    const ratio = subtotal === 0 ? 0 : (subtotal - discountAmount) / subtotal;

    return {
      subtotal,
      discountAmount,
      vat: vat * ratio,
      grandTotal: subtotal - discountAmount + vat * ratio,
    };
  }, [lines, overallDiscount]);

  const exceedsCap = lines.some(
    (line) => (Number(line.discountRate) || 0) > line.product.maxDiscountRate,
  );
  const needsApproval = exceedsCap || paymentMethod === 3;

  function addLine(product: RetailProduct) {
    setLines((current) => {
      if (current.some((line) => line.product.id === product.id)) return current;

      return [
        ...current,
        { key: product.id, product, quantity: "1", discountRate: "0" },
      ];
    });
  }

  /**
   * QR / BARKOD OKUTULDU.
   *
   * Okuyucu bir klavyedir: metni yazar ve Enter'a basar. Üç şey
   * okutulabiliyor — bizim stok etiketimiz (içinde kart URL'i), üretici
   * barkodu ve elle yazılan kod — üçü de bu kutuya düşüyor.
   *
   * AYNI KART İKİNCİ KEZ OKUTULURSA MİKTAR ARTAR, ikinci satır
   * açılmaz: kasada aynı üründen üç tane okutmak olağan ve her
   * seferinde satır eklemek fişi okunmaz hale getirirdi.
   */
  async function handleScan() {
    const parsed = parseScannedItem(scan);
    if (!parsed || !warehouseId) return;

    setScanError("");

    try {
      const found =
        parsed.kind === "id"
          ? await retailSaleService.productById(warehouseId, parsed.id)
          : await retailSaleService.products(warehouseId, parsed.term);

      // Terim birden çok karta uyabilir; TEK eşleşme yoksa otomatik
      // eklemiyoruz. Yanlış ürünü sessizce fişe koymaktansa kullanıcıya
      // listeyi gösterip seçtirmek doğru.
      if (found.length !== 1) {
        setSearch(parsed.kind === "term" ? parsed.term : "");
        setScanError(
          found.length === 0
            ? "Okutulan kod bir ürüne uymadı."
            : `${found.length} ürün eşleşti; listeden seçin.`,
        );
        setScan("");
        return;
      }

      const product = found[0];

      if (product.available <= 0) {
        setScanError(`${product.name}: satılabilir stok yok.`);
        setScan("");
        return;
      }

      setLines((current) => {
        const existing = current.find((line) => line.product.id === product.id);

        if (!existing) {
          return [
            ...current,
            { key: product.id, product, quantity: "1", discountRate: "0" },
          ];
        }

        const next = (Number(existing.quantity) || 0) + 1;

        // Stoktan fazlasını okutmak sessizce geçmemeli.
        if (next > product.available) {
          setScanError(
            `${product.name}: satılabilir ${formatQuantity(product.available)} ${product.unit}.`,
          );
          return current;
        }

        return current.map((line) =>
          line.product.id === product.id
            ? { ...line, quantity: String(next) }
            : line,
        );
      });

      setScan("");
    } catch {
      setScanError("Ürün okunamadı.");
      setScan("");
    }
  }

  function updateLine(key: string, patch: Partial<Line>) {
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );
  }

  async function submitSale() {
    if (!warehouse || lines.length === 0) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const created = await retailSaleService.create({
        companyId: warehouse.companyId,
        warehouseId: warehouse.id,
        saleDate: new Date().toISOString(),
        customerCurrentAccountId: customerId || null,
        walkInCustomerName: customerId ? null : walkInName || null,
        paymentMethod,
        dueDate: paymentMethod === 3 ? dueDate || null : null,
        overallDiscountRate: Number(overallDiscount) || 0,
        cashAmount: canMarkCash ? Number(cashAmount) || 0 : 0,
        cashAccountId: COLLECTS_IMMEDIATELY.has(paymentMethod) ? cashAccountId || null : null,
        items: lines.map((line) => ({
          inventoryItemId: line.product.id,
          quantity: Number(line.quantity) || 0,
          discountRate: Number(line.discountRate) || 0,
        })),
      });

      const result = await retailSaleService.submit(created.id);

      setNotice(
        result.status === 1
          ? `${created.documentNumber} finans onayına gönderildi — ${result.approvalReason}`
          : `${created.documentNumber} tamamlandı.`,
      );

      setLines([]);
      setCashAmount("0");
      setOverallDiscount("0");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Satış kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function openReturn(sale: RetailSaleRow) {
    setReturning(sale);
    setReturnReason("");

    try {
      const items = await retailSaleService.items(sale.id);
      setReturnItems(items);
      setReturnQuantities(Object.fromEntries(items.map((item) => [item.id, "0"])));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiş kalemleri okunamadı.");
      setReturning(null);
    }
  }

  async function submitReturn() {
    if (!returning) return;

    const chosen = returnItems
      .map((item) => ({
        retailSaleItemId: item.id,
        quantity: Number(returnQuantities[item.id]) || 0,
      }))
      .filter((item) => item.quantity > 0);

    if (chosen.length === 0) {
      setError("İade edilecek en az bir kalem ve miktar girin.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      const created = await retailSaleService.createReturn(
        returning.id, returnReason, chosen,
      );

      setNotice(`${created.documentNumber} iade fişi açıldı — finans onayı bekliyor.`);
      setReturning(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İade açılamadı.");
    } finally {
      setSaving(false);
    }
  }

  async function approve(sale: RetailSaleRow) {
    setSaving(true);
    setError("");

    try {
      await retailSaleService.approve(sale.id);
      setNotice(`${sale.documentNumber} onaylandı; fatura ve stok işlendi.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Onay tamamlanamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Perakende Satış"
      description="Merkez depodan hızlı satış — fiyat ve iskonto tavanı stok kartından gelir"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {canSell && (
        <section className="erp-panel">
          <div className="erp-panel-header">
            <h2>Yeni Satış</h2>
            <p>Ürünü aratıp ekleyin; stok adedi merkez depodan anlık gelir.</p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Depo</span>
              <Select
                value={warehouseId}
                onChange={(event) => setWarehouseId(event.target.value)}
                options={(resources?.warehouses ?? []).map((item) => ({
                  value: item.id,
                  label: item.name,
                }))}
              />
            </label>

            <label>
              <span>Ürün ara (kod, ad, barkod)</span>
              <Input value={search} onChange={(event) => setSearch(event.target.value)} />
            </label>

            <label>
              <span>QR / barkod okut</span>
              <Input
                value={scan}
                placeholder="Okutun veya kodu yazıp Enter'a basın"
                onChange={(event) => setScan(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key !== "Enter") return;
                  // Okuyucu Enter'ı kendisi gönderiyor; formun
                  // gönderilmesini engellemezsek fiş yarım kaydedilir.
                  event.preventDefault();
                  void handleScan();
                }}
              />
            </label>
          </div>

          {scanError && <p className="erp-form-error">{scanError}</p>}

          {products.length > 0 && (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Kod</th>
                    <th>Ürün</th>
                    <th style={{ textAlign: "right" }}>Satış Fiyatı</th>
                    <th style={{ textAlign: "right" }}>Tavan</th>
                    <th style={{ textAlign: "right" }}>Stok</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {products.map((product) => (
                    <tr key={product.id}>
                      <td>{product.code}</td>
                      <td>{product.name}</td>
                      <td style={{ textAlign: "right" }}>{unitPrice(product.salesPrice)}</td>
                      <td style={{ textAlign: "right" }}>%{product.maxDiscountRate}</td>
                      <td style={{ textAlign: "right" }}>
                        {formatQuantity(product.available)} {product.unit}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <Button
                          variant="secondary"
                          disabled={product.available <= 0}
                          onClick={() => addLine(product)}
                        >
                          Ekle
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {lines.length > 0 && (
            <div className="erp-table-wrap erp-mt">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Ürün</th>
                    <th style={{ width: 120 }}>Miktar</th>
                    <th style={{ width: 140 }}>İskonto %</th>
                    <th style={{ textAlign: "right" }}>Tutar</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line) => {
                    const count = Number(line.quantity) || 0;
                    const discount = Number(line.discountRate) || 0;
                    const net = count * line.product.salesPrice * (1 - discount / 100);
                    const over = discount > line.product.maxDiscountRate;

                    return (
                      <tr key={line.key}>
                        <td>
                          {line.product.name}
                          {over && (
                            <small className="rw-value-warning" style={{ display: "block" }}>
                              Tavan %{line.product.maxDiscountRate} aşıldı — finans onayına düşer
                            </small>
                          )}
                        </td>
                        <td>
                          <Input
                            value={line.quantity}
                            onChange={(event) =>
                              updateLine(line.key, { quantity: event.target.value })
                            }
                          />
                        </td>
                        <td>
                          <Input
                            value={line.discountRate}
                            onChange={(event) =>
                              updateLine(line.key, { discountRate: event.target.value })
                            }
                          />
                        </td>
                        <td style={{ textAlign: "right" }}>{money(net)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          <div className="erp-form-grid erp-mt">
            <label>
              <span>Ödeme yöntemi</span>
              <Select
                value={String(paymentMethod)}
                onChange={(event) => setPaymentMethod(Number(event.target.value))}
                options={Object.entries(RETAIL_PAYMENT).map(([value, label]) => ({
                  value,
                  label,
                }))}
              />
            </label>

            <label>
              <span>Müşteri {paymentMethod >= 2 ? "(zorunlu)" : "(opsiyonel)"}</span>
              <Select
                value={customerId}
                onChange={(event) => setCustomerId(event.target.value)}
                placeholder="İsimsiz satış"
                options={(resources?.customers ?? []).map((item) => ({
                  value: item.id,
                  label: item.title,
                }))}
              />
            </label>

            {!customerId && (
              <label>
                <span>Müşteri adı (isteğe bağlı)</span>
                <Input value={walkInName} onChange={(event) => setWalkInName(event.target.value)} />
              </label>
            )}

            {paymentMethod === 3 && (
              <label>
                <span>Vade tarihi</span>
                <Input
                  type="date"
                  value={dueDate}
                  onChange={(event) => setDueDate(event.target.value)}
                />
              </label>
            )}

            {COLLECTS_IMMEDIATELY.has(paymentMethod) && (
              <label>
                <span>Tahsilat hesabı</span>
                <Select
                  value={cashAccountId}
                  onChange={(event) => setCashAccountId(event.target.value)}
                  placeholder="Seçin"
                  options={(resources?.cashAccounts ?? []).map((item) => ({
                    value: item.id,
                    // ETİKET TEK KAYNAKTAN. Burada yalnız `item.name`
                    // yazıyordu ve altı banka hesabı BİREBİR AYNI
                    // görünüyordu — üç ekran arasında en kötüsü.
                    label: kasaHesapEtiketi(item),
                  }))}
                />
              </label>
            )}

            <label>
              <span>Fiş geneli iskonto %</span>
              <Input
                value={overallDiscount}
                onChange={(event) => setOverallDiscount(event.target.value)}
              />
            </label>

            {/*
              ELDEN ALANI YALNIZ YETKİLİDE RENDER EDİLİYOR. Yetkisiz
              kullanıcıda alan hiç çizilmiyor ve istek gövdesinde sıfır
              gidiyor; sunucu ayrıca reddediyor.
            */}
            {canMarkCash && (
              <label>
                <span>Elden tutar</span>
                <Input value={cashAmount} onChange={(event) => setCashAmount(event.target.value)} />
              </label>
            )}
          </div>

          <div className="erp-toolbar rw-toolbar-end erp-mt">
            <div>
              <strong>{money(totals.grandTotal)}</strong>
              <small style={{ display: "block" }}>
                Ara toplam {money(totals.subtotal)} · İskonto {money(totals.discountAmount)} ·
                KDV {money(totals.vat)}
                {needsApproval && " · finans onayına düşecek"}
              </small>
            </div>

            <Button disabled={saving || lines.length === 0} onClick={() => void submitSale()}>
              {needsApproval ? "Onaya Gönder" : "Satışı Tamamla"}
            </Button>
          </div>
        </section>
      )}

      <section className="erp-panel erp-mt">
        <div className="erp-panel-header">
          <h2>Satışlar</h2>
          <p>
            {sales.length} fiş
            {hiddenCount > 0 && ` · ${hiddenCount} fişte elden tutar gizli`}
          </p>
        </div>

        {loading ? (
          <div className="erp-loading">Satışlar yükleniyor...</div>
        ) : sales.length === 0 ? (
          <EmptyState title="Satış yok" description="Henüz perakende satış fişi oluşturulmadı." />
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Fiş No</th>
                  <th>Müşteri</th>
                  <th>Ödeme</th>
                  <th style={{ textAlign: "right" }}>Toplam</th>
                  <th style={{ textAlign: "right" }}>Kayıtlı</th>
                  <th style={{ textAlign: "right" }}>Elden</th>
                  {!profitHidden && <th style={{ textAlign: "right" }}>Kâr</th>}
                  <th>Durum</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {sales.map((sale) => (
                  <tr key={sale.id}>
                    <td>{sale.documentNumber}</td>
                    <td>{sale.customerTitle ?? "İsimsiz"}</td>
                    <td>{RETAIL_PAYMENT[sale.paymentMethod]}</td>
                    <td style={{ textAlign: "right" }}>{money(sale.grandTotal)}</td>
                    <td style={{ textAlign: "right" }}>{money(sale.recordedAmount)}</td>
                    <td style={{ textAlign: "right" }}>
                      {sale.cashAmount === null || sale.cashAmount === undefined
                        ? "—"
                        : money(sale.cashAmount)}
                    </td>
                    {!profitHidden && (
                      <td style={{ textAlign: "right" }}>
                        {sale.profit === null || sale.profit === undefined
                          ? "—"
                          : money(sale.profit)}
                      </td>
                    )}
                    <td>
                      {RETAIL_STATUS[sale.status]}
                      {sale.approvalReason && sale.status === 1 && (
                        <small style={{ display: "block" }}>{sale.approvalReason}</small>
                      )}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {canSell && sale.status === 2 && !sale.documentNumber.startsWith("PIF") && (
                        <Button
                          variant="secondary"
                          disabled={saving}
                          onClick={() => void openReturn(sale)}
                        >
                          İade
                        </Button>
                      )}{" "}
                      {canApprove && sale.status === 2 && (
                        <Button
                          variant="danger"
                          disabled={saving}
                          onClick={() => setCancelling(sale)}
                        >
                          İptal
                        </Button>
                      )}{" "}
                      {canApprove && sale.status === 1 && (
                        <>
                          <Button
                            variant="secondary"
                            disabled={saving}
                            onClick={() => void approve(sale)}
                          >
                            Onayla
                          </Button>{" "}
                          <Button
                            variant="danger"
                            disabled={saving}
                            onClick={() => setRejecting(sale)}
                          >
                            Reddet
                          </Button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <ConfirmDialog
        open={cancelling !== null}
        title="Satışı iptal et"
        description={
          `${cancelling?.documentNumber ?? ""} iptal edilecek: stok geri dönecek, ` +
          "fatura ters kayıt alacak ve tahsilat kapanacak. Bu işlem geri alınamaz."
        }
        confirmLabel="İptal Et"
        requireReason
        reasonLabel="İptal gerekçesi"
        onCancel={() => setCancelling(null)}
        onConfirm={(reason) => {
          if (!cancelling) return;

          void (async () => {
            try {
              await retailSaleService.cancel(cancelling.id, reason);
              setNotice(`${cancelling.documentNumber} iptal edildi.`);
              setCancelling(null);
              await load();
            } catch (err) {
              setError(err instanceof Error ? err.message : "İptal tamamlanamadı.");
            }
          })();
        }}
      />

      <Modal
        open={returning !== null}
        title={`İade — ${returning?.documentNumber ?? ""}`}
        onClose={() => setReturning(null)}
      >
        <p>
          İade edilecek miktarları girin. Fiş finans onayına düşer; onaya kadar
          stok değişmez.
        </p>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Kalem</th>
                <th style={{ textAlign: "right" }}>Satılan</th>
                <th style={{ textAlign: "right" }}>İade edilen</th>
                {!profitHidden && <th style={{ textAlign: "right" }}>Satır kârı</th>}
                <th style={{ width: 120 }}>İade</th>
              </tr>
            </thead>
            <tbody>
              {returnItems.map((item) => {
                const remaining = item.quantity - item.alreadyReturned;

                return (
                  <tr key={item.id}>
                    <td>{item.description}</td>
                    <td style={{ textAlign: "right" }}>
                      {formatQuantity(item.quantity)} {item.unit}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {formatQuantity(item.alreadyReturned)}
                    </td>
                    {!profitHidden && (
                      <td style={{ textAlign: "right" }}>
                        {item.lineProfit === null || item.lineProfit === undefined
                          ? "—"
                          : money(item.lineProfit)}
                      </td>
                    )}
                    <td>
                      <Input
                        value={returnQuantities[item.id] ?? "0"}
                        disabled={remaining <= 0}
                        onChange={(event) =>
                          setReturnQuantities((current) => ({
                            ...current,
                            [item.id]: event.target.value,
                          }))
                        }
                      />
                      <small style={{ display: "block" }}>
                        en fazla {formatQuantity(remaining)}
                      </small>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <label>
          <span>İade gerekçesi</span>
          <Input
            value={returnReason}
            onChange={(event) => setReturnReason(event.target.value)}
          />
        </label>

        <div className="erp-toolbar rw-toolbar-end erp-mt">
          <Button variant="secondary" onClick={() => setReturning(null)}>Vazgeç</Button>{" "}
          <Button
            disabled={saving || returnReason.trim().length === 0}
            onClick={() => void submitReturn()}
          >
            İadeyi Onaya Gönder
          </Button>
        </div>
      </Modal>

      <ConfirmDialog
        open={rejecting !== null}
        title="Satışı reddet"
        description={`${rejecting?.documentNumber ?? ""} reddedilecek. Stok rezervi serbest kalır.`}
        confirmLabel="Reddet"
        requireReason
        reasonLabel="Red gerekçesi"
        onCancel={() => setRejecting(null)}
        onConfirm={(reason) => {
          if (!rejecting) return;

          void (async () => {
            try {
              await retailSaleService.reject(rejecting.id, reason);
              setNotice(`${rejecting.documentNumber} reddedildi.`);
              setRejecting(null);
              await load();
            } catch (err) {
              setError(err instanceof Error ? err.message : "Red tamamlanamadı.");
            }
          })();
        }}
      />
    </ErpShell>
  );
}
