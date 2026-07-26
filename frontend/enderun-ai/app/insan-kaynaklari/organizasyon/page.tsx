import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="İK Organizasyon Yönetimi"
      description="Departman, pozisyon ve organizasyon yapısının yönetimi"
      icon="▤"
      apiEndpoint="/api/hr/organization"
      features={[
        "Departman tanımları",
        "Pozisyon ve görev tanımları",
        "Organizasyon hiyerarşisi",
        "Şube ve şirket bazlı yapı",
        "Organizasyon raporları",
      ]}
    />
  );
}
