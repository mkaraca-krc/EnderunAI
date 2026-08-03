"use client";

import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  purchaseOrderService,
  type PurchaseOrderListItem,
} from "@/services/purchase-order.service";
import {
  financeSettingsService,
  supplierInvoiceService,
  type SupplierInvoiceItemPayload,
} from "@/services/supplier-invoice.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

type DraftItem = {
  description: string;
  quantity: string;
  unit: string;
  unitPrice: string;
  vatRate: string;
};

function emptyItem(vatRate: string): DraftItem {
  return { description: "", quantity: "1", unit: "adet", unitPrice: "", vatRate };
}

export default function NewSupplierInvoicePage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [suppliers, setSuppliers] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [orders, setOrders] = useState<PurchaseOrderListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [purchaseOrderId, setPurchaseOrderId] = useState("");
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [dueDate, setDueDate] = useState("");
  const [description, setDescription] = useState("");
  const [defaultVatRate, setDefaultVatRate] = useState("20");
  const [items, setItems] = useState<DraftItem[]>([emptyItem("20")]);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const loadReferenceData = useCallback(async () => {
    try {
      const [companyList, settings] = await Promise.all([
        companyService.getAll(),
        financeSettingsService.get().catch(() => null),
      ]);

      setCompanies(companyList);
      setCompanyId(companyList[0]?.id ?? "");

      if (settings) {
        const rate = String(settings.defaultVatRate);
        setDefaultVatRate(rate);
        setItems([emptyItem(rate)]);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Referans veriler alınamadı.");
    }
  }, []);

  useEffect(() => {
    void loadReferenceData();
  }, [loadReferenceData]);

  useEffect(() => {
    if (!companyId) return;

    let active = true;

    void Promise.all([
      currentAccountService.getAll(companyId),
      projectService.getAll(companyId),
      purchaseOrderService.getAll({ companyId }),
    ])
      .then(([accountList, projectList, orderList]) => {
        if (!active) return;
        // Yalnızca onaylı (status=2) tedarikçi rolündeki cariler.
        setSuppliers(
          accountList.filter(
            (account) => account.status === 2 && (account.roles & 2) === 2
          )
        );
        setProjects(projectList);
        setOrders(orderList);
      })
      .catch((err) => {
        if (active) {
          setError(err instanceof Error ? err.message : "Liste verileri alınamadı.");
        }
      });

    return () => {
      active = false;
    };
  }, [companyId]);

  const filteredOrders = useMemo(
    () =>
      orders.filter(
        (order) =>
          (!supplierId || order.supplierCurrentAccountId === supplierId) &&
          (!projectId || order.projectId === projectId)
      ),
    [orders, supplierId, projectId]
  );

  const totals = useMemo(() => {
    let subtotal = 0;
    let vat = 0;

    for (const item of items) {
      const quantity = Number(item.quantity) || 0;
      const unitPrice = Number(item.unitPrice) || 0;
      const vatRate = Number(item.vatRate) || 0;
      const lineSubtotal = Math.round(quantity * unitPrice * 100) / 100;
      subtotal += lineSubtotal;
      vat += Math.round((lineSubtotal * vatRate) / 100 * 100) / 100;
    }

    return { subtotal, vat, grandTotal: subtotal + vat };
  }, [items]);

  function updateItem(index: number, patch: Partial<DraftItem>) {
    setItems((current) =>
      current.map((item, i) => (i === index ? { ...item, ...patch } : item))
    );
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");

    try {
      const payloadItems: SupplierInvoiceItemPayload[] = items.map((item) => ({
        description: item.description.trim(),
        quantity: Number(item.quantity),
        unit: item.unit.trim() || "adet",
        unitPrice: Number(item.unitPrice),
        vatRate: Number(item.vatRate),
        purchaseOrderItemId: null,
      }));

      const created = await supplierInvoiceService.create({
        companyId,
        supplierCurrentAccountId: supplierId,
        projectId,
        purchaseOrderId: purchaseOrderId || null,
        goodsReceiptId: null,
        invoiceNumber: invoiceNumber.trim(),
        invoiceDate,
        dueDate: dueDate || null,
        currencyCode: "TRY",
        exchangeRate: 1,
        description: description.trim() || null,
        items: payloadItems,
      });

      router.push(`/muhasebe/faturalar/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fatura kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  const canSubmit =
    Boolean(companyId && supplierId && projectId && invoiceNumber.trim()) &&
    items.every((item) => item.description.trim() && Number(item.quantity) > 0);

  return (
    <ErpShell
      title="Yeni Tedarikçi Faturası"
      description="Kalem bazlı KDV'li alış faturası; onaylandığında muhasebe fişi otomatik oluşur"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-header">
          <h2>Fatura Bilgileri</h2>
          <p>
            Sipariş seçilirse onaya gönderirken 3 yönlü kontrol (sipariş = mal
            kabul = fatura) otomatik çalışır.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>
            <select
              required
              value={companyId}
              onChange={(e) => setCompanyId(e.target.value)}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Tedarikçi *</span>
            <select
              required
              value={supplierId}
              onChange={(e) => setSupplierId(e.target.value)}
            >
              <option value="">Onaylı tedarikçi seçin</option>
              {suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>
                  {supplier.code} — {supplier.title}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Proje *</span>
            <select
              required
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
            >
              <option value="">Proje seçin</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Sipariş (ops.)</span>
            <select
              value={purchaseOrderId}
              onChange={(e) => setPurchaseOrderId(e.target.value)}
            >
              <option value="">Siparişsiz (doğrudan fatura)</option>
              {filteredOrders.map((order) => (
                <option key={order.id} value={order.id}>
                  {order.orderNumber} — {money.format(order.grandTotal)}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Tedarikçi Fatura No *</span>
            <input
              required
              type="text"
              value={invoiceNumber}
              onChange={(e) => setInvoiceNumber(e.target.value)}
              placeholder="Örn. ABC2026000123"
            />
          </label>

          <label>
            <span>Fatura Tarihi *</span>
            <input
              required
              type="date"
              value={invoiceDate}
              onChange={(e) => setInvoiceDate(e.target.value)}
            />
          </label>

          <label>
            <span>Vade Tarihi (ops.)</span>
            <input
              type="date"
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
            />
          </label>

          <label className="span-2">
            <span>Açıklama</span>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </label>
        </div>

        <div className="erp-form-header" style={{ marginTop: "20px" }}>
          <h2>Kalemler</h2>
          <p>KDV oranı kalem bazında değiştirilebilir.</p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Açıklama *</th>
                <th>Miktar *</th>
                <th>Birim</th>
                <th>Birim Fiyat *</th>
                <th>KDV %</th>
                <th>Tutar</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((item, index) => {
                const lineSubtotal =
                  (Number(item.quantity) || 0) * (Number(item.unitPrice) || 0);
                return (
                  <tr key={index}>
                    <td>
                      <input
                        type="text"
                        value={item.description}
                        onChange={(e) =>
                          updateItem(index, { description: e.target.value })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        step="0.0001"
                        min="0"
                        value={item.quantity}
                        onChange={(e) => updateItem(index, { quantity: e.target.value })}
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={item.unit}
                        onChange={(e) => updateItem(index, { unit: e.target.value })}
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        step="0.0001"
                        min="0"
                        value={item.unitPrice}
                        onChange={(e) => updateItem(index, { unitPrice: e.target.value })}
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        max="100"
                        value={item.vatRate}
                        onChange={(e) => updateItem(index, { vatRate: e.target.value })}
                      />
                    </td>
                    <td>{money.format(lineSubtotal)}</td>
                    <td>
                      {items.length > 1 && (
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() =>
                            setItems((current) => current.filter((_, i) => i !== index))
                          }
                        >
                          Sil
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <div className="erp-form-actions" style={{ justifyContent: "space-between" }}>
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() =>
              setItems((current) => [...current, emptyItem(defaultVatRate)])
            }
          >
            + Kalem Ekle
          </button>

          <div style={{ textAlign: "right" }}>
            <div>Ara toplam: {money.format(totals.subtotal)}</div>
            <div>KDV: {money.format(totals.vat)}</div>
            <strong>Genel toplam: {money.format(totals.grandTotal)}</strong>
          </div>
        </div>

        <div className="erp-form-actions">
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/muhasebe/faturalar")}
          >
            Vazgeç
          </button>

          <button type="submit" className="erp-primary-button" disabled={saving || !canSubmit}>
            {saving ? "Kaydediliyor..." : "Taslak Olarak Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
