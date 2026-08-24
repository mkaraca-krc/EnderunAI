"use client";

import { useCallback, useMemo } from "react";

import { usePermissions } from "@/lib/use-permissions";

/**
 * ELEMAN SEVİYESİ YETKİ — aksiyon düğmeleri.
 *
 * R1 rota seviyesini kapattı (menü + sayfa kapısı, tek kaynak). Bu,
 * bir granülerlik aşağısı: ekranın İÇİNDEKİ düğmeler.
 *
 * KURAL: bir düğmenin kontrol ettiği izin, çağırdığı ucun zorladığı
 * izinle AYNI olmak zorunda. Ayrı tutulursa iki sapma doğuyor —
 * "görünür ama reddedilir" (kullanıcı basar, 403 yer) ya da "gizli
 * ama izinli" (yetkisi olan düğmeyi hiç göremez). İkincisi daha
 * sinsi: kimse şikâyet etmez, iş yapılmaz ve sebebi bilinmez.
 *
 * BU YARDIMCI İKİNCİ BİR İZİN HARİTASI DEĞİL. Yaptığı tek şey
 * "modül.eylem" birleştirmesi; hangi düğmenin hangi eyleme bağlandığı
 * UCUN KENDİ RequirePermission'ından okunup çağrı yerinde yazılıyor.
 * Tahminle türetmek yanlış olurdu: ölçümde aynı adlı iki düğmenin
 * farklı izne bağlı olduğu görüldü — `markPaid` avansta
 * `attendance-payroll.create`, bordroda `attendance-payroll.edit`.
 *
 * ARAYÜZ GÜVENLİK SINIRI DEĞİLDİR. Düğme gizli olsa da uç yine
 * reddeder; buradaki bir hata veriyi açığa çıkarmaz, yalnızca
 * kullanıcıya yapamayacağı işi gösterir ya da yapabileceğini gizler.
 */
export function useModuleActions(module: string) {
  const { has, loading } = usePermissions();

  /**
   * `can("approve")` -> `has("attendance-payroll.approve")`
   *
   * Eylem adı UCUN izninden gelir, düğmenin adından değil.
   */
  const can = useCallback(
    (action: string) => has(`${module}.${action}`),
    [has, module],
  );

  /*
   * NESNE KİMLİĞİ SABİTLENİYOR — SÜS DEĞİL, KUSUR DÜZELTMESİ.
   *
   * Bu kanca her render'da YENİ bir nesne döndürüyordu. `can` zaten
   * `useCallback` ile sarılıydı ama SARMALAYAN NESNE değildi.
   *
   * Sonucu 2026-08-24'te canlıda görüldü: `/yapilacaklar` ekranı
   * beş `useModuleActions` çağrısının dönüşünü bir `useCallback`
   * bağımlılık dizisine koyuyor; nesneler her render'da yenilendiği
   * için o callback de yenileniyor, ona bağlı efekt her render'da
   * tetikleniyor ve sonsuz istek döngüsü doğuyordu. Ölçüm: 1,5
   * saniyede 1831 istek. Ekran "Yükleniyor…" durumundan hiç
   * çıkmıyordu ve gösterilecek bir HATA da yoktu — istekler 200
   * dönüyordu.
   *
   * `tests/hook-referans-kararliligi.test.tsx` bunu kilitliyor.
   */
  return useMemo(
    () => ({
      can,
    /**
     * İzinler HENÜZ YÜKLENMEDİ.
     *
     * Yüklenirken düğme GÖSTERİLMEZ. R1'de menü için aynı karar
     * verilmişti: dolu menüyü gösterip sonra öğe kaybetmek,
     * kullanıcıya olmayan yetkiyi bir an için göstermekti. Düğmede de
     * aynısı geçerli — üstelik orada kullanıcı tıklamaya da yetişir.
     */
      loading,
    }),
    [can, loading]
  );
}
