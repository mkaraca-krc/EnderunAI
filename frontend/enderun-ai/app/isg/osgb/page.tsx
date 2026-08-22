"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  Button,
  ConfirmDialog,
  SearchableSelect,
} from "@/components/ui";
import { money } from "@/lib/format/turkish";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  isgService,
  labelOf,
  OSGB_BILLING_TYPES,
  OSGB_EXPERT_TYPES,
  type IsgOsgbContractDetail,
  type IsgOsgbContractListItem,
  type IsgOsgbExpertPayload,
} from "@/services/isg.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** CurrentAccountStatus.Approved */
const APPROVED_STATUS = 2;

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

function statusClass(statusName: string) {
  if (statusName.includes("doldu")) return "red";
  if (statusName.includes("doluyor")) return "yellow";
  return "green";
}

type ExpertDraft = IsgOsgbExpertPayload & { key: string };

function emptyExpert(): ExpertDraft {
  return {
    key: crypto.randomUUID(),
    expertType: 0,
    fullName: "",
    certificateNumber: "",
    expertClass: "",
    phone: "",
    email: "",
    startDate: new Date().toISOString().slice(0, 10),
    endDate: "",
  };
}

export default function IsgOsgbPage() {
  const { has } = usePermissions();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);

  const [contracts, setContracts] = useState<IsgOsgbContractListItem[]>([]);
  const [selected, setSelected] = useState<IsgOsgbContractDetail | null>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [pendingDelete, setPendingDelete] =
    useState<string | null>(null);
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [currentAccountId, setCurrentAccountId] = useState("");
  const [contractNumber, setContractNumber] = useState("");
  const [startDate, setStartDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [endDate, setEndDate] = useState("");
  const [billingType, setBillingType] = useState("0");
  const [monthlyFee, setMonthlyFee] = useState("");
  const [perPersonFee, setPerPersonFee] = useState("");
  const [notes, setNotes] = useState("");
  const [experts, setExperts] = useState<ExpertDraft[]>([emptyExpert()]);

  const canManage = has("isg.create") || has("isg.edit");

  useEffect(() => {
    void (async () => {
      try {
        const result = await companyService.getAll();
        setCompanies(result);
        setCompanyId(result[0]?.id ?? "");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      const [contractList, accountList] = await Promise.all([
        isgService.getContracts(companyId),
        currentAccountService.getAll(companyId).catch(() => []),
      ]);

      setContracts(contractList);
      setAccounts(
        accountList.filter((account) => account.status === APPROVED_STATUS)
      );
    } catch (err) {
      setContracts([]);
      setError(err instanceof Error ? err.message : "Sözleşmeler alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 150);
    return () => window.clearTimeout(timer);
  }, [load]);

  async function openDetail(id: string) {
    setError("");

    try {
      setSelected(await isgService.getContract(id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sözleşme açılamadı.");
    }
  }

  function resetForm() {
    setEditingId(null);
    setCurrentAccountId("");
    setContractNumber("");
    setStartDate(new Date().toISOString().slice(0, 10));
    setEndDate("");
    setBillingType("0");
    setMonthlyFee("");
    setPerPersonFee("");
    setNotes("");
    setExperts([emptyExpert()]);
  }

  function startCreate() {
    resetForm();
    setFormOpen(true);
    setNotice("");
  }

  async function startEdit(id: string) {
    setError("");

    try {
      const detail = await isgService.getContract(id);

      setEditingId(detail.id);
      setCurrentAccountId(detail.currentAccountId);
      setContractNumber(detail.contractNumber);
      setStartDate(detail.startDate);
      setEndDate(detail.endDate ?? "");
      setBillingType(String(detail.billingType));
      setMonthlyFee(String(detail.monthlyFee));
      setPerPersonFee(String(detail.perPersonFee));
      setNotes(detail.notes ?? "");
      setExperts(
        detail.experts.length > 0
          ? detail.experts.map((expert) => ({
              key: expert.id,
              expertType: expert.expertType,
              fullName: expert.fullName,
              certificateNumber: expert.certificateNumber ?? "",
              expertClass: expert.expertClass ?? "",
              phone: expert.phone ?? "",
              email: expert.email ?? "",
              startDate: expert.startDate,
              endDate: expert.endDate ?? "",
            }))
          : [emptyExpert()]
      );
      setFormOpen(true);
      setNotice("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sözleşme açılamadı.");
    }
  }

  function updateExpert(key: string, patch: Partial<ExpertDraft>) {
    setExperts((current) =>
      current.map((expert) =>
        expert.key === key ? { ...expert, ...patch } : expert
      )
    );
  }

  const validationErrors: string[] = [];
  if (formOpen) {
    if (!currentAccountId) validationErrors.push("OSGB carisini seçin.");
    if (!contractNumber.trim()) validationErrors.push("Sözleşme numarası girin.");
    if (!startDate) validationErrors.push("Başlangıç tarihi girin.");

    if (billingType === "0" && !(Number(monthlyFee) > 0)) {
      validationErrors.push("Sabit aylık bedelde aylık ücret sıfırdan büyük olmalı.");
    }
    if (billingType === "1" && !(Number(perPersonFee) > 0)) {
      validationErrors.push("Kişi başı bedelde birim ücret sıfırdan büyük olmalı.");
    }

    experts.forEach((expert, index) => {
      if (!expert.fullName.trim()) {
        validationErrors.push(`Uzman ${index + 1}: ad soyad girin.`);
      }
      if (!expert.startDate) {
        validationErrors.push(`Uzman ${index + 1}: başlangıç tarihi girin.`);
      }
    });
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    const payload = {
      companyId,
      currentAccountId,
      contractNumber: contractNumber.trim(),
      startDate,
      endDate: endDate || null,
      billingType: Number(billingType),
      monthlyFee: Number(monthlyFee) || 0,
      perPersonFee: Number(perPersonFee) || 0,
      currencyCode: "TRY",
      notes: notes.trim() || null,
      experts: experts.map((expert) => ({
        expertType: expert.expertType,
        fullName: expert.fullName.trim(),
        certificateNumber: expert.certificateNumber?.trim() || null,
        expertClass: expert.expertClass?.trim() || null,
        phone: expert.phone?.trim() || null,
        email: expert.email?.trim() || null,
        startDate: expert.startDate,
        endDate: expert.endDate || null,
      })),
    };

    try {
      if (editingId) {
        await isgService.updateContract(editingId, payload);
        setNotice("Sözleşme güncellendi.");
      } else {
        await isgService.createContract(payload);
        setNotice("Sözleşme kaydedildi.");
      }

      setFormOpen(false);
      resetForm();
      setSelected(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sözleşme kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function remove(id: string) {
    setPendingDelete(null);

    setError("");

    try {
      await isgService.deleteContract(id);
      setNotice("Sözleşme silindi.");
      if (selected?.id === id) setSelected(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sözleşme silinemedi.");
    }
  }

  /**
   * Cari seçenekleri TEK YERDE: kod, ünvan ve vergi no üzerinden
   * aranıyor. Her çağrı yeri kendi eşlemesini yazsaydı bir ekranda
   * vergi numarasıyla bulunan cari diğerinde bulunamazdı.
   */
  const cariOptions = useMemo(
    () =>
      accounts.map((account) => ({
        id: account.id,
        code: account.code,
        title: account.title,
        extra: [account.shortName, account.taxNumber],
      })),
    [accounts]
  );

  return (
    <ErpShell
      design="redwood"
      title="OSGB Sözleşmeleri"
      description="Dış İSG hizmeti sözleşmesi, atanan uzman ve hekim, OSGB faturaları"
    >
      <div className="erp-page-toolbar">
        {/* Sözleşme ve uzman atamaları başka kullanıcı tarafından değiştiriliyor. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <div>
          <strong>{contracts.length} sözleşme</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            OSGB ayrı bir kayıt türü değil, bir cari — faturaları tedarikçi
            faturası akışından geçer.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => {
              setCompanyId(event.target.value);
              setSelected(null);
              setFormOpen(false);
            }}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          {canManage && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={startCreate}
            >
              + Yeni Sözleşme
            </button>
          )}
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {formOpen && (
        <form className="erp-form-card" onSubmit={submit}>
          <div className="erp-form-header">
            <h2>{editingId ? "Sözleşmeyi Düzenle" : "Yeni OSGB Sözleşmesi"}</h2>
            <p>
              Denetimde sorulan bilgi budur: hangi OSGB ile hangi tarihler
              arasında sözleşme var ve kim atanmış.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>OSGB Carisi *</span>
              <SearchableSelect
                value={currentAccountId}
                onChange={(next) => setCurrentAccountId(next)}
                options={cariOptions}
                emptyLabel="Onaylı cari seçin"
              />
              {accounts.length === 0 && (
                <small>
                  Bu şirkette onaylı cari kartı yok. OSGB firmasını önce cari
                  olarak tanımlayıp onaylayın.
                </small>
              )}
            </label>

            <label>
              <span>Sözleşme No *</span>
              <input
                type="text"
                value={contractNumber}
                onChange={(event) => setContractNumber(event.target.value)}
              />
            </label>

            <label>
              <span>Başlangıç *</span>
              <input
                type="date"
                value={startDate}
                onChange={(event) => setStartDate(event.target.value)}
              />
            </label>

            <label>
              <span>Bitiş (ops.)</span>
              <input
                type="date"
                value={endDate}
                onChange={(event) => setEndDate(event.target.value)}
              />
              <small>Boş bırakılırsa süresiz sayılır.</small>
            </label>

            <label>
              <span>Ücretlendirme *</span>
              <select
                value={billingType}
                onChange={(event) => setBillingType(event.target.value)}
              >
                {OSGB_BILLING_TYPES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            {billingType === "0" ? (
              <label>
                <span>Aylık Bedel *</span>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={monthlyFee}
                  onChange={(event) => setMonthlyFee(event.target.value)}
                />
              </label>
            ) : (
              <label>
                <span>Kişi Başı Bedel *</span>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={perPersonFee}
                  onChange={(event) => setPerPersonFee(event.target.value)}
                />
                <small>
                  Hakediş kesinti önerisi bu tutarı o dönemde şantiyede aktif
                  personel sayısıyla çarpar.
                </small>
              </label>
            )}

            <label className="span-2">
              <span>Not</span>
              <input
                type="text"
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
              />
            </label>
          </div>

          <div className="erp-form-header" style={{ marginTop: "20px" }}>
            <h2>Atanan Uzman ve Hekim</h2>
            <p>Belge numarası ve sınıfı denetimde sorulur.</p>
          </div>

          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Görev</th>
                  <th>Ad Soyad *</th>
                  <th>Belge No</th>
                  <th>Sınıf</th>
                  <th>Telefon</th>
                  <th>Başlangıç *</th>
                  <th>Bitiş</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {experts.map((expert) => (
                  <tr key={expert.key}>
                    <td>
                      <select
                        value={expert.expertType}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            expertType: Number(event.target.value),
                          })
                        }
                      >
                        {OSGB_EXPERT_TYPES.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <input
                        type="text"
                        value={expert.fullName}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            fullName: event.target.value,
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={expert.certificateNumber ?? ""}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            certificateNumber: event.target.value,
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={expert.expertClass ?? ""}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            expertClass: event.target.value,
                          })
                        }
                        placeholder="A / B / C"
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={expert.phone ?? ""}
                        onChange={(event) =>
                          updateExpert(expert.key, { phone: event.target.value })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="date"
                        value={expert.startDate}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            startDate: event.target.value,
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="date"
                        value={expert.endDate ?? ""}
                        onChange={(event) =>
                          updateExpert(expert.key, {
                            endDate: event.target.value,
                          })
                        }
                      />
                    </td>
                    <td>
                      {experts.length > 1 && (
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() =>
                            setExperts((current) =>
                              current.filter((item) => item.key !== expert.key)
                            )
                          }
                        >
                          Sil
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="erp-form-actions" style={{ justifyContent: "flex-start" }}>
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setExperts((current) => [...current, emptyExpert()])}
            >
              + Uzman Ekle
            </button>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => {
                setFormOpen(false);
                resetForm();
              }}
            >
              Vazgeç
            </button>

            <button type="submit" className="erp-primary-button" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Sözleşmeler</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : contracts.length === 0 ? (
          <div className="erp-empty-state">
            <p>Bu şirkette OSGB sözleşmesi tanımlı değil.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Sözleşme No</th>
                  <th>OSGB</th>
                  <th>Dönem</th>
                  <th>Ücretlendirme</th>
                  <th>Uzman</th>
                  <th>Durum</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {contracts.map((contract) => (
                  <tr key={contract.id}>
                    <td>
                      <strong>{contract.contractNumber}</strong>
                    </td>
                    <td>{contract.osgbTitle}</td>
                    <td>
                      {formatDate(contract.startDate)} →{" "}
                      {contract.endDate ? formatDate(contract.endDate) : "Süresiz"}
                    </td>
                    <td>
                      {contract.billingTypeName}
                      <small>
                        {contract.billingType === 0
                          ? money(contract.monthlyFee)
                          : `${money(contract.perPersonFee)} / kişi`}
                      </small>
                    </td>
                    <td>{contract.expertCount}</td>
                    <td>
                      <span className={`erp-status ${statusClass(contract.statusName)}`}>
                        {contract.statusName}
                      </span>
                      {typeof contract.daysUntilExpiry === "number" && (
                        <small>{contract.daysUntilExpiry} gün</small>
                      )}
                    </td>
                    <td>
                      <div className="erp-row-actions">
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() => void openDetail(contract.id)}
                        >
                          Detay
                        </button>

                        {has("isg.edit") && (
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => void startEdit(contract.id)}
                          >
                            Düzenle
                          </button>
                        )}

                        {has("isg.delete") && (
                          <button
                            type="button"
                            className="erp-secondary-button"
                            onClick={() => setPendingDelete(contract.id)}
                          >
                            Sil
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selected && (
        <div className="erp-panel erp-mt">
          <div className="erp-panel-header">
            <h2>
              {selected.contractNumber} — {selected.osgbTitle}
            </h2>
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setSelected(null)}
            >
              Kapat
            </button>
          </div>

          <div className="erp-detail-grid">
            <div>
              <span className="erp-stat-label">Vergi No</span>
              <strong>{selected.osgbTaxNumber ?? "—"}</strong>
            </div>
            <div>
              <span className="erp-stat-label">Durum</span>
              <strong>{selected.statusName}</strong>
            </div>
            <div>
              <span className="erp-stat-label">Ücretlendirme</span>
              <strong>
                {selected.billingType === 0
                  ? money(selected.monthlyFee)
                  : `${money(selected.perPersonFee)} / kişi`}
              </strong>
            </div>
            <div>
              <span className="erp-stat-label">Dönem</span>
              <strong>
                {formatDate(selected.startDate)} →{" "}
                {selected.endDate ? formatDate(selected.endDate) : "Süresiz"}
              </strong>
            </div>
            {selected.notes && (
              <div className="span-2">
                <span className="erp-stat-label">Not</span>
                <strong>{selected.notes}</strong>
              </div>
            )}
          </div>

          <h3 style={{ marginTop: "20px" }}>Atanan Uzman ve Hekim</h3>

          {selected.experts.length === 0 ? (
            <p>Sözleşmeye uzman/hekim atanmamış.</p>
          ) : (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Görev</th>
                    <th>Ad Soyad</th>
                    <th>Belge No</th>
                    <th>Sınıf</th>
                    <th>İletişim</th>
                    <th>Dönem</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {selected.experts.map((expert) => (
                    <tr key={expert.id}>
                      <td>{labelOf(OSGB_EXPERT_TYPES, expert.expertType)}</td>
                      <td>
                        <strong>{expert.fullName}</strong>
                      </td>
                      <td>{expert.certificateNumber ?? "—"}</td>
                      <td>{expert.expertClass ?? "—"}</td>
                      <td>
                        {expert.phone ?? "—"}
                        {expert.email && <small>{expert.email}</small>}
                      </td>
                      <td>
                        {formatDate(expert.startDate)} →{" "}
                        {expert.endDate ? formatDate(expert.endDate) : "Süresiz"}
                      </td>
                      <td>
                        <span
                          className={`erp-status ${
                            expert.isCurrentlyAssigned ? "green" : "gray"
                          }`}
                        >
                          {expert.isCurrentlyAssigned ? "Görevde" : "Ayrıldı"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <h3 style={{ marginTop: "20px" }}>OSGB Faturaları</h3>

          {selected.invoices.length === 0 ? (
            <p>
              Bu cariye ait tedarikçi faturası yok. OSGB faturaları ayrı bir
              yerde tutulmuyor; Tedarikçi Faturaları ekranından girilir.
            </p>
          ) : (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>İç No</th>
                    <th>Fatura No</th>
                    <th>Tarih</th>
                    <th>Tutar</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {selected.invoices.map((invoice) => (
                    <tr key={invoice.id}>
                      <td>{invoice.internalNumber}</td>
                      <td>{invoice.invoiceNumber}</td>
                      <td>{formatDate(invoice.invoiceDate)}</td>
                      <td>{money(invoice.grandTotal)}</td>
                      <td>
                        <span className="erp-status gray">{invoice.statusName}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
      <ConfirmDialog
        open={pendingDelete !== null}
        title="OSGB Sözleşmesini Sil"
        description={"Sözleşme kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz."}
        confirmLabel="Sözleşmeyi Sil"
        error={error}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => void remove(pendingDelete!)}
      />
    </ErpShell>
  );
}
