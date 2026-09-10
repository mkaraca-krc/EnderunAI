import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

const kok = join(__dirname, "..");
const oku = (yol: string) => readFileSync(join(kok, yol), "utf8");

/**
 * CANLI BAĞLANTI SÖZLEŞMESİ (M3/2c-2).
 *
 * Bu dosya soketin GERÇEKTEN kurulduğunu sınamıyor — o tarayıcıda
 * ölçülüyor (`window.__mesajCanliTasima`). Sınadığı şey, bağlantının
 * kurulma ve kapanma YOLLARININ yerinde durması: her biri bir kez
 * kırılmış ya da kırılabilir olduğu için burada duruyor.
 */
describe("canlı mesaj bağlantısı", () => {
  const modul = oku("lib/mesajlasma/canli-baglanti.ts");

  /**
   * TAŞIMA ADI DIŞARIDAN OKUNABİLİR OLMALI.
   *
   * WebSocket kurulamazsa SignalR SESSİZCE LongPolling'e düşer ve
   * ekran çalışıyor görünür. "Çalışıyor" ile "doğru çalışıyor"
   * dışarıdan aynı görünmesin diye seçilen taşımanın adı `window`'a
   * yazılıyor; bu satır silinirse fark ölçülemez hale gelir.
   */
  it("secilen_tasima_adini_window_a_yazar", () => {
    expect(modul).toContain("__mesajCanliTasima");
    // Ad, kütüphanenin günlük satırından ayıklanıyor — sınıf adından
    // DEĞİL: üretim yapısı küçültülürken sınıf adları bozulur.
    expect(modul).toContain("Selecting transport");
  });

  /**
   * JETON URL'E KONMAZ.
   *
   * SignalR'ın yaygın örneği `accessTokenFactory` kullanır ve jetonu
   * sorgu dizesine koyar; oradan erişim kaydına, tarayıcı geçmişine
   * ve vekil sunucu kayıtlarına düşer. Portal anahtarında yaşadığımız
   * sızıntının aynısı olurdu. Kimlik çerezle taşınıyor.
   */
  it("jetonu_sorgu_dizesine_koymaz", () => {
    expect(modul).not.toContain("accessTokenFactory");
    expect(modul).toContain("withCredentials: true");
  });

  /**
   * ÇIKIŞ BAĞLANTIYI KAPATIR.
   *
   * Sunucu tarafındaki `CloseOnAuthenticationExpiration` yalnız JETON
   * SÜRESİNİ dinler; çıkış jetonu süresinden düşürmez. Kapatılmayan
   * soket, aynı makineyi kullanan bir sonraki kişinin ekranına önceki
   * kullanıcının mesajlarını düşürürdü.
   */
  it("cikis_dugmesi_baglantiyi_kapatir", () => {
    const cikis = oku("components/logout-button.tsx");

    // KORUNAN İDDİA: çıkış düğmesi canlı bağlantıyı KAPATIR.
    expect(cikis).toContain("canliBaglantiyiKapat");

    /*
     * POZİTİF KONTROL: dosya gerçekten çıkış akışı — yalnız adı geçen
     * bir dosyayı okumuş olmayalım.
     *
     * ÇIPA TAŞINDI (GÜNLÜK/1, 2026-09-10): eskiden `/api/auth/logout`
     * metni aranıyordu. Çıkış çağrısı `lib/auth/cikis.ts` ortak
     * modülüne alındı (iki çağıran aynı sebep kümesini kullansın diye)
     * ve adres artık bu dosyada geçmiyor.
     *
     * KORUNAN İDDİA DEĞİŞMEDİ — yalnız çıpa, çağrının YENİ ADINA
     * bağlandı. `cikisIstegi` çıkış akışına özgü; başka bir dosyada
     * tesadüfen bulunmaz.
     */
    expect(cikis).toContain("clearCurrentUserCache");
    expect(cikis).toContain("cikisIstegi");
  });

  /**
   * BAĞLANTI BALONCUKTA KURULUR — PANELDE DEĞİL (ya da SADECE panelde
   * değil).
   *
   * Panel yalnız AÇIKKEN monte. Bağlantı yalnız orada kurulsaydı
   * panel kapalıyken hiç mesaj gelmez ve rozet hiç artmazdı — yani
   * rozetin tek işe yaradığı durumda çalışmazdı.
   */
  it("baglanti_baloncukta_kurulur", () => {
    const baloncuk = oku("components/mesajlar/mesaj-baloncugu.tsx");

    expect(baloncuk).toContain("canliBaglantiyiBaslat");
    expect(baloncuk).toContain("canliMesajDinle");
  });

  /**
   * YENİDEN BAĞLANMA ARALIKLARI ARTAN OLMALI.
   *
   * Sabit kısa aralık, safe-deploy sırasında ayağa kalkan servisi
   * her açık sekmenin aynı anda vurmasıyla ikinci kez düşürebilirdi.
   */
  it("yeniden_baglanma_araliklari_artan", () => {
    const eslesme = /withAutomaticReconnect\(\[([^\]]+)\]\)/.exec(modul);
    expect(eslesme).not.toBeNull();

    const araliklar = eslesme![1]
      .split(",")
      .map((x) => Number(x.trim()))
      .filter((x) => Number.isFinite(x));

    expect(araliklar.length).toBeGreaterThanOrEqual(3);

    for (let i = 1; i < araliklar.length; i += 1) {
      expect(araliklar[i]).toBeGreaterThan(araliklar[i - 1]);
    }
  });

  /**
   * OKUNMAMIŞ SAYISI SUNUCUDAN OKUNUR, YEREL SAYAÇLA TUTULMAZ.
   *
   * Yerel sayaç üç yerde yanlışa düşerdi: başka sekmede okunan mesaj
   * düşmez, kaçırılan yayın hiç eklenmez, yeniden bağlanma
   * aralığında gelenler kaybolur.
   */
  it("rozet_sunucudan_toplanir", () => {
    const baloncuk = oku("components/mesajlar/mesaj-baloncugu.tsx");

    // Çağrı satır sonuna sarılabildiği için düz metin değil, boşluğa
    // dayanıksız bir desen aranıyor.
    expect(baloncuk).toMatch(/messagingService\s*\.\s*konusmalar\(\)/);
    expect(baloncuk).toContain("okunmamisSayisi");
  });
});
