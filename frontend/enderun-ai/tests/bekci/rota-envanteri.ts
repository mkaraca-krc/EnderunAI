import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";

/**
 * ROTA BEKÇİSİ — ORTAK ARAÇLAR.
 *
 * ROTALAR KAYNAKTAN TÜRETİLİYOR, DERLEME ÇIKTISINDAN DEĞİL.
 * `.next/routes-manifest.json` bir yapı artığı: bayat olabilir, CI'da
 * hiç bulunmayabilir, ve "bekçi yeşil çünkü dosya yok" durumu
 * sessizce oluşur. `app/**\/page.tsx` ise tek gerçek kaynak.
 *
 * KAPSAM YALNIZ SAYFA ROTALARI. `app/api/**` altındaki route
 * handler'lar (uç noktalar) BU TURDA DIŞARIDA — ayrı bir bekçi işi.
 */

export const KOK = join(__dirname, "..", "..");

const TARANAN_DIZINLER = ["app", "components", "services", "lib"];

function dosyalariTopla(dizin: string, uzantilar: RegExp): string[] {
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
      bulunan.push(...dosyalariTopla(yol, uzantilar));
      continue;
    }

    if (uzantilar.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

/** `app/gorevler/[id]/page.tsx` -> `/gorevler/[id]` */
export function rotalar(): string[] {
  const appKok = join(KOK, "app");

  return dosyalariTopla(appKok, /^page\.tsx$/)
    .filter((yol) => !yol.startsWith(join(appKok, "api") + sep))
    .map((yol) => {
      const parcalar = relative(appKok, yol).split(sep);
      parcalar.pop(); // page.tsx

      // Rota grupları — `(pazarlama)` gibi — yola girmez.
      const yolParcalari = parcalar.filter((p) => !/^\(.*\)$/.test(p));

      return "/" + yolParcalari.join("/");
    })
    .map((r) => (r === "/" ? "/" : r.replace(/\/$/, "")))
    .sort();
}

/** `/hakedis/[id]` -> `^/hakedis/[^/]+$` */
export function rotaDeseni(rota: string): RegExp {
  const parcalar = rota.split("/").filter(Boolean);

  const desen = parcalar
    .map((p) => {
      if (/^\[\.\.\..+\]$/.test(p)) return ".+";           // catch-all
      if (/^\[\[\.\.\..+\]\]$/.test(p)) return ".*";        // opsiyonel catch-all
      if (/^\[.+\]$/.test(p)) return "[^/]+";               // [id]
      return p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    })
    .join("/");

  return new RegExp(`^/${desen}$`);
}

export type Hedef = {
  ham: string;
  dosya: string;
  tur: "degismez" | "sablon";
};

/*
 * BAĞLANTI KALIPLARI.
 *
 * Yalnız İÇ yollar toplanıyor (`/` ile başlayan). Dış bağlantılar,
 * `mailto:`, `tel:`, çapa (`#`) ve `/api/` hariç — sonuncusu
 * kapsam dışı olduğu için, yanlış olduğu için değil.
 */
const KALIPLAR = [
  /href\s*=\s*"([^"]+)"/g,
  /href\s*=\s*\{\s*"([^"]+)"\s*\}/g,
  /href\s*=\s*\{\s*`([^`]+)`\s*\}/g,
  /href\s*:\s*"([^"]+)"/g,
  /href\s*:\s*`([^`]+)`/g,
  /router\.(?:push|replace)\(\s*"([^"]+)"/g,
  /router\.(?:push|replace)\(\s*`([^`]+)`/g,
  /(?<!\w)(?:permanentRedirect|redirect)\(\s*"([^"]+)"/g,
  /(?<!\w)(?:permanentRedirect|redirect)\(\s*`([^`]+)`/g,
];

/** Değişkenden gelen, doğrulanamayan hedefler. */
const HESAPLANMIS_KALIPLAR = [
  /href\s*=\s*\{\s*([A-Za-z_$][\w$]*(?:[.?][\w$]+)*)\s*\}/g,
  /router\.(?:push|replace)\(\s*([A-Za-z_$][\w$]*(?:[.?][\w$]+)*)\s*\)/g,
];

function kaynakDosyalari(): { yol: string; kod: string }[] {
  const dosyalar: string[] = [];

  for (const d of TARANAN_DIZINLER) {
    dosyalar.push(...dosyalariTopla(join(KOK, d), /\.(tsx|ts)$/));
  }

  return dosyalar.map((yol) => ({ yol, kod: readFileSync(yol, "utf8") }));
}

export function tarananDosyaSayisi(): number {
  return kaynakDosyalari().length;
}

function icYolMu(ham: string): boolean {
  if (!ham.startsWith("/")) return false;
  if (ham.startsWith("//")) return false;
  if (ham.startsWith("/api/")) return false; // kapsam dışı — bu tur
  return true;
}

export function hedefler(): Hedef[] {
  const bulunan: Hedef[] = [];

  for (const { yol, kod } of kaynakDosyalari()) {
    for (const kalip of KALIPLAR) {
      kalip.lastIndex = 0;

      let eslesme: RegExpExecArray | null;
      while ((eslesme = kalip.exec(kod)) !== null) {
        const ham = eslesme[1];
        if (!icYolMu(ham)) continue;

        bulunan.push({
          ham,
          dosya: relative(KOK, yol),
          tur: ham.includes("${") ? "sablon" : "degismez",
        });
      }
    }
  }

  return bulunan;
}

/** Dosya bazında hesaplanmış (doğrulanamaz) hedef sayısı. */
export function hesaplanmisHedefler(): Map<string, number> {
  const sayac = new Map<string, number>();

  for (const { yol, kod } of kaynakDosyalari()) {
    let adet = 0;

    for (const kalip of HESAPLANMIS_KALIPLAR) {
      kalip.lastIndex = 0;
      while (kalip.exec(kod) !== null) adet++;
    }

    if (adet > 0) sayac.set(relative(KOK, yol), adet);
  }

  return sayac;
}

/** Şablon hedefi karşılaştırılabilir biçime getirir. */
export function sablonuNormalize(ham: string): string {
  return ham
    .split("?")[0]
    .split("#")[0]
    .replace(/\$\{[^}]*\}/g, "X");
}

export function cozuluyorMu(yol: string, desenler: RegExp[]): boolean {
  let temiz = yol.split("?")[0].split("#")[0];
  if (temiz.length > 1 && temiz.endsWith("/")) temiz = temiz.slice(0, -1);
  if (temiz === "") temiz = "/";

  return desenler.some((d) => d.test(temiz));
}
