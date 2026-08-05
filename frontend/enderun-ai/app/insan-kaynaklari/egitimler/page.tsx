import { redirect } from "next/navigation";

/**
 * Eğitim takibi İSG modülüne taşındı.
 *
 * Bu sayfa var olmayan bir uca (/api/hr/trainings) bağlı bir taslaktı;
 * menüde duruyordu ama hiçbir veri gösteremiyordu. Gerçek kayıtlar
 * IsgTraining tablosunda tutuluyor.
 */
export default function Page() {
  redirect("/isg/personel");
}
