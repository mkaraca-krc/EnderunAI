"use client";

import { useCallback, useEffect, useState } from "react";
import { coefficient, decimal, money } from "@/lib/format/turkish";

import {
  personnelOvertimeService,
  type PersonnelOvertimeSummary,
} from "@/services/personnel-overtime.service";

/**
 * Personel kartının fazla mesai bölümü.
 *
 * Hesap burada YAPILMIYOR: yıllık kümülatif, tür kırılımı ve sınır
 * durumu uçtan gelir; uç da fazla mesai köprüsündeki kuralın aynısını
 * kullanır. Ekran yalnızca gösterir.
 *
 * Mesai tutarı ELDEN ödemedir: yalnızca extra_payment.view olan
 * kullanıcıya döner, payroll.view tek başına yetmez. Tutar gizlenmez,
 * yanıttan hiç gelmez — saha kullanıcısı saati görür, tutarı görmez.
 */
export default function PersonnelOvertimePanel({
  personnelId,
}: {
  personnelId: string;
}) {
  const [data, setData] = useState<PersonnelOvertimeSummary | null>(null);
  const [year, setYear] = useState(new Date().getFullYear());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    if (!personnelId) return;

    try {
      setData(await personnelOvertimeService.get(personnelId, year));
      setError("");
    } catch (err) {
      setData(null);
      setError(
        err instanceof Error ? err.message : "Fazla mesai bilgisi alınamadı."
      );
    }
  }, [personnelId, year]);

  useEffect(() => {
    let active = true;

    void (async () => {
      await load();

      if (active) setLoading(false);
    })();

    return () => {
      active = false;
    };
  }, [load]);

  if (loading) return <div style={box}>Fazla mesai bilgisi yükleniyor...</div>;
  if (error) return <div style={errorBox}>{error}</div>;
  if (!data) return <div style={box}>Fazla mesai kaydı bulunmuyor.</div>;

  const limitTone =
    data.limitStatus === "exceeded"
      ? danger
      : data.limitStatus === "near"
        ? warning
        : data.limitStatus === "undefined"
          ? neutral
          : success;

  const years = [year + 1, year, year - 1, year - 2].filter(
    (value, index, all) => all.indexOf(value) === index
  );

  return (
    <div style={{ display: "grid", gap: 14 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <span style={{ fontSize: 12, color: "#475569" }}>Yıl</span>

        <select
          value={year}
          onChange={(event) => {
            setYear(Number(event.target.value));
            setLoading(true);
          }}
          style={select}
        >
          {years.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </div>

      {/* --- Yıllık sınır özeti --- */}
      <div style={{ ...panel, ...limitTone }}>
        <div style={{ fontSize: 13, fontWeight: 700 }}>
          {data.limitStatus === "undefined"
            ? "Yıllık sınır girilmedi"
            : `Fazla çalışma: ${format(data.overtimeHours)} / ${format(
                data.annualLimit ?? 0
              )} saat`}
        </div>

        <div style={{ marginTop: 6, fontSize: 12 }}>
          {data.limitStatus === "undefined" ? (
            <>
              {year} yılı için yıllık fazla mesai sınırı tanımlanmadığından aşım
              kontrolü yapılamıyor. Şirket Ayarları → Bordro Parametreleri
              ekranından girin.
            </>
          ) : (
            <>
              {data.limitStatusName}. Sınır ENGEL DEĞİL uyarıdır: aşan onay yine
              geçer, onaylayan aşımı görür.
            </>
          )}
        </div>

        <div style={{ marginTop: 8, fontSize: 12, color: "#475569" }}>
          Hafta tatili {format(data.sundayHours)} saat · Genel tatil{" "}
          {format(data.publicHolidayHours)} saat — tatil çalışması yasal sınır
          sayımına girmez.
        </div>
      </div>

      {/* --- Bu ay: saat ve tutar ayrı satır --- */}
      <div style={{ ...panel, background: "#fff" }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: "#0f172a" }}>
          Bu ay ({String(data.currentMonth.month).padStart(2, "0")}.
          {data.currentMonth.year})
        </div>

        <div style={{ marginTop: 8, display: "grid", gap: 6, fontSize: 13 }}>
          <div style={splitRow}>
            <span style={{ color: "#64748b" }}>Mesai saati</span>
            <strong>{format(data.currentMonth.hours)} saat</strong>
          </div>

          {data.amountsHidden ? null : (
            <div style={splitRow}>
              <span style={{ color: "#64748b" }}>Mesai tutarı (elden)</span>
              <strong>{money(data.currentMonth.amount)}</strong>
            </div>
          )}
        </div>

        {data.amountsHidden ? null : (
          <div
            style={{
              marginTop: 10,
              paddingTop: 10,
              borderTop: "1px solid #e2e8f0",
              display: "grid",
              gap: 6,
              fontSize: 13,
            }}
          >
            <div style={splitRow}>
              <span style={{ color: "#64748b" }}>Resmî net</span>
              <span>{money(data.takeHome.officialNet)}</span>
            </div>

            <div style={splitRow}>
              <span style={{ color: "#64748b" }}>Manuel elden</span>
              <span>{money(data.takeHome.manualExtraMonthly)}</span>
            </div>

            <div style={splitRow}>
              <span style={{ color: "#64748b" }}>Mesai (elden)</span>
              <span>{money(data.takeHome.overtimeExtra)}</span>
            </div>

            <div style={{ ...splitRow, fontWeight: 700 }}>
              <span>Toplam ele geçen</span>
              <span>{money(data.takeHome.totalTakeHome)}</span>
            </div>

            <div style={{ fontSize: 11, color: "#94a3b8", marginTop: 4 }}>
              Mesai saat ücreti resmî net + manuel elden üzerinden
              hesaplanır ({money(data.takeHome.hourlyRate)}/saat); mesai bu
              tabana geri beslenmez ve toplam eldene bir kez girer.
            </div>
          </div>
        )}
      </div>

      {/* --- Muvafakat --- */}
      <div style={{ ...panel, ...(data.consent.isValid ? success : warning) }}>
        <div style={{ fontSize: 13, fontWeight: 700 }}>
          {data.consent.isValid
            ? `${year} fazla mesai muvafakati alınmış`
            : "Fazla mesai muvafakati eksik"}
        </div>

        <div style={{ marginTop: 6, fontSize: 12 }}>
          {data.consent.isValid ? (
            <>Tarih: {date(data.consent.date)}</>
          ) : (
            <>
              Fazla çalışma için işçiden yılda bir yazılı onay alınması gerekir.
              {data.consent.year
                ? ` Kayıtlı muvafakat ${data.consent.year} yılına ait.`
                : " Kayıtlı muvafakat yok."}{" "}
              Personel kartından yıl ve tarih girilir.
            </>
          )}
        </div>
      </div>

      {/* --- Puantaja düşmeyen saatler --- */}
      {data.notLandedCount > 0 ? (
        <div style={{ ...panel, ...warning }}>
          <div style={{ fontSize: 13, fontWeight: 700 }}>
            {data.notLandedCount} onaylı mesai puantaja düşmedi
          </div>

          <div style={{ marginTop: 6, fontSize: 12 }}>
            Bu saatler bordroya girmez. Genellikle o günün puantajı onaylı
            olduğu için olur: puantajın onayını kaldırıp mesaiyi yeniden
            onaylayın.
          </div>
        </div>
      ) : null}

      {/* --- Döküm --- */}
      {data.lines.length === 0 ? (
        <div style={box}>{year} yılında onaylı fazla mesai yok.</div>
      ) : (
        <div style={{ display: "grid", gap: 8 }}>
          {data.lines.map((line) => (
            <div key={line.id} style={row}>
              <div style={{ minWidth: 0 }}>
                <strong>{date(line.workDate)}</strong>

                <div style={{ marginTop: 4, fontSize: 13, color: "#64748b" }}>
                  {line.kindName} · {coefficient(line.multiplier)}×
                  {line.reason ? ` · ${line.reason}` : ""}
                </div>

                <div style={{ marginTop: 6, fontSize: 12 }}>
                  {line.landedOnAttendance ? (
                    <span style={{ color: "#64748b" }}>
                      Puantaj ayı: {line.attendanceMonth}
                    </span>
                  ) : (
                    <span style={{ color: "#b45309", fontWeight: 600 }}>
                      Puantaja düşmedi
                    </span>
                  )}
                </div>
              </div>

              <div style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                <div style={{ fontWeight: 700 }}>{format(line.hours)} saat</div>

                {data.amountsHidden ? null : (
                  <div style={{ marginTop: 4, fontSize: 13, color: "#64748b" }}>
                    {money(line.amount)}
                  </div>
                )}
              </div>
            </div>
          ))}

          {data.amountsHidden ? null : (
            <div style={{ ...row, background: "#fff", fontWeight: 700 }}>
              <span>Toplam</span>
              <span>{money(data.totalAmount)}</span>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * SAAT ile ÇARPAN aynı işlevden geçiyordu; ikisi ayrı sayı tipi.
 * Saat iki hane yeter (7,50); çarpan katsayıdır ve sondaki sıfırı
 * yazılmaz — "1,50" değil "1,5".
 */
function format(value: number) {
  return decimal(value, 2);
}

function date(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

const box = {
  padding: 24,
  textAlign: "center",
  borderRadius: 12,
  background: "#f8fafc",
  border: "1px solid #e2e8f0",
  color: "#64748b",
} as const;

const errorBox = {
  padding: 13,
  borderRadius: 11,
  background: "#fef2f2",
  border: "1px solid #fecaca",
  color: "#b91c1c",
  fontWeight: 700,
} as const;

const panel = {
  padding: 13,
  borderRadius: 11,
  border: "1px solid #e2e8f0",
} as const;

const row = {
  display: "flex",
  justifyContent: "space-between",
  gap: 16,
  padding: 13,
  border: "1px solid #e2e8f0",
  borderRadius: 11,
  background: "#f8fafc",
} as const;

const splitRow = {
  display: "flex",
  justifyContent: "space-between",
  gap: 12,
} as const;

const select = {
  height: 34,
  padding: "0 10px",
  borderRadius: 9,
  border: "1px solid #cbd5e1",
  background: "#fff",
  color: "#0f172a",
} as const;

const success = { background: "#ecfdf5", borderColor: "#a7f3d0", color: "#065f46" } as const;
const warning = { background: "#fffbeb", borderColor: "#fcd34d", color: "#92400e" } as const;
const danger = { background: "#fef2f2", borderColor: "#fecaca", color: "#b91c1c" } as const;
const neutral = { background: "#f8fafc", borderColor: "#e2e8f0", color: "#475569" } as const;
