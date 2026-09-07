import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /*
   * ═══ YAPI DİZİNİ DIŞARIDAN VERİLEBİLİR — CANLIYI EZMEMEK İÇİN ═══
   *
   * ÖLÇÜLEN OLAY (2026-09-07): düzen testi rig'i `npm run build`'i
   * CANLININ SERVİS ETTİĞİ dizinde koşuyordu. Canlı Next süreci
   * 20:33:39'da başlamıştı; rig 20:50:40'ta `.next`i altından
   * değiştirdi. Çalışan sunucu ESKİ manifest'i tutuyor, diskteki
   * dosyalar YENİ — sonuç: parça 404'leri ve bozuk render.
   *
   * Kullanıcıya "yetki matrisi bozuldu" ve "/dokumanlar açılmıyor"
   * diye görünen şey buydu. Kod değil, YAPI DİZİNİ çakışmasıydı.
   *
   * Artık rig `NEXT_DIST_DIR=.next-duzen` ile ayrı dizine derliyor;
   * canlının `.next`ine hiç dokunmuyor. Değişken verilmezse davranış
   * bugünküyle aynı.
   */
  distDir: process.env.NEXT_DIST_DIR || ".next",

  experimental: {
    proxyClientMaxBodySize: "100mb",
  },
};

export default nextConfig;
