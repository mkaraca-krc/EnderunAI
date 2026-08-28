"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { usePermissions } from "@/lib/use-permissions";
import { useRefreshable } from "@/lib/data/use-refreshable";
import { date, dateTime } from "@/lib/format/turkish";
import { companyService } from "@/services/company.service";
import {
  odemePlaniService,
  OdemePlaniDurumu,
  ODEME_PLANI_DURUM_ETIKETLERI,
  type PlanOzeti,
} from "@/services/odeme-plani.service";

/**
 * E1 — HAFTALIK ÖDEME PLANI LİSTESİ.
 *
 * Ekranın tek işi "hangi hafta hangi durumda" sorusuna bakışta cevap
 * vermek. TUTAR BURADA YOK: onaylanan tutar satır satır karara bağlı
 * ve haftanın toplamı yalnız onay bittikten sonra anlam taşıyor. Bir
 * "toplam" sütunu konsaydı taslak haftadaki öneri toplamını
 * onaylanmış gibi okuyan biri çıkardı.
 *
 * VERİ ÇEKME `useRefreshable` ÜZERİNDEN. Kanca uygulamanın tek
 * tazeleme mekanizması olarak yazılmıştı ama hiçbir ekran
 * kullanmıyordu (0 çağrı yeri). Kendi `load()` fonksiyonunu yazan her
 * ekran `set-state-in-effect` ihlali üretiyor ve lint çizgisi bu
 * yüzden 110 dosya taşıyor; bu iki ekran çizgiyi YÜKSELTMİYOR.
 *
 * ŞİRKET SEÇİMİ DURUMDA DEĞİL REF'TE. Kanca fetcher'ı ref'te
 * sabitliyor: parametre değişince kendiliğinden yeniden çekmez.
 * Seçim durumda tutulup bir efektle tazelenseydi, kaçınmak istediğimiz
 * ihlal geri gelirdi. Olay işleyicisinden `refresh()` çağırmak hem
 * doğru hem de kuralın dışında.
 */

const DURUM_RENGI: Record<number, string> = {
  [OdemePlaniDurumu.Taslak]: "gray",
  [OdemePlaniDurumu.Onayda]: "yellow",
  [OdemePlaniDurumu.Onaylandi]: "blue",
  [OdemePlaniDurumu.Uygulandi]: "green",
  [OdemePlaniDurumu.Kapandi]: "gray",
};

/** Plan haftası pazartesi başlar. */
function buHaftaninPazartesisi(): string {
  const bugun = new Date();
  const gun = bugun.getDay(); // 0 = pazar
  const geri = gun === 0 ? 6 : gun - 1;
  const pazartesi = new Date(
    Date.UTC(bugun.getFullYear(), bugun.getMonth(), bugun.getDate() - geri),
  );
  return pazartesi.toISOString().slice(0, 10);
}

