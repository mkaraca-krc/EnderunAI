"use client";

import Link from "next/link";
import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney, moneyWhole, percent } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { branchService, type BranchListItem } from "@/services/branch.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  OFFER_COUNTERPARTY_ROLES,
  OFFER_KINDS,
  OFFER_LOST_REASONS,
  OFFER_NEXT_STATUSES,
  OFFER_STATUS,
  OFFER_STATUS_LABELS,
  PROGRESS_PAYMENT_PERIODS,
  PROJECT_CONTRACT_TYPES,
  offerService,
  type OfferListItem,
  type OfferWinRate,
} from "@/services/offer.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Huni başlıklarındaki toplamlar — kuruşsuz.
 *
 * Bu rakamlar okunmak için değil, büyüklüğü görülmek için var
 * ("42.500.000 ₺ beklemede"); kuruş burada gürültü.
 *
 * TEKLİF SATIRINDA KULLANILMAZ: aşağıdaki tabloda tek bir teklifin
 * kendi tutarı yazıyor ve o rakam sözleşmeye giren rakamla birebir
 * aynı görünmeli. Bu ayrım daha önce yoktu — tek bir `maximumFractionDigits: 0`
 * biçimleyici hem panelleri hem tablo satırını basıyordu, yani teklif
 * listesindeki tutarlar yuvarlanmış gösteriliyordu.
 */
function summaryMoney(value: number) {
  return moneyWhole(value);
}

function labelOf(list: [number, string][], value: number) {
  return list.find(([key]) => key === value)?.[1] ?? "—";
}

