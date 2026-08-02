import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Yetkinlik Yönetimi"
      description="Personel yetkinlikleri ve proje ihtiyaçlarının eşleştirilmesi"
      icon="★"
      apiEndpoint="/api/hr/competencies"
      features={[
        "Yetkinlik tanımları",
        "Personel yetkinlik seviyeleri",
        "Doğrulama ve geçerlilik",
        "Proje yetkinlik ihtiyaçları",
        "Uygun personel analizi",
      ]}
    />
  );
}
