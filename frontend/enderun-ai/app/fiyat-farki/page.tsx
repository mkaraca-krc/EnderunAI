"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { decimal } from "@/lib/format/turkish";

import {
  priceDifferenceService,
  PriceDifferenceCalculationType,
  type PriceDifferenceIndexPeriod,
  type PriceDifferenceProfile,
} from "@/services/price-difference.service";

/**
 * Endeks katsayısı — sekiz haneye kadar, sondaki sıfırlar yazılmadan.
 *
 * SEKİZ HANE YALNIZCA GÖSTERİM İÇİN. Fiyat farkı hesabı backend'de
 * tam hassasiyette yapılıyor; burada kırpılan tek şey ekrandaki
 * basamak sayısı, hesaba giren değer değil.
 *
 * Sabit haneli biçim kullanılamaz: katsayıların çoğu 1,5 gibi kısa
 * sayılar ve "1,50000000" diye yazılsalardı tablo okunmaz olurdu.
 */
function decimal8(value: number) {
  return decimal(value, 8);
}

const calculationTypeLabels: Record<
  PriceDifferenceCalculationType,
  string
> = {
  [PriceDifferenceCalculationType.PublicContractFormula]:
    "Kamu Sözleşmesi Formülü",
  [PriceDifferenceCalculationType.FixedRate]:
    "Sabit Oran",
  [PriceDifferenceCalculationType.Manual]:
    "Manuel",
};

export default function PriceDifferencePage() {
  const [profiles, setProfiles] = useState<
    PriceDifferenceProfile[]
  >([]);

  const [indexes, setIndexes] = useState<
    PriceDifferenceIndexPeriod[]
  >([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      const [profileResult, indexResult] =
        await Promise.all([
          priceDifferenceService.getProfiles(),
          priceDifferenceService.getIndexes(),
        ]);

      setProfiles(profileResult);
      setIndexes(indexResult);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Fiyat farkı verileri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const defaultProfileCount = useMemo(
    () => profiles.filter((x) => x.isDefault).length,
    [profiles]
  );

  const latestPeriod = useMemo(() => {
    if (indexes.length === 0) {
      return null;
    }

    return [...indexes].sort(
      (a, b) =>
        b.year * 100 +
        b.month -
        (a.year * 100 + a.month)
    )[0];
  }, [indexes]);

  return (
    <ErpShell
      design="redwood"
      title="Fiyat Farkı"
      description="Profil, endeks ve hakediş fiyat farkı yönetimi"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Fiyat Farkı Yönetimi</strong>
          <small>
            Katsayılar, dönem endeksleri ve hesaplama
            altyapısı
          </small>
        </div>

        <div className="erp-actions">

          <Link href="/fiyat-farki/profiller/yeni">
            Yeni Profil
          </Link>

          <Link href="/fiyat-farki/endeksler/yeni">
            Yeni Endeks
          </Link>

          <button
            type="button"
            disabled={loading}
            onClick={() => void load()}
          >
            Yenile
          </button>

          <Link href="/hakedis">
            Hakedişlere Git
          </Link>

        </div>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <Summary
            label="Profil Sayısı"
            value={String(profiles.length)}
          />

          <Summary
            label="Varsayılan Profil"
            value={String(defaultProfileCount)}
          />

          <Summary
            label="Endeks Dönemi"
            value={String(indexes.length)}
          />

          <Summary
            label="Son Endeks Dönemi"
            value={
              latestPeriod
                ? `${latestPeriod.month
                    .toString()
                    .padStart(2, "0")}/${latestPeriod.year}`
                : "—"
            }
          />
        </div>
      </div>

      {loading ? (
        <div
          className="erp-form-card"
          style={{ marginTop: 16 }}
        >
          Fiyat farkı verileri yükleniyor...
        </div>
      ) : (
        <>
          <div
            className="erp-table-card"
            style={{ marginTop: 16 }}
          >
            <div className="erp-toolbar">
              <div>
                <strong>Fiyat Farkı Profilleri</strong>
                <small>
                  {profiles.length} kayıt
                </small>
              </div>
            </div>

            <div style={{ overflowX: "auto" }}>
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Profil</th>
                    <th>Yöntem</th>
                    <th>Baz Dönem</th>
                    <th>Para Birimi</th>
                    <th>Katsayı Toplamı</th>
                    <th>Durum</th>
                  </tr>
                </thead>

                <tbody>
                  {profiles.length === 0 ? (
                    <tr>
                      <td colSpan={6}>
                        Henüz fiyat farkı profili
                        bulunmuyor.
                      </td>
                    </tr>
                  ) : (
                    profiles.map((profile) => (
                      <tr key={profile.id}>
                        <td>
                          <strong>
                            {profile.profileName}
                          </strong>

                          {profile.formulaName && (
                            <div>
                              <small>
                                {profile.formulaName}
                              </small>
                            </div>
                          )}
                        </td>

                        <td>
                          {
                            calculationTypeLabels[
                              profile.calculationType
                            ]
                          }
                        </td>

                        <td>
                          {profile.baseMonth
                            .toString()
                            .padStart(2, "0")}
                          /{profile.baseYear}
                        </td>

                        <td>
                          {profile.currencyCode}
                        </td>

                        <td>
                          {decimal8(
                            profile.coefficient.total
                          )}
                        </td>

                        <td>
                          {profile.isDefault ? (
                            <span className="erp-status green">
                              Varsayılan
                            </span>
                          ) : (
                            <span className="erp-status gray">
                              Standart
                            </span>
                          )}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          <div
            className="erp-table-card"
            style={{ marginTop: 16 }}
          >
            <div className="erp-toolbar">
              <div>
                <strong>Endeks Dönemleri</strong>
                <small>
                  {indexes.length} kayıt
                </small>
              </div>
            </div>

            <div style={{ overflowX: "auto" }}>
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Dönem</th>
                    <th>Kaynak</th>
                    <th>İşçilik</th>
                    <th>Akaryakıt</th>
                    <th>Malzeme</th>
                    <th>Makine</th>
                    <th>Çimento</th>
                    <th>Diğer</th>
                  </tr>
                </thead>

                <tbody>
                  {indexes.length === 0 ? (
                    <tr>
                      <td colSpan={8}>
                        Henüz endeks dönemi
                        bulunmuyor.
                      </td>
                    </tr>
                  ) : (
                    indexes.map((period) => (
                      <tr key={period.id}>
                        <td>
                          <strong>
                            {period.month
                              .toString()
                              .padStart(2, "0")}
                            /{period.year}
                          </strong>
                        </td>

                        <td>{period.sourceName}</td>

                        <td>
                          {decimal8(
                            period.laborIndex
                          )}
                        </td>

                        <td>
                          {decimal8(
                            period.fuelIndex
                          )}
                        </td>

                        <td>
                          {decimal8(
                            period.materialIndex
                          )}
                        </td>

                        <td>
                          {decimal8(
                            period.machineryIndex
                          )}
                        </td>

                        <td>
                          {decimal8(
                            period.cementIndex
                          )}
                        </td>

                        <td>
                          {decimal8(
                            period.otherIndex
                          )}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </ErpShell>
  );
}

function Summary({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div>
      <small>{label}</small>
      <div style={{ marginTop: 6 }}>
        <strong>{value}</strong>
      </div>
    </div>
  );
}
