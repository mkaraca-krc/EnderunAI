import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { foldTurkish, matchesSearch } from "@/lib/search/fold";

const ROOT = join(__dirname, "..");

/**
 * TÜRKÇE KİP TUZAĞI — GERİLEME BEKÇİSİ.
 *
 * Türkçe kipte "I" harfi noktasız "ı"ya döner. Bu kod tabanında tam
 * olarak bu yüzden:
 *  - "SCHNEIDER" yazarak marka bulunamıyordu,
 *  - "insaat" yazarak "İnşaat" bulunamıyordu (bu sektörde neredeyse
 *    her cari unvanında geçen kelime),
 *  - kodunda "I" geçen proje silinemiyordu,
 *  - "ADMIN" rolündeki kullanıcı bütçe onay düğmesini göremiyordu.
 *
 * ARAMA VE KARŞILAŞTIRMA artık tek kaynaktan (lib/search/fold.ts).
 * GÖSTERİM amaçlı kullanım DOĞRUDUR ve serbesttir: baş harfler, antet
 * unvanı, bildirim metni, dosya adı. Aşağıdaki liste o istisnaları
 * GEREKÇESİYLE tutuyor — yeni bir arama/karşılaştırma bu tuzağa
 * düşerse test düşer.
 */
/**
 * İSTİSNA DOSYA BAZINDA DEĞİL, KULLANIM YERİNDE İŞARETLENİR.
 *
 * İlk sürüm dosya adına göre istisna tutuyordu ve BU BİR AÇIKTI:
 * `personeller/page.tsx` baş harf rozetinde meşru bir gösterim
 * kullanımı taşıdığı için dosyanın TAMAMI muaf oluyordu — aynı
 * dosyaya eklenen gerçek bir arama ihlali görünmez kalıyordu. Sonda
 * bunu gösterdi: aramayı ham kipe geri çevirdim, test geçmeye devam
 * etti.
 *
 * Şimdi her muaf kullanım, kendi satırında ya da bir üst satırda
 * "GÖSTERİM" işareti taşıyor. İşaret yoksa ihlal sayılıyor.
 */
const ISARET = "GÖSTERİM";

/**
 * Yorumları boşlukla değiştirir — SATIR NUMARALARI KORUNARAK.
 *
 * Kuralın kendisi yorumlarda anlatılıyor ("toLocaleLowerCase(\"tr-TR\")
 * kullanılıyordu…"); sayılsalardı bekçi kendi belgesini ihlal sanardı.
 * Satır satır soymak YETMEZ: blok yorumun ORTA satırı tek başına yorum
 * gibi görünmez. Bu yüzden dosya bütün olarak soyuluyor ama satır
 * sayısı bozulmuyor.
 */
