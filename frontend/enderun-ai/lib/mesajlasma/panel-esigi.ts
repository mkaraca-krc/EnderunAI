/**
 * MESAJ PANELİ GENİŞLİK EŞİĞİ — KANONİK DEĞER BURADA.
 *
 * ═══ NEDEN İKİ YERDE DURUYOR ═══
 *
 * Bu sayı hem TypeScript'te (baloncuk `/mesajlar`'a yönlendirsin mi)
 * hem CSS'te (`@media` panel kutusunu gizlesin mi) gerekiyor.
 *
 * **CSS medya sorgusu `var()` OKUYAMAZ** — `@media (max-width: var(--x))`
 * geçersizdir. Yani CSS tarafındaki sayı her hâlükârda literal kalır ve
 * "tek dosyada tut" mümkün değil.
 *
 * ═══ O HÂLDE NE YAPILDI ═══
 *
 * Kanonik değer burası. `globals.css`teki literalin buna EŞİT kaldığını
 * `panel-esigi-tek-kaynak.test.ts` tutuyor: test CSS'i okuyup literali
 * çıkarıyor ve bu sabitle karşılaştırıyor.
 *
 * **Mehmet, 2026-09-07:** *"İki yerde durmaya devam edecekler ama
 * sessizce ayrışamayacaklar. Bu hafta üç kez ödediğimiz bedel 'iki
 * yerde olması' değil, 'ayrıştığını kimsenin görmemesi'ydi."*
 *
 * ═══ DEĞER NEDEN 900 ═══
 *
 * Ölçülerek seçildi (2026-09-06): `.mesaj-duzen` zaten 900px'te iki
 * sütundan tek sütuna geçiyor. Yeni bir eşik, aynı ekranın iki farklı
 * noktada kırılması demekti.
 */
export const PANEL_DAR_EKRAN_ESIGI = 900;
