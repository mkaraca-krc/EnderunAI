"use client";

import {
  ChangeEvent,
  useEffect,
  useMemo,
  useState,
} from "react";
import { apiFetch } from "@/lib/api";

type Kesinti = {
  ad: string;
  oran: number;
  manuelTutar: number;
};

type TevkifatSecenegi = {
  kod: string;
  ad: string;
  pay: number;
  payda: number;
};

type YuklenenDosya = {
  originalName?: string;
  storedName: string;
  extension: string;
  contentType: string;
  size: number;
  uploadedAtUtc: string;
};

const para = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const tevkifatSecenekleri: TevkifatSecenegi[] = [
  {
    kod: "yok",
    ad: "Tevkifat Yok",
    pay: 0,
    payda: 10,
  },
  {
    kod: "yapim",
    ad: "Yapım işleri ve bağlantılı mühendislik hizmetleri",
    pay: 4,
    payda: 10,
  },
  {
    kod: "etut",
    ad: "Etüt, plan, proje, danışmanlık ve denetim hizmetleri",
    pay: 9,
    payda: 10,
  },
  {
    kod: "bakim",
    ad: "Makine ve teçhizat bakım-onarım hizmetleri",
    pay: 7,
    payda: 10,
  },
  {
    kod: "temizlik",
    ad: "Temizlik, çevre ve bahçe bakım hizmetleri",
    pay: 9,
    payda: 10,
  },
  {
    kod: "isgucu",
    ad: "İşgücü temin hizmetleri",
    pay: 9,
    payda: 10,
  },
  {
    kod: "servis",
    ad: "Personel ve öğrenci taşıma hizmetleri",
    pay: 5,
    payda: 10,
  },
  {
    kod: "diger2",
    ad: "Diğer işlem",
    pay: 2,
    payda: 10,
  },
  {
    kod: "diger3",
    ad: "Diğer işlem",
    pay: 3,
    payda: 10,
  },
  {
    kod: "diger5",
    ad: "Diğer işlem",
    pay: 5,
    payda: 10,
  },
  {
    kod: "diger7",
    ad: "Diğer işlem",
    pay: 7,
    payda: 10,
  },
  {
    kod: "diger9",
    ad: "Diğer işlem",
    pay: 9,
    payda: 10,
  },
];

const baslangicKesintileri: Kesinti[] = [
  { ad: "Stopaj", oran: 0, manuelTutar: 0 },
  { ad: "Damga Vergisi", oran: 0, manuelTutar: 0 },
  { ad: "Konaklama", oran: 0, manuelTutar: 0 },
  { ad: "Yemek", oran: 0, manuelTutar: 0 },
  { ad: "Avans Mahsubu", oran: 0, manuelTutar: 0 },
  { ad: "Teminat Kesintisi", oran: 0, manuelTutar: 0 },
  { ad: "SGK / Vergi Borcu", oran: 0, manuelTutar: 0 },
  { ad: "Ceza / Gecikme", oran: 0, manuelTutar: 0 },
  { ad: "Malzeme Kesintisi", oran: 0, manuelTutar: 0 },
  { ad: "İşçilik Kesintisi", oran: 0, manuelTutar: 0 },
  { ad: "Diğer Kesinti", oran: 0, manuelTutar: 0 },
];

