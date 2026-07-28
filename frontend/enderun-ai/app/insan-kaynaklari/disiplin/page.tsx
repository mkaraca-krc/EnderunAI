import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Disiplin Yönetimi"
      description="Savunma, inceleme, karar ve disiplin kayıtları"
      icon="⚖"
      apiEndpoint="/api/hr/disciplinary"
      features={[
        "Disiplin olayı kaydı",
        "Savunma talebi",
        "İnceleme süreci",
        "Karar ve yaptırım",
        "Disiplin geçmişi",
      ]}
    />
  );
}
