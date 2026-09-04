import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * PERSONEL EKRANI — DEPARTMAN SÖZLEŞMESİ.
 *
 * Bu testler ekranın DAVRANIŞINI değil, kaybolduğunda sessiz kalacak
 * üç KARARINI sabitliyor. Üçü de bir kez kaybolursa kimse fark etmez:
 * ekran yine açılır, yine çalışır, yalnız yanlış çalışır.
 */

const KOK = join(__dirname, "..");
const EKRAN = join(KOK, "app", "insan-kaynaklari", "personeller", "page.tsx");
const SERVIS = join(KOK, "services", "personnel.service.ts");

const ekran = readFileSync(EKRAN, "utf8");
const servis = readFileSync(SERVIS, "utf8");

describe("personel ekranı departman sözleşmesi", () => {
  it("kaynak dosyalar okunabiliyor — POZİTİF KONTROL", () => {
    /*
     * Yol bozulursa aşağıdaki testler "metin bulunamadı" diye değil,
     * sessizce boş dosyada arayarak yanlış cevap verirdi. Kural 48:
     * boş sonuç yokluğun kanıtı değildir.
     */
    expect(ekran.length).toBeGreaterThan(5000);
    expect(servis.length).toBeGreaterThan(1000);
    expect(ekran).toContain("insan-kaynaklari");
  });

  it("yanıltıcı 'Departman / Pozisyon' başlığı geri gelmiyor", () => {
    /*
     * BU BAŞLIK AYLARCA YANLIŞTI: kolon "Departman / Pozisyon"
     * yazıyordu ama `profession` (meslek) ve `jobTitle` gösteriyordu.
     * Personelin gerçek departmanı hiç görünmüyordu — ve canlıda 79
     * personelin hiçbirinde dolu değildi. Ekranın "Departman" yazan
     * bir kolonda başka bir şey göstermesi, boşluğun fark
     * edilmemesinin sebeplerinden biriydi.
     *
     * Başlık geri gelirse aynı yanılgı geri gelir.
     */
    /*
     * BAŞLIĞIN KENDİSİ ARANIYOR, DİZE DEĞİL: dosya bu yanılgıyı
     * ANLATAN bir yorum taşıyor ve o yorum dizeyi içeriyor. Serbest
     * metin araması, kaydı yazmayı imkânsız kılardı — hatayı
     * açıklamak, hatayı geri getirmek sayılırdı.
     */
    expect(ekran).not.toContain("<TableHead>Departman / Pozisyon</TableHead>");
    expect(ekran).toContain("<TableHead>Meslek / Pozisyon</TableHead>");
    expect(ekran).toContain("<TableHead>Departman</TableHead>");
  });

  it("departman seçicisinde BOŞ seçenek var", () => {
    /*
     * Boş seçenek departmandan ÇIKARMANIN tek yolu. Kaldırılırsa
     * yanlış atanmış bir personel düzeltilemez hale gelir — ekran
     * çalışmaya devam eder, yalnız tek yönlü olur.
     */
    expect(ekran).toContain("— Departman yok —");
  });

  it("departman ataması sürüm damgası GÖNDERİYOR", () => {
    /*
     * `recordVersion` çıkarılırsa eşzamanlılık koruması sessizce
     * kapanır: iki kişi aynı listeyi açtığında ikincisinin ataması
     * birincisininkini izsiz ezer. Uç sürümü ZORUNLU tutuyor, ama
     * ekran onu göndermeyi bırakırsa kullanıcı her denemede hata alır
     * — yani sessizlik değil, kırılma. Bu test ikisini de tutuyor.
     */
    expect(servis).toContain("recordVersion");
    expect(ekran).toContain("recordVersion: item.recordVersion");
  });

  it("toplu atama SÜZÜLENLERİ seçiyor, tüm listeyi değil", () => {
    /*
     * Kullanıcı önce süzer (ör. meslek = SAHA GÖREVLİSİ), sonra
     * "hepsini seç" der. Süzgeci yok sayan bir "hepsini seç",
     * GÖRMEDİĞİ satırları da değiştirirdi — ve bu, 79 kişilik bir
     * listede fark edilmesi en zor hata türü.
     */
    expect(ekran).toContain("filteredItems.map((x) => x.id)");
    expect(ekran).not.toContain("items.map((x) => x.id)");
  });

  it("toplu atamada KISMİ BAŞARISIZLIK sessiz geçmiyor", () => {
    /*
     * 40 satır tek tek uygulanırken biri düşerse (ör. sürüm
     * çakışması) "tamamlandı" demek yanlış olur. Başarısızlar seçili
     * kalıyor ve sayısı yazılıyor — yeniden denemenin hazır hâli.
     */
    expect(ekran).toContain("BAŞARISIZ");
    expect(ekran).toContain("Başarısız satırlar seçili bırakıldı");
  });

  it("toplu atama TEK SATIR UCUNU kullanıyor, toplu uç yok", () => {
    /*
     * Toplu bir uç açmak İKİNCİ BİR YAZMA YOLU doğururdu ve her
     * satırın kendi sürüm damgası olduğu için ya sürüm kontrolünü
     * atlamak ya da onu ikinci kez yazmak zorunda kalırdı.
     *
     * Bu kod tabanının en sık hatası ikinci yazma yolu — bir günde
     * altı kez görüldü.
     */
    expect(ekran).toContain("personnelService.setDepartment");
    expect(servis).not.toContain("setDepartmentBulk");
    expect(servis).not.toContain("departman/toplu");
  });

  it("meslek süzgecinin yanıltıcı yer tutucusu geri gelmiyor", () => {
    /*
     * Süzgeç "Tüm departman / meslekler" yazıyordu ama YALNIZ meslek
     * süzüyordu — kolon başlığındaki aynı yanılgının süzgeçte kalmış
     * hâli. Departman ayrı bir alan.
     */
    expect(ekran).not.toContain('placeholder="Tüm departman / meslekler"');
    expect(ekran).toContain('placeholder="Tüm meslekler"');
  });

  it("kaydetme başarısız olduğunda ekranda hata gösteriliyor", () => {
    /*
     * Satır içi seçici, kaydetme başarısız olduğunda eski değerine
     * döner. Şerit olmasa kullanıcı atamanın yapıldığını sanırdı —
     * sessiz başarısızlık.
     */
    expect(ekran).toContain("departmentError");
    expect(ekran).toContain("Departman ataması kaydedilemedi.");
  });
});