export default function HakedisPage() {
  const [proje, setProje] = useState("");
  const [isveren, setIsveren] = useState("");
  const [hakedisNo, setHakedisNo] = useState("");
  const [donem, setDonem] = useState("");
  const [hakedisTarihi, setHakedisTarihi] =
    useState("");
  const [aciklama, setAciklama] = useState("");

  const [dosya, setDosya] = useState<File | null>(
    null
  );
  const [uploading, setUploading] = useState(false);
  const [uploadMessage, setUploadMessage] =
    useState("");
  const [arsivYukleniyor, setArsivYukleniyor] =
    useState(false);
  const [arsivMesaji, setArsivMesaji] = useState("");
  const [yuklenenDosyalar, setYuklenenDosyalar] =
    useState<YuklenenDosya[]>([]);

  const [hakedisBedeli, setHakedisBedeli] =
    useState(0);
  const [kdvOrani, setKdvOrani] = useState(20);
  const [tevkifatKodu, setTevkifatKodu] =
    useState("yok");

  const [kesintiler, setKesintiler] =
    useState<Kesinti[]>(baslangicKesintileri);

  const seciliTevkifat =
    tevkifatSecenekleri.find(
      (secenek) => secenek.kod === tevkifatKodu
    ) ?? tevkifatSecenekleri[0];

  const tevkifatOrani =
    seciliTevkifat.pay / seciliTevkifat.payda;

  const hesap = useMemo(() => {
    const kdvTutari =
      hakedisBedeli * (kdvOrani / 100);

    const kdvDahilToplam =
      hakedisBedeli + kdvTutari;

    const tevkifatTutari =
      kdvTutari * tevkifatOrani;

    const digerKesintiTutari = kesintiler.reduce(
      (toplam, kesinti) => {
        if (kesinti.manuelTutar > 0) {
          return toplam + kesinti.manuelTutar;
        }

        return (
          toplam +
          hakedisBedeli * (kesinti.oran / 100)
        );
      },
      0
    );

    const toplamKesinti =
      tevkifatTutari + digerKesintiTutari;

    const netOdenecek =
      kdvDahilToplam - toplamKesinti;

    return {
      kdvTutari,
      kdvDahilToplam,
      tevkifatTutari,
      digerKesintiTutari,
      toplamKesinti,
      netOdenecek,
    };
  }, [
    hakedisBedeli,
    kdvOrani,
    tevkifatOrani,
    kesintiler,
  ]);

  useEffect(() => {
    void arsiviGetir();
  }, []);

  async function arsiviGetir() {
    setArsivYukleniyor(true);
    setArsivMesaji("");

    try {
      const response = await apiFetch(
        "/api/hakedis/files"
      );

      if (response.status === 401) {
        throw new Error(
          "Oturum süresi dolmuş. Lütfen yeniden giriş yapın."
        );
      }

      const result = (await response.json()) as
        | YuklenenDosya[]
        | { message?: string };

      if (!response.ok) {
        const hata =
          "message" in result
            ? result.message
            : undefined;

        throw new Error(
          hata ?? "Hakediş arşivi alınamadı."
        );
      }

      setYuklenenDosyalar(
        Array.isArray(result) ? result : []
      );
    } catch (error) {
      setArsivMesaji(
        error instanceof Error
          ? error.message
          : "Hakediş arşivi alınamadı."
      );
    } finally {
      setArsivYukleniyor(false);
    }
  }

  async function dosyaSec(
    event: ChangeEvent<HTMLInputElement>
  ) {
    const secilenDosya =
      event.target.files?.[0] ?? null;

    if (!secilenDosya) {
      setDosya(null);
      setUploadMessage("");
      return;
    }

    const uzanti = secilenDosya.name
      .split(".")
      .pop()
      ?.toLowerCase();

    const izinliUzantilar = [
      "pdf",
      "xlsx",
      "xls",
      "csv",
    ];

    if (
      !uzanti ||
      !izinliUzantilar.includes(uzanti)
    ) {
      setDosya(null);
      setUploadMessage(
        "Yalnızca PDF, Excel veya CSV dosyası yükleyebilirsiniz."
      );
      event.target.value = "";
      return;
    }

    setDosya(secilenDosya);
    setUploading(true);
    setUploadMessage(
      "Dosya sunucuya yükleniyor..."
    );

    try {
      const formData = new FormData();
      formData.append("file", secilenDosya);

      const response = await apiFetch(
        "/api/hakedis/upload",
        {
          method: "POST",
          body: formData,
        }
      );

      const responseText = await response.text();

let result: {
  message?: string;
  originalName?: string;
  storedName?: string;
} = {};

if (responseText) {
  try {
    result = JSON.parse(responseText);
  } catch {
    throw new Error(
      `Sunucu geçersiz cevap verdi. HTTP ${response.status}`
    );
  }
}

      if (response.status === 401) {
        throw new Error(
          "Oturum süresi dolmuş. Lütfen yeniden giriş yapın."
        );
      }

      if (!response.ok) {
        throw new Error(
          result.message ?? "Dosya yüklenemedi."
        );
      }

      setUploadMessage(
        `Dosya başarıyla yüklendi: ${
          result.originalName ?? secilenDosya.name
        }`
      );

      await arsiviGetir();
    } catch (error) {
      setUploadMessage(
        error instanceof Error
          ? error.message
          : "Dosya yüklenemedi."
      );
    } finally {
      setUploading(false);
    }
  }

  async function dosyaSil(storedName: string) {
    const onay = window.confirm(
      "Bu dosyayı arşivden silmek istediğinize emin misiniz?"
    );

    if (!onay) {
      return;
    }

    try {
      const response = await apiFetch(
        `/api/hakedis/files/${encodeURIComponent(
          storedName
        )}`,
        {
          method: "DELETE",
        }
      );

      const result = (await response.json()) as {
        message?: string;
      };

      if (!response.ok) {
        throw new Error(
          result.message ?? "Dosya silinemedi."
        );
      }

      await arsiviGetir();
    } catch (error) {
      alert(
        error instanceof Error
          ? error.message
          : "Dosya silinemedi."
      );
    }
  }

  async function dosyaIndir(storedName: string) {
    try {
      const response = await apiFetch(
        `/api/hakedis/files/${encodeURIComponent(
          storedName
        )}`
      );

      if (!response.ok) {
        throw new Error("Dosya indirilemedi.");
      }

      const blob = await response.blob();
      const adres = URL.createObjectURL(blob);
      const baglanti =
        document.createElement("a");

      baglanti.href = adres;
      baglanti.download = storedName;
      document.body.appendChild(baglanti);
      baglanti.click();
      baglanti.remove();
      URL.revokeObjectURL(adres);
    } catch (error) {
      alert(
        error instanceof Error
          ? error.message
          : "Dosya indirilemedi."
      );
    }
  }

  function kesintiGuncelle(
    index: number,
    alan: "oran" | "manuelTutar",
    deger: number
  ) {
    setKesintiler((mevcutKesintiler) =>
      mevcutKesintiler.map(
        (kesinti, satirIndex) => {
          if (satirIndex !== index) {
            return kesinti;
          }

          if (alan === "oran") {
            return {
              ...kesinti,
              oran: deger,
              manuelTutar:
                deger > 0
                  ? 0
                  : kesinti.manuelTutar,
            };
          }

          return {
            ...kesinti,
            manuelTutar: deger,
            oran: deger > 0 ? 0 : kesinti.oran,
          };
        }
      )
    );
  }

  function formuTemizle() {
    setProje("");
    setIsveren("");
    setHakedisNo("");
    setDonem("");
    setHakedisTarihi("");
    setAciklama("");
    setDosya(null);
    setUploadMessage("");
    setHakedisBedeli(0);
    setKdvOrani(20);
    setTevkifatKodu("yok");
    setKesintiler(baslangicKesintileri);
  }
    return (
    <main className="mx-auto w-full max-w-[1500px] space-y-6 px-4 py-6">
      <header className="flex flex-col gap-4 rounded-2xl bg-slate-950 px-6 py-5 text-white shadow-sm md:flex-row md:items-center md:justify-between">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-300">
            Enderun AI Yönetim Sistemi
          </p>

          <h1 className="mt-1 text-2xl font-bold">
            Hakediş Net Ödeme Hesabı
          </h1>

          <p className="mt-1 text-sm text-slate-300">
            Hakediş dosyasını yükleyin, vergileri ve kesintileri
            hesaplayın.
          </p>
        </div>

        <div className="rounded-xl border border-white/15 bg-white/10 px-4 py-3">
          <p className="text-xs text-slate-300">
            Net Ödenecek Tutar
          </p>

          <p
            className={`mt-1 text-2xl font-bold ${
              hesap.netOdenecek < 0
                ? "text-red-300"
                : "text-emerald-300"
            }`}
          >
            {para.format(hesap.netOdenecek)}
          </p>
        </div>
      </header>

      <Bolum baslik="Hakediş Bilgileri">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <Alan etiket="Proje">
            <input
              type="text"
              value={proje}
              onChange={(event) =>
                setProje(event.target.value)
              }
              className="form-input"
              placeholder="Proje adı"
            />
          </Alan>

          <Alan etiket="İşveren">
            <input
              type="text"
              value={isveren}
              onChange={(event) =>
                setIsveren(event.target.value)
              }
              className="form-input"
              placeholder="İşveren firma"
            />
          </Alan>

          <Alan etiket="Hakediş No">
            <input
              type="text"
              value={hakedisNo}
              onChange={(event) =>
                setHakedisNo(event.target.value)
              }
              className="form-input"
              placeholder="Örneğin 11"
            />
          </Alan>

          <Alan etiket="Dönem">
            <input
              type="text"
              value={donem}
              onChange={(event) =>
                setDonem(event.target.value)
              }
              className="form-input"
              placeholder="Temmuz 2026"
            />
          </Alan>

          <Alan etiket="Hakediş Tarihi">
            <input
              type="date"
              value={hakedisTarihi}
              onChange={(event) =>
                setHakedisTarihi(event.target.value)
              }
              className="form-input"
            />
          </Alan>

          <div className="md:col-span-2 xl:col-span-3">
            <Alan etiket="Açıklama">
              <input
                type="text"
                value={aciklama}
                onChange={(event) =>
                  setAciklama(event.target.value)
                }
                className="form-input"
                placeholder="Hakediş açıklaması"
              />
            </Alan>
          </div>
        </div>
      </Bolum>

      <Bolum baslik="Hakediş Dosyası">
        <div className="grid gap-5 lg:grid-cols-[1fr_360px]">
          <label className="flex min-h-44 cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed border-slate-300 bg-slate-50 px-6 py-8 text-center transition hover:border-blue-400 hover:bg-blue-50">
            <input
              type="file"
              accept=".pdf,.xlsx,.xls,.csv"
              onChange={dosyaSec}
              className="hidden"
              disabled={uploading}
            />

            <span className="text-3xl">📄</span>

            <span className="mt-3 text-base font-bold text-slate-900">
              {uploading
                ? "Dosya yükleniyor..."
                : "Hakediş dosyasını seç"}
            </span>

            <span className="mt-1 text-sm text-slate-500">
              PDF, Excel veya CSV dosyası
            </span>
          </label>

          <div className="rounded-2xl border border-slate-200 bg-white p-5">
            <p className="text-xs font-bold uppercase tracking-wide text-slate-500">
              Seçilen Dosya
            </p>

            {dosya ? (
              <>
                <p className="mt-3 break-all font-bold text-slate-900">
                  {dosya.name}
                </p>

                <p className="mt-2 text-sm text-slate-500">
                  Boyut:{" "}
                  {(dosya.size / 1024 / 1024).toFixed(2)} MB
                </p>

                {uploading && (
                  <p className="mt-3 text-sm font-semibold text-blue-600">
                    Dosya sunucuya yükleniyor...
                  </p>
                )}

                {uploadMessage && (
                  <p
                    className={`mt-3 text-sm font-semibold ${
                      uploadMessage.includes("başarıyla")
                        ? "text-emerald-700"
                        : uploadMessage.includes("yükleniyor")
                          ? "text-blue-600"
                          : "text-red-600"
                    }`}
                  >
                    {uploadMessage}
                  </p>
                )}

                <button
                  type="button"
                  onClick={() => {
                    setDosya(null);
                    setUploadMessage("");
                  }}
                  className="mt-4 text-sm font-bold text-red-600"
                  disabled={uploading}
                >
                  Dosyayı kaldır
                </button>
              </>
            ) : (
              <>
                <p className="mt-3 text-sm text-slate-500">
                  Henüz bir dosya seçilmedi.
                </p>

                {uploadMessage && (
                  <p className="mt-3 text-sm font-semibold text-red-600">
                    {uploadMessage}
                  </p>
                )}
              </>
            )}
          </div>
        </div>

        <div className="mt-4 rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-900">
          Dosya seçildiğinde otomatik olarak güvenli hakediş arşivine
          yüklenir. Bir sonraki aşamada Excel ve PDF içeriği otomatik
          analiz edilecek.
        </div>
      </Bolum>
            <Bolum baslik="Vergi ve Tevkifat Bilgileri">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <Alan etiket="Hakediş Bedeli — KDV Hariç">
            <input
              type="number"
              min="0"
              step="0.01"
              value={hakedisBedeli || ""}
              onChange={(event) =>
                setHakedisBedeli(
                  Number(event.target.value)
                )
              }
              className="form-input text-right font-semibold"
              placeholder="0,00"
            />
          </Alan>

          <Alan etiket="KDV Oranı (%)">
            <input
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={kdvOrani}
              onChange={(event) =>
                setKdvOrani(
                  Number(event.target.value)
                )
              }
              className="form-input text-right"
            />
          </Alan>

          <div className="md:col-span-2">
            <Alan etiket="KDV Tevkifat Türü">
              <select
                value={tevkifatKodu}
                onChange={(event) =>
                  setTevkifatKodu(
                    event.target.value
                  )
                }
                className="form-input"
              >
                {tevkifatSecenekleri.map(
                  (secenek) => (
                    <option
                      key={secenek.kod}
                      value={secenek.kod}
                    >
                      {secenek.ad} —{" "}
                      {secenek.pay}/
                      {secenek.payda}
                    </option>
                  )
                )}
              </select>
            </Alan>
          </div>
        </div>

        <div className="mt-5 grid gap-4 md:grid-cols-4">
          <BilgiKutusu
            baslik="Hesaplanan KDV"
            tutar={hesap.kdvTutari}
          />

          <BilgiKutusu
            baslik={`Tevkifat ${seciliTevkifat.pay}/${seciliTevkifat.payda}`}
            tutar={hesap.tevkifatTutari}
          />

          <BilgiKutusu
            baslik="KDV Dahil Toplam"
            tutar={hesap.kdvDahilToplam}
          />

          <BilgiKutusu
            baslik="Tevkifat Sonrası"
            tutar={
              hesap.kdvDahilToplam -
              hesap.tevkifatTutari
            }
          />
        </div>

        <div className="mt-4 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          Tevkifat seçimi sistem önerisidir.
          İşin kapsamı, alıcının statüsü,
          sözleşme bedeli ve güncel mevzuat
          mali müşavir tarafından kontrol
          edilmelidir.
        </div>
      </Bolum>

      <Bolum baslik="Diğer Kesintiler">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[920px] table-fixed border-collapse">
            <thead>
              <tr className="bg-slate-100 text-left text-xs font-bold uppercase tracking-wide text-slate-700">
                <th className="w-[30%] px-4 py-3">
                  Kesinti Türü
                </th>

                <th className="w-[18%] px-4 py-3">
                  Oran (%)
                </th>

                <th className="w-[22%] px-4 py-3">
                  Manuel Tutar
                </th>

                <th className="w-[30%] px-4 py-3 text-right">
                  Uygulanan Kesinti
                </th>
              </tr>
            </thead>

            <tbody>
              {kesintiler.map(
                (kesinti, index) => {
                  const uygulananTutar =
                    kesinti.manuelTutar > 0
                      ? kesinti.manuelTutar
                      : hakedisBedeli *
                        (kesinti.oran / 100);

                  return (
                    <tr key={kesinti.ad}>
                      <td className="border-b border-slate-100 px-4 py-3 font-medium text-slate-800">
                        {kesinti.ad}
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3">
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={
                            kesinti.oran || ""
                          }
                          onChange={(event) =>
                            kesintiGuncelle(
                              index,
                              "oran",
                              Number(
                                event.target.value
                              )
                            )
                          }
                          className="table-input text-right"
                          placeholder="0,00"
                        />
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3">
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={
                            kesinti.manuelTutar ||
                            ""
                          }
                          onChange={(event) =>
                            kesintiGuncelle(
                              index,
                              "manuelTutar",
                              Number(
                                event.target.value
                              )
                            )
                          }
                          className="table-input text-right"
                          placeholder="0,00"
                        />
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3 text-right font-semibold text-slate-900">
                        {para.format(
                          uygulananTutar
                        )}
                      </td>
                    </tr>
                  );
                }
              )}
            </tbody>

            <tfoot>
              <tr className="bg-slate-950 text-white">
                <td
                  colSpan={3}
                  className="px-4 py-4 font-bold"
                >
                  TOPLAM KESİNTİ
                </td>

                <td className="px-4 py-4 text-right text-lg font-bold">
                  {para.format(
                    hesap.toplamKesinti
                  )}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      </Bolum>
            <Bolum baslik="Hakediş Arşivi">
        <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold text-slate-900">
              Yüklenen Hakediş Dosyaları
            </p>

            <p className="mt-1 text-sm text-slate-500">
              Dosyaları indirebilir veya arşivden silebilirsiniz.
            </p>
          </div>

          <button
            type="button"
            onClick={() => void arsiviGetir()}
            disabled={arsivYukleniyor}
            className="rounded-xl border border-slate-300 bg-white px-4 py-2 text-sm font-bold text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {arsivYukleniyor
              ? "Yenileniyor..."
              : "Arşivi Yenile"}
          </button>
        </div>

        {arsivMesaji && (
          <div className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-semibold text-red-700">
            {arsivMesaji}
          </div>
        )}

        {arsivYukleniyor ? (
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-8 text-center text-sm font-semibold text-slate-500">
            Hakediş arşivi yükleniyor...
          </div>
        ) : yuklenenDosyalar.length === 0 ? (
          <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 px-4 py-10 text-center">
            <p className="font-semibold text-slate-700">
              Arşivde henüz dosya bulunmuyor.
            </p>

            <p className="mt-1 text-sm text-slate-500">
              Yüklediğiniz dosyalar burada listelenecek.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[850px] border-collapse">
              <thead>
                <tr className="bg-slate-100 text-left text-xs font-bold uppercase tracking-wide text-slate-700">
                  <th className="px-4 py-3">
                    Dosya
                  </th>

                  <th className="px-4 py-3">
                    Tür
                  </th>

                  <th className="px-4 py-3">
                    Boyut
                  </th>

                  <th className="px-4 py-3">
                    Yüklenme Tarihi
                  </th>

                  <th className="px-4 py-3 text-right">
                    İşlemler
                  </th>
                </tr>
              </thead>

              <tbody>
                {yuklenenDosyalar.map(
                  (arsivDosyasi) => (
                    <tr
                      key={arsivDosyasi.storedName}
                      className="transition hover:bg-slate-50"
                    >
                      <td className="border-b border-slate-100 px-4 py-3">
                        <p className="max-w-[420px] break-all font-semibold text-slate-900">
                          {arsivDosyasi.originalName ??
                            arsivDosyasi.storedName}
                        </p>

                        <p className="mt-1 break-all text-xs text-slate-400">
                          {arsivDosyasi.storedName}
                        </p>
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3 text-sm font-semibold uppercase text-slate-600">
                        {arsivDosyasi.extension.replace(
                          ".",
                          ""
                        )}
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3 text-sm text-slate-600">
                        {dosyaBoyutu(
                          arsivDosyasi.size
                        )}
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3 text-sm text-slate-600">
                        {tarihGoster(
                          arsivDosyasi.uploadedAtUtc
                        )}
                      </td>

                      <td className="border-b border-slate-100 px-4 py-3">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            onClick={() =>
                              void dosyaIndir(
                                arsivDosyasi.storedName
                              )
                            }
                            className="rounded-lg border border-blue-200 bg-blue-50 px-3 py-2 text-xs font-bold text-blue-700 transition hover:bg-blue-100"
                          >
                            İndir
                          </button>

                          <button
                            type="button"
                            onClick={() =>
                              void dosyaSil(
                                arsivDosyasi.storedName
                              )
                            }
                            className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs font-bold text-red-700 transition hover:bg-red-100"
                          >
                            Sil
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                )}
              </tbody>
            </table>
          </div>
        )}
      </Bolum>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <OzetKart
          baslik="Hakediş Bedeli"
          tutar={hakedisBedeli}
        />

        <OzetKart
          baslik="KDV Dahil Toplam"
          tutar={hesap.kdvDahilToplam}
        />

        <OzetKart
          baslik="Toplam Kesinti"
          tutar={hesap.toplamKesinti}
        />

        <OzetKart
          baslik="Net Ödenecek Tutar"
          tutar={hesap.netOdenecek}
          vurgulu
        />
      </section>

      <section className="flex flex-col gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:flex-row sm:flex-wrap">
        <button
          type="button"
          className="rounded-xl bg-slate-950 px-5 py-3 text-sm font-bold text-white transition hover:bg-slate-800"
        >
          Hakedişi Kaydet
        </button>

        <button
          type="button"
          className="rounded-xl border border-slate-300 bg-white px-5 py-3 text-sm font-bold text-slate-700 transition hover:bg-slate-50"
        >
          PDF Oluştur
        </button>

        <button
          type="button"
          className="rounded-xl border border-slate-300 bg-white px-5 py-3 text-sm font-bold text-slate-700 transition hover:bg-slate-50"
        >
          Excel'e Aktar
        </button>

        <button
          type="button"
          onClick={formuTemizle}
          className="rounded-xl border border-red-200 bg-red-50 px-5 py-3 text-sm font-bold text-red-700 transition hover:bg-red-100 sm:ml-auto"
        >
          Formu Temizle
        </button>
      </section>

      <style jsx global>{`
        .form-input {
          width: 100%;
          min-height: 44px;
          border-radius: 0.75rem;
          border: 1px solid #cbd5e1;
          background-color: #ffffff;
          padding: 0.7rem 0.85rem;
          color: #0f172a;
          outline: none;
          transition:
            border-color 150ms ease,
            box-shadow 150ms ease;
        }

        .form-input::placeholder {
          color: #94a3b8;
        }

        .form-input:focus {
          border-color: #2563eb;
          box-shadow: 0 0 0 3px
            rgba(37, 99, 235, 0.12);
        }

        .table-input {
          width: 100%;
          min-height: 40px;
          border-radius: 0.6rem;
          border: 1px solid #cbd5e1;
          background-color: #fffdf5;
          padding: 0.55rem 0.7rem;
          color: #0f172a;
          outline: none;
        }

        .table-input:focus {
          border-color: #2563eb;
          background-color: #ffffff;
          box-shadow: 0 0 0 3px
            rgba(37, 99, 235, 0.1);
        }
      `}</style>
    </main>
  );
}

