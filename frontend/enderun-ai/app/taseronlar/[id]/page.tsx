"use client";

import { use, useEffect, useMemo, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { ApiError } from "@/lib/api/api-client";
import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  subcontractorService,
  subcontractorDocumentService,
  SubcontractorContractType,
  SubcontractorDocumentStatus,
  SubcontractorDocumentType,
  SubcontractorResponsibility,
  type SubcontractorContractDetail,
  type SubcontractorDocument,
} from "@/services/subcontractor.service";
import {
  subcontractorProgressPaymentService,
  subcontractorTeamService,
  subcontractorLedgerService,
  SubcontractorLedgerKind,
  SubcontractorProgressPaymentStatus,
  type SubcontractorLedgerSummary,
  type SubcontractorProgressPaymentDetail,
  type SubcontractorProgressPaymentListItem,
  type SubcontractorTeamMember,
} from "@/services/subcontractor-progress-payment.service";

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function quantity(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 4,
  }).format(value);
}

function date(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

const today = () => new Date().toISOString().slice(0, 10);

export default function SubcontractorDetailPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);

  const [contract, setContract] = useState<SubcontractorContractDetail | null>(
    null
  );
  const [payments, setPayments] = useState<
    SubcontractorProgressPaymentListItem[]
  >([]);
  const [detail, setDetail] = useState<SubcontractorProgressPaymentDetail | null>(
    null
  );
  const [selectedPaymentId, setSelectedPaymentId] = useState("");

  const [team, setTeam] = useState<SubcontractorTeamMember[]>([]);
  const [socialSecurityWithUs, setSocialSecurityWithUs] = useState(false);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [teamPick, setTeamPick] = useState("");

  const [refreshKey, setRefreshKey] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [periodStart, setPeriodStart] = useState(today());
  const [periodEnd, setPeriodEnd] = useState(today());

  const [documents, setDocuments] = useState<SubcontractorDocument[]>([]);
  const [docType, setDocType] = useState(
    String(SubcontractorDocumentType.SocialSecurityClearance)
  );
  const [docTitle, setDocTitle] = useState("");
  const [docIssueDate, setDocIssueDate] = useState(today());
  const [docValidUntil, setDocValidUntil] = useState("");
  const [docFile, setDocFile] = useState<File | null>(null);

  const [ledger, setLedger] = useState<SubcontractorLedgerSummary | null>(null);
  const [entryKind, setEntryKind] = useState(String(SubcontractorLedgerKind.Payment));
  const [entryIsCash, setEntryIsCash] = useState(false);
  const [entryAmount, setEntryAmount] = useState("");
  const [entryVatRate, setEntryVatRate] = useState("20");
  const [entryDate, setEntryDate] = useState(today());
  const [entryDescription, setEntryDescription] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const [
          contractResult,
          paymentResult,
          teamResult,
          personnelResult,
          ledgerResult,
          documentResult,
        ] = await Promise.all([
          subcontractorService.getById(id),
          subcontractorProgressPaymentService.list({
            subcontractorContractId: id,
          }),
          subcontractorTeamService.get(id),
          personnelService.getAll(),
          subcontractorLedgerService.get(id),
          subcontractorDocumentService.list(id),
        ]);

        if (cancelled) return;

        setContract(contractResult);
        setPayments(paymentResult);
        setTeam(teamResult.members);
        setSocialSecurityWithUs(teamResult.socialSecurityWithUs);
        setLedger(ledgerResult);
        setDocuments(documentResult);
        setPersonnel(
          personnelResult.filter(
            (x) => x.companyId === contractResult.companyId && x.isActive
          )
        );
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [id, refreshKey]);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      if (!selectedPaymentId) {
        if (!cancelled) setDetail(null);
        return;
      }

      try {
        const result = await subcontractorProgressPaymentService.getById(
          selectedPaymentId
        );
        if (!cancelled) setDetail(result);
      } catch (loadError) {
        if (!cancelled) setError(errorMessage(loadError));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [selectedPaymentId, refreshKey]);

  const isLumpSum =
    contract?.contractType === SubcontractorContractType.LumpSum;

  const scopeSummary = useMemo(() => {
    if (!contract) return [];

    return [
      ["Yemek", contract.mealResponsibility],
      ["Konaklama", contract.accommodationResponsibility],
      ["Sigorta / SGK", contract.socialSecurityResponsibility],
      ["Malzeme", contract.materialResponsibility],
      ["İSG", contract.ohsResponsibility],
    ] as Array<[string, number]>;
  }, [contract]);

  const availablePersonnel = useMemo(
    () => personnel.filter((x) => !team.some((member) => member.id === x.id)),
    [personnel, team]
  );

  async function createPayment() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await subcontractorProgressPaymentService.create({
        subcontractorContractId: id,
        periodStartDate: periodStart,
        periodEndDate: periodEnd,
        progressPaymentDate: today(),
      });

      setNotice(`${result.progressPaymentNumber} açıldı.`);
      setSelectedPaymentId(result.id);
      setRefreshKey((current) => current + 1);
    } catch (createError) {
      setError(errorMessage(createError));
    } finally {
      setSaving(false);
    }
  }

  async function approvePayment(paymentId: string) {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await subcontractorProgressPaymentService.approve(paymentId);
      setNotice("Hakediş onaylandı.");
      setRefreshKey((current) => current + 1);
    } catch (approveError) {
      setError(errorMessage(approveError));
    } finally {
      setSaving(false);
    }
  }

  async function saveLedgerEntry() {
    setSaving(true);
    setError("");
    setNotice("");

    const amount = Number(entryAmount.replace(",", ".")) || 0;

    try {
      if (entryIsCash) {
        await subcontractorLedgerService.createCash({
          subcontractorContractId: id,
          kind: Number(entryKind),
          entryDate,
          amount,
          description: entryDescription.trim() || null,
        });
      } else {
        const result = await subcontractorLedgerService.create({
          subcontractorContractId: id,
          kind: Number(entryKind),
          entryDate,
          amount,
          vatRate: Number(entryVatRate.replace(",", ".")) || 0,
          description: entryDescription.trim() || null,
        });

        setNotice(
          `Kaydedildi. Tevkifat ${money(result.withholdingAmount)}, ` +
            `ödenecek ${money(result.payableAmount)}.`
        );
      }

      if (entryIsCash) setNotice("Elden kayıt eklendi.");
      setEntryAmount("");
      setEntryDescription("");
      setRefreshKey((current) => current + 1);
    } catch (entryError) {
      setError(errorMessage(entryError));
    } finally {
      setSaving(false);
    }
  }

  async function uploadDocument() {
    if (!docFile) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await subcontractorDocumentService.upload({
        subcontractorContractId: id,
        documentType: Number(docType),
        title: docTitle.trim(),
        issueDate: docIssueDate,
        validUntil: docValidUntil || null,
        file: docFile,
      });

      setNotice(result.message);
      setDocTitle("");
      setDocValidUntil("");
      setDocFile(null);
      setRefreshKey((current) => current + 1);
    } catch (uploadError) {
      setError(errorMessage(uploadError));
    } finally {
      setSaving(false);
    }
  }

  async function saveTeam(nextIds: string[]) {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await subcontractorTeamService.replace(id, nextIds);
      setNotice("Taşeron ekibi güncellendi.");
      setRefreshKey((current) => current + 1);
    } catch (teamError) {
      setError(errorMessage(teamError));
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell title="Taşeron Sözleşmesi">
      <main style={{ padding: 24, display: "grid", gap: 18 }}>
        <section style={topBar}>
          <div>
            <Link href="/taseronlar" style={{ color: "#0f766e", fontSize: 13 }}>
              ← Taşeronlar
            </Link>
            <h1 style={{ margin: "6px 0 0", fontSize: 26 }}>
              {contract?.contractNumber ?? "Yükleniyor..."}
            </h1>
            <p style={{ margin: "6px 0 0", color: "#64748b" }}>
              {contract?.workDescription} · {contract?.contractTypeName} ·{" "}
              {contract ? money(contract.contractAmount, contract.currencyCode) : ""}
            </p>
          </div>
        </section>

        {error && <div style={{ ...box, color: "#b91c1c" }}>{error}</div>}
        {notice && <div style={{ ...box, color: "#047857" }}>{notice}</div>}

        <section style={{ ...card, display: "grid", gap: 10 }}>
          <h2 style={{ margin: 0, fontSize: 18 }}>Sözleşme Kapsamı</h2>
          <p style={{ margin: 0, color: "#64748b", fontSize: 13 }}>
            Bizde olan kalemler hakedişte kesinti satırı açar; taşeronda
            olanlar hakedişte hiç görünmez.
          </p>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {scopeSummary.map(([label, value]) => {
              const ours = value === SubcontractorResponsibility.Us;
              return (
                <span
                  key={label}
                  style={{
                    padding: "6px 12px",
                    borderRadius: 999,
                    fontSize: 13,
                    fontWeight: 600,
                    background: ours ? "#ecfdf5" : "#f1f5f9",
                    color: ours ? "#047857" : "#64748b",
                    border: `1px solid ${ours ? "#a7f3d0" : "#e2e8f0"}`,
                  }}
                >
                  {label}: {ours ? "bizde" : "taşeronda"}
                </span>
              );
            })}
          </div>
        </section>

        {/* Taşeron ekibi yalnızca SGK bizdeyken anlamlı. */}
        {socialSecurityWithUs && (
          <section style={{ ...card, display: "grid", gap: 12 }}>
            <h2 style={{ margin: 0, fontSize: 18 }}>Taşeron Ekibi</h2>
            <p style={{ margin: 0, color: "#64748b", fontSize: 13 }}>
              SGK bizde olduğu için bu işçiler bizim bordromuzda. Bordro
              maliyetleri taşeron hakedişinde SGK / işçilik kesintisi olarak
              birikir.
            </p>

            {team.length === 0 ? (
              <div style={{ color: "#94a3b8" }}>Ekibe personel eklenmemiş.</div>
            ) : (
              <div style={{ display: "grid", gap: 6 }}>
                {team.map((member) => (
                  <div
                    key={member.id}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "8px 12px",
                      border: "1px solid #e2e8f0",
                      borderRadius: 10,
                    }}
                  >
                    <span>
                      {member.employeeNumber} · {member.fullName}
                      {member.jobTitle && (
                        <span style={{ color: "#64748b" }}> · {member.jobTitle}</span>
                      )}
                    </span>
                    <button
                      type="button"
                      style={smallButton}
                      disabled={saving}
                      onClick={() =>
                        void saveTeam(
                          team.filter((x) => x.id !== member.id).map((x) => x.id)
                        )
                      }
                    >
                      Çıkar
                    </button>
                  </div>
                ))}
              </div>
            )}

            <div style={{ display: "flex", gap: 10 }}>
              <select
                value={teamPick}
                onChange={(event) => setTeamPick(event.target.value)}
                style={{ ...input, minWidth: 280 }}
              >
                <option value="">Personel seçin</option>
                {availablePersonnel.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.employeeNumber} · {person.fullName}
                  </option>
                ))}
              </select>
              <button
                type="button"
                style={smallButton}
                disabled={!teamPick || saving}
                onClick={() => {
                  void saveTeam([...team.map((x) => x.id), teamPick]);
                  setTeamPick("");
                }}
              >
                Ekibe Ekle
              </button>
            </div>
          </section>
        )}

        <section style={{ ...card, display: "grid", gap: 14 }}>
          <h2 style={{ margin: 0, fontSize: 18 }}>Evraklar</h2>
          <p style={{ margin: 0, color: "#64748b", fontSize: 13 }}>
            SGK borcu yoktur yazısı kanunen üç ay geçerlidir; bitiş tarihi
            girilmese bile buna göre takip edilir.
          </p>

          {documents.length === 0 ? (
            <div style={{ color: "#94a3b8" }}>Evrak yüklenmemiş.</div>
          ) : (
            <div style={{ display: "grid", gap: 8 }}>
              {documents.map((document) => {
                const tone =
                  document.status === SubcontractorDocumentStatus.Expired
                    ? { bg: "#fef2f2", border: "#fecaca", text: "#b91c1c" }
                    : document.status === SubcontractorDocumentStatus.ExpiringSoon
                      ? { bg: "#fffbeb", border: "#fde68a", text: "#b45309" }
                      : { bg: "#fff", border: "#e2e8f0", text: "#0f172a" };

                return (
                  <div
                    key={document.id}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      gap: 12,
                      flexWrap: "wrap",
                      padding: 12,
                      borderRadius: 10,
                      background: tone.bg,
                      border: `1px solid ${tone.border}`,
                    }}
                  >
                    <div>
                      <strong>{document.documentTypeName}</strong> ·{" "}
                      {document.title}
                      <div style={{ marginTop: 4, fontSize: 12, color: "#64748b" }}>
                        Düzenlenme: {date(document.issueDate)}
                        {document.effectiveValidUntil && (
                          <>
                            {" · "}Geçerlilik:{" "}
                            {date(document.effectiveValidUntil)}
                            {document.validUntilIsImplied && " (kanuni üç ay)"}
                          </>
                        )}
                      </div>
                    </div>

                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: 10,
                      }}
                    >
                      <span style={{ color: tone.text, fontWeight: 600 }}>
                        {document.statusName}
                        {document.daysRemaining != null &&
                          document.status !== SubcontractorDocumentStatus.NoExpiry &&
                          ` (${document.daysRemaining} gün)`}
                      </span>
                      <a
                        href={subcontractorDocumentService.downloadUrl(document.id)}
                        style={linkButton}
                      >
                        İndir
                      </a>
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          <div
            style={{
              display: "grid",
              gridTemplateColumns:
                "190px minmax(0,1fr) 150px 150px 200px 120px",
              gap: 10,
              alignItems: "end",
            }}
          >
            <label style={fieldLabel}>
              Belge Türü
              <select
                value={docType}
                onChange={(event) => setDocType(event.target.value)}
                style={input}
              >
                <option value={SubcontractorDocumentType.Contract}>
                  Sözleşme
                </option>
                <option value={SubcontractorDocumentType.SignatureCircular}>
                  İmza sirküleri
                </option>
                <option value={SubcontractorDocumentType.TaxCertificate}>
                  Vergi levhası
                </option>
                <option value={SubcontractorDocumentType.SocialSecurityClearance}>
                  SGK borcu yoktur
                </option>
                <option value={SubcontractorDocumentType.TaxClearance}>
                  Vergi borcu yoktur
                </option>
                <option value={SubcontractorDocumentType.OccupationalSafety}>
                  İSG evrakı
                </option>
                <option value={SubcontractorDocumentType.TradeRegistry}>
                  Ticaret sicil gazetesi
                </option>
                <option value={SubcontractorDocumentType.InsurancePolicy}>
                  Sigorta poliçesi
                </option>
                <option value={SubcontractorDocumentType.Other}>Diğer</option>
              </select>
            </label>

            <label style={fieldLabel}>
              Başlık
              <input
                value={docTitle}
                onChange={(event) => setDocTitle(event.target.value)}
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              Düzenlenme
              <input
                type="date"
                value={docIssueDate}
                onChange={(event) => setDocIssueDate(event.target.value)}
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              Geçerlilik Bitişi
              <input
                type="date"
                value={docValidUntil}
                onChange={(event) => setDocValidUntil(event.target.value)}
                style={input}
              />
            </label>

            <label style={fieldLabel}>
              Dosya
              <input
                type="file"
                onChange={(event) =>
                  setDocFile(event.target.files?.[0] ?? null)
                }
                style={{ ...input, paddingTop: 9 }}
              />
            </label>

            <button
              type="button"
              style={primaryButton}
              disabled={saving || !docFile || !docTitle.trim()}
              onClick={() => void uploadDocument()}
            >
              Yükle
            </button>
          </div>
        </section>

        {ledger && (
          <section style={{ ...card, display: "grid", gap: 14 }}>
            <h2 style={{ margin: 0, fontSize: 18 }}>Ödemeler ve Avanslar</h2>

            {ledger.overAdvanceWarning && (
              <div
                style={{
                  padding: 12,
                  borderRadius: 10,
                  background: "#fffbeb",
                  border: "1px solid #fde68a",
                  color: "#b45309",
                }}
              >
                {ledger.overAdvanceWarning}
              </div>
            )}

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit,minmax(190px,1fr))",
                gap: 12,
              }}
            >
              <Total
                label="Faturalı Ödeme"
                value={ledger.invoicedPaymentTotal}
                currency={ledger.currencyCode}
              />
              <Total
                label="Faturalı Avans"
                value={ledger.invoicedAdvanceTotal}
                currency={ledger.currencyCode}
              />
              {/* Elden tutarlar yetki yoksa sunucudan hiç gelmiyor. */}
              {ledger.cashHidden ? (
                <div style={hiddenTile}>
                  <div style={{ fontSize: 12, color: "#64748b" }}>Elden</div>
                  <div style={{ marginTop: 6, fontSize: 18, color: "#94a3b8" }}>
                    gizli
                  </div>
                  <div style={{ marginTop: 4, fontSize: 12, color: "#94a3b8" }}>
                    Görme yetkiniz yok
                  </div>
                </div>
              ) : (
                <>
                  <Total
                    label="Elden Ödeme"
                    value={ledger.cashPaymentTotal ?? 0}
                    currency={ledger.currencyCode}
                  />
                  <Total
                    label="Elden Avans"
                    value={ledger.cashAdvanceTotal ?? 0}
                    currency={ledger.currencyCode}
                  />
                </>
              )}
              <Total
                label="Açık Avans"
                value={ledger.openAdvance}
                currency={ledger.currencyCode}
                strong
              />
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns:
                  "150px 140px 140px 110px 150px minmax(0,1fr) 120px",
                gap: 10,
                alignItems: "end",
              }}
            >
              <label style={fieldLabel}>
                Tür
                <select
                  value={entryKind}
                  onChange={(event) => setEntryKind(event.target.value)}
                  style={input}
                >
                  <option value={SubcontractorLedgerKind.Payment}>Ödeme</option>
                  <option value={SubcontractorLedgerKind.Advance}>Avans</option>
                </select>
              </label>

              <label style={fieldLabel}>
                Kayıt Şekli
                <select
                  value={entryIsCash ? "cash" : "invoiced"}
                  onChange={(event) =>
                    setEntryIsCash(event.target.value === "cash")
                  }
                  style={input}
                >
                  <option value="invoiced">Faturalı</option>
                  <option value="cash">Elden</option>
                </select>
              </label>

              <label style={fieldLabel}>
                Tutar
                <input
                  value={entryAmount}
                  onChange={(event) => setEntryAmount(event.target.value)}
                  inputMode="decimal"
                  placeholder="0,00"
                  style={input}
                />
              </label>

              <label style={fieldLabel}>
                KDV %
                <input
                  value={entryVatRate}
                  onChange={(event) => setEntryVatRate(event.target.value)}
                  inputMode="decimal"
                  style={input}
                  disabled={entryIsCash}
                />
              </label>

              <label style={fieldLabel}>
                Tarih
                <input
                  type="date"
                  value={entryDate}
                  onChange={(event) => setEntryDate(event.target.value)}
                  style={input}
                />
              </label>

              <label style={fieldLabel}>
                Açıklama
                <input
                  value={entryDescription}
                  onChange={(event) => setEntryDescription(event.target.value)}
                  style={input}
                />
              </label>

              <button
                type="button"
                style={primaryButton}
                disabled={saving || !entryAmount}
                onClick={() => void saveLedgerEntry()}
              >
                Kaydet
              </button>
            </div>

            <div style={{ fontSize: 12, color: "#64748b" }}>
              {entryIsCash
                ? "Elden kayıt resmî muhasebeye fiş yazmaz ve proje maliyeti defterine satır açmaz."
                : "Tevkifat oranı sözleşmeden alınır; faturalı ödeme proje maliyetine taşeron işçiliği olarak yazılır."}
            </div>

            {(ledger.entries.length > 0 ||
              (ledger.cashEntries?.length ?? 0) > 0) && (
              <div style={{ overflowX: "auto" }}>
                <table
                  style={{ width: "100%", borderCollapse: "collapse", minWidth: 760 }}
                >
                  <thead>
                    <tr style={{ background: "#f8fafc" }}>
                      {["Tarih", "Tür", "Şekil", "Tutar", "Tevkifat", "Açıklama"].map(
                        (title) => (
                          <th key={title} style={th}>
                            {title}
                          </th>
                        )
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {[...ledger.entries, ...(ledger.cashEntries ?? [])]
                      .sort((a, b) => b.entryDate.localeCompare(a.entryDate))
                      .map((entry) => (
                        <tr key={entry.id}>
                          <td style={td}>{date(entry.entryDate)}</td>
                          <td style={td}>{entry.kindName}</td>
                          <td style={td}>
                            {entry.isCash ? (
                              <span style={{ color: "#b45309" }}>Elden</span>
                            ) : (
                              "Faturalı"
                            )}
                          </td>
                          <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                            {money(entry.amount, entry.currencyCode)}
                          </td>
                          <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                            {entry.withholdingAmount != null
                              ? money(entry.withholdingAmount, entry.currencyCode)
                              : "—"}
                          </td>
                          <td style={td}>{entry.description ?? "—"}</td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        )}

        <section style={{ ...card, display: "grid", gap: 12 }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "end",
              gap: 12,
              flexWrap: "wrap",
            }}
          >
            <h2 style={{ margin: 0, fontSize: 18 }}>Hakedişler</h2>
            <div style={{ display: "flex", gap: 10, alignItems: "end" }}>
              <label style={fieldLabel}>
                Dönem Başı
                <input
                  type="date"
                  value={periodStart}
                  onChange={(event) => setPeriodStart(event.target.value)}
                  style={input}
                />
              </label>
              <label style={fieldLabel}>
                Dönem Sonu
                <input
                  type="date"
                  value={periodEnd}
                  onChange={(event) => setPeriodEnd(event.target.value)}
                  style={input}
                />
              </label>
              <button
                type="button"
                style={primaryButton}
                onClick={() => void createPayment()}
                disabled={saving}
              >
                Yeni Hakediş
              </button>
            </div>
          </div>

          {payments.length === 0 ? (
            <div style={{ color: "#94a3b8" }}>Henüz hakediş açılmamış.</div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 900 }}>
                <thead>
                  <tr style={{ background: "#f8fafc" }}>
                    {[
                      "No",
                      "Dönem",
                      "Bu Dönem İş",
                      "Kesinti",
                      "Net Ödenecek",
                      "Durum",
                      "",
                    ].map((title) => (
                      <th key={title} style={th}>
                        {title}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {payments.map((payment) => (
                    <tr key={payment.id}>
                      <td style={td}>
                        <strong>{payment.progressPaymentNumber}</strong>
                      </td>
                      <td style={td}>
                        {date(payment.periodStartDate)} – {date(payment.periodEndDate)}
                      </td>
                      <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                        {money(payment.currentAmount, payment.currencyCode)}
                      </td>
                      <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                        {money(payment.totalDeductionAmount, payment.currencyCode)}
                      </td>
                      <td
                        style={{
                          ...td,
                          fontVariantNumeric: "tabular-nums",
                          fontWeight: 700,
                        }}
                      >
                        {money(payment.netPayableAmount, payment.currencyCode)}
                      </td>
                      <td style={td}>{payment.statusName}</td>
                      <td style={td}>
                        <div style={{ display: "flex", gap: 6 }}>
                          <button
                            type="button"
                            style={smallButton}
                            onClick={() => setSelectedPaymentId(payment.id)}
                          >
                            Aç
                          </button>
                          {payment.status ===
                            SubcontractorProgressPaymentStatus.Draft && (
                            <button
                              type="button"
                              style={smallButton}
                              disabled={saving}
                              onClick={() => void approvePayment(payment.id)}
                            >
                              Onayla
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
        </section>

        {detail && (
          <section style={{ ...card, display: "grid", gap: 14 }}>
            <h2 style={{ margin: 0, fontSize: 18 }}>
              {detail.progressPaymentNumber} · {detail.statusName}
            </h2>

            {isLumpSum ? (
              <div style={{ overflowX: "auto" }}>
                <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 820 }}>
                  <thead>
                    <tr style={{ background: "#f8fafc" }}>
                      {[
                        "Kısım",
                        "Kısım Bedeli",
                        "Önceki %",
                        "Saha Önerisi %",
                        "Mutabakat %",
                        "Bu Dönem",
                      ].map((title) => (
                        <th key={title} style={th}>
                          {title}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {detail.sections.map((section) => (
                      <tr key={section.id}>
                        <td style={td}>{section.sectionName}</td>
                        <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                          {money(section.sectionAmount, detail.currencyCode)}
                        </td>
                        <td style={td}>%{section.previousProgressRate}</td>
                        <td style={{ ...td, color: "#64748b" }}>
                          %{section.suggestedProgressRate}
                        </td>
                        <td style={{ ...td, fontWeight: 600 }}>
                          %{section.agreedProgressRate}
                        </td>
                        <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                          {money(section.currentAmount, detail.currencyCode)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <div style={{ overflowX: "auto" }}>
                <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 980 }}>
                  <thead>
                    <tr style={{ background: "#f8fafc" }}>
                      {[
                        "Poz",
                        "Açıklama",
                        "Birim",
                        "Önceki",
                        "Saha Önerisi",
                        "Mutabakat",
                        "Bu Dönem",
                        "B.F.",
                        "Tutar",
                      ].map((title) => (
                        <th key={title} style={th}>
                          {title}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {detail.items.length === 0 && (
                      <tr>
                        <td colSpan={9} style={{ ...td, color: "#94a3b8" }}>
                          Bu hakedişe henüz kalem girilmemiş.
                        </td>
                      </tr>
                    )}
                    {detail.items.map((line) => (
                      <tr key={line.id}>
                        <td style={td}>{line.positionCode}</td>
                        <td style={td}>{line.description}</td>
                        <td style={td}>{line.unit}</td>
                        <td style={td}>{quantity(line.previousQuantity)}</td>
                        <td style={{ ...td, color: "#64748b" }}>
                          {quantity(line.suggestedQuantity)}
                        </td>
                        <td style={{ ...td, fontWeight: 600 }}>
                          {quantity(line.agreedQuantity)}
                        </td>
                        <td style={td}>{quantity(line.currentQuantity)}</td>
                        <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                          {money(line.unitPrice, detail.currencyCode)}
                        </td>
                        <td style={{ ...td, fontVariantNumeric: "tabular-nums" }}>
                          {money(line.currentAmount, detail.currencyCode)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div>
              <h3 style={{ margin: "0 0 8px", fontSize: 16 }}>Kesintiler</h3>
              <p style={{ margin: "0 0 10px", color: "#64748b", fontSize: 13 }}>
                Kalemler sözleşmenin kapsam tiklerinden gelir; tutarlar
                öneridir ve mutabakata göre düzeltilebilir.
              </p>

              {detail.deductions.length === 0 ? (
                <div style={{ color: "#94a3b8" }}>
                  Bu sözleşmede kesinti üreten bir kapsam yok.
                </div>
              ) : (
                <div style={{ display: "grid", gap: 8 }}>
                  {detail.deductions.map((deduction) => (
                    <div
                      key={deduction.id}
                      style={{
                        border: "1px solid #e2e8f0",
                        borderRadius: 10,
                        padding: 12,
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          gap: 12,
                        }}
                      >
                        <strong>{deduction.description}</strong>
                        <span style={{ fontVariantNumeric: "tabular-nums" }}>
                          {money(deduction.amount, detail.currencyCode)}
                        </span>
                      </div>
                      {deduction.suggestionBasis && (
                        <div
                          style={{ marginTop: 4, fontSize: 12, color: "#64748b" }}
                        >
                          {deduction.suggestionBasis}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit,minmax(200px,1fr))",
                gap: 12,
              }}
            >
              <Total label="Bu Dönem İş" value={detail.currentAmount} currency={detail.currencyCode} />
              <Total label="Kümülatif İş" value={detail.cumulativeAmount} currency={detail.currencyCode} />
              <Total label="Kesinti" value={detail.totalDeductionAmount} currency={detail.currencyCode} />
              <Total label="Net Ödenecek" value={detail.netPayableAmount} currency={detail.currencyCode} strong />
            </div>
          </section>
        )}
      </main>
    </ErpShell>
  );
}

function Total({
  label,
  value,
  currency,
  strong,
}: {
  label: string;
  value: number;
  currency: string;
  strong?: boolean;
}) {
  return (
    <div
      style={{
        border: "1px solid #e2e8f0",
        borderRadius: 12,
        padding: 14,
        background: strong ? "#f8fafc" : "#fff",
      }}
    >
      <div style={{ fontSize: 12, color: "#64748b" }}>{label}</div>
      <div
        style={{
          marginTop: 6,
          fontSize: 20,
          fontWeight: strong ? 700 : 600,
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {money(value, currency)}
      </div>
    </div>
  );
}

const card = { background: "#fff", border: "1px solid #e2e8f0", borderRadius: 16, padding: 18, boxShadow: "0 8px 24px rgba(15,23,42,.05)" } as const;
const topBar = { display: "flex", justifyContent: "space-between", alignItems: "center", gap: 18, flexWrap: "wrap", background: "#fff", border: "1px solid #e2e8f0", borderRadius: 16, padding: 18 } as const;
const box = { background: "#fff", border: "1px solid #e2e8f0", borderRadius: 12, padding: 14 } as const;
const input = { minHeight: 42, border: "1px solid #cbd5e1", borderRadius: 10, padding: "8px 11px", background: "#fff", color: "#0f172a" } as const;
const fieldLabel = { display: "grid", gap: 6, fontSize: 13, color: "#475569" } as const;
const th = { padding: "13px 14px", textAlign: "left", color: "#475569", fontSize: 13, borderBottom: "1px solid #e2e8f0" } as const;
const td = { padding: "13px 14px", borderBottom: "1px solid #eef2f7" } as const;
const primaryButton = { height: 42, padding: "0 18px", borderRadius: 10, border: "none", background: "#0f766e", color: "#fff", fontWeight: 600, cursor: "pointer" } as const;
const linkButton = { display: "inline-flex", alignItems: "center", height: 34, padding: "0 12px", borderRadius: 10, border: "1px solid #cbd5e1", background: "#fff", color: "#0f172a", fontWeight: 600, textDecoration: "none" } as const;
const hiddenTile = { border: "1px dashed #cbd5e1", borderRadius: 12, padding: 14, background: "#f8fafc" } as const;
const smallButton = { height: 36, padding: "0 12px", borderRadius: 10, border: "1px solid #cbd5e1", background: "#fff", color: "#0f172a", fontWeight: 600, cursor: "pointer" } as const;
