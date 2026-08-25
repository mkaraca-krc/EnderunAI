import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * KAPSAM AÇIĞI — AÇIK TARAFA DÜŞEN SÜZGEÇ.
 *
 * DESEN: sunucudan gelen bir listeyi kapsam alanına göre süzerken
 * `!item.companyId || item.companyId === companyId` yazmak. Alan
 * gelmezse satır listeye GİRER — yani varsayılan AÇIK.
 *
 * Şirket izolasyonunda varsayılan KAPALI olmalı; bu, yorum kapısının
 * "bilinmeyen tipte kapalı düşer" kuralının aynısı (DURUM.md M1/7-0).
 *
 * TETİKLEYİCİ — G3 NÖBETÇİSİYLE AYNI: bugün canlıda tek şirket var
 * ve tüm aktif kullanıcılar global kapsamlı, o yüzden bu desen
 * zararsız. İkinci şirket açıldığı ya da kapsamı sınırlı bir
 * kullanıcı tanımlandığı gün sızdırır. `ScopeDeferralWatchdog` tam
 * olarak bu iki koşulu izliyor.
 *
 * KULLANICININ KENDİ SEÇİMİ BU SINIFA GİRMEZ: `!form.companyId ||`
 * deseni "kullanıcı henüz şirket seçmedi, hepsini göster" demektir
 * ve altındaki liste sunucudan zaten kapsamla gelmiştir. İlk ölçüm
 * 22 isabet vermişti; ayıklayınca sunucu verisi üzerinde açık tarafa
 * düşen 2 tane çıktı, ikisi de kapatıldı.
 */

const KOK = join(__dirname, "..");
const DIZINLER = ["app", "components", "services", "lib"];

/** Sunucu verisi üzerinde açık tarafa düşen süzgeç. */
const DESEN = /!(\w+)\.(companyId|projectId|branchId|siteId)\s*\|\|/;

/** Kullanıcının form seçimi — kapsam süzgeci değil. */
const FORM_DEGISKENLERI = /^(form|bulkForm|filters|filter|draft|yeni)$/;

function kaynaklar(dizin: string): string[] {
  const bulunan: string[] = [];

  let girdiler: string[];
  try {
    girdiler = readdirSync(dizin);
  } catch {
    return bulunan;
  }

  for (const girdi of girdiler) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...kaynaklar(yol));
      continue;
    }

    if (/\.(ts|tsx)$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

function bulgular(): string[] {
  const sonuc: string[] = [];

  for (const d of DIZINLER) {
    for (const yol of kaynaklar(join(KOK, d))) {
      const satirlar = readFileSync(yol, "utf8").split("\n");

      satirlar.forEach((satir, i) => {
        // Yorum satırları sayılmaz: bu dosyada da deseni ANLATAN
        // yorumlar var ve onlar kusur değil.
        const kirpik = satir.trim();
        if (kirpik.startsWith("*") || kirpik.startsWith("//")) return;

        const m = DESEN.exec(satir);
        if (!m) return;
        if (FORM_DEGISKENLERI.test(m[1])) return;

        sonuc.push(`${relative(KOK, yol)}:${i + 1}`);
      });
    }
  }

  return sonuc;
}

describe("kapsam açığı — açık tarafa düşen süzgeç", () => {
  it("tarama boşa düşmüyor", () => {
    const hepsi = DIZINLER.flatMap((d) => kaynaklar(join(KOK, d)));
    expect(hepsi.length).toBeGreaterThan(300);
  });

  it("sunucu verisinde açık tarafa düşen süzgeç yok", () => {
    expect(
      bulgular(),
      "Bu satırlar kapsam alanı EKSİKSE satırı listeye ALIYOR. " +
        "Şirket izolasyonunda varsayılan KAPALI olmalı: alan yoksa " +
        "satır elenir. `!x.companyId || x.companyId === y` yerine " +
        "`x.companyId === y` yazın.\n" + bulgular().join("\n")
    ).toEqual([]);
  });
});
