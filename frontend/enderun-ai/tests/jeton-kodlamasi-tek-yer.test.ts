import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * JETON KODLAMASININ TEK YORUMLAYICISI — MUHAFIZ (JETON/1 · Ş1).
 *
 * Üç alan adı (`all_permissions`, `not_permissions`, `permissions`)
 * yalnız yorumlayıcı dosyalarda geçebilir. Başka bir yerde geçmesi,
 * kodlamanın ikinci bir karar yeri kazandığı anlamına gelir.
 *
 * NEDEN: üç kodlama üç yere dağılırsa biri güncellenip ötekiler
 * kalır. Bu programda tam olarak bu yaşandı — çek toplamlarında,
 * `includeVoided` süzgecinde, `RoleCatalog`ta. Jetonda aynısı olursa
 * sonucu SESSİZ bir yetki hatasıdır: `all_permissions` bayrağını tek
 * başına okuyan bir tüketici, yanındaki `not_permissions` listesini
 * görmez ve kullanıcıya olmayan bir yetki verir.
 *
 * HER İKİ TARAF DA TARANIYOR. Arka uç ve ön yüz ayrı çalışma
 * ortamları olduğu için iki yorumlayıcı var; ama her tarafta TEK
 * yer. Yalnız ön yüz taransaydı, arka uçtaki ikinci bir okuma
 * görülmezdi.
 *
 * YORUMLAR ELENİYOR: bir yorum karar veremez. Alan adını AÇIKLAYAN
 * bir yorum, onu OKUYAN bir kod değildir. (Bu ayrımı yapmayan bir
 * sayaç, aynı gün redwood sözleşmesinde yanlış alarm verdi.)
 */

const ONYUZ = join(__dirname, "..");
const DEPO = join(ONYUZ, "..", "..");

const ALANLAR = ["all_permissions", "not_permissions"];

/**
 * `permissions` LİSTESİ AYRI ELE ALINIYOR: kelime her yerde geçen bir
 * iş terimi ("permissions" tablosu, `user.permissions` alanı). Yalnız
 * JETON alanı olarak okunması yasak; onu `payload.permissions` ve
 * `FindAll("permissions")` biçimleriyle arıyoruz.
 */
const JETON_OKUMA_KALIPLARI = [
  /payload\.permissions/,
  /payload\["permissions"\]/,
  /FindAll\("permissions"\)/,
  /FindFirstValue\("all_permissions"\)/,
];

/**
 * ALAN ADI ÜÇ BİÇİMDE OKUNABİLİR ve muhafız üçünü de görmeli:
 *   "all_permissions"        → tırnaklı (claim adı, sözlük anahtarı)
 *   .all_permissions         → ÖZELLİK ERİŞİMİ  ← ilk sürüm bunu KAÇIRDI
 *   ["all_permissions"]      → köşeli erişim
 *
 * SONDA BUNU YAKALADI: middleware'e `payload.all_permissue === true`
 * biçiminde ikinci bir okuma kondu ve muhafız YEŞİL kaldı, çünkü
 * yalnız tırnaklı biçimi arıyordu. Yani muhafızın koruduğunu
 * söylediğim şeyi, en doğal yazım biçiminde görmüyordu.
 *
 * Kural 42'nin bir başka yüzü: ölçülmemiş bir koruma konmuş sayılmaz.
 * Sabotaj olmasaydı bu delik commit'e girer ve "Ş1 korunuyor" yazardı.
 */
function alanKaliplari(alan: string): RegExp[] {
  return [
    new RegExp(`"${alan}"`),
    new RegExp(`\\.${alan}\\b`),
    new RegExp(`\\['${alan}'\\]`),
  ];
}

/** Yorumlayıcının kendisi ve onu SINAYAN testler muaf. */
const MUAF = [
  join("frontend", "enderun-ai", "lib", "auth", "jeton-izinleri.ts"),
  join("backend", "EnderunAI.Api", "Security", "JetonIzinKodlamasi.cs"),
  join("frontend", "enderun-ai", "tests", "jeton-kodlamasi-tek-yer.test.ts"),
  join("frontend", "enderun-ai", "tests", "jeton-izinleri.test.ts"),
  join("backend", "EnderunAI.Api.Tests", "TokenCookieSizeTests.cs"),
  join("backend", "EnderunAI.Api.Tests", "JetonIzinKodlamasiTests.cs"),
  join("backend", "EnderunAI.Api.Tests", "SessionPermissionFlagTests.cs"),
];

function kaynaklar(dizin: string, uzantilar: string[]): string[] {
  const bulunan: string[] = [];

  let girdiler: string[];
  try {
    girdiler = readdirSync(dizin);
  } catch {
    return bulunan;
  }

  for (const girdi of girdiler) {
    if (
      girdi === "node_modules" ||
      girdi === ".next" ||
      girdi === "bin" ||
      girdi === "obj" ||
      girdi === "Migrations"
    ) {
      continue;
    }

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...kaynaklar(yol, uzantilar));
      continue;
    }

    if (uzantilar.some((u) => yol.endsWith(u))) bulunan.push(yol);
  }

  return bulunan;
}

