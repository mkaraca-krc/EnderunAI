"use client";

import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import CurrencyRateFields from "@/components/accounting/currency-rate-fields";
import ErpSearchSelect, {
  type SearchSelectOption,
} from "@/components/erp/erp-search-select";
import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";
import { branchService, type BranchListItem } from "@/services/branch.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  purchaseOrderService,
  type PurchaseOrderListItem,
} from "@/services/purchase-order.service";
import {
  financeSettingsService,
  supplierInvoiceService,
  SupplierInvoiceType,
  type SupplierInvoiceItemPayload,
} from "@/services/supplier-invoice.service";
import {
  warehouseService,
  type WarehouseListItem,
} from "@/services/warehouse.service";

/** CurrentAccountStatus.Approved */
const APPROVED_STATUS = 2;

/**
 * Bize fatura kesebilecek cari rolleri: tedarikçi, taşeron, resmi kurum,
 * banka, hizmet firması (OSGB burada), kiralama firması, diğer. Yalnızca
 * "müşteri" rolü dışarıda kalır.
 *
 * Ekran daha önce yalnızca Tedarikçi (2) bitini arıyordu; taşeron ve OSGB
 * faturası hiç girilemiyordu. Backend zaten yalnızca "onaylı cari" şartı
 * koyuyor (SupplierInvoiceService.ValidateHeaderAsync).
 */
const SUPPLIER_SIDE_ROLES = 2 | 4 | 8 | 16 | 32 | 64 | 128;

/** Son kullanılan gider hesapları burada tutulur (şirket bazında). */
const RECENT_ACCOUNTS_KEY = "enderun.gider-hesabi.son-kullanilan";
const RECENT_ACCOUNTS_LIMIT = 5;

type DraftItem = {
  description: string;
  quantity: string;
  unit: string;
  unitPrice: string;
  vatRate: string;
  inventoryItemId: string;
  warehouseId: string;
  expenseAccountId: string;
  costCenterCode: string;
};

function emptyItem(vatRate: string): DraftItem {
  return {
    description: "",
    quantity: "1",
    unit: "adet",
    unitPrice: "",
    vatRate,
    inventoryItemId: "",
    warehouseId: "",
    expenseAccountId: "",
    costCenterCode: "",
  };
}

function readRecentAccounts(companyId: string): string[] {
  if (typeof window === "undefined" || !companyId) return [];

  try {
    const raw = window.localStorage.getItem(
      `${RECENT_ACCOUNTS_KEY}.${companyId}`
    );
    const parsed = raw ? (JSON.parse(raw) as unknown) : null;

    return Array.isArray(parsed)
      ? parsed.filter((x): x is string => typeof x === "string")
      : [];
  } catch {
    // Bozuk kayıt yüzünden ekran açılmamalı; öneri kaybolur, o kadar.
    return [];
  }
}

function writeRecentAccounts(companyId: string, accountIds: string[]) {
  if (typeof window === "undefined" || !companyId) return;

  try {
    window.localStorage.setItem(
      `${RECENT_ACCOUNTS_KEY}.${companyId}`,
      JSON.stringify(accountIds.slice(0, RECENT_ACCOUNTS_LIMIT))
    );
  } catch {
    // Depolama kotası dolu olabilir; kaydedememek akışı bozmamalı.
  }
}

