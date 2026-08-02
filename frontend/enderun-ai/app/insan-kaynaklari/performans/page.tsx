import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Performans Yönetimi"
      description="Personel değerlendirme, hedef ve gelişim süreçleri"
      icon="↗"
      apiEndpoint="/api/hr/performance"
      features={[
        "Dönemsel değerlendirme",
        "Performans puanları",
        "İSG ve disiplin puanları",
        "Gelişim planları",
        "Terfi adayı analizi",
      ]}
    />
  );
}