/** Yorum satırlarını atar; bir yorum karar veremez. */
function koddanArindir(metin: string): string[] {
  return metin
    .split("\n")
    .filter((satir) => {
      const s = satir.trim();
      return !(
        s.startsWith("//") ||
        s.startsWith("*") ||
        s.startsWith("/*") ||
        s.startsWith("///")
      );
    });
}

function ihlaller(): string[] {
  const dosyalar = [
    ...kaynaklar(join(DEPO, "frontend", "enderun-ai", "app"), [".ts", ".tsx"]),
    ...kaynaklar(join(DEPO, "frontend", "enderun-ai", "lib"), [".ts", ".tsx"]),
    ...kaynaklar(join(DEPO, "frontend", "enderun-ai", "components"), [".ts", ".tsx"]),
    ...kaynaklar(join(DEPO, "frontend", "enderun-ai", "services"), [".ts", ".tsx"]),
    join(DEPO, "frontend", "enderun-ai", "middleware.ts"),
    ...kaynaklar(join(DEPO, "backend", "EnderunAI.Api"), [".cs"]),
  ];

  const bulunan: string[] = [];

  for (const yol of dosyalar) {
    const gorece = relative(DEPO, yol);

    if (MUAF.some((muaf) => gorece.endsWith(muaf) || gorece === muaf)) continue;

    let metin: string;
    try {
      metin = readFileSync(yol, "utf8");
    } catch {
      continue;
    }

    for (const satir of koddanArindir(metin)) {
      const vurdu =
        ALANLAR.some((alan) =>
          alanKaliplari(alan).some((kalip) => kalip.test(satir)),
        ) || JETON_OKUMA_KALIPLARI.some((k) => k.test(satir));

      if (vurdu) bulunan.push(`${gorece}: ${satir.trim().slice(0, 90)}`);
    }
  }

  return bulunan;
}

describe("jeton kodlaması tek yerde", () => {
  /**
   * TARAMA BOŞA DÜŞMÜYOR. Dosya bulunamazsa "ihlal yok" testi yeşil
   * kalır — boş küme her iddiayı doğrular (Kural 48).
   */
  it("tarama boşa düşmüyor", () => {
    const arkaUc = kaynaklar(join(DEPO, "backend", "EnderunAI.Api"), [".cs"]);
    const onYuz = kaynaklar(join(DEPO, "frontend", "enderun-ai", "lib"), [".ts"]);

    expect(arkaUc.length).toBeGreaterThan(100);
    expect(onYuz.length).toBeGreaterThan(20);
  });

  /**
   * MUAF LİSTESİ GERÇEK DOSYALARI GÖSTERİYOR. Yolu değişmiş bir muaf
   * girdisi sessizce ölür ve muhafız o dosyayı taramaya başlar —
   * ya da tersi: yanlış yazılmış bir muafiyet hiçbir şeyi muaf
   * tutmaz ama kimse fark etmez.
   */
  it("yorumlayıcı dosyaları yerinde", () => {
    const yorumlayicilar = [
      join(DEPO, "frontend", "enderun-ai", "lib", "auth", "jeton-izinleri.ts"),
      join(DEPO, "backend", "EnderunAI.Api", "Security", "JetonIzinKodlamasi.cs"),
    ];

    for (const yol of yorumlayicilar) {
      expect(() => readFileSync(yol, "utf8"), `${yol} yok`).not.toThrow();
    }
  });

  /*
   * SÜRE SINIRI YÜKSELTİLDİ — İDDİA DEĞİŞMEDİ.
   *
   * Bu test arka uçta ~500, ön yüzde ~300 kaynak dosyayı okuyup
   * tarıyor. Tek başına ~1 sn, ama tam takımda CPU rekabetiyle 8 sn
   * sürdü ve vitest'in varsayılan 5 sn sınırına takıldı.
   *
   * ZAMAN AŞIMI GERÇEK İHLALLE AYNI GÖRÜNÜR: ikisi de kırmızı verir
   * ve "muhafız bir şey buldu" sanılır. Ölçüldü — tek başına koşuda
   * yeşil, yani ihlal yok, iş ağır.
   *
   * İddia aynen duruyor; yalnız işin gerçek süresine yer açıldı.
   */
  it("kodlama alanları yorumlayıcı dışında okunmuyor", { timeout: 30_000 }, () => {
    const bulunan = ihlaller();

    expect(
      bulunan,
      "Jeton izin kodlaması yorumlayıcı DIŞINDA okunuyor:\n" +
        bulunan.join("\n") +
        "\n\nÜç kodlama (all_permissions / not_permissions / permissions) " +
        "yalnız lib/auth/jeton-izinleri.ts ve Security/JetonIzinKodlamasi.cs " +
        "içinde yorumlanır. İkinci bir karar yeri açılırsa biri " +
        "güncellenip diğeri kalır ve sonuç SESSİZ bir yetki hatası olur: " +
        "bayrağı tek başına okuyan tüketici, yanındaki tümleyen listesini " +
        "görmez ve kullanıcıya olmayan bir yetki verir.",
    ).toEqual([]);
  });
});
