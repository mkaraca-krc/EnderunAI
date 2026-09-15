import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import {
  istemciAdresZinciri,
  istemciAdresiniIlet,
} from "@/lib/vekil/istemci-adresi";

/**
 * VEKİL/1 MUHAFIZI (2026-09-15).
 *
 * Vekil rotaları arka uca giden isteğe yeni bir `Headers` kuruyor ve
 * `X-Forwarded-For`u kopyalamıyordu; arka uç her isteği `127.0.0.1`
 * sanıyordu. İki zarar ölçüldü: parola değiştirme kısıtı herkes için
 * tek anahtarda toplanıyordu, ve GÜNLÜK/1'in IP sütunu doğduğu gün
 * anlamsızdı.
 *
 * BU DOSYADAKİ EN ÖNEMLİ TEST `ZINCIRE_EKLEME_YAPMAZ`. Arka uç zincirin
 * SON elemanını okur; buraya kendi adresimizi eklersek son eleman
 * 127.0.0.1 olur ve hız sınırı SESSİZCE çöker. "Daha doğru görünen"
 * bir ekleme, düzeltmenin kendisini iptal eder.
 */
describe("VEKİL/1 — istemci adresi arka uca taşınır", () => {
  it("zinciri AYNEN geçirir — ZINCIRE_EKLEME_YAPMAZ", () => {
    const gelen = new Headers({
      "x-forwarded-for": "203.0.113.9, 78.175.232.135",
    });
    const giden = new Headers();

    istemciAdresiniIlet(gelen, giden);

    // Aynen: ne önüne ne sonuna bir şey eklenmiş olmalı.
    expect(giden.get("x-forwarded-for")).toBe(
      "203.0.113.9, 78.175.232.135"
    );
    // Son eleman gerçek adres olarak KALMALI (arka ucun okuduğu yer).
    const son = giden
      .get("x-forwarded-for")!
      .split(",")
      .map((p) => p.trim())
      .pop();
    expect(son).toBe("78.175.232.135");
    expect(son).not.toBe("127.0.0.1");
  });

  it("zincir yoksa x-real-ip'e düşer", () => {
    const giden = new Headers();
    istemciAdresiniIlet(
      new Headers({ "x-real-ip": "31.223.72.51" }),
      giden
    );
    expect(giden.get("x-forwarded-for")).toBe("31.223.72.51");
  });

  it("hiçbiri yoksa başlık HİÇ konulmaz (boş dizge gönderilmez)", () => {
    const giden = new Headers();
    istemciAdresiniIlet(new Headers(), giden);
    expect(giden.has("x-forwarded-for")).toBe(false);
  });

  it("boş/boşluklu değer yokluk sayılır", () => {
    expect(istemciAdresZinciri(new Headers({ "x-forwarded-for": "   " })))
      .toBeNull();
  });

  it("diğer başlıkları ezmez", () => {
    const giden = new Headers({ authorization: "Bearer jeton" });
    istemciAdresiniIlet(
      new Headers({ "x-forwarded-for": "198.51.100.4" }),
      giden
    );
    expect(giden.get("authorization")).toBe("Bearer jeton");
    expect(giden.get("x-forwarded-for")).toBe("198.51.100.4");
  });
});

/**
 * KAYNAK MUHAFIZI — DIŞLAMA ESASLI.
 *
 * Yeni bir vekil rotası yazan kişi adresi taşımayı unutabilir; bu test
 * unutulduğu anda kırmızı yanar. Kapsam: arka uca `fetch` atan HER
 * `app/api/**\/route.ts`.
 *
 * Gerekçeli istisna isteyen bir rota çıkarsa buraya ADIYLA ve SEBEBİYLE
 * yazılır — sessiz atlama yok.
 */
const GEREKCELI_ISTISNALAR: Record<string, string> = {};

function rotaDosyalari(kok: string): string[] {
  const bulunan: string[] = [];
  for (const girdi of readdirSync(kok, { withFileTypes: true })) {
    const yol = join(kok, girdi.name);
    if (girdi.isDirectory()) bulunan.push(...rotaDosyalari(yol));
    else if (girdi.name === "route.ts") bulunan.push(yol);
  }
  return bulunan;
}

describe("VEKİL/1 kaynak muhafızı", () => {
  const apiKok = join(__dirname, "..", "app", "api");
  const hepsi = rotaDosyalari(apiKok);

  const arkaUcaGidenler = hepsi.filter((yol) => {
    const metin = readFileSync(yol, "utf8");
    return /fetch\(/.test(metin) && /backendApiUrl|BACKEND_URL|targetUrl/.test(metin);
  });

  it("tarama sağlığı: arka uca giden rota BULUNDU (boş küme kanıt değil)", () => {
    // Kural 48: bu satır olmadan aşağıdaki test boş kümede yeşil yanar.
    expect(hepsi.length).toBeGreaterThan(0);
    expect(arkaUcaGidenler.length).toBeGreaterThanOrEqual(5);
  });

  it("arka uca giden her rota istemci adresini iletir", () => {
    const iletmeyenler = arkaUcaGidenler
      .map((yol) => yol.slice(apiKok.length + 1))
      .filter((kisa) => !(kisa in GEREKCELI_ISTISNALAR))
      .filter((kisa) => {
        const metin = readFileSync(join(apiKok, kisa), "utf8");
        return !/istemciAdresiniIlet\s*\(/.test(metin);
      });

    expect(iletmeyenler, `adres taşımayan rota: ${iletmeyenler.join(", ")}`)
      .toEqual([]);
  });

  it("ölü istisna bırakılmaz", () => {
    for (const kisa of Object.keys(GEREKCELI_ISTISNALAR)) {
      expect(
        arkaUcaGidenler.some((y) => y.endsWith(kisa)),
        `istisna artık geçersiz: ${kisa}`
      ).toBe(true);
    }
  });
});
