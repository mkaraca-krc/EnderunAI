import { apiClient } from "@/lib/api/api-client";

/**
 * İSTEMCİ HATASI BİLDİRİMİ.
 *
 * NEDEN GEREKLİ: hata sınırı kullanıcıya bir ekran gösteriyor ama
 * kimse haberdar olmuyordu. Kullanıcı "bir şeyler ters gitti" görüp
 * başka bir ekrana geçtiğinde olay kayıtsız kayboluyor; aynı hata
 * yüz kişide olsa bile kimse bilmiyor.
 *
 * KAYDA NE GİDER — YALNIZ YAPISAL BİLGİ:
 *   nerede   → "kabuk" / "içerik"
 *   hataAdi  → "TypeError"
 *   mesaj    → 200 karaktere kısaltılmış
 *   yol      → tarayıcıdaki yol (sunucu ayrıca maskeliyor)
 *
 * NE GİTMEZ: kullanıcı adı, e-posta, tutar, IBAN, cari unvanı, form
 * içeriği, bileşen yığını. Kullanıcının kim olduğunu sunucu zaten
 * oturumdan biliyor; istemcinin ayrıca göndermesi günlüğe kişisel
 * veri taşıma yolu açardı.
 */
export type IstemciHatasi = {
  nerede: string;
  hataAdi: string;
  mesaj: string;
  yol: string;
};

/**
 * BİLDİRİM SESSİZ BAŞARISIZ OLUR — VE BU BİLEREK BÖYLE.
 *
 * Kullanıcı zaten bir hata ekranına bakıyor. Bildirim de patlarsa
 * (ağ yok, oturum düşmüş, uç 500 dönüyor) ikinci bir hata fırlatmak
 * hata sınırının kendisini döngüye sokardı: `componentDidCatch`
 * içinden atılan hata yakalanmaz, ağacı tekrar söker.
 *
 * `void` ile ayrılıyor: çağıran beklemiyor. Beklenseydi hata ekranı
 * ağ turu kadar geç görünürdü.
 */
export function istemciHatasiBildir(hata: IstemciHatasi): void {
  void apiClient<void>("istemci-hatalari", {
    method: "POST",
    body: hata,
  }).catch(() => {
    /* Yutuluyor — gerekçe yukarıda. */
  });
}
