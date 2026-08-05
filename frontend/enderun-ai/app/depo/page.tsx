import { redirect } from "next/navigation";

/**
 * Depo modülünün gerçek adresi /depo-stok.
 *
 * Burası eski AppShell'i kullanan, hiçbir veriye bağlı olmayan bir
 * taslaktı; menüde bağlantısı yoktu ama adres elle yazıldığında
 * "modül hazırlanıyor" diyen ölü bir sayfa açılıyordu.
 */
export default function Page() {
  redirect("/depo-stok");
}