export default function NewSupplierInvoicePage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [suppliers, setSuppliers] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [orders, setOrders] = useState<PurchaseOrderListItem[]>([]);
  const [warehouses, setWarehouses] = useState<WarehouseListItem[]>([]);
  const [inventoryItems, setInventoryItems] = useState<InventoryItemListItem[]>([]);
  const [expenseAccounts, setExpenseAccounts] = useState<AccountingAccountListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [recentAccountIds, setRecentAccountIds] = useState<string[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [invoiceType, setInvoiceType] = useState<number>(SupplierInvoiceType.Stock);
  const [supplierId, setSupplierId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [purchaseOrderId, setPurchaseOrderId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [costCenterCode, setCostCenterCode] = useState("");
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [currencyCode, setCurrencyCode] = useState("TRY");
  const [exchangeRate, setExchangeRate] = useState(1);
  const [invoiceDate, setInvoiceDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [dueDate, setDueDate] = useState("");
  const [description, setDescription] = useState("");
  const [defaultVatRate, setDefaultVatRate] = useState("20");
  const [items, setItems] = useState<DraftItem[]>([emptyItem("20")]);

  const [newCardIndex, setNewCardIndex] = useState<number | null>(null);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const isStock = invoiceType === SupplierInvoiceType.Stock;

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
      warehouseService.getAll({ companyId }),
      inventoryService.getItems({ companyId }),
      accountingAccountService.getAll({ companyId, isActive: true }),
      branchService.getAll(companyId).catch(() => [] as BranchListItem[]),
    ])
      .then(
        ([
          accountList,
          projectList,
          orderList,
          warehouseList,
          itemList,
          accountingList,
          branchList,
        ]) => {
          if (!active) return;
          // Onaylı ve bize fatura kesebilecek roldeki cariler.
          setSuppliers(
            accountList.filter(
              (account) =>
                account.status === APPROVED_STATUS &&
                (account.roles & SUPPLIER_SIDE_ROLES) !== 0
            )
          );
          setProjects(projectList);
          setOrders(orderList);
          setWarehouses(warehouseList.filter((warehouse) => warehouse.isActive));
          setInventoryItems(itemList.filter((item) => item.isActive));
          // Fişe kayıt kabul eden 6xx/7xx hesaplar; grup hesapları
          // seçilemez, backend zaten reddediyor.
          setExpenseAccounts(
            accountingList.filter(
              (account) =>
                account.isPostingAllowed &&
                (account.code.startsWith("6") || account.code.startsWith("7"))
            )
          );
          setBranches(branchList);
          setRecentAccountIds(readRecentAccounts(companyId));
        }
      )
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

  const inventoryOptions = useMemo<SearchSelectOption[]>(
    () =>
      inventoryItems.map((item) => ({
        value: item.id,
        label: `${item.code} — ${item.name}`,
        hint: `${item.unit}${item.category ? ` · ${item.category}` : ""} · stok ${item.totalStock}`,
        keywords: `${item.brand ?? ""} ${item.model ?? ""} ${item.barcode ?? ""}`,
      })),
    [inventoryItems]
  );

  const accountOptions = useMemo<SearchSelectOption[]>(
    () =>
      expenseAccounts.map((account) => ({
        value: account.id,
        label: `${account.code} — ${account.name}`,
        hint: account.requiresProject ? "Proje gerektirir" : undefined,
      })),
    [expenseAccounts]
  );

  const recentAccountOptions = useMemo<SearchSelectOption[]>(
    () =>
      recentAccountIds
        .map((id) => accountOptions.find((option) => option.value === id))
        .filter((option): option is SearchSelectOption => option !== undefined),
    [recentAccountIds, accountOptions]
  );

  /**
   * Masraf merkezi seçenekleri: Merkez (şube kodu) ve projeler. Serbest
   * metin bırakılsaydı aynı şantiye üç farklı yazımla üç ayrı masraf
   * merkezi gibi görünürdü.
   */
  const costCenterOptions = useMemo(() => {
    const options: { code: string; label: string }[] = [];

    for (const branch of branches) {
      if (branch.costCenterCode) {
        options.push({
          code: branch.costCenterCode,
          label: `${branch.costCenterCode} — ${branch.name}`,
        });
      }
    }

    for (const project of projects) {
      options.push({ code: project.code, label: `${project.code} — ${project.name}` });
    }

    return options;
  }, [branches, projects]);

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

  /**
   * Stok girişi isteniyor mu: depo ya da herhangi bir kalemde stok kartı
   * seçilmişse evet. Hiçbiri yoksa fatura stoğa uğramayan düz bir alış
   * (nakliye, hizmet) olarak kaydedilir — backend de aynı kuralı işletir.
   */
  const wantsStockEntry = useMemo(
    () =>
      isStock &&
      (warehouseId !== "" ||
        items.some((item) => item.inventoryItemId !== "" || item.warehouseId !== "")),
    [isStock, warehouseId, items]
  );

  const validationErrors = useMemo(() => {
    const messages: string[] = [];

    if (!companyId) messages.push("Şirket seçin.");
    if (!supplierId) messages.push("Tedarikçi seçin.");
    if (!invoiceNumber.trim()) messages.push("Tedarikçi fatura numarası girin.");
    if (!invoiceDate) messages.push("Fatura tarihi girin.");
    if (currencyCode !== "TRY" && (!exchangeRate || exchangeRate <= 0))
      messages.push("Dövizli faturada kur girin.");

    if (!isStock && !costCenterCode && !projectId) {
      messages.push("Gider faturasında masraf merkezi veya proje seçin.");
    }

    items.forEach((item, index) => {
      const line = index + 1;

      if (!item.description.trim()) {
        messages.push(`Kalem ${line}: açıklama girin.`);
      }

      if (!(Number(item.quantity) > 0)) {
        messages.push(`Kalem ${line}: miktar sıfırdan büyük olmalı.`);
      }

      // Boş birim fiyat Number("") ile sessizce 0'a dönüşüp sıfır tutarlı
      // fatura kaydediyordu; boş bırakmak ile 0 yazmak ayrı şeyler.
      if (item.unitPrice.trim() === "") {
        messages.push(`Kalem ${line}: birim fiyat girin.`);
      } else if (!(Number(item.unitPrice) >= 0)) {
        messages.push(`Kalem ${line}: birim fiyat geçersiz.`);
      }

      const vatRate = Number(item.vatRate);
      if (!(vatRate >= 0 && vatRate <= 100)) {
        messages.push(`Kalem ${line}: KDV oranı 0-100 arasında olmalı.`);
      }

      if (wantsStockEntry) {
        if (!item.inventoryItemId) {
          messages.push(`Kalem ${line}: stok kartı seçin.`);
        }

        if (!item.warehouseId && !warehouseId) {
          messages.push(`Kalem ${line}: depo seçin.`);
        }
      }

      if (!isStock && !item.expenseAccountId) {
        messages.push(`Kalem ${line}: gider hesabı seçin.`);
      }
    });

    return messages;
  }, [
    companyId,
    supplierId,
    invoiceNumber,
    invoiceDate,
    currencyCode,
    exchangeRate,
    isStock,
    costCenterCode,
    projectId,
    items,
    wantsStockEntry,
    warehouseId,
  ]);

  function updateItem(index: number, patch: Partial<DraftItem>) {
    setItems((current) =>
      current.map((item, i) => (i === index ? { ...item, ...patch } : item))
    );
  }

  /**
   * Kalem, stok kartı seçilince kartın adı ve birimiyle doldurulur.
   * Açıklama zaten yazılmışsa üzerine yazılmaz — kullanıcının yazdığı
   * metin kaybolmamalı.
   */
  function chooseInventoryItem(index: number, inventoryItemId: string) {
    const card = inventoryItems.find((item) => item.id === inventoryItemId);

    setItems((current) =>
      current.map((item, i) => {
        if (i !== index) return item;

        return {
          ...item,
          inventoryItemId,
          description: item.description.trim() || card?.name || item.description,
          unit: card?.unit || item.unit,
          vatRate:
            card?.vatRate !== null && card?.vatRate !== undefined
              ? String(card.vatRate)
              : item.vatRate,
        };
      })
    );
  }

  function chooseExpenseAccount(index: number, expenseAccountId: string) {
    updateItem(index, { expenseAccountId });

    if (!expenseAccountId) return;

    setRecentAccountIds((current) => {
      const next = [
        expenseAccountId,
        ...current.filter((id) => id !== expenseAccountId),
      ].slice(0, RECENT_ACCOUNTS_LIMIT);

      writeRecentAccounts(companyId, next);
      return next;
    });
  }


  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // Buton hiçbir zaman pasif değil; eksik varsa kullanıcıya NE eksik
    // olduğu söyleniyor. Sessizce disabled kalmak, kullanıcının neyi
    // dolduramadığını tahmin etmesine yol açıyordu.
    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

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
        inventoryItemId: isStock ? item.inventoryItemId || null : null,
        warehouseId: isStock ? item.warehouseId || null : null,
        expenseAccountId: isStock ? null : item.expenseAccountId || null,
        costCenterCode: isStock ? null : item.costCenterCode || null,
      }));

      const created = await supplierInvoiceService.create({
        companyId,
        supplierCurrentAccountId: supplierId,
        projectId: projectId || null,
        purchaseOrderId: purchaseOrderId || null,
        goodsReceiptId: null,
        invoiceNumber: invoiceNumber.trim(),
        invoiceDate,
        dueDate: dueDate || null,
        currencyCode,
        exchangeRate,
        description: description.trim() || null,
        items: payloadItems,
        invoiceType,
        warehouseId: isStock ? warehouseId || null : null,
        costCenterCode: costCenterCode || null,
      });

      router.push(`/muhasebe/faturalar/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fatura kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Tedarikçi Faturası"
      description="Alış faturası stoğa girer, gider faturası doğrudan gider hesabına yazılır"
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
              onChange={(e) => {
                // Şirket değişince tedarikçi/proje/sipariş başka şirkete ait
                // kalıyordu ve kayıtta "Tedarikçi cari kartı bulunamadı"
                // hatası veriyordu.
                setCompanyId(e.target.value);
                setSupplierId("");
                setProjectId("");
                setPurchaseOrderId("");
                setWarehouseId("");
                setCostCenterCode("");
                setItems((current) =>
                  current.map((item) => ({
                    ...item,
                    inventoryItemId: "",
                    warehouseId: "",
                    expenseAccountId: "",
                  }))
                );
              }}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Fatura Tipi *</span>
            <select
              value={String(invoiceType)}
              onChange={(e) => {
                const next = Number(e.target.value);
                setInvoiceType(next);

                // Tip değişince diğer tipin alanları anlamsızlaşıyor;
                // ekranda kalırlarsa kullanıcı gönderdiğini sanır ama
                // backend o alanları o tipte kabul etmez.
                setWarehouseId("");
                setItems((current) =>
                  current.map((item) => ({
                    ...item,
                    inventoryItemId: "",
                    warehouseId: "",
                    expenseAccountId: "",
                    costCenterCode: "",
                  }))
                );
              }}
            >
              <option value="0">Alış (Stok)</option>
              <option value="1">Gider</option>
            </select>
            <small>
              {isStock
                ? "Malzeme alışı: stok kartı ve depo seçilirse onayda stok girer."
                : "Elektrik, kira, müşavirlik gibi giderler; stoğa girmez."}
            </small>
          </label>

          <label>
            <span>Tedarikçi *</span>
            <select
              required
              value={supplierId}
              onChange={(e) => {
                // Sipariş listesi tedarikçiye göre süzülüyor; seçili sipariş
                // artık bu tedarikçiye ait olmayabilir.
                setSupplierId(e.target.value);
                setPurchaseOrderId("");
              }}
            >
              <option value="">Onaylı tedarikçi seçin</option>
              {suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>
                  {supplier.code} — {supplier.title}
                </option>
              ))}
            </select>
            {companyId && suppliers.length === 0 && (
              <small>
                Bu şirkette onaylı tedarikçi cari kartı yok. Cari kartı önce
                Cariler ekranından onaylayın.
              </small>
            )}
          </label>

          <label>
            <span>Proje</span>
            <select
              value={projectId}
              onChange={(e) => {
                setProjectId(e.target.value);
                setPurchaseOrderId("");
              }}
            >
              <option value="">Projesiz (merkez gideri)</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
            <small>
              Ofis elektriği, kira, müşavirlik gibi giderlerin projesi yoktur;
              boş bırakın.
            </small>
          </label>

          {isStock ? (
            <>
              <label>
                <span>Sipariş (ops.)</span>
                <select
                  value={purchaseOrderId}
                  onChange={(e) => setPurchaseOrderId(e.target.value)}
                >
                  <option value="">Siparişsiz (doğrudan fatura)</option>
                  {filteredOrders.map((order) => (
                    <option key={order.id} value={order.id}>
                      {order.orderNumber} — {money(order.grandTotal)}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Depo (fatura geneli)</span>
                <select
                  value={warehouseId}
                  onChange={(e) => setWarehouseId(e.target.value)}
                >
                  <option value="">Depoya girmeyecek</option>
                  {warehouses.map((warehouse) => (
                    <option key={warehouse.id} value={warehouse.id}>
                      {warehouse.code} — {warehouse.name}
                    </option>
                  ))}
                </select>
                <small>
                  Kalemde ayrı depo seçilmezse bu depo kullanılır. Depo ve stok
                  kartı boşsa fatura stoğa uğramaz (nakliye, hizmet gibi).
                </small>
              </label>
            </>
          ) : (
            <label>
              <span>Masraf Merkezi (fatura geneli)</span>
              <select
                value={costCenterCode}
                onChange={(e) => setCostCenterCode(e.target.value)}
              >
                <option value="">Seçilmedi</option>
                {costCenterOptions.map((option) => (
                  <option key={option.code} value={option.code}>
                    {option.label}
                  </option>
                ))}
              </select>
              <small>
                Kalemde ayrı masraf merkezi seçilmezse bu kullanılır.
              </small>
            </label>
          )}

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

          <CurrencyRateFields
            currency={currencyCode}
            exchangeRate={exchangeRate}
            documentDate={invoiceDate}
            onChange={(next) => {
              setCurrencyCode(next.currency);
              setExchangeRate(next.exchangeRate);
            }}
          />

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
          <p>
            {isStock
              ? "Stok kartı listeden seçilir; kart yoksa satırdaki kısayoldan açılır."
              : "Her kalem hesap planındaki bir gider hesabına yazılır."}
          </p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th style={{ minWidth: "220px" }}>
                  {isStock ? "Stok Kartı" : "Gider Hesabı *"}
                </th>
                <th>Açıklama *</th>
                <th>Miktar *</th>
                <th>Birim</th>
                <th>Birim Fiyat *</th>
                <th>KDV %</th>
                <th style={{ minWidth: "150px" }}>
                  {isStock ? "Depo" : "Masraf Merkezi"}
                </th>
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
                      {isStock ? (
                        <ErpSearchSelect
                          options={inventoryOptions}
                          value={item.inventoryItemId}
                          onChange={(next) => chooseInventoryItem(index, next)}
                          placeholder="Kart ara (kod, isim, marka)"
                          emptyMessage="Eşleşen stok kartı yok."
                          onCreate={() => setNewCardIndex(index)}
                          createLabel="Yeni stok kartı"
                        />
                      ) : (
                        <ErpSearchSelect
                          options={accountOptions}
                          value={item.expenseAccountId}
                          onChange={(next) => chooseExpenseAccount(index, next)}
                          placeholder="Hesap ara (kod veya ad)"
                          emptyMessage="Eşleşen gider hesabı yok."
                          quickPicks={recentAccountOptions}
                          quickPickLabel="Son kullanılanlar"
                        />
                      )}
                    </td>
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
                    <td>
                      {isStock ? (
                        <select
                          value={item.warehouseId}
                          onChange={(e) =>
                            updateItem(index, { warehouseId: e.target.value })
                          }
                        >
                          <option value="">Fatura deposu</option>
                          {warehouses.map((warehouse) => (
                            <option key={warehouse.id} value={warehouse.id}>
                              {warehouse.code} — {warehouse.name}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <select
                          value={item.costCenterCode}
                          onChange={(e) =>
                            updateItem(index, { costCenterCode: e.target.value })
                          }
                        >
                          <option value="">Fatura masraf merkezi</option>
                          {costCenterOptions.map((option) => (
                            <option key={option.code} value={option.code}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      )}
                    </td>
                    <td>{money(lineSubtotal)}</td>
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

          <div className="num">
            <div>Ara toplam: {money(totals.subtotal)}</div>
            <div>KDV: {money(totals.vat)}</div>
            <strong>Genel toplam: {money(totals.grandTotal)}</strong>
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

          <button type="submit" className="erp-primary-button" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Taslak Olarak Kaydet"}
          </button>
        </div>
      </form>

      {newCardIndex !== null && (
        <div className="erp-modal-backdrop" role="presentation">
          <div className="erp-modal" role="dialog" aria-modal="true">
            <div className="erp-form-header">
              <h2>Stok Kartı Bulunamadı</h2>
              <p>
                Kart açmak artık <strong>kategori seçimi</strong> gerektiriyor:
                birim, özellikler ve mükerrer engeli oradan geliyor.
              </p>
            </div>

            {/*
              HIZLI KART AÇMA KALDIRILDI (S2).

              Burada iki alan vardı — kod ve ad — ve tam olarak bu
              kısayol sınıflandırılmamış kart üretiyordu: canlıda bir
              kartın kategorisi "TURAN" (tedarikçi adı) yazıyordu,
              dördünde kategori boştu.

              Kod artık otomatik, ad özelliklerden üretiliyor ve aynı
              malzeme ikinci kez açılamıyor. Bunların hiçbiri iki
              alanlık bir kutuya sığmaz; sığdırmaya çalışmak yeni
              "TURAN"lar üretirdi.
            */}
            <div className="erp-alert warning">
              Faturayı kaybetmeden yeni sekmede kart açıp buraya dönebilirsiniz.
            </div>

            <div className="erp-form-actions">
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setNewCardIndex(null)}
              >
                Vazgeç
              </button>

              <a
                className="erp-primary-button"
                href="/depo-stok/yeni"
                target="_blank"
                rel="noreferrer"
              >
                Yeni Sekmede Kart Aç
              </a>
            </div>
          </div>
        </div>
      )}
    </ErpShell>
  );
}
