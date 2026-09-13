import type { Metadata, Viewport } from "next";
import "./globals.css";
import { ServiceWorkerRegistration } from "@/components/service-worker-registration";
import MesajBaloncugu from "@/components/mesajlar/mesaj-baloncugu";
import { HataSiniri } from "@/components/erp/hata-siniri";
import { TaslakDeposuSaglayici } from "@/lib/mesajlasma/taslak-deposu";

/*
 * ═══ SÜRÜM/1 — TARAYICIDAKİ YAPI DIŞARIDAN ÖLÇÜLEBİLİR OLMALI ═══
 *
 * ÖLÇÜLDÜ (2026-09-13): canlı HTML hiçbir yapı kimliği taşımıyordu
 * (App Router `__NEXT_DATA__.buildId` basmıyor). "Kullanıcı yeni yapıyı
 * mı görüyor?" sorusu bu yüzden sunucudaki `.next` parça tarihlerine
 * bakılarak cevaplanmak zorunda kaldı — ve o dizinde BAYAT ARTIKLAR
 * vardı (OTURUM/1: 09-11 12:13 canlı yapı, 09-10 04:11 artık).
 *
 * Meta etiketi curl ile okunabiliyor:
 *     curl -s https://enderunai.com.tr/login | grep enderun-surum
 *
 * Değer yayında `NEXT_PUBLIC_SURUM` ile geliyor; gelmezse
 * "bilinmiyor" yazılır — boş bırakmak, bilmediğimizi bildiğimiz hâli
 * gizlerdi.
 */
export const ENDERUN_SURUM = process.env.NEXT_PUBLIC_SURUM ?? "bilinmiyor";

export const metadata: Metadata = {
  title: "Enderun ERP",
  other: {
    "enderun-surum": ENDERUN_SURUM,
  },
  description: "Enderun Enerji kurumsal yönetim platformu",
  manifest: "/manifest.json",
  icons: {
    icon: [
      { url: "/favicon-32.png", sizes: "32x32", type: "image/png" },
      { url: "/favicon-16.png", sizes: "16x16", type: "image/png" },
    ],
    apple: "/apple-touch-icon.png",
  },
  appleWebApp: {
    capable: true,
    statusBarStyle: "black-translucent",
    title: "Enderun ERP",
  },
};

export const viewport: Viewport = {
  themeColor: "#0f4648",
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="tr">
      <body>
        {/*
          TASLAK DEPOSU HEM SAYFAYI HEM PANELİ SARIYOR.

          İki yazma kutusu var — yüzen panel ve /mesajlar tam sayfası —
          ve ikisi aynı taslağı görmeli. Depo panelde tutulsaydı panel
          kapanınca, sayfada tutulsaydı rota değişiminde giderdi. Kök,
          ikisinin de üstünde olan tek yer.
        */}
        <TaslakDeposuSaglayici>
          {children}
          <ServiceWorkerRegistration />

        {/*
          MESAJ PANELİ KÖKTE, {children}'IN DIŞINDA — VE SEBEBİ ÖLÇÜLDÜ.

          Ortak bir layout kabuğu yok: her sayfa kendi ERP kabuğunu
          kuruyor (173 dosya). Rota değişiminde {children} konumundaki
          bileşen TİPİ değişiyor ve React alt ağacın tamamını söküyor —
          kabuk, baloncuk ve panel dahil. Panel kabukta dururken her
          ekran geçişinde yeniden doğuyordu; taslak da onunla gidiyordu.

          Burası rota değişiminde yeniden kurulmuyor. Panelin monte
          sayısı ekran değişimlerinden etkilenmiyor ve M3/2c-2'nin canlı
          bağlantısı bu sayının 1'de kalmasına bağlı.

          KENDİ HATA SINIRINDA: panel çökerse uygulamanın tamamı
          düşmesin. Kökte başka bir sınır yok, bu yüzden burada şart.

          OTURUM/PORTAL KONTROLÜ BALONCUĞUN İÇİNDE — burada rota listesi
          tutulmuyor; bkz. mesaj-baloncugu.tsx.
        */}
          <HataSiniri nerede="mesaj-paneli" bicim="govde">
            <MesajBaloncugu />
          </HataSiniri>
        </TaslakDeposuSaglayici>
      </body>
    </html>
  );
}