export default function OdemePlanlariPage() {
  const { has } = usePermissions();
  const canPrepare = has("payment.plan.prepare");

  const seciliRef = useRef("");
  const [islemHatasi, setIslemHatasi] = useState("");

  const { data, loading, error, lastUpdatedAt, refresh, mutate } =
    useRefreshable(async () => {
      const companies = await companyService.getAll();
      const secili = seciliRef.current || companies[0]?.id || "";
      seciliRef.current = secili;

      const planlar = secili
        ? await odemePlaniService.listele(secili)
        : [];

      return { companies, secili, planlar };
    });

  const sirketDegistir = (id: string) => {
    seciliRef.current = id;
    void refresh();
  };

  const taslakOlustur = async () => {
    setIslemHatasi("");
    try {
      await mutate(() =>
        odemePlaniService.taslakOlustur(
          seciliRef.current,
          buHaftaninPazartesisi(),
        ),
      );
    } catch (e) {
      /*
       * "Bu haftanın planı zaten var" sunucudan gelen NORMAL bir cevap
       * — haftalık teklik kuralı orada. Mesaj olduğu gibi gösteriliyor;
       * burada ikinci bir kontrol yazılsaydı sunucununkiyle ayrışırdı.
       */
      setIslemHatasi(
        e instanceof Error ? e.message : "Taslak oluşturulamadı.",
      );
    }
  };

  const planlar = data?.planlar ?? [];
  /*
   * SÜTUNLAR — ham <table> DEĞİL, DataTable.
   *
   * Standart bileşen sayfalama, yazdırma ve dışa aktarmayı getiriyor;
   * ham tablo yazan her yeni ekran taşınma borcunu büyütüyor
   * (tests/list-component-ratchet.test.ts).
   *
   * `value` her sütunda AYRI: dışa aktarmaya rozet değil düz metin
   * gitmeli — "Onayda" yazmalı, renkli bir etiket değil.
   */
  const sutunlar = useMemo<DataTableColumn<PlanOzeti>[]>(
    () => [
      {
        key: "hafta",
        header: "Hafta",
        value: (p) => date(p.haftaBaslangici),
        render: (p) => (
          <>
            <strong>{date(p.haftaBaslangici)}</strong>
            <small>haftası</small>
          </>
        ),
      },
      {
        key: "odemeGunu",
        header: "Ödeme Günü",
        value: (p) => date(p.odemeGunu),
      },
      {
        key: "durum",
        header: "Durum",
        value: (p) => ODEME_PLANI_DURUM_ETIKETLERI[p.durum],
        render: (p) => (
          <span className={`erp-status ${DURUM_RENGI[p.durum] ?? "gray"}`}>
            {ODEME_PLANI_DURUM_ETIKETLERI[p.durum]}
          </span>
        ),
      },
      {
        key: "satirSayisi",
        header: "Satır",
        numeric: true,
        align: "right",
        value: (p) => p.satirSayisi,
      },
      {
        key: "bekleyenSatir",
        header: "Karar Bekleyen",
        numeric: true,
        align: "right",
        value: (p) => p.bekleyenSatir,
        render: (p) =>
          p.bekleyenSatir > 0 ? <strong>{p.bekleyenSatir}</strong> : "—",
      },
      {
        key: "ac",
        header: "",
        // Eylem sütunu çıktıya girmez.
        value: () => "",
        align: "right",
        render: (p) => (
          <Link
            className="erp-secondary-button"
            href={`/finans/odeme-planlari/${p.id}`}
          >
            Aç
          </Link>
        ),
      },
    ],
    [],
  );


  return (
    <ErpShell
      design="redwood"
      title="Ödeme Planları"
      description="Haftalık ödeme planı — hazırlama, onay ve uygulama"
    >
      <div className="erp-toolbar">
        <select
          value={data?.secili ?? ""}
          onChange={(e) => sirketDegistir(e.target.value)}
        >
          {(data?.companies ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>

        <div className="rw-toolbar-end">
          {lastUpdatedAt && (
            <small>Son güncelleme {dateTime(lastUpdatedAt)}</small>
          )}
          <Button variant="secondary" disabled={loading} onClick={() => void refresh()}>
            Yenile
          </Button>
          {canPrepare && (
            <Button
              disabled={loading || !data?.secili}
              onClick={() => void taslakOlustur()}
            >
              Bu Haftanın Taslağı
            </Button>
          )}
        </div>
      </div>

      {(error || islemHatasi) && (
        <div className="erp-alert error">{islemHatasi || error}</div>
      )}

      <DataTable
        title="Haftalar"
        rows={planlar}
        columns={sutunlar}
        rowKey={(p) => p.id}
        loading={loading}
        resetKey={data?.secili ?? ""}
        emptyText={
          "Bu şirket için henüz ödeme planı açılmamış. Pazartesi sabahı " +
          "taslak kendiliğinden oluşur; beklemek istemiyorsanız " +
          "\u201CBu Haftanın Taslağı\u201D ile şimdi açabilirsiniz."
        }
      />

    </ErpShell>
  );
}
