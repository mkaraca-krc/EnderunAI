import { redirect } from "next/navigation";

/**
 * Sertifika takibi İSG modülüne taşındı.
 *
 * Bu sayfa var olmayan bir uca (/api/hr/certificates) bağlı bir
 * taslaktı; menüde duruyordu ama hiçbir veri gösteremiyordu. Gerçek
 * kayıtlar IsgCertificate tablosunda tutuluyor.
 */
export default function Page() {
  redirect("/isg/personel");
}
