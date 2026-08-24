"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef } from "react";

/**
 * 404 — BULUNAMAYAN SAYFA.
 *
 * BU DOSYA ÖNCE YOKTU VE EKSİKLİĞİ BİR ARIZAYI SEKİZ GÜN GİZLEDİ:
 * M1/5'te Yapılacaklar satırları `/gorevler/{id}`'ye bağlandı ama o
 * rota hiç oluşturulmamıştı. Kullanıcı boş sayfa gördü, sistem
 * hiçbir yere hiçbir şey yazmadı, kimse fark etmedi.
 *
 * Rota bekçisi (7a/A) artık bunu testte yakalıyor. Bu kayıt ikinci
 * savunma hattı: bekçinin göremediği yollar da var — hesaplanmış
 * hedefler (29 tane), elle yazılan adresler, eski yer imleri.
 */
export default function NotFound() {
  const pathname = usePathname();
  const bildirildi = useRef(false);

  useEffect(() => {
    // Aynı sayfa için tek kayıt: React iki kez çizerse ikinci
    // kayıt gürültüdür.
    if (bildirildi.current) return;
    bildirildi.current = true;

    void fetch("/kayit/404", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        path: pathname,
        referrer: document.referrer || null,
      }),
      keepalive: true,
    }).catch(() => {
      // Kayıt gönderilemezse sessiz kal: 404 ekranı, kayıt
      // altyapısının arızası yüzünden ikinci bir hata göstermemeli.
    });
  }, [pathname]);

  return (
    <div className="erp-panel" style={{ margin: "48px auto", maxWidth: 560 }}>
      <header className="erp-panel-header">
        <h2>Sayfa bulunamadı</h2>
      </header>

      <p>
        Aradığınız sayfa yok ya da adresi değişmiş olabilir. Bir
        bağlantıdan geldiyseniz o bağlantı bozuk demektir; kayıt
        tutuldu.
      </p>

      <p style={{ marginTop: 16 }}>
        <Link href="/dashboard" className="erp-primary-button">
          Panoya dön
        </Link>
      </p>
    </div>
  );
}