function statusClass(status: number) {
  if (status === OFFER_STATUS.Won) return "erp-status green";
  if (status === OFFER_STATUS.Lost) return "erp-status red";
  if (status === OFFER_STATUS.Cancelled) return "erp-status gray";
  if (status === OFFER_STATUS.Pending) return "erp-status orange";
  if (status === OFFER_STATUS.Submitted) return "erp-status blue";
  return "erp-status gray";
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

/**
 * İş / teklif takip merkezi — fırsat hunisi.
 *
 * Teklif HAZIRLAMA ekranı ayrı (/teklifler) ve dokunulmadı; burası
 * verilen teklifin akıbetini izleyen katman: kime verildi, hangi
 * durumda, kaybedildiyse neden ve kazanma oranımız ne.
 *
 * Kaybedilen teklifler ayrı sekmede kalıcı olarak duruyor —
 * "geçen sefer bu işe şu fiyatı vermiştik" sorusunun tek cevabı.
 */
export default function OfferTrackingPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [items, setItems] = useState<OfferListItem[]>([]);
  const [winRate, setWinRate] = useState<OfferWinRate | null>(null);

  const [tab, setTab] = useState<"open" | "won" | "lost" | "all">("open");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  // Takip künyesi düzenleme
  const [trackingId, setTrackingId] = useState("");
  const [trackingForm, setTrackingForm] = useState({
    counterpartyCurrentAccountId: "",
    counterpartyRole: 1,
    kind: 1,
  });

  // Durum değiştirme
  const [statusId, setStatusId] = useState("");
  const [statusForm, setStatusForm] = useState({
    status: OFFER_STATUS.Submitted as number,
    lostReason: 1,
    lostReasonNote: "",
    note: "",
  });

  // Sözleşme künyesi
  const [contractOffer, setContractOffer] = useState<OfferListItem | null>(null);
  const [contractForm, setContractForm] = useState({
    projectId: "",
    branchId: "",
    code: "",
    name: "",
    contractNumber: "",
    contractDate: today(),
    contractAmount: "",
    contractType: "",
    plannedStartDate: "",
    plannedEndDate: "",
    cashRetentionRate: "5",
    vatRate: "20",
    withholdingTaxRate: "0",
    materialDeductionRate: "0",
    progressPaymentPeriod: "1",
    paymentTerms: "",
    city: "",
    district: "",
    transferToBoq: true,
  });

  const loadOffers = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      const [offers, rate] = await Promise.all([
        offerService.getAll({ companyId }),
        offerService.getWinRate({ companyId }),
      ]);

      setItems(offers);
      setWinRate(rate);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      try {
        const rows = await companyService.getAll();
        setCompanies(rows);

        const first = rows.find((x) => x.isActive !== false) ?? rows[0];
        if (first) setCompanyId((current) => current || first.id);
      } catch (err) {
        setError(messageOf(err));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  useEffect(() => {
    void (async () => {
      await loadOffers();
    })();
  }, [loadOffers]);

  useEffect(() => {
    if (!companyId) return;

    let cancelled = false;

    void (async () => {
      const [accountRows, branchRows, projectRows] = await Promise.all([
        currentAccountService.getAll(companyId).catch(() => []),
        branchService.getAll(companyId).catch(() => []),
        projectService.getAll(companyId).catch(() => []),
      ]);

      if (cancelled) return;

      setAccounts(accountRows);
      setBranches(branchRows);
      setProjects(projectRows);
    })();

    return () => {
      cancelled = true;
    };
  }, [companyId]);

  const visible = items.filter((item) => {
    if (tab === "open") return [0, 1, 2].includes(item.status);
    if (tab === "won") return item.status === OFFER_STATUS.Won;
    if (tab === "lost") return item.status === OFFER_STATUS.Lost;
    return true;
  });

  function openTracking(item: OfferListItem) {
    setStatusId("");
    setTrackingId(item.id);
    setTrackingForm({
      counterpartyCurrentAccountId: item.counterpartyCurrentAccountId ?? "",
      counterpartyRole: item.counterpartyRole || 1,
      kind: item.kind || 1,
    });
  }

  function openStatus(item: OfferListItem) {
    setTrackingId("");
    setStatusId(item.id);

    const next = OFFER_NEXT_STATUSES[item.status] ?? [];

    setStatusForm({
      status: next[0] ?? OFFER_STATUS.Submitted,
      lostReason: 1,
      lostReasonNote: "",
      note: "",
    });
  }

  async function submitTracking(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await offerService.updateTracking(trackingId, {
        counterpartyCurrentAccountId:
          trackingForm.counterpartyCurrentAccountId || null,
        counterpartyRole: trackingForm.counterpartyCurrentAccountId
          ? trackingForm.counterpartyRole
          : 0,
        kind: trackingForm.kind,
      });

      setNotice(result.message);
      setTrackingId("");
      await loadOffers();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function submitStatus(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setNotice("");

    try {
      const result = await offerService.changeStatus(statusId, {
        status: statusForm.status,
        lostReason:
          statusForm.status === OFFER_STATUS.Lost ? statusForm.lostReason : 0,
        lostReasonNote:
          statusForm.status === OFFER_STATUS.Lost
            ? statusForm.lostReasonNote || null
            : null,
        note: statusForm.note || null,
      });

      const changed = items.find((x) => x.id === statusId) ?? null;

      setNotice(result.message);
      setStatusId("");
      await loadOffers();

      // Kazanıldıysa sözleşme künyesi hemen istenir; proje ve icmal
      // ancak bu adımdan sonra doğuyor.
      if (result.requiresContract && changed) {
        setContractOffer(changed);
        setContractForm((prev) => ({
          ...prev,
          name: changed.title,
          contractAmount: String(changed.grandTotal),
          contractType: changed.kind === 2 ? "1" : changed.kind === 1 ? "2" : "",
        }));
      }
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function submitContract(event: FormEvent) {
    event.preventDefault();
    if (!contractOffer) return;

    setBusy(true);
    setError("");
    setNotice("");

    const extraWork = Boolean(contractForm.projectId);

    try {
      const result = await offerService.createContract(contractOffer.id, {
        projectId: contractForm.projectId || null,
        branchId: extraWork ? null : contractForm.branchId || null,
        code: extraWork ? null : contractForm.code,
        name: extraWork ? null : contractForm.name,
        contractNumber: contractForm.contractNumber || null,
        contractDate: contractForm.contractDate || null,
        contractAmount: contractForm.contractAmount
          ? Number(contractForm.contractAmount)
          : null,
        contractType: contractForm.contractType
          ? Number(contractForm.contractType)
          : null,
        plannedStartDate: contractForm.plannedStartDate || null,
        plannedEndDate: contractForm.plannedEndDate || null,
        cashRetentionRate: Number(contractForm.cashRetentionRate || 0),
        vatRate: Number(contractForm.vatRate || 0),
        withholdingTaxRate: Number(contractForm.withholdingTaxRate || 0),
        materialDeductionRate: Number(contractForm.materialDeductionRate || 0),
        progressPaymentPeriod: Number(contractForm.progressPaymentPeriod || 0),
        paymentTerms: contractForm.paymentTerms || null,
        city: contractForm.city || null,
        district: contractForm.district || null,
        transferToBoq: contractForm.transferToBoq,
      });

      setNotice(
        `${result.message} Proje: ${result.projectCode}` +
          (result.boqNumber ? ` · İcmal: ${result.boqNumber}` : "") +
          (result.warnings.length > 0
            ? ` — ${result.warnings.join(" ")}`
            : "")
      );

      setContractOffer(null);
      await loadOffers();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="İş / Teklif Takibi"
      description="Verdiğimiz her teklif, akıbeti ve kazanma oranımız"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-page-toolbar">
        {/* Huni rakamları ve teklif durumları ekip içinde
            değişiyor; tazeleme olmadan ekran eskiyordu. */}
        <button
          type="button"
          disabled={loading || busy}
          onClick={() => void loadOffers()}
        >
          Yenile
        </button>

        <select
          value={companyId}
          onChange={(event) => setCompanyId(event.target.value)}
        >
          {companies.map((company) => (
            <option key={company.id} value={company.id}>
              {company.code} · {company.name}
            </option>
          ))}
        </select>

        <Link className="erp-secondary-button" href="/teklifler">
          Teklif Hazırlama
        </Link>
      </div>

      {winRate && (
        <div className="erp-quick-grid">
          <div className="erp-panel">
            <small style={{ display: "block" }}>Kazanma Oranı (adet)</small>
            <strong style={{ fontSize: 22 }}>
              {percent(winRate.countWinRate)}
            </strong>
            <small style={{ display: "block" }}>
              {winRate.wonCount} kazanıldı / {winRate.lostCount} kaybedildi
            </small>
          </div>

          <div className="erp-panel">
            <small style={{ display: "block" }}>Kazanma Oranı (tutar)</small>
            <strong style={{ fontSize: 22 }}>
              {percent(winRate.amountWinRate)}
            </strong>
            <small style={{ display: "block" }}>
              {summaryMoney(winRate.wonAmount)} / {summaryMoney(winRate.lostAmount)}
            </small>
          </div>

          <div className="erp-panel">
            <small style={{ display: "block" }}>Açık Huni</small>
            <strong style={{ fontSize: 22 }}>{winRate.openCount}</strong>
            <small style={{ display: "block" }}>
              {summaryMoney(winRate.openAmount)} beklemede
            </small>
          </div>

          <div className="erp-panel">
            <small style={{ display: "block" }}>Toplam Teklif</small>
            <strong style={{ fontSize: 22 }}>{winRate.totalCount}</strong>
            <small style={{ display: "block" }}>
              {winRate.cancelledCount} iptal
            </small>
          </div>
        </div>
      )}

      {winRate && winRate.lostReasons.length > 0 && (
        <section className="erp-panel" style={{ marginTop: 16 }}>
          <h3 style={{ marginTop: 0 }}>Neden Kaybediyoruz</h3>
          <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13 }}>
            {winRate.lostReasons.map((row) => (
              <li key={row.reason} style={{ marginBottom: 4 }}>
                {row.reasonName}: <strong>{row.count} teklif</strong> ·{" "}
                {summaryMoney(row.amount)}
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="erp-page-toolbar" style={{ marginTop: 16 }}>
        {(
          [
            ["open", "Açık"],
            ["won", "Kazanılan"],
            ["lost", "Kaybedilen (arşiv)"],
            ["all", "Tümü"],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={
              tab === key ? "erp-primary-button" : "erp-secondary-button"
            }
            onClick={() => setTab(key)}
          >
            {label}
          </button>
        ))}
      </div>

      <section className="erp-table-card" style={{ marginTop: 12 }}>
        <div className="erp-table-header">
          <h2>Teklifler</h2>
          <small>{visible.length} kayıt</small>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : visible.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Bu görünümde teklif yok</strong>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Teklif</th>
                  <th>Kime Verildi</th>
                  <th>Tip</th>
                  <th>Tarih</th>
                  <th>Tutar</th>
                  <th>Durum</th>
                  <th>İşlem</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((item) => {
                  const next = OFFER_NEXT_STATUSES[item.status] ?? [];

                  return (
                    <tr key={item.id}>
                      <td>
                        <strong>{item.offerNumber}</strong>
                        <small style={{ display: "block" }}>{item.title}</small>
                      </td>
                      <td>
                        {item.counterpartyName ?? (
                          <em className="rw-value-warning">Belirtilmedi</em>
                        )}
                        {item.counterpartyName && (
                          <small style={{ display: "block" }}>
                            {labelOf(
                              OFFER_COUNTERPARTY_ROLES,
                              item.counterpartyRole
                            )}
                          </small>
                        )}
                      </td>
                      <td>{labelOf(OFFER_KINDS, item.kind)}</td>
                      <td>{dateFormat.format(new Date(item.offerDate))}</td>
                      <td className="num">{currencyMoney(item.grandTotal, item.currency)}</td>
                      <td>
                        <span className={statusClass(item.status)}>
                          {OFFER_STATUS_LABELS[item.status]}
                        </span>
                        {item.status === OFFER_STATUS.Lost && (
                          <small style={{ display: "block" }}>
                            {labelOf(OFFER_LOST_REASONS, item.lostReason)}
                          </small>
                        )}
                        {item.status === OFFER_STATUS.Won && !item.projectId && (
                          <small className="rw-value-warning" style={{ display: "block" }}>
                            sözleşme bekliyor
                          </small>
                        )}
                      </td>
                      <td>
                        <div
                          style={{ display: "flex", gap: 6, flexWrap: "wrap" }}
                        >
                          <Link
                            className="erp-secondary-button"
                            href={`/teklifler/${item.id}`}
                          >
                            Aç
                          </Link>

                          {next.length > 0 && (
                            <>
                              <button
                                type="button"
                                className="erp-secondary-button"
                                onClick={() => openTracking(item)}
                              >
                                Künye
                              </button>
                              <button
                                type="button"
                                className="erp-primary-button"
                                onClick={() => openStatus(item)}
                              >
                                Durum
                              </button>
                            </>
                          )}

                          {item.status === OFFER_STATUS.Won &&
                            !item.projectId && (
                              <button
                                type="button"
                                className="erp-primary-button"
                                onClick={() => {
                                  setContractOffer(item);
                                  setContractForm((prev) => ({
                                    ...prev,
                                    name: item.title,
                                    contractAmount: String(item.grandTotal),
                                    contractType:
                                      item.kind === 2
                                        ? "1"
                                        : item.kind === 1
                                          ? "2"
                                          : "",
                                  }));
                                }}
                              >
                                Sözleşme Aç
                              </button>
                            )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {trackingId && (
        <section className="erp-panel" style={{ marginTop: 16 }}>
          <h3 style={{ marginTop: 0 }}>Takip Künyesi</h3>
          <p style={{ fontSize: 13, marginTop: 0 }}>
            Teklifi kime verdiğimiz belirtilmeden &quot;Verildi&quot; adımına
            geçilemez; kazanma oranının kırılımı bu bilgiye dayanıyor.
          </p>

          <form onSubmit={submitTracking}>
            <div className="erp-form-grid">
              <label>
                Kime Verildi (cari)
                <select
                  value={trackingForm.counterpartyCurrentAccountId}
                  onChange={(event) =>
                    setTrackingForm((prev) => ({
                      ...prev,
                      counterpartyCurrentAccountId: event.target.value,
                    }))
                  }
                >
                  <option value="">Seçiniz</option>
                  {accounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.code} · {account.title}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Karşı Taraf
                <select
                  value={trackingForm.counterpartyRole}
                  onChange={(event) =>
                    setTrackingForm((prev) => ({
                      ...prev,
                      counterpartyRole: Number(event.target.value),
                    }))
                  }
                  disabled={!trackingForm.counterpartyCurrentAccountId}
                >
                  {OFFER_COUNTERPARTY_ROLES.map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Teklif Tipi
                <select
                  value={trackingForm.kind}
                  onChange={(event) =>
                    setTrackingForm((prev) => ({
                      ...prev,
                      kind: Number(event.target.value),
                    }))
                  }
                >
                  {OFFER_KINDS.map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
              <button
                type="submit"
                className="erp-primary-button"
                disabled={busy}
              >
                Kaydet
              </button>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setTrackingId("")}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </section>
      )}

      {statusId && (
        <section className="erp-panel" style={{ marginTop: 16 }}>
          <h3 style={{ marginTop: 0 }}>Durum Değiştir</h3>
          <p style={{ fontSize: 13, marginTop: 0 }}>
            Kazanıldı, Kaybedildi ve İptal <strong>nihaidir</strong> — sonradan
            geri alınamaz.
          </p>

          <form onSubmit={submitStatus}>
            <div className="erp-form-grid">
              <label>
                Yeni Durum
                <select
                  value={statusForm.status}
                  onChange={(event) =>
                    setStatusForm((prev) => ({
                      ...prev,
                      status: Number(event.target.value),
                    }))
                  }
                >
                  {(
                    OFFER_NEXT_STATUSES[
                      items.find((x) => x.id === statusId)?.status ?? 0
                    ] ?? []
                  ).map((value) => (
                    <option key={value} value={value}>
                      {OFFER_STATUS_LABELS[value]}
                    </option>
                  ))}
                </select>
              </label>

              {statusForm.status === OFFER_STATUS.Lost && (
                <>
                  <label>
                    Kayıp Nedeni *
                    <select
                      value={statusForm.lostReason}
                      onChange={(event) =>
                        setStatusForm((prev) => ({
                          ...prev,
                          lostReason: Number(event.target.value),
                        }))
                      }
                    >
                      {OFFER_LOST_REASONS.map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Kayıp Açıklaması
                    <input
                      value={statusForm.lostReasonNote}
                      onChange={(event) =>
                        setStatusForm((prev) => ({
                          ...prev,
                          lostReasonNote: event.target.value,
                        }))
                      }
                      placeholder="Rakip %12 altında kaldı"
                    />
                  </label>
                </>
              )}

              <label>
                Gerekçe / Not
                <input
                  value={statusForm.note}
                  onChange={(event) =>
                    setStatusForm((prev) => ({
                      ...prev,
                      note: event.target.value,
                    }))
                  }
                />
              </label>
            </div>

            <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
              <button
                type="submit"
                className="erp-primary-button"
                disabled={busy}
              >
                {busy ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setStatusId("")}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </section>
      )}

      {contractOffer && (
        <section className="erp-panel" style={{ marginTop: 16 }}>
          <h3 style={{ marginTop: 0 }}>
            Sözleşme Künyesi — {contractOffer.offerNumber}
          </h3>
          <p style={{ fontSize: 13, marginTop: 0 }}>
            Proje seçmezseniz yeni proje açılır, şantiye deposu kurulur ve
            teklif kalemleri icmale aktarılır. Mevcut proje seçerseniz bu bir{" "}
            <strong>ek iştir</strong>: o projenin sözleşme künyesi değişmez,
            yalnız ek icmal açılır.
          </p>

          <form onSubmit={submitContract}>
            <div className="erp-form-grid">
              <label>
                Proje
                <select
                  value={contractForm.projectId}
                  onChange={(event) =>
                    setContractForm((prev) => ({
                      ...prev,
                      projectId: event.target.value,
                    }))
                  }
                >
                  <option value="">Yeni proje aç</option>
                  {projects.map((project) => (
                    <option key={project.id} value={project.id}>
                      Ek iş → {project.code} · {project.name}
                    </option>
                  ))}
                </select>
              </label>

              {!contractForm.projectId && (
                <>
                  <label>
                    Şube *
                    <select
                      value={contractForm.branchId}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          branchId: event.target.value,
                        }))
                      }
                      required
                    >
                      <option value="">Seçiniz</option>
                      {branches.map((branch) => (
                        <option key={branch.id} value={branch.id}>
                          {branch.code} · {branch.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Proje Kodu *
                    <input
                      value={contractForm.code}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          code: event.target.value,
                        }))
                      }
                      required
                    />
                  </label>

                  <label>
                    Proje Adı
                    <input
                      value={contractForm.name}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          name: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Sözleşme Tipi
                    <select
                      value={contractForm.contractType}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          contractType: event.target.value,
                        }))
                      }
                    >
                      <option value="">Teklif tipinden türet</option>
                      {PROJECT_CONTRACT_TYPES.filter(([v]) => v !== 0).map(
                        ([value, label]) => (
                          <option key={value} value={value}>
                            {label}
                          </option>
                        )
                      )}
                    </select>
                  </label>
                </>
              )}

              <label>
                Sözleşme No
                <input
                  value={contractForm.contractNumber}
                  onChange={(event) =>
                    setContractForm((prev) => ({
                      ...prev,
                      contractNumber: event.target.value,
                    }))
                  }
                />
              </label>

              <label>
                İmza Tarihi
                <input
                  type="date"
                  value={contractForm.contractDate}
                  onChange={(event) =>
                    setContractForm((prev) => ({
                      ...prev,
                      contractDate: event.target.value,
                    }))
                  }
                />
              </label>

              {!contractForm.projectId && (
                <>
                  <label>
                    Sözleşme Bedeli
                    <input
                      type="number"
                      step="0.01"
                      value={contractForm.contractAmount}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          contractAmount: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    İşe Başlama
                    <input
                      type="date"
                      value={contractForm.plannedStartDate}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          plannedStartDate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Termin
                    <input
                      type="date"
                      value={contractForm.plannedEndDate}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          plannedEndDate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Teminat Kesintisi (%)
                    <input
                      type="number"
                      step="0.01"
                      value={contractForm.cashRetentionRate}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          cashRetentionRate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    KDV (%)
                    <input
                      type="number"
                      step="0.01"
                      value={contractForm.vatRate}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          vatRate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Stopaj (%)
                    <input
                      type="number"
                      step="0.01"
                      value={contractForm.withholdingTaxRate}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          withholdingTaxRate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Hakediş Periyodu
                    <select
                      value={contractForm.progressPaymentPeriod}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          progressPaymentPeriod: event.target.value,
                        }))
                      }
                    >
                      {PROGRESS_PAYMENT_PERIODS.map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    İl
                    <input
                      value={contractForm.city}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          city: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    İlçe
                    <input
                      value={contractForm.district}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          district: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Ödeme Koşulları
                    <input
                      value={contractForm.paymentTerms}
                      onChange={(event) =>
                        setContractForm((prev) => ({
                          ...prev,
                          paymentTerms: event.target.value,
                        }))
                      }
                      placeholder="Hakediş onayından 30 gün sonra"
                    />
                  </label>
                </>
              )}
            </div>

            <label
              style={{
                display: "flex",
                alignItems: "center",
                gap: 8,
                marginTop: 12,
                fontSize: 13,
              }}
            >
              <input
                type="checkbox"
                checked={contractForm.transferToBoq}
                onChange={(event) =>
                  setContractForm((prev) => ({
                    ...prev,
                    transferToBoq: event.target.checked,
                  }))
                }
              />
              Teklif kalemlerini icmale aktar (hakedişin referansı)
            </label>

            <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
              <button
                type="submit"
                className="erp-primary-button"
                disabled={busy}
              >
                {busy ? "Oluşturuluyor..." : "Sözleşmeyi Aç"}
              </button>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setContractOffer(null)}
              >
                Sonra
              </button>
            </div>
          </form>
        </section>
      )}
    </ErpShell>
  );
}
