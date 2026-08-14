"use client";

import PersonnelDocumentsPanel from "@/components/hr/personnel-documents-panel";
import PersonnelOvertimePanel from "@/components/hr/personnel-overtime-panel";
import { FormEvent, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import { personnelService } from "@/services/personnel.service";
import {
  personnel360Service,
  type Personnel360Response,
} from "@/services/personnel-360.service";
import { extraPaymentService } from "@/services/termination.service";

type PersonnelOption = {
  id: string;
  employeeNumber: string;
  fullName: string;
  companyId: string;
  isActive: boolean;
};

type TabKey =
  | "genel"
  | "puantaj"
  | "zimmet"
  | "egitim"
  | "belgeler"
  | "ozluk"
  | "mesai"
  | "kariyer"
  | "performans"
  | "disiplin";

const tabs: Array<{ key: TabKey; label: string }> = [
  { key: "genel", label: "Genel" },
  { key: "puantaj", label: "Puantaj" },
  { key: "zimmet", label: "Zimmet" },
  { key: "egitim", label: "Eğitim" },
  { key: "belgeler", label: "Sertifikalar" },
  { key: "ozluk", label: "Özlük Belgeleri" },
  { key: "mesai", label: "Fazla Mesai" },
  { key: "kariyer", label: "Kariyer" },
  { key: "performans", label: "Performans" },
  { key: "disiplin", label: "Disiplin" },
];

function date(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function money(value?: number | null, currency = "TRY") {
  return currencyMoney(Number(value ?? 0), currency);
}

function riskTone(level: string) {
  if (level === "High") return { bg: "var(--color-semantic-danger-tint)", border: "var(--color-semantic-danger-border)", text: "var(--color-semantic-danger)", label: "Yüksek" };
  if (level === "Medium") return { bg: "var(--color-semantic-warning-tint)", border: "var(--color-semantic-warning-border)", text: "var(--color-semantic-warning)", label: "Orta" };
  return { bg: "var(--color-semantic-success-tint)", border: "var(--color-semantic-success-border)", text: "var(--color-semantic-success)", label: "Düşük" };
}

function alertTone(severity: string) {
  if (severity === "High") return { bg: "var(--color-semantic-danger-tint)", border: "var(--color-semantic-danger-border)", text: "var(--color-semantic-danger)" };
  if (severity === "Medium") return { bg: "var(--color-semantic-warning-tint)", border: "var(--color-semantic-warning-border)", text: "var(--color-semantic-warning)" };
  return { bg: "var(--color-semantic-info-tint)", border: "var(--color-semantic-info-border)", text: "var(--color-semantic-info)" };
}

export default function Personnel360Page() {
  const [personnel, setPersonnel] = useState<PersonnelOption[]>([]);
  const [personnelId, setPersonnelId] = useState("");
  const [search, setSearch] = useState("");
  const [data, setData] = useState<Personnel360Response | null>(null);
  const [tab, setTab] = useState<TabKey>("genel");
  const [loading, setLoading] = useState(true);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [error, setError] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  const filteredPersonnel = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    return personnel.filter((x) => {
      if (!term) return true;
      return `${x.employeeNumber} ${x.fullName}`
        .toLocaleLowerCase("tr-TR")
        .includes(term);
    });
  }, [personnel, search]);

  useEffect(() => {
    async function load() {
      try {
        const result = await personnelService.getAll();
        const options = (result as PersonnelOption[]).filter((x) => x.isActive !== false);
        setPersonnel(options);
        if (options.length) setPersonnelId(options[0].id);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Personeller yüklenemedi.");
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, []);

  useEffect(() => {
    if (!personnelId) return;

    async function loadDetail() {
      setLoadingDetail(true);
      setError("");

      try {
        const result = await personnel360Service.get(personnelId);
        setData(result);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Personel 360 verileri yüklenemedi.");
      } finally {
        setLoadingDetail(false);
      }
    }

    void loadDetail();
    // refreshKey: ek ödeme kaydedildikten sonra kart yeniden çekilsin.
  }, [personnelId, refreshKey]);

  const activeAssignment = data?.assignments.find((x) => x.isActive && x.isPrimaryAssignment)
    ?? data?.assignments.find((x) => x.isActive);

  const timeline = useMemo(() => {
    if (!data) return [];

    const rows = [
      ...data.careerHistory.map((x) => ({
        date: x.effectiveDate,
        title: x.actionTypeName,
        detail: x.reason || x.notes || "Kariyer işlemi",
        type: "Kariyer",
      })),
      ...data.assets.map((x) => ({
        date: x.assignmentDate,
        title: `${x.assetCode} · ${x.assetName}`,
        detail: x.serialNumber ? `Seri No: ${x.serialNumber}` : x.statusName,
        type: "Zimmet",
      })),
      ...data.trainings.map((x) => ({
        date: x.completedAtUtc || x.plannedStartDate,
        title: "Eğitim kaydı",
        detail: x.trainerName || x.locationName || x.statusName,
        type: "Eğitim",
      })),
      ...data.performanceReviews.map((x) => ({
        date: `${x.year}-${String(Math.min(12, Math.max(1, x.periodNumber))).padStart(2, "0")}-01`,
        title: `${x.periodName} performans`,
        detail: `Puan: ${x.overallScore}`,
        type: "Performans",
      })),
      ...data.disciplinaryRecords.map((x) => ({
        date: x.incidentDate,
        title: x.subject,
        detail: x.statusName,
        type: "Disiplin",
      })),
    ];

    return rows
      .filter((x) => x.date)
      .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
      .slice(0, 18);
  }, [data]);

  return (
    <ErpShell design="redwood" title="Personel 360°">
      <main style={{ padding: 24, display: "grid", gap: 18 }}>
        <section style={topBar}>
          <div>
            <h1 style={{ margin: 0, fontSize: 28 }}>Personel 360°</h1>
            <p style={{ margin: "6px 0 0", color: "var(--erp-muted)" }}>
              Personelin tüm İK, performans, zimmet ve risk bilgileri tek ekranda.
            </p>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "220px 320px", gap: 10 }}>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Sicil veya personel ara..."
              style={input}
            />
            <select
              value={personnelId}
              onChange={(e) => setPersonnelId(e.target.value)}
              style={input}
              disabled={loading}
            >
              <option value="">Personel seçiniz</option>
              {filteredPersonnel.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.employeeNumber} · {x.fullName}
                </option>
              ))}
            </select>
          </div>
        </section>

        {error && <div style={errorBox}>{error}</div>}

        {loadingDetail && <div style={loadingBox}>Personel 360 verileri hazırlanıyor...</div>}

        {!loadingDetail && data && (
          <>
            <section style={{ display: "grid", gridTemplateColumns: "300px minmax(0,1fr)", gap: 18 }}>
              <aside style={card}>
                <div style={avatar}>
                  {data.profile.firstName?.[0] ?? ""}
                  {data.profile.lastName?.[0] ?? ""}
                </div>
                <h2 style={{ margin: "14px 0 4px", textAlign: "center" }}>
                  {data.profile.fullName}
                </h2>
                <div style={{ textAlign: "center", color: "var(--erp-muted)" }}>
                  {data.profile.jobTitle || data.profile.profession || "Görev belirtilmemiş"}
                </div>

                <div style={{ marginTop: 18, display: "grid", gap: 10 }}>
                  <Info label="Sicil No" value={data.profile.employeeNumber} />
                  <Info label="Durum" value={data.profile.statusName} />
                  <Info label="Telefon" value={data.profile.phone || "—"} />
                  <Info label="E-posta" value={data.profile.email || "—"} />
                  <Info label="İşe Giriş" value={date(data.profile.employmentStartDate)} />
                  <Info label="Aktif Rol" value={activeAssignment?.role || "—"} />
                </div>
              </aside>

              <div style={{ display: "grid", gap: 18 }}>
                <section style={{ display: "grid", gridTemplateColumns: "repeat(4,minmax(0,1fr))", gap: 12 }}>
                  <Kpi label="Toplam Saat" value={`${data.attendance.totalHours}`} sub="Seçilen dönem" />
                  <Kpi label="Fazla Mesai" value={`${data.attendance.overtimeHours}`} sub="Saat" />
                  <Kpi label="Aktif Zimmet" value={`${data.humanResources.activeAssetCount}`} sub="Ekipman" />
                  <Kpi label="İzin" value={`${data.humanResources.approvedLeaveDays}`} sub="Onaylı gün" />
                  <Kpi label="Eğitim" value={`${data.humanResources.completedTrainingCount}`} sub="Tamamlanan" />
                  <Kpi label="Sertifika" value={`${data.humanResources.validCertificateCount}`} sub="Geçerli" />
                  <Kpi label="Performans" value={`${data.humanResources.latestPerformanceScore ?? "—"}`} sub="Son puan" />
                  <Kpi label="AI Risk" value={`${data.analysis.riskScore}/100`} sub={riskTone(data.analysis.riskLevel).label} />
                </section>

                <section style={{ ...card, padding: 0, overflow: "hidden" }}>
                  <div style={tabBar}>
                    {tabs.map((x) => (
                      <button
                        key={x.key}
                        type="button"
                        onClick={() => setTab(x.key)}
                        style={{
                          ...tabButton,
                          ...(tab === x.key ? activeTabButton : {}),
                        }}
                      >
                        {x.label}
                      </button>
                    ))}
                  </div>
                  <div style={{ padding: 18 }}>
                    <TabContent
                      tab={tab}
                      data={data}
                      personnelId={personnelId}
                    />
                  </div>
                </section>
              </div>
            </section>

            <SalaryPanel
              personnelId={personnelId}
              financial={data.financial}
              onSaved={() => setRefreshKey((current) => current + 1)}
            />

            <section style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 380px", gap: 18 }}>
              <div style={card}>
                <h3 style={{ marginTop: 0 }}>Zaman Çizelgesi</h3>
                {timeline.length === 0 ? (
                  <Empty text="Zaman çizelgesi verisi bulunmuyor." />
                ) : (
                  <div style={{ display: "grid", gap: 12 }}>
                    {timeline.map((x, index) => (
                      <div key={`${x.type}-${x.date}-${index}`} style={timelineRow}>
                        <div style={timelineDot} />
                        <div>
                          <div style={{ fontSize: 12, color: "var(--erp-muted)" }}>
                            {date(x.date)} · {x.type}
                          </div>
                          <strong>{x.title}</strong>
                          <div style={{ marginTop: 3, color: "var(--erp-muted)" }}>{x.detail}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <aside style={{ display: "grid", gap: 14, alignContent: "start" }}>
                <div
                  style={{
                    ...card,
                    background: riskTone(data.analysis.riskLevel).bg,
                    borderColor: riskTone(data.analysis.riskLevel).border,
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", gap: 10 }}>
                    <h3 style={{ margin: 0 }}>AI Yönetici Özeti</h3>
                    <strong style={{ color: riskTone(data.analysis.riskLevel).text }}>
                      {riskTone(data.analysis.riskLevel).label} Risk
                    </strong>
                  </div>
                  <p style={{ color: "var(--erp-muted)", lineHeight: 1.55 }}>
                    {data.analysis.summary}
                  </p>

                  <h4 style={{ marginBottom: 8 }}>Olumlu Bulgular</h4>
                  {data.analysis.positiveFindings.map((x, i) => (
                    <div key={i} style={finding}>✓ {x}</div>
                  ))}

                  <h4 style={{ marginBottom: 8 }}>Dikkat Edilecekler</h4>
                  {data.analysis.attentionPoints.map((x, i) => (
                    <div key={i} style={attention}>! {x}</div>
                  ))}
                </div>

                <div style={card}>
                  <h3 style={{ marginTop: 0 }}>Aktif Uyarılar</h3>
                  {data.alerts.length === 0 ? (
                    <Empty text="Aktif uyarı bulunmuyor." />
                  ) : (
                    <div style={{ display: "grid", gap: 10 }}>
                      {data.alerts.slice(0, 8).map((x, i) => {
                        const tone = alertTone(x.severity);
                        return (
                          <div key={`${x.code}-${i}`} style={{ padding: 11, borderRadius: 10, background: tone.bg, border: `1px solid ${tone.border}`, color: tone.text }}>
                            <strong>{x.title}</strong>
                            <div style={{ marginTop: 4, fontSize: 13 }}>{x.description}</div>
                            {x.dueDate && <div style={{ marginTop: 5, fontSize: 12 }}>Tarih: {date(x.dueDate)}</div>}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              </aside>
            </section>
          </>
        )}
      </main>
    </ErpShell>
  );
}

/**
 * Ücret bloğu: resmî net + elden ödeme + toplam ele geçen.
 *
 * Üçü aynı yerde durmazsa "eline ne geçiyor" sorusu hiçbir ekranda
 * cevaplanmıyor; bugüne kadar elden ödeme yalnızca ayrı bir listede
 * duruyordu ve personel kartında hiç görünmüyordu.
 *
 * Gizleme sunucuda yapılıyor: yetkisi olmayan kullanıcıya tutarlar
 * null geliyor, burada yalnızca bunu anlatıyoruz.
 */
function SalaryPanel({
  personnelId,
  financial,
  onSaved,
}: {
  personnelId: string;
  financial: Personnel360Response["financial"];
  onSaved: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [startDate, setStartDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");
  const [failure, setFailure] = useState("");

  const currency = financial.currencyCode;
  const officialNet = financial.officialNetSalary ?? financial.currentNetSalary;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const parsed = Number(amount.replace(",", "."));

    if (!Number.isFinite(parsed) || parsed < 0) {
      setFailure("Tutar geçerli bir sayı olmalıdır.");
      return;
    }

    setSaving(true);
    setFailure("");
    setMessage("");

    try {
      // Yürürlükteki kayıt varsa üzerine yazılır; yoksa yeni açılır.
      // İki kayıt bırakmak "hangisi geçerli" belirsizliği doğururdu.
      const existing = await extraPaymentService.list(personnelId);
      const today = new Date().toISOString().slice(0, 10);
      const effective = existing
        .filter(
          (x) =>
            x.effectiveStartDate.slice(0, 10) <= today &&
            (!x.effectiveEndDate || x.effectiveEndDate.slice(0, 10) >= today)
        )
        .sort((a, b) =>
          b.effectiveStartDate.localeCompare(a.effectiveStartDate)
        )[0];

      const payload = {
        personnelId,
        monthlyAmount: parsed,
        effectiveStartDate: startDate,
        effectiveEndDate: null,
        note: note.trim() || null,
      };

      if (effective) {
        await extraPaymentService.update(effective.id, payload);
      } else {
        await extraPaymentService.create(payload);
      }

      setMessage("Ek ödeme kaydedildi.");
      setAmount("");
      setNote("");
      setOpen(false);
      onSaved();
    } catch (error) {
      setFailure(
        error instanceof ApiError || error instanceof Error
          ? error.message
          : "Ek ödeme kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  if (financial.salaryHidden) {
    return (
      <section style={card}>
        <h3 style={{ marginTop: 0 }}>Ücret</h3>
        <div style={{ color: "var(--erp-muted)" }}>
          Ücret rakamlarını görme yetkiniz yok.
        </div>
      </section>
    );
  }

  return (
    <section style={card}>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          gap: 12,
        }}
      >
        <h3 style={{ margin: 0 }}>Ücret ve Ek Ödeme</h3>
        {!financial.extraPaymentHidden && (
          <button
            type="button"
            onClick={() => setOpen((current) => !current)}
            style={smallButton}
          >
            {open ? "Vazgeç" : "Ek Ödemeyi Düzenle"}
          </button>
        )}
      </div>

      <div
        style={{
          marginTop: 14,
          display: "grid",
          gridTemplateColumns: "repeat(3,minmax(0,1fr))",
          gap: 12,
        }}
      >
        <MoneyTile
          label="Resmî Net"
          value={officialNet != null ? money(officialNet, currency) : "—"}
          hint="Bordroda görünen"
        />
        <MoneyTile
          label="Ek Ödeme (Elden)"
          value={
            financial.extraPaymentHidden
              ? "gizli"
              : money(financial.extraPaymentMonthlyAmount, currency)
          }
          hint={
            financial.extraPaymentHidden
              ? "Görme yetkiniz yok"
              : "Resmî bordroya girmez"
          }
        />
        <MoneyTile
          label="Toplam Ele Geçen"
          value={
            financial.totalTakeHome != null
              ? money(financial.totalTakeHome, currency)
              : "—"
          }
          hint="Resmî net + elden"
          strong
        />
      </div>

      {message && (
        <div style={{ marginTop: 12, color: "var(--color-semantic-success)" }}>{message}</div>
      )}
      {failure && (
        <div style={{ marginTop: 12, color: "var(--color-semantic-danger)" }}>{failure}</div>
      )}

      {open && !financial.extraPaymentHidden && (
        <form
          onSubmit={handleSubmit}
          style={{
            marginTop: 14,
            display: "grid",
            gridTemplateColumns: "180px 180px minmax(0,1fr) 140px",
            gap: 10,
            alignItems: "end",
          }}
        >
          <label style={fieldLabel}>
            Aylık Ek Ödeme
            <input
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
              inputMode="decimal"
              placeholder="0,00"
              style={input}
              required
            />
          </label>
          <label style={fieldLabel}>
            Geçerlilik Başlangıcı
            <input
              type="date"
              value={startDate}
              onChange={(event) => setStartDate(event.target.value)}
              style={input}
              required
            />
          </label>
          <label style={fieldLabel}>
            Not
            <input
              value={note}
              onChange={(event) => setNote(event.target.value)}
              placeholder="İsteğe bağlı"
              style={input}
            />
          </label>
          <button type="submit" style={smallButton} disabled={saving}>
            {saving ? "Kaydediliyor..." : "Kaydet"}
          </button>
        </form>
      )}
    </section>
  );
}

function MoneyTile({
  label,
  value,
  hint,
  strong,
}: {
  label: string;
  value: string;
  hint: string;
  strong?: boolean;
}) {
  return (
    <div
      style={{
        border: "1px solid var(--erp-border)",
        borderRadius: 12,
        padding: 14,
        background: strong ? "var(--erp-bg)" : "var(--erp-panel)",
      }}
    >
      <div style={{ fontSize: 12, color: "var(--erp-muted)" }}>{label}</div>
      <div
        style={{
          marginTop: 6,
          fontSize: 22,
          fontWeight: strong ? 700 : 600,
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {value}
      </div>
      <div style={{ marginTop: 4, fontSize: 12, color: "var(--erp-muted)" }}>{hint}</div>
    </div>
  );
}

const fieldLabel = { display: "grid", gap: 6, fontSize: 12, color: "var(--erp-muted)" } as const;
const smallButton = { height: 38, padding: "0 14px", borderRadius: 10, border: "1px solid var(--erp-border)", background: "var(--erp-panel)", color: "var(--erp-text)", fontWeight: 600, cursor: "pointer" } as const;

function TabContent({
  tab,
  data,
  personnelId,
}: {
  tab: TabKey;
  data: Personnel360Response;
  personnelId: string;
}) {
  if (tab === "genel") {
    return (
      <div style={grid2}>
        <Info label="Meslek" value={data.profile.profession || "—"} />
        <Info label="SGK Sicil" value={data.profile.sgkRegistrationNumber || "—"} />
        <Info label="Doğum Tarihi" value={date(data.profile.birthDate)} />
        <Info label="Adres" value={data.profile.address || "—"} />
        <Info
          label="Güncel Net Ücret"
          value={
            data.financial.salaryHidden
              ? "gizli"
              : money(
                  data.financial.officialNetSalary ??
                    data.financial.currentNetSalary,
                  data.financial.currencyCode
                )
          }
        />
        <Info
          label="Son Bordro"
          value={
            data.financial.salaryHidden
              ? "gizli"
              : money(
                  data.financial.lastPayrollNetAmount,
                  data.financial.currencyCode
                )
          }
        />
      </div>
    );
  }

  if (tab === "puantaj") {
    return (
      <div style={grid2}>
        <Info label="Kayıt Sayısı" value={String(data.attendance.recordCount)} />
        <Info label="Onaylı Kayıt" value={String(data.attendance.approvedRecordCount)} />
        <Info label="Normal Saat" value={String(data.attendance.normalHours)} />
        <Info label="Pazar Mesaisi" value={String(data.attendance.sundayHours)} />
        <Info label="Resmî Tatil" value={String(data.attendance.publicHolidayHours)} />
      </div>
    );
  }

  if (tab === "zimmet") {
    return <SimpleRows rows={data.assets.map((x) => ({
      title: `${x.assetCode} · ${x.assetName}`,
      detail: `${x.assetType} · Seri: ${x.serialNumber || "—"} · ${x.statusName}`,
      date: x.assignmentDate,
    }))} empty="Zimmet kaydı bulunmuyor." />;
  }

  if (tab === "egitim") {
    return <SimpleRows rows={data.trainings.map((x) => ({
      title: x.trainerName || "Eğitim",
      detail: `${x.locationName || "Konum yok"} · ${x.statusName}${x.examScore != null ? ` · Puan: ${x.examScore}` : ""}`,
      date: x.completedAtUtc || x.plannedStartDate,
    }))} empty="Eğitim kaydı bulunmuyor." />;
  }

  if (tab === "belgeler") {
    return <SimpleRows rows={data.certificates.map((x) => ({
      title: x.certificateNumber || "Sertifika",
      detail: `${x.issuingAuthority || "Kurum belirtilmemiş"} · ${x.statusName} · ${x.isVerified ? "Doğrulanmış" : "Doğrulanmamış"}`,
      date: x.expiryDate || x.issueDate,
    }))} empty="Sertifika kaydı bulunmuyor." />;
  }

  // Özlük belgeleri kendi verisini çeker: 360 yanıtında yok ve
  // yükleme/doğrulama/silme kendi uçlarına gider.
  if (tab === "ozluk") {
    return <PersonnelDocumentsPanel personnelId={personnelId} />;
  }

  // Fazla mesai kendi verisini çeker: yıllık kümülatif ve sınır
  // durumu 360 yanıtında yok, köprüyle aynı kuralı kullanan ayrı
  // uçtan gelir.
  if (tab === "mesai") {
    return <PersonnelOvertimePanel personnelId={personnelId} />;
  }

  if (tab === "kariyer") {
    return <SimpleRows rows={data.careerHistory.map((x) => ({
      title: x.actionTypeName,
      detail: x.reason || x.notes || "Kariyer işlemi",
      date: x.effectiveDate,
    }))} empty="Kariyer geçmişi bulunmuyor." />;
  }

  if (tab === "performans") {
    return <SimpleRows rows={data.performanceReviews.map((x) => ({
      title: `${x.year} · ${x.periodName}`,
      detail: `Genel puan: ${x.overallScore} · Yönetici: ${x.managerName || "—"} · ${x.statusName}`,
      date: `${x.year}-${String(Math.min(12, Math.max(1, x.periodNumber))).padStart(2, "0")}-01`,
    }))} empty="Performans değerlendirmesi bulunmuyor." />;
  }

  return <SimpleRows rows={data.disciplinaryRecords.map((x) => ({
    title: x.subject,
    detail: `${x.statusName} · ${x.decisionText || x.incidentDescription}`,
    date: x.incidentDate,
  }))} empty="Disiplin kaydı bulunmuyor." />;
}

function SimpleRows({
  rows,
  empty,
}: {
  rows: Array<{ title: string; detail: string; date: string }>;
  empty: string;
}) {
  if (!rows.length) return <Empty text={empty} />;

  return (
    <div style={{ display: "grid", gap: 10 }}>
      {rows.map((x, i) => (
        <div key={`${x.title}-${i}`} style={listRow}>
          <div>
            <strong>{x.title}</strong>
            <div style={{ marginTop: 4, color: "var(--erp-muted)" }}>{x.detail}</div>
          </div>
          <span style={{ color: "var(--erp-muted)", whiteSpace: "nowrap" }}>{date(x.date)}</span>
        </div>
      ))}
    </div>
  );
}

function Kpi({ label, value, sub }: { label: string; value: string; sub: string }) {
  return (
    <div style={kpi}>
      <div style={{ color: "var(--erp-muted)", fontSize: 13, fontWeight: 800 }}>{label}</div>
      <div style={{ marginTop: 7, fontSize: 25, fontWeight: 900, color: "var(--erp-text)" }}>{value}</div>
      <div style={{ marginTop: 4, color: "var(--erp-muted)", fontSize: 12 }}>{sub}</div>
    </div>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gap: 3 }}>
      <span style={{ color: "var(--erp-muted)", fontSize: 12, fontWeight: 800 }}>{label}</span>
      <strong style={{ color: "var(--erp-text)" }}>{value}</strong>
    </div>
  );
}

function Empty({ text }: { text: string }) {
  return <div style={{ padding: 22, textAlign: "center", color: "var(--erp-muted)" }}>{text}</div>;
}

const input = { minHeight: 42, border: "1px solid var(--erp-border)", borderRadius: 10, padding: "8px 11px", background: "var(--erp-panel)", color: "var(--erp-text)" } as const;
const card = { background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 16, padding: 18, boxShadow: "0 8px 24px rgba(15,23,42,.05)" } as const;
const topBar = { display: "flex", justifyContent: "space-between", alignItems: "center", gap: 18, flexWrap: "wrap", background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 16, padding: 18 } as const;
const avatar = { width: 92, height: 92, margin: "0 auto", borderRadius: "50%", display: "grid", placeItems: "center", background: "var(--erp-primary)", color: "var(--color-on-brand)", fontSize: 30, fontWeight: 900 } as const;
const kpi = { background: "var(--erp-panel)", border: "1px solid var(--erp-border)", borderRadius: 14, padding: 15, boxShadow: "0 5px 18px rgba(15,23,42,.04)" } as const;
const tabBar = { display: "flex", gap: 2, padding: 8, overflowX: "auto", borderBottom: "1px solid var(--erp-border)", background: "var(--erp-bg)" } as const;
const tabButton = { minHeight: 38, border: 0, borderRadius: 9, padding: "0 13px", background: "transparent", color: "var(--erp-muted)", fontWeight: 800, cursor: "pointer", whiteSpace: "nowrap" } as const;
const activeTabButton = { background: "var(--erp-primary)", color: "var(--color-on-brand)" } as const;
const grid2 = { display: "grid", gridTemplateColumns: "repeat(2,minmax(0,1fr))", gap: 16 } as const;
const listRow = { display: "flex", justifyContent: "space-between", gap: 16, padding: 13, border: "1px solid var(--erp-border)", borderRadius: 11, background: "var(--erp-bg)" } as const;
const timelineRow = { position: "relative", display: "grid", gridTemplateColumns: "14px minmax(0,1fr)", gap: 12, paddingBottom: 12 } as const;
const timelineDot = { width: 12, height: 12, marginTop: 5, borderRadius: "50%", background: "var(--erp-primary)", boxShadow: "0 0 0 4px var(--color-brand-primary-tint)" } as const;
const finding = { marginTop: 7, padding: 9, borderRadius: 9, background: "rgba(255,255,255,.7)", color: "var(--color-semantic-success)" } as const;
const attention = { marginTop: 7, padding: 9, borderRadius: 9, background: "rgba(255,255,255,.7)", color: "var(--color-semantic-warning)" } as const;
const errorBox = { padding: 14, borderRadius: 12, background: "var(--color-semantic-danger-tint)", border: "1px solid var(--color-semantic-danger-border)", color: "var(--color-semantic-danger)", fontWeight: 800 } as const;
const loadingBox = { padding: 28, textAlign: "center", borderRadius: 14, background: "var(--erp-panel)", border: "1px solid var(--erp-border)", color: "var(--erp-muted)" } as const;