function yorumlariAt(kaynak: string): string {
  const bosluklaDegistir = (parca: string) =>
    parca.replace(/[^\n]/g, " ");

  return kaynak
    .replace(/\/\*[\s\S]*?\*\//g, bosluklaDegistir)
    .replace(/\/\/[^\n]*/g, bosluklaDegistir);
}

describe("Türkçe katlama sözleşmesi", () => {
  it("arama ve karşılaştırmada ham tr-TR kipi KALMADI", () => {
    /** Kaynak dosyaları toplar — node sürümünden bağımsız olsun diye elle. */
    function topla(dizin: string, biriken: string[] = []): string[] {
      for (const giris of readdirSync(join(ROOT, dizin), {
        withFileTypes: true,
      })) {
        const yol = `${dizin}/${giris.name}`;

        if (giris.isDirectory()) topla(yol, biriken);
        else if (/\.tsx?$/.test(giris.name)) biriken.push(yol);
      }

      return biriken;
    }

    const dosyalar = ["app", "components", "lib"].flatMap((d) => topla(d));

    const ihlaller: string[] = [];

    for (const dosya of dosyalar) {
      const ham = readFileSync(join(ROOT, dosya), "utf8");
      const satirlar = ham.split("\n");
      const kodSatirlari = yorumlariAt(ham).split("\n");

      kodSatirlari.forEach((kodSatiri, index) => {
        if (!/toLocale(Lower|Upper)Case\("tr-TR"\)/.test(kodSatiri)) return;

        // İşaret kullanım yerinde ya da ONU KURAN ifadenin başında
        // olabilir; zincirli çağrılarda (`const x = (…)\n  .toLocale…`)
        // yorum birkaç satır yukarıda kalır.
        const pencere = satirlar.slice(Math.max(0, index - 3), index + 1);

        if (pencere.some((satir) => satir.includes(ISARET))) return;

        ihlaller.push(`${dosya}:${index + 1}`);
      });
    }

    expect(
      ihlaller,
      "Bu satırlar arama/karşılaştırmada kültüre bağlı kip kullanıyor: " +
        ihlaller.join(", ") +
        ". Arama ve karşılaştırma lib/search/fold.ts üzerinden yapılır " +
        "(foldTurkish / matchesSearch). Gerçekten gösterim amaçlıysa " +
        "kullanım yerine gerekçesiyle bir GÖSTERİM yorumu koyun."
    ).toEqual([]);
  });

  // ---------------------------------------------------------------
  // KOVA BAŞINA GERÇEK SENARYO
  // ---------------------------------------------------------------

  it("ARAMA: büyük harfle yazılan marka bulunuyor", () => {
    // Kültüre bağlı küçültme "SCHNEIDER" -> "schneıder" yapıyordu.
    expect(matchesSearch("schneider", "SCHNEIDER Elektrik A.Ş.")).toBe(true);
    expect(matchesSearch("SCHNEIDER", "schneider elektrik")).toBe(true);
  });

  it("ARAMA: Türkçe karakterle ve ASCII ile aynı sonuç", () => {
    // Kullanıcı arama kutusuna Türkçe karakter yazmak için klavye
    // değiştirmez.
    expect(matchesSearch("sube", "Şube Müdürlüğü")).toBe(true);
    expect(matchesSearch("ŞUBE", "şube müdürlüğü")).toBe(true);
    expect(matchesSearch("cankaya", "ÇANKAYA")).toBe(true);
    expect(matchesSearch("ÇANKAYA", "cankaya")).toBe(true);
  });

  it("ARAMA: bu sektörün en sık kelimesi — İnşaat", () => {
    // Düzeltmeden önce HİÇBİRİ bulunmuyordu: toLowerCase("İ") "i" +
    // birleşik nokta üretiyor ve nokta katlanmıyordu.
    expect(matchesSearch("insaat", "YILMAZ İNŞAAT")).toBe(true);
    expect(matchesSearch("insaat", "Yılmaz İnşaat")).toBe(true);
    expect(matchesSearch("İNŞAAT", "yilmaz insaat")).toBe(true);
    expect(matchesSearch("istanbul", "İSTANBUL ŞUBE")).toBe(true);
  });

  it("KARŞILAŞTIRMA: mükerrer isim katlanmış karşılaştırmayla yakalanır", () => {
    // Aynı kısım "İnşaat" ve "inşaat" olarak iki kez açılmamalı.
    expect(foldTurkish("İnşaat")).toBe(foldTurkish("inşaat"));
    expect(foldTurkish("INSAAT")).toBe(foldTurkish("ınsaat"));

    // Ama gerçekten farklı iki ad eşit sayılmamalı.
    expect(foldTurkish("Kaba İnşaat")).not.toBe(foldTurkish("İnce İnşaat"));
  });

  it("KARŞILAŞTIRMA: anahtar karşılaştırması dile bağımsız", () => {
    // Birim gibi anahtarlar katlanmaz, dile bağımsız karşılaştırılır:
    // "LT" ile "lt" aynı, "LİTRE" ile "litre" AYRI anahtar.
    expect("LT".toLowerCase()).toBe("lt".toLowerCase());
    expect("LİTRE".toLowerCase()).not.toBe("litre".toLowerCase());
  });
});
