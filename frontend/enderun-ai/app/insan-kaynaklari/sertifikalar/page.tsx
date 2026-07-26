import HrModulePage from "@/components/hr/hr-module-page";

export default function Page() {
  return (
    <HrModulePage
      title="Sertifika Yönetimi"
      description="Personel sertifika geçerlilik ve yenileme takibi"
      icon="□"
      apiEndpoint="/api/hr/certificates"
      features={[
        "Sertifika tanımları",
        "Personel sertifika kayıtları",
        "Belge doğrulama",
        "Süre sonu uyarıları",
        "Proje giriş yeterlilikleri",
      ]}
    />
  );
}
