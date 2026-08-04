"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { Fragment, useCallback, useEffect, useState } from "react";

import {
  progressPaymentService,
  ProgressPaymentPaymentType,
  type ProgressPaymentDetail,
} from "@/services/progress-payment.service";
import {
  companySettingsService,
  type CompanySettings,
} from "@/services/company-settings.service";

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "-";
}

function formatNumber(value: number, digits = 2) {
  return new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value);
}

/**
 * Tutarı Türkçe yazıya çevirir. Sunucudaki TurkishNumberToWords ile aynı
 * kuralları uygular: "biryüz"/"birbin" denmez, "birmilyon" denir.
 */
const ONES = ["", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz"];
const TENS = ["", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan"];
const GROUPS = ["", "bin", "milyon", "milyar", "trilyon"];

function convertGroup(value: number) {
  let text = "";
  const hundreds = Math.floor(value / 100);
  const remainder = value % 100;

  if (hundreds > 0) {
    if (hundreds > 1) text += ONES[hundreds];
    text += "yüz";
  }

  text += TENS[Math.floor(remainder / 10)];
  text += ONES[remainder % 10];

  return text;
}

function convertWhole(value: number) {
  if (value === 0) return "";

  const groups: number[] = [];
  let rest = value;

  while (rest > 0) {
    groups.push(rest % 1000);
    rest = Math.floor(rest / 1000);
  }

  let text = "";

  for (let index = groups.length - 1; index >= 0; index--) {
    const group = groups[index];
    if (group === 0) continue;

    // "birbin" değil "bin"; ama "birmilyon" doğru.
    if (index === 1 && group === 1) {
      text += GROUPS[index];
      continue;
    }

    text += convertGroup(group) + GROUPS[index];
  }

  return text;
}

function amountInWords(amount: number) {
  const negative = amount < 0;
  const value = Math.abs(Math.round(amount * 100) / 100);
  const whole = Math.trunc(value);
  const cents = Math.round((value - whole) * 100);

  let text = negative ? "eksi " : "";
  text += whole === 0 ? "sıfır" : convertWhole(whole);
  text += " TL";

  if (cents > 0) text += ` ${convertWhole(cents)} Kr`;

  return text;
}

/**
 * NATURA formatında hakediş çıktısı: logo antetli başlık, bölüm bazlı
 * imalat icmali, ihzarat, alt kalemli kesinti icmali, üst hesap, üç
 * parçalı ödeme dağılımı, yazı ile tutar ve imza blokları.
 *
 * PDF, tarayıcının yazdırma penceresinden alınır — ayrı bir PDF
 * kütüphanesi eklenmedi.
 */
export default function HakedisPrintPage() {
  const params = useParams<{ id: string }>();

  const [payment, setPayment] = useState<ProgressPaymentDetail | null>(null);
  const [company, setCompany] = useState<CompanySettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const [detail, companyResult] = await Promise.all([
        progressPaymentService.getById(params.id),
        companySettingsService.get().catch(() => null),
      ]);

      setPayment(detail);
      setCompany(companyResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hakediş belgesi yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <main className="min-h-screen bg-slate-100 p-8">
        <div className="mx-auto max-w-5xl rounded-lg bg-white p-12 text-center text-sm text-slate-500">
          Hakediş belgesi hazırlanıyor...
        </div>
      </main>
    );
  }

  if (error || !payment) {
    return (
      <main className="min-h-screen bg-slate-100 p-8">
        <div className="mx-auto max-w-5xl rounded-lg border border-red-200 bg-red-50 p-8 text-red-700">
          {error || "Hakediş bulunamadı."}
        </div>
      </main>
    );
  }

  const sectionsById = new Map(payment.sections.map((x) => [x.id, x]));

  return (
    <main className="min-h-screen bg-slate-100 py-8 print:bg-white print:py-0">
      <div className="mx-auto mb-5 flex max-w-[297mm] justify-between px-2 print:hidden">
        <Link
          href={`/hakedis/${payment.id}`}
          className="inline-flex h-10 items-center rounded-lg border border-slate-300 bg-white px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          Hakedişe Dön
        </Link>

        <button
          type="button"
          onClick={() => window.print()}
          className="inline-flex h-10 items-center rounded-lg bg-slate-900 px-5 text-sm font-medium text-white hover:bg-slate-800"
        >
          PDF Kaydet / Yazdır
        </button>
      </div>

      {/* Hakediş tablosu geniş olduğu için yatay A4. */}
      <article className="mx-auto w-[297mm] bg-white px-[12mm] py-[10mm] text-[10px] text-slate-900 shadow-xl print:w-full print:px-[8mm] print:py-[6mm] print:shadow-none">
        <header className="border-b-2 border-slate-900 pb-4">
          <div className="flex items-start justify-between gap-8">
            <div className="flex items-start gap-3">
              {company?.logoUrl && (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={company.logoUrl}
                  alt=""
                  className="h-14 w-14 shrink-0 object-contain"
                />
              )}
              <div>
                <h1 className="text-xl font-bold tracking-wide">
                  {(company?.tradeName || company?.name || "ENDERUN ENERJİ").toLocaleUpperCase(
                    "tr-TR"
                  )}
                </h1>
                {company?.address && (
                  <p className="mt-0.5 max-w-[110mm] text-[9px] leading-4 text-slate-600">
                    {company.address}
                  </p>
                )}
              </div>
            </div>

            <div className="text-right">
              <h2 className="text-lg font-bold">HAKEDİŞ RAPORU</h2>
              <p className="mt-1 text-[11px] font-bold">
                {payment.progressPaymentNumber}
              </p>
              <p className="text-[10px] text-slate-600">
                {payment.periodNumber}. Hakediş
              </p>
            </div>
          </div>

          <dl className="mt-4 grid grid-cols-4 gap-x-6 gap-y-1">
            <Field label="Proje" value={`${payment.projectCode} — ${payment.projectName}`} />
            <Field label="Tanzim Tarihi" value={formatDate(payment.progressPaymentDate)} />
            <Field
              label="Dönem"
              value={
                payment.periodStartDate && payment.periodEndDate
                  ? `${formatDate(payment.periodStartDate)} - ${formatDate(payment.periodEndDate)}`
                  : "-"
              }
            />
            <Field label="Para Birimi" value={payment.currencyCode} />
          </dl>
        </header>

        {/* --- İMALAT İCMALİ --- */}
        {payment.sections.length > 0 && (
          <Section title="İMALAT İCMALİ">
            <table className="w-full border-collapse">
              <thead>
                <tr className="bg-slate-100 text-left">
                  <Th>Bölüm</Th>
                  <Th right>Malzeme</Th>
                  <Th right>Montaj</Th>
                  <Th right>GG&amp;K</Th>
                  <Th right>Bu Hakediş</Th>
                  <Th right>Genel Toplam</Th>
                </tr>
              </thead>
              <tbody>
                {payment.sections.map((section) => (
                  <tr key={section.id} className="border-b border-slate-200">
                    <Td>{section.name}</Td>
                    <Td right>{formatNumber(section.materialAmount)}</Td>
                    <Td right>{formatNumber(section.laborAmount)}</Td>
                    <Td right>{formatNumber(section.overheadAmount)}</Td>
                    <Td right>{formatNumber(section.currentAmount)}</Td>
                    <Td right>{formatNumber(section.cumulativeAmount)}</Td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-slate-900 font-bold">
                  <Td>TOPLAM</Td>
                  <Td right>
                    {formatNumber(
                      payment.sections.reduce((sum, x) => sum + x.materialAmount, 0)
                    )}
                  </Td>
                  <Td right>
                    {formatNumber(
                      payment.sections.reduce((sum, x) => sum + x.laborAmount, 0)
                    )}
                  </Td>
                  <Td right>
                    {formatNumber(
                      payment.sections.reduce((sum, x) => sum + x.overheadAmount, 0)
                    )}
                  </Td>
                  <Td right>
                    {formatNumber(
                      payment.sections.reduce((sum, x) => sum + x.currentAmount, 0)
                    )}
                  </Td>
                  <Td right>
                    {formatNumber(
                      payment.sections.reduce((sum, x) => sum + x.cumulativeAmount, 0)
                    )}
                  </Td>
                </tr>
              </tfoot>
            </table>
          </Section>
        )}

        {/* --- POZ DETAYI --- */}
        <Section title="POZ DETAYI">
          <table className="w-full border-collapse">
            <thead>
              <tr className="bg-slate-100 text-left">
                <Th>Poz</Th>
                <Th>Açıklama</Th>
                <Th>Br.</Th>
                <Th right>Sözleşme</Th>
                <Th right>Önceki</Th>
                <Th right>Bu Dönem</Th>
                <Th right>Toplam</Th>
                <Th right>B. Fiyat</Th>
                <Th right>Tutar</Th>
                <Th right>%</Th>
              </tr>
            </thead>
            <tbody>
              {payment.items.map((item) => (
                <tr key={item.id} className="border-b border-slate-200">
                  <Td>{item.positionCode}</Td>
                  <Td>
                    {item.description}
                    {item.progressPaymentSectionId && (
                      <span className="text-slate-500">
                        {" "}
                        ({sectionsById.get(item.progressPaymentSectionId)?.name})
                      </span>
                    )}
                  </Td>
                  <Td>{item.unit}</Td>
                  <Td right>{formatNumber(item.contractQuantity, 2)}</Td>
                  <Td right>{formatNumber(item.previousQuantity, 2)}</Td>
                  <Td right>{formatNumber(item.currentQuantity, 2)}</Td>
                  <Td right>{formatNumber(item.cumulativeQuantity, 2)}</Td>
                  <Td right>{formatNumber(item.unitPrice)}</Td>
                  <Td right>{formatNumber(item.currentAmount)}</Td>
                  <Td right>{formatNumber(item.completionRate, 1)}</Td>
                </tr>
              ))}
            </tbody>
          </table>
        </Section>

        {/* --- İHZARAT --- */}
        {payment.advanceMaterials.length > 0 && (
          <Section title="İHZARAT">
            <table className="w-full border-collapse">
              <thead>
                <tr className="bg-slate-100 text-left">
                  <Th>Poz</Th>
                  <Th>Açıklama</Th>
                  <Th right>Miktar</Th>
                  <Th right>B. Fiyat</Th>
                  <Th right>Bedel. %</Th>
                  <Th right>Tutar</Th>
                  <Th right>Mahsup</Th>
                  <Th right>Açık Bakiye</Th>
                </tr>
              </thead>
              <tbody>
                {payment.advanceMaterials.map((item) => (
                  <tr key={item.id} className="border-b border-slate-200">
                    <Td>{item.positionCode}</Td>
                    <Td>{item.description}</Td>
                    <Td right>{formatNumber(item.quantity, 2)}</Td>
                    <Td right>{formatNumber(item.unitPrice)}</Td>
                    <Td right>{formatNumber(item.valuationRate, 0)}</Td>
                    <Td right>{formatNumber(item.amount)}</Td>
                    <Td right>{formatNumber(item.offsetAmount)}</Td>
                    <Td right>{formatNumber(item.openAmount)}</Td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Section>
        )}

        {/* --- KESİNTİ İCMALİ --- */}
        {payment.deductions.length > 0 && (
          <Section title="KESİNTİ İCMALİ">
            <table className="w-full border-collapse">
              <thead>
                <tr className="bg-slate-100 text-left">
                  <Th>Kesinti</Th>
                  <Th right>Oran %</Th>
                  <Th right>Küm. Taban</Th>
                  <Th right>Önceden Kesilen</Th>
                  <Th right>Bu Hakediş</Th>
                  <Th right>Kümülatif</Th>
                </tr>
              </thead>
              <tbody>
                {payment.deductions.map((deduction) => (
                  <Fragment key={deduction.id}>
                    <tr className="border-b border-slate-200 font-bold">
                      <Td>{deduction.description}</Td>
                      <Td right>{formatNumber(deduction.rate, 2)}</Td>
                      <Td right>{formatNumber(deduction.cumulativeBaseAmount)}</Td>
                      <Td right>{formatNumber(deduction.previousAmount)}</Td>
                      <Td right>{formatNumber(deduction.amount)}</Td>
                      <Td right>{formatNumber(deduction.cumulativeAmount)}</Td>
                    </tr>
                    {deduction.lines.map((line) => (
                      <tr key={line.id} className="border-b border-slate-100 text-slate-600">
                        <Td>
                          <span className="pl-4">
                            {line.name} — {formatNumber(line.quantity, 0)} ×{" "}
                            {formatNumber(line.unitPrice)} (KDV %
                            {formatNumber(line.vatRate, 0)})
                          </span>
                        </Td>
                        <Td right>-</Td>
                        <Td right>-</Td>
                        <Td right>-</Td>
                        <Td right>{formatNumber(line.grossAmount)}</Td>
                        <Td right>-</Td>
                      </tr>
                    ))}
                  </Fragment>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-slate-900 font-bold">
                  <Td>TOPLAM KESİNTİ</Td>
                  <Td right>-</Td>
                  <Td right>-</Td>
                  <Td right>-</Td>
                  <Td right>{formatNumber(payment.totalDeductionAmount)}</Td>
                  <Td right>-</Td>
                </tr>
              </tfoot>
            </table>
          </Section>
        )}

        {/* --- ÜST HESAP --- */}
        <Section title="ÜST HESAP">
          <div className="ml-auto w-[110mm]">
            <Line label="Kümülatif İmalat" value={payment.cumulativeWorkAmount} />
            <Line label="Açık İhzarat" value={payment.cumulativeAdvanceMaterialAmount} />
            <Line label="Kümülatif Toplam" value={payment.cumulativeAmount} bold />
            <Line label="Önceki Hakedişler (Minha)" value={-payment.previousAmount} />
            <Line label="Bu Hakediş" value={payment.currentAmount} bold />
            {payment.priceDifferenceAmount !== 0 && (
              <Line label="Fiyat Farkı" value={payment.priceDifferenceAmount} />
            )}
            <Line
              label={`KDV (%${formatNumber(payment.vatRate, 0)})`}
              value={payment.vatAmount}
            />
            <Line label="Brüt Tutar" value={payment.grossPayableAmount} bold />
            {payment.withholdingAmount > 0 && (
              <Line
                label={`KDV Tevkifatı (${payment.withholdingNumerator}/${payment.withholdingDenominator})`}
                value={-payment.withholdingAmount}
              />
            )}
            {payment.incomeTaxWithholdingAmount > 0 && (
              <Line
                label={`Stopaj (%${formatNumber(payment.incomeTaxWithholdingRate, 2)})`}
                value={-payment.incomeTaxWithholdingAmount}
              />
            )}
            <Line label="Kesintiler" value={-payment.totalDeductionAmount} />
            <div className="mt-1 border-t-2 border-slate-900 pt-1">
              <Line label="TAHSİL EDİLECEK" value={payment.netPayableAmount} bold />
            </div>
          </div>

          <p className="mt-3 border border-slate-300 bg-slate-50 px-3 py-2">
            <span className="font-bold">Yazı ile: </span>
            <span className="capitalize">{amountInWords(payment.netPayableAmount)}</span>
          </p>
        </Section>

        {/* --- ÖDEME DAĞILIMI --- */}
        {payment.paymentPlans.length > 0 && (
          <Section title="ÖDEME DAĞILIMI">
            <table className="w-full border-collapse">
              <thead>
                <tr className="bg-slate-100 text-left">
                  <Th>Ödeme Şekli</Th>
                  <Th right>Oran %</Th>
                  <Th right>Tutar</Th>
                  <Th>Vade</Th>
                  <Th>Çek No</Th>
                </tr>
              </thead>
              <tbody>
                {payment.paymentPlans.map((plan) => (
                  <tr key={plan.id} className="border-b border-slate-200">
                    <Td>
                      {plan.paymentType === ProgressPaymentPaymentType.Cash
                        ? "Nakit"
                        : `Vadeli Çek (${plan.maturityDays} gün)`}
                    </Td>
                    <Td right>{formatNumber(plan.rate, 2)}</Td>
                    <Td right>{formatNumber(plan.amount)}</Td>
                    <Td>{plan.dueDate ? formatDate(plan.dueDate) : "-"}</Td>
                    <Td>{plan.chequeNumber ?? "-"}</Td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Section>
        )}

        <footer className="mt-10 grid grid-cols-3 gap-8 break-inside-avoid">
          {["Hazırlayan", "Kontrol Eden", "Onaylayan"].map((role) => (
            <div key={role} className="text-center">
              <div className="h-16 border-b border-slate-400" />
              <p className="mt-1 font-bold">{role}</p>
              <p className="text-[9px] text-slate-500">Ad Soyad / İmza</p>
            </div>
          ))}
        </footer>
      </article>
    </main>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[9px] uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="font-bold">{value}</dd>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mt-5 break-inside-avoid">
      <h3 className="mb-1 border-b border-slate-400 pb-0.5 text-[11px] font-bold tracking-wide">
        {title}
      </h3>
      {children}
    </section>
  );
}

function Th({ children, right }: { children: React.ReactNode; right?: boolean }) {
  return (
    <th
      className={`border-b border-slate-300 px-1.5 py-1 text-[9px] font-bold uppercase ${
        right ? "text-right" : "text-left"
      }`}
    >
      {children}
    </th>
  );
}

function Td({ children, right }: { children: React.ReactNode; right?: boolean }) {
  return (
    <td
      className={`px-1.5 py-1 ${right ? "text-right tabular-nums" : "text-left"}`}
    >
      {children}
    </td>
  );
}

function Line({
  label,
  value,
  bold,
}: {
  label: string;
  value: number;
  bold?: boolean;
}) {
  return (
    <div className={`flex justify-between py-0.5 ${bold ? "font-bold" : ""}`}>
      <span>{label}</span>
      <span className="tabular-nums">{formatNumber(value)}</span>
    </div>
  );
}
