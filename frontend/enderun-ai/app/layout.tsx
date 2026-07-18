import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Enderun AI Yönetim Merkezi",
  description: "Enderun Enerji kurumsal yönetim sistemi",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}
