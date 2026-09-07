import type { Metadata, Viewport } from "next";
import "./globals.css";
import { ServiceWorkerRegistration } from "@/components/service-worker-registration";
import MesajBaloncugu from "@/components/mesajlar/mesaj-baloncugu";
import { HataSiniri } from "@/components/erp/hata-siniri";

export const metadata: Metadata = {
  title: "Enderun ERP",
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
      </body>
    </html>
  );
}
