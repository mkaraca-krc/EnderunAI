import { redirect } from "next/navigation";

/**
 * ONAY MERKEZİ → YAPILACAKLAR.
 *
 * Onay kuyrukları artık `/yapilacaklar` ekranının üst bölümünde, görev
 * onaylarıyla TEK LİSTEDE gösteriliyor: kullanıcı iki ayrı "bekleyen
 * iş" listesine bakmak zorunda kalmasın.
 *
 * ADRES KORUNUYOR: yer imi ya da eski bir bağlantı kırılmasın diye
 * sayfa silinmedi, yönlendirmeye çevrildi.
 *
 * ESKİ EKRANIN İÇERİĞİ git geçmişinde duruyor; toplu onay/ret
 * yeteneği bu yönlendirmeyle birlikte kalktı ve bu bilinçli bir
 * karardır — onay/ret artık kaydın kendi ekranından yapılıyor, orada
 * kararın dayandığı bütün ayrıntı (kalemler, tutarlar, geçmiş)
 * görünüyor. Listeden tek tıkla onaylamak, bakmadan onaylamayı
 * kolaylaştırıyordu.
 */
export default function OnayMerkeziYonlendirme() {
  redirect("/yapilacaklar");
}
