"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { offerService, type OfferChain } from "@/services/offer.service";
import { currencyMoney } from "@/lib/format/turkish";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Zincirdeki her rakam SÖZLEŞMEYE GİREN rakam: teklif bedeli, icmal
 * toplamı, hakediş tutarı. Buradaki eski biçim hepsini kuruşsuz
 * basıyordu; kullanıcı aynı tutarı hakediş ekranında kuruşlu görüp
 * ikisinin tutmadığını sanıyordu.
 */
const money = currencyMoney;

/**
 * Teklifin iş zinciri: teklif → proje → icmal → hakediş.
 *
 * Bir kalemin fiyatı tartışıldığında hangi teklife dayandığını
 * göstermek için var. Yetkisi olmayan kullanıcıda (takip izni yok)
 * sessizce çizilmez — teklif detayının kendisi ayrı bir izinle
 * korunuyor ve zincir uçtan 403 dönerse boş panel göstermek kafa
 * karıştırırdı.
 */
export default function OfferChainPanel({ offerId }: { offerId: string }) {
  const [chain, setChain] = useState<OfferChain | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await offerService.getChain(offerId);
        if (!cancelled) setChain(result);
      } catch {
        if (!cancelled) setUnavailable(true);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [offerId]);

  if (unavailable || !chain) return null;

  const { project, boqs, progressPayments } = chain;

  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>İş Zinciri</h2>
        <small>teklif → proje → icmal → hakediş</small>
      </div>

      <div style={{ padding: "12px 16px", fontSize: 13 }}>
        {!project ? (
          <p style={{ margin: 0 }}>
            Bu teklif henüz bir projeye bağlanmadı.{" "}
            {chain.offer.status === 4
              ? "Kazanıldı olarak işaretlendi; sözleşme künyesini İş / Teklif Takibi ekranından girebilirsiniz."
              : "Kazanılan teklifler sözleşme künyesiyle proje doğurur."}
          </p>
        ) : (
          <>
            <p style={{ marginTop: 0 }}>
              <strong>Proje: </strong>
              <Link href={`/projeler/${project.id}`}>
                {project.code} — {project.name}
              </Link>
              {!project.bornFromThisOffer && (
                <span style={{ color: "#b45309" }}>
                  {" "}
                  (bu teklif projeye <strong>ek iş</strong> olarak bağlandı)
                </span>
              )}
            </p>

            <p style={{ margin: "0 0 8px" }}>
              Sözleşme No: <strong>{project.contractNumber ?? "—"}</strong>
              {" · "}
              Bedel:{" "}
              <strong>
                {project.contractAmount != null
                  ? money(project.contractAmount, project.currencyCode)
                  : "—"}
              </strong>
              {project.contractDate && (
                <>
                  {" · "}İmza:{" "}
                  {dateFormat.format(new Date(project.contractDate))}
                </>
              )}
            </p>

            {boqs.length > 0 && (
              <>
                <strong>İcmaller</strong>
                <ul style={{ margin: "4px 0 12px", paddingLeft: 18 }}>
                  {boqs.map((boq) => (
                    <li key={boq.id} style={{ marginBottom: 2 }}>
                      {boq.boqNumber} — {boq.name} · {boq.itemCount} kalem ·{" "}
                      {money(boq.totalAmount, project.currencyCode)}
                      {boq.fromThisOffer && (
                        <strong> · bu teklifden üretildi</strong>
                      )}
                    </li>
                  ))}
                </ul>
              </>
            )}

            {progressPayments.length > 0 ? (
              <>
                <strong>Hakedişler</strong>
                <ul style={{ margin: "4px 0 0", paddingLeft: 18 }}>
                  {progressPayments.map((payment) => (
                    <li key={payment.id} style={{ marginBottom: 2 }}>
                      {payment.periodNumber}. hakediş —{" "}
                      {payment.progressPaymentNumber} ·{" "}
                      {money(payment.currentAmount, payment.currencyCode)}
                      {" · kümülatif "}
                      {money(payment.cumulativeAmount, payment.currencyCode)}
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <p style={{ margin: 0 }}>Henüz hakediş düzenlenmedi.</p>
            )}
          </>
        )}
      </div>
    </section>
  );
}
