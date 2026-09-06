"use client";

import MesajPaneli from "@/components/mesajlar/mesaj-paneli";

/**
 * MESAJLAR — TAM SAYFA.
 *
 * ═══ İNCE SARMALAYICI, İKİNCİ KOPYA DEĞİL ═══
 *
 * Bu sayfanın gövdesi `components/mesajlar/mesaj-paneli.tsx` içine
 * taşındı ve panel de AYNI bileşeni kullanıyor. Fark yalnız `kip`
 * parametresinde.
 *
 * Panel ile tam sayfa ayrı yazılsaydı zamanla ayrışırlardı ve AYRIŞAN
 * HER NOKTA, BİRİNİN SINAMADIĞI BİR NOKTADIR. Bu kod tabanının en sık
 * hatası aynı şeyin ikinci kopyası; burada baştan engellendi.
 *
 * ═══ TAM SAYFA NEDEN DURUYOR ═══
 *
 * Panel onun YERİNE değil, YANINA geldi (Mehmet, 2026-09-06): uzun
 * konuşma okumak ve arama yapmak için geniş ekran daha iyi. Dar
 * ekranda ise panel hiç açılmaz, baloncuk buraya yönlendirir.
 */
export default function MesajlarSayfasi() {
  return <MesajPaneli kip="tam-sayfa" />;
}
