import { describe, expect, it } from "vitest";

import { MENU_GROUPS } from "@/lib/navigation/menu";

/*
 * NEDEN VAR: Genel Müdür `/yapilacaklar` ekranını açtı ve "burda görev
 * veya emir yazılacak bir yer yok" dedi. Haklıydı — ama oluşturma formu
 * 26 Temmuz'dan beri `/gorevler`de duruyordu.
 *
 * KÖK SEBEP BİR ADLANDIRMA ARIZASIYDI: menüde "Yapılacaklar" ve
 * "Görevler" yan yana duruyordu ve bu ikisi Türkçede EŞ ANLAMLI.
 * Kullanıcının hangisinin gelen kutusu, hangisinin iş emri kütüğü
 * olduğunu anlamasını sağlayan hiçbir işaret yoktu; üstte olana bastı.
 *
 * Aynı ekran menüde İKİ AYRI ADLA da duruyordu: "Onay Merkezi"
 * (/onay-merkezi) ile "Yapılacaklar" (/yapilacaklar) — birincisi
 * ikincisine yönlendiriyor.
 *
 * Bu bekçi Kural 69'un mekanik karşılığıdır.
 *
 * DÜRÜST SINIR: eş anlamlılık bir kelime listesinden okunuyor. Listede
 * olmayan yeni bir eş anlamlı çift bu testten geçer. Liste, arıza
 * tekrar ettikçe büyür — bu bir tam kanıt değil, bilinen çiftlere
 * karşı bir cırcırdır.
 */

/** Türkçede birbirinin yerine geçebilen menü sözcükleri. */
const ES_ANLAMLI_KUMELER: string[][] = [
  ["yapılacaklar", "görevler", "işler", "yapılacak işler"],
  ["onay merkezi", "onaylar", "bekleyen onaylar"],
  ["belgeler", "dokümanlar", "evraklar"],
];

function normalize(x: string): string {
  return x.trim().toLocaleLowerCase("tr-TR");
}

describe("menü adlandırması", () => {
  it("tarama boşa düşmüyor", () => {
    const toplamOge = MENU_GROUPS.flatMap((g) => g.items).length;

    // POZİTİF KONTROL: menü boşalır ya da içe aktarım bozulursa
    // aşağıdaki iddiaların hepsi boş kümede sessizce yeşil kalırdı.
    expect(MENU_GROUPS.length).toBeGreaterThan(10);
    expect(toplamOge).toBeGreaterThan(50);
  });

  it("iki menü girişi eş anlamlı ada sahip değil", () => {
    const etiketler = MENU_GROUPS.flatMap((g) =>
      g.items.map((i) => ({ label: normalize(i.label), href: i.href })),
    );

    const ihlaller: string[] = [];

    for (const kume of ES_ANLAMLI_KUMELER) {
      const bulunan = etiketler.filter((e) => kume.includes(e.label));

      if (bulunan.length > 1) {
        ihlaller.push(
          `eş anlamlı: ${bulunan
            .map((b) => `"${b.label}" (${b.href})`)
            .join(" ile ")}`,
        );
      }
    }

    expect(ihlaller, ihlaller.join("\n")).toEqual([]);
  });

  it("bir ekran menüde birden fazla girişle görünmüyor", () => {
    /*
     * BİLİNEN ÇAPRAZ LİSTELEMELER — KASITLI OLABİLİR, KARAR BEKLİYOR.
     *
     * Bu muhafız ilk koşusunda üç tekrar buldu. Biri gerçek arızaydı ve
     * bu pakette kaldırıldı: "Onay Merkezi" ile "Bekleyen İşler" aynı
     * ekranı gösteriyordu.
     *
     * Kalan ikisi FARKLI BİR ŞEY olabilir: aynı ekran iki DEPARTMANIN
     * menüsünde duruyor, çünkü iş gerçekten iki departmanı da
     * ilgilendiriyor. Mal kabul hem satın almanın hem deponun işidir.
     *
     * Bunları tek taraflı silmedim: satın alma ve depo kullanıcılarının
     * gezinme yolunu bu paketin kapsamı dışında değiştirmek olurdu.
     * Karar Mehmet'te; karar verilene kadar burada AÇIKÇA duruyorlar.
     *
     * Liste büyümemeli. Yeni bir tekrar eklenirse bu test kırmızı olur
     * ve buraya yazılması için gerekçe ister.
     */
    const BILINEN_CAPRAZ = new Set([
      "/raporlar", // Rapor Merkezi (muhasebe) + Raporlar (belgeler)
      "/depo-stok/mal-kabul", // Mal Kabul (satın alma) + Mal Kabul (depo)
    ]);

    const sayac = new Map<string, string[]>();

    for (const grup of MENU_GROUPS) {
      for (const oge of grup.items) {
        const yol = oge.href.split("?")[0];
        sayac.set(yol, [...(sayac.get(yol) ?? []), oge.label]);
      }
    }

    const tekrarlar = [...sayac.entries()]
      .filter(([yol, etiketler]) => etiketler.length > 1 && !BILINEN_CAPRAZ.has(yol))
      .map(([yol, etiketler]) => `${yol} → ${etiketler.join(", ")}`);

    expect(tekrarlar, tekrarlar.join("\n")).toEqual([]);
  });

  it("menü grubu anahtarı tekil — kabuk onu React anahtarı yapıyor", () => {
    /*
     * erp-shell.tsx:493 `key={group.key}` kullanıyor. Aynı anahtar iki
     * kardeş düğümde olursa React uzlaştırmayı şaşırır; kullanıcı
     * tarafında da yan menüde aynı başlık iki kez görünür.
     */
    const anahtarlar = MENU_GROUPS.map((g) => g.key);
    const etiketler = MENU_GROUPS.map((g) => normalize(g.label));

    expect(new Set(anahtarlar).size, `tekrarlı grup anahtarı: ${anahtarlar}`)
      .toBe(anahtarlar.length);
    expect(new Set(etiketler).size, `tekrarlı grup etiketi: ${etiketler}`)
      .toBe(etiketler.length);
  });
});
