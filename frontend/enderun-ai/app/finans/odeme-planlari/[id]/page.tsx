"use client";

import { useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { Button, SearchableSelect, TutarInput } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { useRefreshable } from "@/lib/data/use-refreshable";
import { money, date, dateTime } from "@/lib/format/turkish";
import { kasaHesapEtiketi } from "@/lib/finans/kasa-hesap-etiketi";
import {
  cashAccountService,
  type CashAccount,
} from "@/services/cash-account.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  odemePlaniService,
  OdemePlaniDurumu,
  OdemeSatirKarari,
  OdemeYontemi,
  OdemeSatirOdemeDurumu,
  BakiyeKaynagi,
  ODEME_PLANI_DURUM_ETIKETLERI,
  ODEME_KARAR_ETIKETLERI,
  ODEME_YONTEM_ETIKETLERI,
  ODEME_DURUM_ETIKETLERI,
  type PlanDetayi,
  type SatirOzeti,
  type ButceOzeti,
  type SatirIstegi,
} from "@/services/odeme-plani.service";

/**
 * E2/E3/E4 — ÖDEME PLANI DETAYI.
 *
 * TEK EKRAN, ÜÇ KİP. Hazırlama, onay ve uygulama ayrı sayfalara
 * bölünmedi: plan tek bir nesne ve haftanın içinde durumdan duruma
 * geçiyor. Ayrı yollara bölünseydi kullanıcının "bu hafta hangi
 * adreste" diye bilmesi gerekirdi; üstelik onaydaki bir planda satır
 * düzeltmek (D1) hazırlama kipine geri dönmeyi gerektirirdi.
 *
 * Kip DURUMDAN ve İZİNDEN birlikte çıkar. İzni olmayan yalnız
 * düğmeyi görmez; asıl kapı uçlarda (RequirePermission).
 */

const DURUM_RENGI: Record<number, string> = {
  [OdemePlaniDurumu.Taslak]: "gray",
  [OdemePlaniDurumu.Onayda]: "yellow",
  [OdemePlaniDurumu.Onaylandi]: "blue",
  [OdemePlaniDurumu.Uygulandi]: "green",
  [OdemePlaniDurumu.Kapandi]: "gray",
};

const KARAR_RENGI: Record<number, string> = {
  [OdemeSatirKarari.Bekliyor]: "gray",
  [OdemeSatirKarari.Onaylandi]: "green",
  [OdemeSatirKarari.Reddedildi]: "red",
  [OdemeSatirKarari.Kismi]: "yellow",
};

const AY_ADLARI = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

type SatirFormu = {
  currentAccountId: string;
  tutar: number | null;
  yontem: number;
  cekVadesi: string;
  oncelik: number;
  cashAccountId: string;
  aciklama: string;
};

const BOS_FORM: SatirFormu = {
  currentAccountId: "",
  tutar: null,
  yontem: OdemeYontemi.HavaleEft,
  cekVadesi: "",
  oncelik: 3,
  cashAccountId: "",
  aciklama: "",
};

function formdanIstek(form: SatirFormu): SatirIstegi {
  return {
    currentAccountId: form.currentAccountId,
    tutar: form.tutar ?? 0,
    yontem: form.yontem,
    cekVadesi: form.yontem === OdemeYontemi.Cek && form.cekVadesi
      ? form.cekVadesi
      : null,
    oncelik: form.oncelik,
    cashAccountId: form.yontem === OdemeYontemi.Cek
      ? null
      : form.cashAccountId || null,
    aciklama: form.aciklama.trim() || null,
  };
}

/**
 * K6 — İKİ AYRI SAYI, TOPLANMAZ.
 *
 * Üstte bu cuma hesaptan ÇIKACAK nakit, altta bu cuma YARATILAN
 * gelecek çek borcu. Tek bir "haftanın toplamı" satırı YOK ve
 * bilerek yok: çek bu hafta para çıkarmıyor, ama hafta bittiğinde
 * borç duruyor. Toplansalardı hafta olduğundan pahalı görünür,
 * gerçek nakit ihtiyacı bu şişkinliğin içinde kaybolurdu.
 *
 * K9 — YETMEZLİK UYARISI, ENGEL DEĞİL. Fark eksiyse ekran açıkça
 * söyler; GM yine onaylayabilir ama görmeden onaylamış olmaz.
 */
