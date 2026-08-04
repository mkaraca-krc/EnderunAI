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
import { financeSettingsService } from "@/services/supplier-invoice.service";
import {
  salesInvoiceService,
  type SalesInvoiceItemPayload,
} from "@/services/sales-invoice.service";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });

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

export default function NewSalesInvoicePage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [customers, setCustomers] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [officialInvoiceNumber, setOfficialInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(new Date().toISOString().slice(0, 10));
  const [dueDate, setDueDate] = useState("");
  const [withholdingAmount, setWithholdingAmount] = useState("0");
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

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
    ])
      .then(([accountList, projectList]) => {
        if (!active) return;
        // Yalnızca onaylı (status=2) müşteri rolündeki cariler.
        setCustomers(
          accountList.filter(
            (account) => account.status === 2 && (account.roles & 1) === 1
          )
        );
        setProjects(projectList);
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

  const totals = useMemo(() => {
    let subtotal = 0;
    let vat = 0;

    for (const item of items) {
      const quantity = Number(item.quantity) || 0;
      const unitPrice = Number(item.unitPrice) || 0;
      const vatRate = Number(item.vatRate) || 0;
      const lineSubtotal = Math.round(quantity * unitPrice * 100) / 100;
      subtotal += lineSubtotal;
      vat += Math.round(((lineSubtotal * vatRate) / 100) * 100) / 100;
    }

    const withholding = Number(withholdingAmount) || 0;

    return {
      subtotal,
      vat,
      grandTotal: subtotal + vat,
      withholding,
      netReceivable: subtotal + vat - withholding,
    };
  }, [items, withholdingAmount]);

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
      const payloadItems: SalesInvoiceItemPayload[] = items.map((item) => ({
        description: item.description.trim(),
        quantity: Number(item.quantity),
        unit: item.unit.trim() || "adet",
        unitPrice: Number(item.unitPrice),
        vatRate: Number(item.vatRate),
      }));

      const created = await salesInvoiceService.create({
        companyId,
        customerCurrentAccountId: customerId,
        projectId: projectId || null,
        officialInvoiceNumber: officialInvoiceNumber.trim() || null,
        invoiceDate,
        dueDate: dueDate || null,
        currencyCode: "TRY",
        exchangeRate: 1,
        withholdingAmount: Number(withholdingAmount) || 0,
        description: description.trim() || null,
        notes: notes.trim() || null,
        items: payloadItems,
      });

      router.push(`/muhasebe/satis-faturalari/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fatura kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  const withholdingTooHigh = totals.withholding > totals.vat;

  const canSubmit =
    Boolean(companyId && customerId) &&
    !withholdingTooHigh &&
    items.every(
      (item) => item.description.trim() && Number(item.quantity) > 0
    );

  return (
    <ErpShell
      title="Yeni Satış Faturası"
      description="Hakediş dışı satış; kesinleştirildiğinde gelir fişi otomatik oluşur"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-header">
          <h2>Fatura Bilgileri</h2>
          <p>
            Resmi (GİB) fatura numarasını sonradan da girebilirsiniz; ancak
            numara girilmeden fatura kesinleştirilemez.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>
            <select required value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Müşteri *</span>
            <select required value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
              <option value="">Onaylı müşteri seçin</option>
              {customers.map((customer) => (
                <option key={customer.id} value={customer.id}>
                  {customer.code} — {customer.title}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Proje (ops.)</span>
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
              <option value="">Projesiz satış</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Resmi Fatura No (GİB)</span>
            <input
              type="text"
              value={officialInvoiceNumber}
              onChange={(e) => setOfficialInvoiceNumber(e.target.value)}
              placeholder="Örn. ENE2026000000123"
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
            <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
          </label>

          <label>
            <span>KDV Tevkifatı (TL)</span>
            <input
              type="number"
              step="0.01"
              min="0"
              value={withholdingAmount}
              onChange={(e) => setWithholdingAmount(e.target.value)}
            />
          </label>

          <label>
            <span>Açıklama</span>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </label>

          <label className="span-2">
            <span>Notlar</span>
            <input type="text" value={notes} onChange={(e) => setNotes(e.target.value)} />
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
                        onChange={(e) => updateItem(index, { description: e.target.value })}
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

        {withholdingTooHigh && (
          <div className="erp-alert error">
            Tevkifat ({money.format(totals.withholding)}) hesaplanan KDV&apos;den
            ({money.format(totals.vat)}) büyük olamaz.
          </div>
        )}

        <div className="erp-form-actions" style={{ justifyContent: "space-between" }}>
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => setItems((current) => [...current, emptyItem(defaultVatRate)])}
          >
            + Kalem Ekle
          </button>

          <div style={{ textAlign: "right" }}>
            <div>Ara toplam: {money.format(totals.subtotal)}</div>
            <div>KDV: {money.format(totals.vat)}</div>
            {totals.withholding > 0 && (
              <div>Tevkifat: -{money.format(totals.withholding)}</div>
            )}
            <strong>Tahsil edilecek: {money.format(totals.netReceivable)}</strong>
          </div>
        </div>

        <div className="erp-form-actions">
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/muhasebe/satis-faturalari")}
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
