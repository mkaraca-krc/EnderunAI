import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * KIRPILMIŞ LİSTE TOPLAM DİYE GÖSTERİLEMEZ.
 *
 * NEDEN VAR: poz kütüphanesi ekranı uçtan gelen diziyi sayıp
 * "Toplam Poz — Kütüphanedeki kayıtlar" diye gösteriyordu. Uç ise
 * varsayılan olarak yalnız 100 kayıt döndürüyor. Sonuç: canlıda
 * 23.531 poz varken ekranda 100 yazıyordu ve kullanıcı 101. pozun
 * var olmadığını sanıyordu.
 *
 * Bu hata sessiz: ekran çalışıyor, sayı biçimli, hiçbir uyarı yok.
 * Yalnızca YANLIŞ. Kırpma uygulayan uçları tüketen ekranlar bu yüzden
 * toplamı uçtan almak ve kırpıldığını söylemek zorunda.
 */

const ROOT = join(__dirname, "..");
const read = (path: string) => readFileSync(join(ROOT, path), "utf8");

describe("kırpılmış liste dürüstlüğü", () => {
  const pozlar = read("app/muhendislik/pozlar/page.tsx");

  it("poz ekranı toplamı listeden saymaz", () => {
    // `total: items.length` ASIL KUSURDU. Geri gelirse burada düşer.
    expect(pozlar).not.toMatch(/total:\s*items\.length/);
  });

  it("poz ekranı toplamı uçtan alır", () => {
    expect(pozlar).toMatch(/setTotal\(result\.total\)/);
    expect(pozlar).toMatch(/setHasMore\(result\.hasMore\)/);
  });

  it("poz ekranı kırpıldığını kullanıcıya söyler", () => {
    expect(pozlar).toMatch(/hasMore\s*&&/);
    expect(pozlar).toContain("gösteriliyor");
  });

  it("poz servisi sayfalı yanıt tipini kullanır", () => {
    const service = read("services/engineering-position.service.ts");

    expect(service).toContain("PagedPositions");
    // Düz dizi tipine dönmek, toplamın kaybolması demek.
    expect(service).not.toMatch(
      /apiClient<EngineeringPositionListItem\[\]>\(\s*`engineering-positions/
    );
  });

  it("kırpan uçları tüketen ekranlar toplamı uçtan alır", () => {
    // Her biri FAZ 1'de dönüştürülen bir ucu tüketiyor. Sayıyı listeden
    // türetmek, uç kırptığı anda sessizce yanlış olur.
    const kirpanEkranlar: [string, RegExp][] = [
      ["app/sistem-yonetimi/denetim-kayitlari/page.tsx", /setTotal\(result\.total\)/],
      ["app/sistem-yonetimi/erisim-talepleri/page.tsx", /setPendingTotal\(result\.total\)/],
      ["app/muhendislik/receteler/page.tsx", /setRecipeTotal\(recipeItems\.total\)/],
      ["app/teklifler/fiyatlar/page.tsx", /setMatchTotal\(result\.total\)/],
      ["app/dashboard/page.tsx", /setPendingAccessRequests\(requests\.total\)/],
    ];

    for (const [path, beklenen] of kirpanEkranlar) {
      expect(read(path), `${path} toplamı uçtan almalı`).toMatch(beklenen);
    }
  });

  it("kırpan uçları tüketen ekranlar kırpılmayı söyler", () => {
    const uyariVerenler = [
      "app/sistem-yonetimi/denetim-kayitlari/page.tsx",
      "app/sistem-yonetimi/erisim-talepleri/page.tsx",
      "app/muhendislik/receteler/page.tsx",
      "app/teklifler/fiyatlar/page.tsx",
    ];

    for (const path of uyariVerenler) {
      expect(read(path), `${path} hasMore'u kullanmalı`).toMatch(/hasMore/);
    }
  });

  it("sayaç kartı kırpılmış listeden saymaz", () => {
    // ASIL KUSURUN İMZASI: uçtan gelen diziyi sayıp toplam diye
    // göstermek. Dönüştürülen ekranlarda bu desen kalmamalı.
    expect(read("app/sistem-yonetimi/denetim-kayitlari/page.tsx"))
      .not.toMatch(/<Badge>\{events\.length\}<\/Badge>/);

    expect(read("app/muhendislik/receteler/page.tsx"))
      .not.toMatch(/<strong>\{loading \? "…" : recipes\.length\}<\/strong>/);
  });

  it("poz seçicileri kırpılmayı gizlemez", () => {
    const picker = read("components/engineering/position-picker.tsx");
    expect(picker).toMatch(/rows\.hasMore/);

    const teklif = read("app/teklifler/yeni/page.tsx");
    // Açılır liste ucun tavanını AÇIKÇA istemeli; sessiz varsayılan
    // 100, 23.530 aktif poz içinden seçim yaptırıyordu.
    expect(teklif).toMatch(/getAll\(\{\s*status:\s*1,\s*take:\s*500\s*\}\)/);
    expect(teklif).toMatch(/positionsTruncated/);
  });
});