function Bolum({
  baslik,
  children,
}: {
  baslik: string;
  children: React.ReactNode;
}) {
  return (
    <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
      <div className="border-b border-slate-200 bg-slate-50 px-5 py-4">
        <h2 className="text-base font-bold text-slate-950">
          {baslik}
        </h2>
      </div>

      <div className="p-5">
        {children}
      </div>
    </section>
  );
}

function Alan({
  etiket,
  children,
}: {
  etiket: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-2 block text-sm font-semibold text-slate-700">
        {etiket}
      </span>

      {children}
    </label>
  );
}

function BilgiKutusu({
  baslik,
  tutar,
}: {
  baslik: string;
  tutar: number;
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 p-4">
      <p className="text-xs font-bold uppercase tracking-wide text-slate-500">
        {baslik}
      </p>

      <p className="mt-2 text-lg font-bold text-slate-950">
        {para.format(tutar)}
      </p>
    </div>
  );
}

function OzetKart({
  baslik,
  tutar,
  vurgulu = false,
}: {
  baslik: string;
  tutar: number;
  vurgulu?: boolean;
}) {
  return (
    <div
      className={`rounded-2xl border p-5 shadow-sm ${
        vurgulu
          ? "border-emerald-300 bg-emerald-50"
          : "border-slate-200 bg-white"
      }`}
    >
      <p
        className={`text-sm font-semibold ${
          vurgulu
            ? "text-emerald-700"
            : "text-slate-500"
        }`}
      >
        {baslik}
      </p>

      <p
        className={`mt-2 text-2xl font-bold ${
          vurgulu
            ? tutar < 0
              ? "text-red-700"
              : "text-emerald-800"
            : "text-slate-950"
        }`}
      >
        {para.format(tutar)}
      </p>
    </div>
  );
}

function dosyaBoyutu(byte: number) {
  if (byte < 1024) {
    return `${byte} B`;
  }

  if (byte < 1024 * 1024) {
    return `${(byte / 1024).toFixed(1)} KB`;
  }

  return `${(
    byte /
    1024 /
    1024
  ).toFixed(2)} MB`;
}

function tarihGoster(tarih: string) {
  const deger = new Date(tarih);

  if (Number.isNaN(deger.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(deger);
}
