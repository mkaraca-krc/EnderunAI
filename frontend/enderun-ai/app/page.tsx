import { redirect } from "next/navigation";

/**
 * Kök yol gerçek dashboard'a yönlendirilir.
 *
 * Burada eskiden sabit "Hoş geldiniz, Mehmet Bey" başlıklı bir tanıtım
 * maketi duruyordu; başlıktaki ad her kullanıcıya aynı görünüyordu ve
 * sayfadaki rakamların tamamı (12,65 Mn tahsilat, 9 aktif proje, sabit
 * proje ilerlemeleri) uydurmaydı. Giriş yapmış kullanıcı bunu gerçek
 * veri sanabildiği için maket kaldırıldı.
 */
export default function Home() {
  redirect("/dashboard");
}
