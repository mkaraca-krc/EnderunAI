"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import {
  Badge,
  Button,
  EmptyState,
  Input,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  RELIGIOUS_HOLIDAY,
  WORK_WEEK,
  holidayCalendarService,
  type HolidayCalendar,
} from "@/services/attendance-sheet.service";

function formatDate(iso: string) {
  return iso.slice(0, 10).split("-").reverse().join(".");
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/**
 * Resmî tatil takvimi.
 *
 * Dini bayram TARİHLERİ sistemde yok — kayan ve resmî ilana bağlı bir
 * tarihi tahmin etmek, puantajı ve bordroyu sessizce yanlış üretirdi.
 * Kullanıcı bayramın yalnızca ilk gününü giriyor; arife ve kalan
 * günler türetiliyor.
 *
 * Takvim DOĞRULANMADAN puantaj cetveli doldurulamaz ve her değişiklik
 * doğrulamayı düşürür.
 */
export default function HolidayCalendarPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [year, setYear] = useState(new Date().getFullYear());
  const [data, setData] = useState<HolidayCalendar | null>(null);

  const [religiousKind, setReligiousKind] = useState<number>(
    RELIGIOUS_HOLIDAY.Ramazan
  );
  const [religiousFirstDay, setReligiousFirstDay] = useState("");

  const [customDate, setCustomDate] = useState("");
  const [customName, setCustomName] = useState("");
  const [customHalf, setCustomHalf] = useState(false);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  /**
   * Takvim doğrulama onayı.
   *
   * Eskiden window.prompt ile sorulup dönen değer null kontrolü
   * YAPILMADAN servise geçiliyordu: kullanıcı "Vazgeç"e bassa
   * bile takvim doğrulanmış işaretleniyordu. Doğrulama damgası
   * anlamlı — sayfanın kendi metnine göre sonraki her değişiklik
   * onu düşürüyor.
   */
  const [verifyOpen, setVerifyOpen] = useState(false);
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setData(await holidayCalendarService.get(companyId, year));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId, year]);

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
      await load();
    })();
  }, [load]);

  async function run(action: () => Promise<string>) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      setNotice(await action());
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  const days = data?.calendar?.days ?? [];

  return (
    <ErpShell
      design="redwood"
      title="Resmî Tatil Takvimi"
      description="Puantaj cetveli bu takvimden dolar; doğrulanmadan kullanılmaz"
    >
      {/* Takvim başka yöneticinin düzenlemesiyle değişiyor ve doğrulama düşüyor. */}
      <div className="mb-4 flex justify-end">
        <button
          type="button"
          className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          disabled={busy}
          onClick={() => void load()}
        >
          Yenile
        </button>
      </div>

      {error && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}
      {notice && (
        <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {notice}
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-end gap-2">
        <div className="max-w-xs flex-1">
          <Select
            label="Şirket"
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            options={companies.map((company) => ({
              value: company.id,
              label: `${company.code} · ${company.name}`,
            }))}
          />
        </div>

        <div className="w-28">
          <Input
            label="Yıl"
            type="number"
            value={year}
            onChange={(event) => setYear(Number(event.target.value) || year)}
          />
        </div>

        <Link href="/insan-kaynaklari/puantaj-cetveli">
          <Button variant="secondary">Puantaj Cetveli</Button>
        </Link>
      </div>

      {data && (
        <div
          className={`mb-4 rounded-lg border px-4 py-3 text-sm ${
            data.isVerified
              ? "border-emerald-200 bg-emerald-50 text-emerald-800"
              : "border-amber-200 bg-amber-50 text-amber-900"
          }`}
        >
          {data.isVerified ? (
            <>
              <strong>Takvim doğrulandı</strong> — {days.length} gün. Puantaj
              cetveli bu takvimden dolabilir.
            </>
          ) : (
            <>
              <strong>Takvim doğrulanmadı.</strong> {data.message}
            </>
          )}
        </div>
      )}

      <div className="mb-4 grid gap-3 lg:grid-cols-3">
        <div className="rounded-xl border border-slate-200 bg-white p-4">
          <h3 className="mb-1 text-sm font-semibold text-slate-800">
            Sabit resmî tatiller
          </h3>
          <p className="mb-3 text-xs text-slate-500">
            Yılbaşı, 23 Nisan, 1 Mayıs, 19 Mayıs, 15 Temmuz, 30 Ağustos ve
            Cumhuriyet Bayramı. Tarihleri her yıl aynı, hesaplanabilir.
          </p>
          <Button
            variant="secondary"
            disabled={busy || !companyId}
            onClick={() =>
              run(async () => {
                const result = await holidayCalendarService.seedFixed(
                  companyId,
                  year
                );
                return result.message;
              })
            }
          >
            Sabit Tatilleri Ekle
          </Button>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4">
          <h3 className="mb-1 text-sm font-semibold text-slate-800">
            Dini bayram
          </h3>
          <p className="mb-3 text-xs text-slate-500">
            Tarihi sistem bilemez; resmî ilandan bakıp bayramın{" "}
            <strong>birinci gününü</strong> girin. Arife (yarım gün) ve kalan
            günler otomatik eklenir.
          </p>

          <div className="flex flex-col gap-2">
            <Select
              value={String(religiousKind)}
              onChange={(event) => setReligiousKind(Number(event.target.value))}
              options={[
                { value: String(RELIGIOUS_HOLIDAY.Ramazan), label: "Ramazan Bayramı (3 gün)" },
                { value: String(RELIGIOUS_HOLIDAY.Kurban), label: "Kurban Bayramı (4 gün)" },
              ]}
            />
            <Input
              type="date"
              value={religiousFirstDay}
              onChange={(event) => setReligiousFirstDay(event.target.value)}
            />
            <Button
              variant="secondary"
              disabled={busy || !religiousFirstDay}
              onClick={() =>
                run(async () => {
                  const result = await holidayCalendarService.addReligious(
                    companyId,
                    year,
                    religiousKind,
                    religiousFirstDay
                  );

                  setReligiousFirstDay("");
                  return result.message;
                })
              }
            >
              Bayramı Ekle
            </Button>
          </div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4">
          <h3 className="mb-1 text-sm font-semibold text-slate-800">
            Şirkete özel gün
          </h3>
          <p className="mb-3 text-xs text-slate-500">
            İdari izin, şantiye kapanışı gibi günler.
          </p>

          <div className="flex flex-col gap-2">
            <Input
              type="date"
              value={customDate}
              onChange={(event) => setCustomDate(event.target.value)}
            />
            <Input
              placeholder="Açıklama"
              value={customName}
              onChange={(event) => setCustomName(event.target.value)}
            />
            <label className="flex items-center gap-2 text-xs text-slate-600">
              <input
                type="checkbox"
                checked={customHalf}
                onChange={(event) => setCustomHalf(event.target.checked)}
              />
              Yarım gün
            </label>
            <Button
              variant="secondary"
              disabled={busy || !customDate || !customName.trim()}
              onClick={() =>
                run(async () => {
                  const result = await holidayCalendarService.addDay(
                    companyId,
                    year,
                    {
                      date: customDate,
                      name: customName.trim(),
                      isHalfDay: customHalf,
                    }
                  );

                  setCustomDate("");
                  setCustomName("");
                  setCustomHalf(false);
                  return result.message;
                })
              }
            >
              Günü Ekle
            </Button>
          </div>
        </div>
      </div>

      <div className="mb-4 rounded-xl border border-slate-200 bg-white p-4">
        <h3 className="mb-2 text-sm font-semibold text-slate-800">
          Çalışma haftası
        </h3>
        <p className="mb-3 text-xs text-slate-500">
          Şantiyede genelde cumartesi çalışılır, merkez kadrosunda
          çalışılmaz. Ofise cumartesi yazmak gün ve mesai sayısını şişirir.
          Kişiye özel istisna personel kartından tanımlanır.
        </p>

        <div className="flex flex-wrap items-end gap-3">
          <div className="w-56">
            <Select
              label="Şirket geneli (şantiye)"
              value={String(data?.workWeek ?? WORK_WEEK.MondayToSaturday)}
              onChange={(event) =>
                run(async () => {
                  const result = await holidayCalendarService.updateWorkWeek(
                    companyId,
                    year,
                    { workWeek: Number(event.target.value) }
                  );
                  return result.message;
                })
              }
              options={[
                { value: String(WORK_WEEK.MondayToSaturday), label: "Pazartesi–Cumartesi" },
                { value: String(WORK_WEEK.MondayToFriday), label: "Pazartesi–Cuma" },
                { value: String(WORK_WEEK.AllDays), label: "Her gün" },
              ]}
            />
          </div>

          <div className="w-56">
            <Select
              label="Merkez kadrosu"
              value={String(data?.headOfficeWorkWeek ?? "")}
              onChange={(event) =>
                run(async () => {
                  const raw = event.target.value;

                  const result = await holidayCalendarService.updateWorkWeek(
                    companyId,
                    year,
                    { headOfficeWorkWeek: raw ? Number(raw) : null }
                  );
                  return result.message;
                })
              }
              options={[
                { value: "", label: "Şirket geneliyle aynı" },
                { value: String(WORK_WEEK.MondayToFriday), label: "Pazartesi–Cuma" },
                { value: String(WORK_WEEK.MondayToSaturday), label: "Pazartesi–Cumartesi" },
              ]}
            />
          </div>
        </div>
      </div>

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-6 text-sm text-slate-500">
          Yükleniyor...
        </div>
      ) : days.length === 0 ? (
        <EmptyState
          title={`${year} için takvim boş`}
          description="Sabit resmî tatilleri ekleyip dini bayram tarihlerini girin, sonra takvimi doğrulayın."
        />
      ) : (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Tarih</TableHead>
                <TableHead>Tatil</TableHead>
                <TableHead>Süre</TableHead>
                <TableHead className="text-right">İşlem</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {days.map((day) => (
                <TableRow key={day.id}>
                  <TableCell className="whitespace-nowrap">
                    {formatDate(day.date)}
                  </TableCell>
                  <TableCell>{day.name}</TableCell>
                  <TableCell>
                    {day.isHalfDay ? (
                      <Badge variant="warning">Yarım gün</Badge>
                    ) : (
                      <Badge variant="info">Tam gün</Badge>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() =>
                        run(async () => {
                          const result = await holidayCalendarService.removeDay(
                            day.id
                          );
                          return result.message;
                        })
                      }
                    >
                      Kaldır
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          <div className="mt-4 flex items-center gap-3">
            <Button
              disabled={busy || data?.isVerified}
              onClick={() => setVerifyOpen(true)}
            >
              {data?.isVerified ? "Doğrulandı" : "Takvimi Doğrula"}
            </Button>

            <span className="text-xs text-slate-500">
              Doğrulamadan sonra yapılan her değişiklik doğrulamayı düşürür.
            </span>
          </div>
        </>
      )}
      <ConfirmDialog
        open={verifyOpen}
        title="Takvimi Doğrula"
        description={`${year} resmî tatil takvimi doğrulanmış olarak işaretlenecek. Doğrulamadan sonra yapılan her değişiklik doğrulamayı düşürür.`}
        confirmLabel="Takvimi Doğrula"
        showReason
        reasonLabel="Doğrulama notu (isteğe bağlı)"
        busy={busy}
        error={error}
        onCancel={() => setVerifyOpen(false)}
        onConfirm={(note) => {
          setVerifyOpen(false);
          void run(async () => {
            const result = await holidayCalendarService.verify(
              companyId,
              year,
              note.trim() || null
            );

            return result.message;
          });
        }}
      />
    </ErpShell>
  );
}