function ButcePaneli({
  butce,
  hesaplar,
}: {
  butce: ButceOzeti;
  hesaplar: CashAccount[];
}) {
  const yetmeyen = butce.hesapBazindaNakit.filter((h) => h.fark < 0);

  const hesapAdi = (id: string) => {
    const hesap = hesaplar.find((h) => h.id === id);
    return hesap ? kasaHesapEtiketi(hesap) : "Hesap seçilmemiş";
  };

  return (
    <>
      {yetmeyen.length > 0 && (
        <div className="erp-alert error">
          <strong>Bakiye yetmiyor.</strong>{" "}
          {yetmeyen.length === 1
            ? `${hesapAdi(yetmeyen[0].cashAccountId)} hesabında ${money(
                Math.abs(yetmeyen[0].fark),
              )} açık var.`
            : `${yetmeyen.length} hesapta toplam ${money(
                yetmeyen.reduce((t, h) => t + Math.abs(h.fark), 0),
              )} açık var.`}{" "}
          Onaylamak engellenmiyor — kararı görerek verin.
        </div>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Bu Cuma Çıkacak Nakit</h2>
          <small>Havale/EFT ve nakit — hesap bazında</small>
        </div>

        {butce.hesapBazindaNakit.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Onaylanmış nakit ödeme yok</strong>
            <p>Satırlar onaylandıkça bu tablo dolar.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Hesap</th>
                  <th className="num">Çıkacak</th>
                  <th className="num">Gösterilen Bakiye</th>
                  <th className="num">Fark</th>
                </tr>
              </thead>
              <tbody>
                {butce.hesapBazindaNakit.map((h) => (
                  <tr key={h.cashAccountId}>
                    <td>
                      {hesapAdi(h.cashAccountId)}
                      {h.bakiyeKaynagi === BakiyeKaynagi.ElleGirildi && (
                        <small>bakiye elle girildi</small>
                      )}
                    </td>
                    <td className="num">{money(h.nakitCikis)}</td>
                    <td className="num">{money(h.gosterilenBakiye)}</td>
                    <td className="num">
                      <strong>{money(h.fark)}</strong>
                      {h.fark < 0 && <small>açık</small>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Bu Hafta Yaratılan Gelecek Yükümlülük</h2>
          <small>Çek — vade ayına göre</small>
        </div>

        {butce.gelecekYukumlulukler.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Onaylanmış çek yok</strong>
            <p>Bu hafta ileri vadeli borç yaratılmıyor.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Vade Ayı</th>
                  <th className="num">Tutar</th>
                </tr>
              </thead>
              <tbody>
                {butce.gelecekYukumlulukler.map((v) => (
                  <tr key={`${v.yil}-${v.ay}`}>
                    <td>
                      {AY_ADLARI[v.ay - 1]} {v.yil}
                    </td>
                    <td className="num">{money(v.tutar)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}

export default function OdemePlaniDetayPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const planId = params.id;

  const { has } = usePermissions();
  const canPrepare = has("payment.plan.prepare");
  const canApprove = has("payment.plan.approve");

  const [islemHatasi, setIslemHatasi] = useState("");
  const [islemde, setIslemde] = useState(false);

  const [duzenlenen, setDuzenlenen] = useState<string | null>(null);
  const [form, setForm] = useState<SatirFormu>(BOS_FORM);
  const [ekleniyor, setEkleniyor] = useState(false);

  /**
   * TEK ÇEKİM, TEK TAZELEME.
   *
   * Plan, cari listesi ve kasa hesapları tek fetcher'da toplandı;
   * ekran kendi `load()` fonksiyonunu yazmıyor. Sebebi yalnız
   * derli topluluk değil: efektten veri çekip durum yazan her ekran
   * `set-state-in-effect` ihlali üretiyor ve lint çizgisi bu yüzden
   * 110 dosya taşıyor. `useRefreshable` bu iş için yazılmıştı ama
   * hiçbir ekran kullanmıyordu.
   *
   * D4 — K9 HER ONAY İŞLEMİNDEN SONRA YENİDEN HESAPLANIR. `mutate`
   * yalnız BAŞARILI işlemden sonra planı sunucudan yeniden çekiyor;
   * bütçe de planla birlikte geliyor. İstemcide toplansaydı K3'ün
   * geçici retleri, eşzamanlı başka bir karar ya da K2'nin düşürdüğü
   * onay ekrana yansımaz, GM bayat bir farka bakarak onay verirdi.
   */
  const { data, loading, error, lastUpdatedAt, refresh, mutate } =
    useRefreshable(async () => {
      const [detay, cariler, hesaplar] = await Promise.all([
        odemePlaniService.detay(planId),
        currentAccountService.getAll(),
        cashAccountService.getAll({}),
      ]);

      return { detay, cariler, hesaplar };
    });

  const plan: PlanDetayi | null = data?.detay ?? null;
  const butce: ButceOzeti | null = data?.detay.butce ?? null;
  const cariler: CurrentAccountListItem[] = data?.cariler ?? [];
  const hesaplar: CashAccount[] = data?.hesaplar ?? [];

  const durum = plan?.durum ?? OdemePlaniDurumu.Taslak;

  /* D2 — EKLEME/SİLME YALNIZ TASLAKTA. */
  const eklemeSilmeAcik = canPrepare && durum === OdemePlaniDurumu.Taslak;

  /* D1 — DÜZENLEME KAPANMIŞ PLAN DIŞINDA SERBEST. */
  const duzenlemeAcik = canPrepare && durum !== OdemePlaniDurumu.Kapandi;

  /* E3 — karar verme yalnız onaydaki planda ve yalnız onay izniyle. */
  const kararAcik = canApprove && durum === OdemePlaniDurumu.Onayda;

  /* E4 — ödeme kaydı onaylanmış plandan sonra. */
  const odemeAcik =
    canPrepare &&
    (durum === OdemePlaniDurumu.Onaylandi || durum === OdemePlaniDurumu.Uygulandi);

  const islem = async (calis: () => Promise<void>) => {
    setIslemde(true);
    setIslemHatasi("");
    try {
      await mutate(calis);
    } catch (e) {
      setIslemHatasi(
        e instanceof Error ? e.message : "İşlem tamamlanamadı.",
      );
    } finally {
      setIslemde(false);
    }
  };

  /*
   * `data?.cariler ?? []` her render'da YENİ dizi üretiyor; bağımlılık
   * olarak verilseydi useMemo hiç işe yaramaz, seçenekler her tuşta
   * yeniden kurulurdu. Bağımlılık `data`nın kendisi.
   */
  const cariSecenekleri = useMemo(
    () =>
      (data?.cariler ?? []).map((c) => ({
        id: c.id,
        code: c.code,
        title: c.title,
        extra: [c.shortName, c.taxNumber],
      })),
    [data],
  );

  const cariAdi = (satir: SatirOzeti) =>
    satir.cariUnvan ??
    cariler.find((c) => c.id === satir.currentAccountId)?.title ??
    "—";

  const hesapAdi = (id?: string | null) => {
    if (!id) return "—";
    const hesap = hesaplar.find((h) => h.id === id);
    return hesap ? kasaHesapEtiketi(hesap) : "—";
  };

  const formuAc = (satir?: SatirOzeti) => {
    if (satir) {
      setDuzenlenen(satir.id);
      setEkleniyor(false);
      setForm({
        currentAccountId: satir.currentAccountId,
        tutar: satir.onerilenTutar,
        yontem: satir.yontem,
        cekVadesi: satir.cekVadesi ? satir.cekVadesi.slice(0, 10) : "",
        oncelik: satir.oncelik,
        cashAccountId: satir.cashAccountId ?? "",
        aciklama: satir.aciklama ?? "",
      });
    } else {
      setDuzenlenen(null);
      setEkleniyor(true);
      setForm(BOS_FORM);
    }
  };

  const formuKapat = () => {
    setDuzenlenen(null);
    setEkleniyor(false);
    setForm(BOS_FORM);
  };

  const formuKaydet = () =>
    islem(async () => {
      if (duzenlenen) {
        await odemePlaniService.satirGuncelle(duzenlenen, formdanIstek(form));
      } else {
        await odemePlaniService.satirEkle(planId, formdanIstek(form));
      }
      formuKapat();
    });

  if (loading && !plan) {
    return (
      <ErpShell design="redwood" title="Ödeme Planı" description="Yükleniyor…">
        <div className="erp-empty-state">
          <strong>Yükleniyor…</strong>
        </div>
      </ErpShell>
    );
  }

  if (!plan) {
    return (
      <ErpShell design="redwood" title="Ödeme Planı" description="">
        <div className="erp-alert error">{error || "Plan bulunamadı."}</div>
        <Button
          variant="secondary"
          onClick={() => router.push("/finans/odeme-planlari")}
        >
          Listeye Dön
        </Button>
      </ErpShell>
    );
  }

  const bekleyen = plan.satirlar.filter(
    (s) => s.karar === OdemeSatirKarari.Bekliyor,
  ).length;

  return (
    <ErpShell
      design="redwood"
      title={`${date(plan.haftaBaslangici)} Haftası`}
      description={`Ödeme günü ${date(plan.odemeGunu)} — ${
        ODEME_PLANI_DURUM_ETIKETLERI[plan.durum]
      }`}
    >
      <div className="erp-toolbar">
        <span className={`erp-status ${DURUM_RENGI[plan.durum] ?? "gray"}`}>
          {ODEME_PLANI_DURUM_ETIKETLERI[plan.durum]}
        </span>

        <div className="rw-toolbar-end">
          {lastUpdatedAt && (
            <small>Son güncelleme {dateTime(lastUpdatedAt)}</small>
          )}

          <Button
            variant="secondary"
            disabled={loading}
            onClick={() => void refresh()}
          >
            Yenile
          </Button>

          <Button
            variant="secondary"
            onClick={() => router.push("/finans/odeme-planlari")}
          >
            Listeye Dön
          </Button>

          {canPrepare && durum === OdemePlaniDurumu.Taslak && (
            <Button
              disabled={islemde || plan.satirlar.length === 0}
              onClick={() =>
                void islem(() => odemePlaniService.onayaSun(planId))
              }
            >
              Onaya Sun
            </Button>
          )}

          {canPrepare && durum === OdemePlaniDurumu.Uygulandi && (
            <Button
              disabled={islemde}
              onClick={() => void islem(() => odemePlaniService.kapat(planId))}
            >
              Haftayı Kapat
            </Button>
          )}
        </div>
      </div>

      {(error || islemHatasi) && (
        <div className="erp-alert error">{islemHatasi || error}</div>
      )}

      {kararAcik && bekleyen > 0 && (
        <div className="erp-alert warning">
          <strong>{bekleyen} satır karar bekliyor.</strong> Karar satır
          satır verilir; haftanın tamamı tek düğmeyle onaylanmaz.
        </div>
      )}

      {plan.gecenHaftaninPlanDisi.length > 0 && (
        <div className="erp-table-card">
          <div className="erp-table-header">
            <h2>Geçen Haftanın Plan Dışı Ödemeleri</h2>
            <strong>{plan.gecenHaftaninPlanDisi.length} kayıt</strong>
          </div>
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Tarih</th>
                  <th>Cari</th>
                  <th>Sebep</th>
                  <th className="num">Tutar</th>
                </tr>
              </thead>
              <tbody>
                {plan.gecenHaftaninPlanDisi.map((p) => (
                  <tr key={p.id}>
                    <td>{date(p.odemeTarihi)}</td>
                    <td>{p.cariUnvan ?? "—"}</td>
                    <td>{p.sebep}</td>
                    <td className="num">{money(p.tutar)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {butce && <ButcePaneli butce={butce} hesaplar={hesaplar} />}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Ödeme Satırları</h2>
          <div className="rw-toolbar-end">
            {eklemeSilmeAcik && (
              <Button
                variant="secondary"
                disabled={islemde || ekleniyor}
                onClick={() => formuAc()}
              >
                Satır Ekle
              </Button>
            )}
          </div>
        </div>

        {!eklemeSilmeAcik && canPrepare && durum !== OdemePlaniDurumu.Kapandi && (
          <div className="erp-alert warning">
            Plan onaya sunulduktan sonra satır <strong>eklenemez ve
            silinemez</strong>; mevcut satırlar düzeltilebilir. Unutulan bir
            ödeme bu plana sonradan girmez — plan dışı ödeme olarak
            kaydedilir ve gelecek haftanın planının başında görünür.
          </div>
        )}

        {plan.satirlar.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Satır yok</strong>
            <p>Haftanın ödenecekleri buraya satır satır girilir.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Öncelik</th>
                  <th>Cari</th>
                  <th>Yöntem</th>
                  <th className="num">Önerilen</th>
                  <th className="num">Onaylanan</th>
                  <th>Karar</th>
                  <th>Ödeme</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {plan.satirlar.map((satir) => (
                  <SatirSatiri
                    key={satir.id}
                    satir={satir}
                    cariAdi={cariAdi(satir)}
                    hesapAdi={hesapAdi(satir.cashAccountId)}
                    duzenlemeAcik={duzenlemeAcik}
                    eklemeSilmeAcik={eklemeSilmeAcik}
                    kararAcik={kararAcik}
                    odemeAcik={odemeAcik}
                    islemde={islemde}
                    onDuzenle={() => formuAc(satir)}
                    onSil={() =>
                      void islem(() => odemePlaniService.satirSil(satir.id))
                    }
                    onKarar={(istek) =>
                      void islem(() =>
                        odemePlaniService.satirKarar(satir.id, istek),
                      )
                    }
                    onOdeme={(tutar) =>
                      void islem(() =>
                        odemePlaniService.satirOdeme(satir.id, tutar),
                      )
                    }
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {(ekleniyor || duzenlenen) && (
        <div className="erp-table-card">
          <div className="erp-table-header">
            <h2>{duzenlenen ? "Satırı Düzelt" : "Yeni Satır"}</h2>
          </div>

          <div className="erp-form-grid">
            <SearchableSelect
              label="Cari"
              value={form.currentAccountId}
              onChange={(id) => setForm((f) => ({ ...f, currentAccountId: id }))}
              options={cariSecenekleri}
              disabled={Boolean(duzenlenen)}
              required
            />

            {duzenlenen && (
              <small>
                Cari satır açılırken sabitlenir ve sonradan değiştirilemez.
                Yanlış cariye açılmış bir satır <strong>taslak
                aşamasında</strong> silinip yeniden açılır; plan onaya
                sunulduysa satır reddedilir ve ödeme plan dışı olarak
                kaydedilir.
              </small>
            )}

            <TutarInput
              label="Önerilen Tutar"
              value={form.tutar}
              onChange={(v) => setForm((f) => ({ ...f, tutar: v }))}
            />

            <label>
              <span>Yöntem</span>
              <select
                value={form.yontem}
                onChange={(e) =>
                  setForm((f) => ({ ...f, yontem: Number(e.target.value) }))
                }
              >
                {Object.entries(ODEME_YONTEM_ETIKETLERI).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </select>
            </label>

            {form.yontem === OdemeYontemi.Cek ? (
              <label>
                <span>Çek Vadesi</span>
                <input
                  type="date"
                  value={form.cekVadesi}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, cekVadesi: e.target.value }))
                  }
                />
              </label>
            ) : (
              <label>
                <span>Kasa / Banka Hesabı</span>
                <select
                  value={form.cashAccountId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, cashAccountId: e.target.value }))
                  }
                >
                  <option value="">Seçin</option>
                  {hesaplar.map((h) => (
                    <option key={h.id} value={h.id}>
                      {kasaHesapEtiketi(h)}
                    </option>
                  ))}
                </select>
              </label>
            )}

            <label>
              <span>Öncelik</span>
              <input
                type="number"
                min={1}
                max={5}
                value={form.oncelik}
                onChange={(e) =>
                  setForm((f) => ({ ...f, oncelik: Number(e.target.value) }))
                }
              />
            </label>

            <label>
              <span>Açıklama</span>
              <input
                value={form.aciklama}
                onChange={(e) =>
                  setForm((f) => ({ ...f, aciklama: e.target.value }))
                }
              />
            </label>
          </div>

          <div className="erp-toolbar rw-toolbar-end">
            <Button variant="secondary" onClick={formuKapat}>
              Vazgeç
            </Button>
            <Button
              disabled={
                islemde ||
                !form.currentAccountId ||
                !form.tutar ||
                form.tutar <= 0
              }
              onClick={() => void formuKaydet()}
            >
              Kaydet
            </Button>
          </div>
        </div>
      )}
    </ErpShell>
  );
}

/**
 * TEK SATIR — kip ne olursa olsun aynı satır bileşeni.
 *
 * ONAYDAN SONRA DEĞİŞTİ ROZETİ SUNUCUDAN GELİR (`onaydanSonraDegisti`,
 * `degisenAlanlar`). Ekran kendi karşılaştırmasını yapmıyor: yapsaydı
 * sunucudaki K2 ile zamanla ayrışır ve GM "değişmedi" yazan bir satırı
 * onaylarken sunucu onayı düşürürdü.
 */
function SatirSatiri({
  satir,
  cariAdi,
  hesapAdi,
  duzenlemeAcik,
  eklemeSilmeAcik,
  kararAcik,
  odemeAcik,
  islemde,
  onDuzenle,
  onSil,
  onKarar,
  onOdeme,
}: {
  satir: SatirOzeti;
  cariAdi: string;
  hesapAdi: string;
  duzenlemeAcik: boolean;
  eklemeSilmeAcik: boolean;
  kararAcik: boolean;
  odemeAcik: boolean;
  islemde: boolean;
  onDuzenle: () => void;
  onSil: () => void;
  onKarar: (istek: {
    karar: number;
    onaylananTutar?: number | null;
    cekVadesi?: string | null;
    oncelik?: number | null;
  }) => void;
  onOdeme: (tutar: number) => void;
}) {
  const [kismiTutar, setKismiTutar] = useState<number | null>(null);
  const [odenen, setOdenen] = useState<number | null>(null);

  return (
    <tr>
      <td>{satir.oncelik}</td>
      <td>
        <strong>{cariAdi}</strong>
        {satir.aciklama && <small>{satir.aciklama}</small>}
        {satir.devirHaftaSayisi > 0 && (
          <small>{satir.devirHaftaSayisi} haftadır devrediyor</small>
        )}
        {satir.onaydanSonraDegisti && (
          <small>
            <span className="erp-status red">Onaydan sonra değişti</span>{" "}
            {satir.degisenAlanlar.join(", ")}
          </small>
        )}
      </td>
      <td>
        {ODEME_YONTEM_ETIKETLERI[satir.yontem]}
        {satir.yontem === OdemeYontemi.Cek ? (
          <small>{satir.cekVadesi ? date(satir.cekVadesi) : "vade yok"}</small>
        ) : (
          <small>{hesapAdi}</small>
        )}
      </td>
      <td className="num">{money(satir.onerilenTutar)}</td>
      <td className="num">
        {satir.onaylananTutar == null ? "—" : money(satir.onaylananTutar)}
      </td>
      <td>
        <span className={`erp-status ${KARAR_RENGI[satir.karar] ?? "gray"}`}>
          {ODEME_KARAR_ETIKETLERI[satir.karar]}
        </span>
      </td>
      <td>
        {ODEME_DURUM_ETIKETLERI[satir.odemeDurumu]}
        {satir.odemeDurumu !== OdemeSatirOdemeDurumu.Odenmedi && (
          <small>{money(satir.odenenTutar)} ödendi</small>
        )}
        {satir.kapanisSebebi != null && satir.kapanisAciklamasi && (
          <small>{satir.kapanisAciklamasi}</small>
        )}
      </td>
      <td className="num">
        {duzenlemeAcik && (
          <Button variant="secondary" disabled={islemde} onClick={onDuzenle}>
            Düzelt
          </Button>
        )}

        {eklemeSilmeAcik && (
          <Button variant="secondary" disabled={islemde} onClick={onSil}>
            Sil
          </Button>
        )}

        {kararAcik && (
          <>
            <Button
              disabled={islemde}
              onClick={() =>
                onKarar({
                  karar: OdemeSatirKarari.Onaylandi,
                  onaylananTutar: satir.onerilenTutar,
                })
              }
            >
              Onayla
            </Button>
            <Button
              variant="secondary"
              disabled={islemde}
              onClick={() => onKarar({ karar: OdemeSatirKarari.Reddedildi })}
            >
              Reddet
            </Button>
            <TutarInput
              label="Kısmi"
              value={kismiTutar}
              onChange={setKismiTutar}
            />
            <Button
              variant="secondary"
              disabled={islemde || !kismiTutar || kismiTutar <= 0}
              onClick={() =>
                onKarar({
                  karar: OdemeSatirKarari.Kismi,
                  onaylananTutar: kismiTutar,
                })
              }
            >
              Kısmi Onayla
            </Button>
          </>
        )}

        {odemeAcik && satir.karar !== OdemeSatirKarari.Reddedildi && (
          <>
            <TutarInput label="Ödenen" value={odenen} onChange={setOdenen} />
            <Button
              disabled={islemde || !odenen || odenen <= 0}
              onClick={() => onOdeme(odenen ?? 0)}
            >
              Ödeme Kaydet
            </Button>
          </>
        )}
      </td>
    </tr>
  );
}
