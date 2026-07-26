import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Kariyer Yönetimi"
      description="Terfi, görev, departman, proje ve maaş değişikliği geçmişi"
      icon="↑"
      apiEndpoint="/api/hr/career"
      features={[
        "İşe giriş geçmişi",
        "Terfi ve pozisyon değişikliği",
        "Departman ve proje değişikliği",
        "Maaş değişikliği",
        "Kariyer ve terfi analizi",
      ]}
    />
  );
}
