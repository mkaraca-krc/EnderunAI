import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Eğitim Yönetimi"
      description="Personel eğitim planları, katılım ve başarı takibi"
      icon="◇"
      apiEndpoint="/api/hr/trainings"
      features={[
        "Eğitim tanımları",
        "Personel eğitim planı",
        "İSG eğitimleri",
        "Katılım ve başarı durumu",
        "Geciken eğitim uyarıları",
      ]}
    />
  );
}
