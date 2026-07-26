import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="İşe Alım Yönetimi"
      description="Aday başvurusundan personel kaydına kadar işe alım süreçleri"
      icon="+"
      apiEndpoint="/api/hr/recruitment"
      features={[
        "Aday kayıtları",
        "İş ilanları ve pozisyon ihtiyaçları",
        "Mülakat planlama",
        "Teklif ve onay süreci",
        "İşe giriş işlemleri",
      ]}
    />
  );
}
