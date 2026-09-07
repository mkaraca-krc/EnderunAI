"use client";

import ErpShell from "@/components/erp/erp-shell";
import MesajPaneli from "@/components/mesajlar/mesaj-paneli";

/**
 * MESAJLAR — TAM SAYFA.
 *
 * ═══ İNCE SARMALAYICI, İKİNCİ KOPYA DEĞİL ═══
 *
 * Gövde `components/mesajlar/mesaj-paneli.tsx` içinde ve panel de AYNI
 * bileşeni kullanıyor. Fark yalnız `kip` parametresinde. Ayrı
 * yazılsalardı zamanla ayrışırlardı ve AYRIŞAN HER NOKTA, BİRİNİN
 * SINAMADIĞI BİR NOKTADIR.
 *
 * ═══ KABUK — TUR 2'DEN KALMA BİR EKSİK, 2026-09-06'DA KAPANDI ═══
 *
 * Bu sayfa `ErpShell` kullanmıyordu: menü, üst çubuk, Hızır ve mesaj
 * baloncuğu burada YOKTU. Mehmet ölçtü ve iki ekranda birden buldu
 * (`/yapilacaklar` ile birlikte). Kabuğun her sayfaya TEK TEK
 * eklendiği bir düzende, eklemeyi unutmak sessizce geçiyordu.
 *
 * Artık `sayfa-kabuk-sozlesmesi.test.ts` bunu tutuyor.
 *
 * ═══ TAM SAYFA NEDEN DURUYOR ═══
 *
 * Panel onun YERİNE değil, YANINA geldi: uzun konuşma okumak ve arama
 * yapmak için geniş ekran daha iyi. Dar ekranda panel hiç açılmaz,
 * baloncuk buraya yönlendirir.
 */
export default function MesajlarSayfasi() {
  return (
    <ErpShell
      design="redwood"
      title="Mesajlar"
      description="Çalışma arkadaşlarınızla birebir yazışma"
      /*
       * BELGE KAYMAZ, MESAJ LİSTESİ KAYAR.
       *
       * Bu ekranın yazma alanı en altta; belge uzayınca katlanmanın
       * altına düşüyordu (ölçüldü: 390x664'te 68 px dışarıda).
       * Gerekçenin tamamı `erp-shell.tsx` içindeki prop yorumunda.
       */
      tamYukseklik
    >
      <MesajPaneli kip="tam-sayfa" />
    </ErpShell>
  );
}
